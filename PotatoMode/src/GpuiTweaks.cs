// PotatoMode - GPU Instancer Pro vegetation tweaks
//
// Tank Squad draws its bushes/trees through GPU Instancer Pro, so Unity's
// QualitySettings (LOD bias, shadows) never reach them. Every GPUI prototype
// is controlled by a GPUIProfile asset; this module finds those profiles at
// runtime, logs their current values, and overrides the render-only ones.
//
// Fair-play rule: nothing in THIS file changes WHETHER vegetation is drawn or how far
// away it's visible. minMaxDistance, isDistanceCulling and minCullingDistance
// are deliberately never touched. Colliders/physics are not involved at all.
// Exception (opt-in, off by default): VegetationSwitch.cs can hide chosen
// vegetation types / terrain details entirely, for low-end PCs. Single-player /
// co-op vs AI only: the AI's concealment uses separate visibility volumes.
//
// Integration (two lines in the existing plugin):
//   Load():                      GpuiTweaks.Init(Config, Log);
//   MEC.Timing.Update postfix:   GpuiTweaks.Tick();
//
// csproj: add a reference to the GPUI interop assembly (Private=false), e.g.
//   <Reference Include="GPUInstancerPro.Core">
//     <HintPath>$(GameDir)\BepInEx\interop\GPUInstancerPro.Core.dll</HintPath>
//     <Private>false</Private>
//   </Reference>

using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using GPUInstancerPro;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PotatoMode
{
    internal static class GpuiTweaks
    {
        const string Section = "GPUI Vegetation";

        static ConfigFile _cfg;
        static ManualLogSource _log;

        static ConfigEntry<bool> _enabled;
        static ConfigEntry<string> _include;
        static ConfigEntry<int> _shadowCasting;
        static ConfigEntry<float> _shadowDistance;
        static ConfigEntry<int> _shadowCulling;
        static ConfigEntry<float> _lodBiasAdjustment;
        static ConfigEntry<int> _maximumLODLevel;
        static ConfigEntry<int> _lodCrossFade;

        sealed class Original
        {
            public string Name;
            public bool ShadowCasting, CrossFade, ShadowDistCull, ShadowFrustCull, ShadowOccCull;
            public float CustomShadowDistance, LodBiasAdjustment;
            public int MaximumLODLevel;
        }

        static readonly Dictionary<IntPtr, Original> _originals = new();
        static readonly HashSet<IntPtr> _applied = new();
        static readonly HashSet<IntPtr> _touched = new();   // profiles the plugin has changed (so Enabled = false knows what to put back)
        static readonly HashSet<string> _errorsLogged = new();

        public static void Init(ConfigFile cfg, ManualLogSource log)
        {
            _cfg = cfg;
            _log = log;
            VegetationSwitch.Init(cfg, log);
            ShaderCache.Init(cfg, log);

            _enabled = cfg.Bind(Section, "Enabled", true,
                "Master switch for this whole section. false = hands off: puts back anything the plugin changed (profiles and the detail manager), then leaves the game's vegetation alone.");

            _include = cfg.Bind(Section, "IncludeProfiles", "",
                "Comma-separated, case-insensitive substrings of GPUI profile names to modify. " +
                "Empty = all profiles. Check the log: every profile is listed with its name and current values when first found.");

            _shadowCasting = cfg.Bind(Section, "ShadowCasting", -1,
                new ConfigDescription(
                    "Vegetation shadow casting (isShadowCasting). -1 = leave, 0 = off, 1 = on. " +
                    "Off is the biggest GPU saving and does not change what you can see through.",
                    new AcceptableValueRange<int>(-1, 1)));

            _shadowDistance = cfg.Bind(Section, "CustomShadowDistance", -1f,
                "GPUI per-profile shadow distance in metres (customShadowDistance). -1 = leave. " +
                "A middle ground if you want some vegetation shadows. Compare the logged customShadowDist and " +
                "shadowDist values first to see how the game uses it.");

            _shadowCulling = cfg.Bind(Section, "ShadowCulling", -1,
                new ConfigDescription(
                    "Cull vegetation shadow casters by distance, frustum and occlusion " +
                    "(isShadowDistanceCulling / isShadowFrustumCulling / isShadowOcclusionCulling). " +
                    "-1 = leave, 1 = enable all three. Only skips shadows you wouldn't see anyway.",
                    new AcceptableValueRange<int>(-1, 1)));

            _lodBiasAdjustment = cfg.Bind(Section, "LodBiasScale", -1f,
                "Multiplies each profile's own LOD bias (lodBiasAdjustment), so the per-tree tuning the game ships " +
                "with is kept. -1 = leave. Lower = cheaper LODs sooner, e.g. 0.6. Same footprint, less detail.");

            _maximumLODLevel = cfg.Bind(Section, "MaximumLODLevel", -1,
                new ConfigDescription(
                    "Highest-detail LOD GPUI may use (maximumLODLevel). -1 = leave, 0 = full detail, " +
                    "1 = never use LOD0, 2 = never use LOD0/LOD1.",
                    new AcceptableValueRange<int>(-1, 3)));

            _lodCrossFade = cfg.Bind(Section, "LODCrossFade", -1,
                new ConfigDescription(
                    "Dithered cross-fade between LODs (isLODCrossFade). -1 = leave, 0 = off, 1 = on. " +
                    "Off saves work during transitions but LOD switches become visible pops.",
                    new AcceptableValueRange<int>(-1, 1)));

            // Re-apply everything when the config changes or is live-reloaded.
            cfg.SettingChanged += (_, _) => { _applied.Clear(); _rescanRequested = true; };
            cfg.ConfigReloaded += (_, _) => { _applied.Clear(); _rescanRequested = true; };
        }

        // Scanning every loaded object is expensive (it caused a visible hitch every 5 s), so we
        // only scan on a short schedule after each scene change, plus once after a config change.
        // Seconds after a scene change at which to scan; GPUI registers vegetation as the map loads.
        static readonly float[] ScanSchedule = { 1f, 3f, 6f, 12f, 25f, 45f };
        static int _lastSceneKey = int.MinValue;
        static float _sceneChangedAt;
        static int _scanStep = int.MaxValue;
        static bool _rescanRequested;

        public static void Tick()
        {
            if (_cfg == null) return;
            try { ShaderCache.Tick(); } catch (Exception e) { LogOnce("shadercache", "[ShaderCache] " + e.Message); }
            float now = Time.unscaledTime;

            // Cheap per-frame scene-change check: active scene handle + loaded scene count.
            int sceneKey;
            try { sceneKey = SceneManager.GetActiveScene().handle * 31 + SceneManager.sceneCount; }
            catch { sceneKey = 0; }

            if (sceneKey != _lastSceneKey)
            {
                _lastSceneKey = sceneKey;
                _sceneChangedAt = now;
                _scanStep = 0;
            }

            bool due = _scanStep < ScanSchedule.Length && now >= _sceneChangedAt + ScanSchedule[_scanStep];
            if (!due && !_rescanRequested) return;

            if (due) _scanStep++;
            _rescanRequested = false;

            bool active = _enabled.Value;
            if (active || _touched.Count > 0)
            {
                try { Scan(); }
                catch (Exception e) { LogOnce("scan", "[GPUI] Scan failed: " + e); }
            }
            try { VegetationSwitch.Scan(active); }
            catch (Exception e) { LogOnce("vegswitch", "[Vegetation] Scan failed: " + e.Message); }
        }

        static void Scan()
        {
            var found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GPUIProfile>());
            if (found == null || found.Length == 0) return;

            bool newProfiles = false;

            foreach (var obj in found)
            {
                var p = obj?.TryCast<GPUIProfile>();
                if (p == null) continue;

                IntPtr key = p.Pointer;
                string name = SafeName(p);

                // Pointers can be reused after a scene unload; recapture if the object changed.
                if (!_originals.TryGetValue(key, out var o) || o.Name != name)
                {
                    o = Capture(p, name);
                    _originals[key] = o;
                    _applied.Remove(key);
                    newProfiles = true;
                    Quiet.Info(_log, "[GPUI] Found profile " + Describe(p, name));
                }

                if (_applied.Contains(key)) continue;

                try
                {
                    Apply(p, o);
                    _applied.Add(key);
                }
                catch (Exception e)
                {
                    LogOnce("apply:" + name, $"[GPUI] Could not apply to '{name}': {e.Message}");
                    _applied.Add(key); // don't retry every scan; a config change clears this
                }
            }

            if (newProfiles) LogDrawerCount();
        }

        static Original Capture(GPUIProfile p, string name) => new Original
        {
            Name = name,
            ShadowCasting = p.isShadowCasting,
            CustomShadowDistance = p.customShadowDistance,
            ShadowDistCull = p.isShadowDistanceCulling,
            ShadowFrustCull = p.isShadowFrustumCulling,
            ShadowOccCull = p.isShadowOcclusionCulling,
            LodBiasAdjustment = p.lodBiasAdjustment,
            MaximumLODLevel = p.maximumLODLevel,
            CrossFade = p.isLODCrossFade,
        };

        static void Apply(GPUIProfile p, Original o)
        {
            bool on = _enabled.Value && Included(o.Name);
            bool touched = _touched.Contains(p.Pointer);
            if (!on && !touched) return;                      // never changed by the plugin: leave the game's values alone

            var changes = new List<string>();
            // A setting at -1 only puts back the game's value if the plugin had changed this profile.
            bool shadow = on && _shadowCasting.Value >= 0 ? _shadowCasting.Value == 1 : o.ShadowCasting;
            float shadowDist = on && _shadowDistance.Value >= 0f ? _shadowDistance.Value : o.CustomShadowDistance;
            bool cull = on && _shadowCulling.Value == 1;
            float lodBias = on && _lodBiasAdjustment.Value > 0f ? o.LodBiasAdjustment * _lodBiasAdjustment.Value : o.LodBiasAdjustment;
            int maxLod = on && _maximumLODLevel.Value >= 0 ? _maximumLODLevel.Value : o.MaximumLODLevel;
            bool crossFade = on && _lodCrossFade.Value >= 0 ? _lodCrossFade.Value == 1 : o.CrossFade;
            bool any = on && (_shadowCasting.Value >= 0 || _shadowDistance.Value >= 0f || _shadowCulling.Value == 1 || _lodBiasAdjustment.Value > 0f || _maximumLODLevel.Value >= 0 || _lodCrossFade.Value >= 0);
            if (!any && !touched) return;

            if (p.isShadowCasting != shadow) { changes.Add($"shadows {p.isShadowCasting}->{shadow}"); p.isShadowCasting = shadow; }
            if (!Mathf.Approximately(p.customShadowDistance, shadowDist)) { changes.Add($"customShadowDist {p.customShadowDistance}->{shadowDist}"); p.customShadowDistance = shadowDist; }

            bool dc = cull || o.ShadowDistCull, fc = cull || o.ShadowFrustCull, oc = cull || o.ShadowOccCull;
            if (p.isShadowDistanceCulling != dc) { changes.Add($"shadowDistCull ->{dc}"); p.isShadowDistanceCulling = dc; }
            if (p.isShadowFrustumCulling != fc) { changes.Add($"shadowFrustCull ->{fc}"); p.isShadowFrustumCulling = fc; }
            if (p.isShadowOcclusionCulling != oc) { changes.Add($"shadowOccCull ->{oc}"); p.isShadowOcclusionCulling = oc; }

            if (!Mathf.Approximately(p.lodBiasAdjustment, lodBias)) { changes.Add($"lodBiasAdj {p.lodBiasAdjustment}->{lodBias}"); p.lodBiasAdjustment = lodBias; }
            if (p.maximumLODLevel != maxLod) { changes.Add($"maxLOD {p.maximumLODLevel}->{maxLod}"); p.maximumLODLevel = maxLod; }
            if (p.isLODCrossFade != crossFade) { changes.Add($"crossFade {p.isLODCrossFade}->{crossFade}"); p.isLODCrossFade = crossFade; }

            if (any) _touched.Add(p.Pointer); else _touched.Remove(p.Pointer);   // back on the game's values

            if (changes.Count == 0) return;

            // Push the new values into GPUI's GPU-side parameter buffer.
            try { p.SetParameterBufferData(); }
            catch (Exception e) { LogOnce("buffer:" + o.Name, $"[GPUI] SetParameterBufferData failed for '{o.Name}': {e.Message}"); }

            Quiet.Info(_log, $"[GPUI] '{o.Name}': " + string.Join(", ", changes));
        }

        static string Describe(GPUIProfile p, string name)
        {
            var sb = new StringBuilder();
            sb.Append($"'{name}'");
            Try(sb, "default", () => p.isDefaultProfile.ToString());
            Try(sb, "shadows", () => p.isShadowCasting.ToString());
            Try(sb, "customShadowDist", () => p.customShadowDistance.ToString("0.##"));
            Try(sb, "shadowDist", () => p.GetShadowDistance().ToString("0.##"));
            Try(sb, "shadowCull(d/f/o)", () => $"{p.isShadowDistanceCulling}/{p.isShadowFrustumCulling}/{p.isShadowOcclusionCulling}");
            Try(sb, "lodBiasAdj", () => p.lodBiasAdjustment.ToString("0.##"));
            Try(sb, "effectiveLodBias", () => p.GetLODBiasAdjustment().ToString("0.##"));
            Try(sb, "maxLOD", () => p.maximumLODLevel.ToString());
            Try(sb, "crossFade", () => p.isLODCrossFade.ToString());
            Try(sb, "occlusion", () => p.isOcclusionCulling.ToString());
            Try(sb, "distCull", () => p.isDistanceCulling.ToString());
            Try(sb, "minMaxDist", () => p.minMaxDistance.ToString());
            Try(sb, "maxDist", () => p.GetMaxDistance().ToString("0.##"));
            Try(sb, "shadowLODMap", () =>
            {
                var map = p.shadowLODMap;
                if (map == null) return "null";
                var parts = new string[map.Length];
                for (int i = 0; i < map.Length; i++) parts[i] = map[i].ToString("0.##");
                return "[" + string.Join(",", parts) + "]";
            });
            return sb.ToString();
        }

        static void LogDrawerCount()
        {
            try
            {
                var drawers = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<GPUISphericalVegetationDrawer>());
                Quiet.Info(_log, $"[GPUI] GPUISphericalVegetationDrawer instances in scene: {drawers?.Length ?? 0}");
            }
            catch (Exception e) { LogOnce("drawers", "[GPUI] Drawer count failed: " + e.Message); }
        }

        static bool Included(string name)
        {
            string list = _include.Value;
            if (string.IsNullOrWhiteSpace(list)) return true;
            foreach (var part in list.Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0 && name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static string SafeName(GPUIProfile p)
        {
            try { return string.IsNullOrEmpty(p.name) ? "(unnamed)" : p.name; }
            catch { return "(unnamed)"; }
        }

        static void Try(StringBuilder sb, string label, Func<string> get)
        {
            string v;
            try { v = get(); } catch (Exception e) { v = "ERR(" + e.GetType().Name + ")"; }
            sb.Append(' ').Append(label).Append('=').Append(v);
        }

        static void LogOnce(string key, string msg)
        {
            if (_errorsLogged.Add(key)) _log.LogWarning(msg);
        }
    }
}
