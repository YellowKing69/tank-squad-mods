// Tank Squad - Potato VFX (BepInEx 6 IL2CPP plugin)
// - Shell tracers: remove the smoke tail, smaller / dimmer glow.
// - Your tank firing: front and side barrel smoke toggles.
// - Rockets / off-site artillery: smoke toggle, or hide their trails entirely (performance).
// - Machine-gun / rifle tracers (DP-27, MG34 ...): remove the tail behind the bullet, keep the glow.
// Harmony-only: no class injection.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace TankSquadPotatoVFX
{
    [BepInPlugin(Guid, "Potato VFX", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "potatovfx.tanksquad";
        public const string Version = "1.4.3";

        internal static ManualLogSource L;
        internal static Plugin Instance;
        internal static ConfigEntry<bool> ShellRemoveSmoke;
        internal static ConfigEntry<string> ShellSmokeMatch;
        internal static ConfigEntry<float> ShellGlowScale, ShellGlowIntensity;
        internal static ConfigEntry<bool> FrontSmoke, SideSmoke;
        internal static ConfigEntry<bool> OffMapTrails, OffMapSmoke;
        internal static ConfigEntry<string> OffMapMatch;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<bool> MgRemoveTail;
        internal static ConfigEntry<float> MgMaxCaliber, MgGlowScale, MgGlowIntensity;
        internal static ConfigEntry<string> MgTailMatch, MgAmmoMatch, MgTracerMatch, MgTailParts, MgTweakParts, MgTailSettings;

        public override void Load()
        {
            L = Log;
            Instance = this;
            MigrateFromQuickCrew(new[] { "Shells", "OffsiteSmoke" });

            ShellRemoveSmoke = Config.Bind("Shells", "RemoveSmoke", true,
                "Remove the smoke trail behind tank shells (rockets: see OffsiteSmoke). Game: off.");
            ShellSmokeMatch = Config.Bind("Shells", "SmokeMatch", "smoke",
                "Effect parts whose name contains one of these words count as smoke.");
            ShellGlowScale = Config.Bind("Shells", "GlowScale", 0.5f,
                new ConfigDescription("Size of the shell glow. Game: 1.", new AcceptableValueRange<float>(0f, 3f)));
            ShellGlowIntensity = Config.Bind("Shells", "GlowIntensity", 1f,
                new ConfigDescription("Shell glow: below 1 = fainter, above 1 = brighter. Game: 1.", new AcceptableValueRange<float>(0f, 5f)));

            OffMapSmoke = Config.Bind("OffsiteSmoke", "RocketSmoke", false,
                "Show the smoke behind rockets and off-site artillery. Off = better performance. Game: on.");
            OffMapTrails = Config.Bind("OffsiteSmoke", "Trails", true,
                "Show rocket and off-site artillery trails at all (fire, smoke, glow). Off = best performance. Game: on.");
            OffMapMatch = Config.Bind("OffsiteSmoke", "Match", "rocket,nebelwerfer,katyusha,calliope",
                "Trail names containing one of these words count as rockets.");
            // FrontSmoke used to be a number; drop an old numeric value so the toggle starts clean.
            try
            {
                var od = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) as Dictionary<ConfigDefinition, string>;
                foreach (var key in new[] { "FrontSmoke", "SideSmoke" })
                {
                    var fk = new ConfigDefinition("Shells", key);
                    if (od != null && od.TryGetValue(fk, out var fv) && !bool.TryParse(fv?.Trim(), out _)) od.Remove(fk);
                }
            }
            catch { }
            FrontSmoke = Config.Bind("Shells", "FrontSmoke", false,
                "Show the smoke cloud in front of your barrel when firing. Game: on.");
            SideSmoke = Config.Bind("Shells", "SideSmoke", true,
                "Show the smoke fanning out left and right of your barrel when firing (the flash stays). Game: on.");


            MgRemoveTail = Config.Bind("MGTracers", "RemoveTail", true,
                "Remove the tail/streak behind machine-gun and rifle tracers (DP-27, MG34 ...). The glowing bullet stays. Game: off.");
            MgMaxCaliber = Config.Bind("MGTracers", "MaxCaliber", 15f,
                new ConfigDescription("Rounds up to this calibre in mm count as machine-gun / rifle ammo (7.62, 7.92 ...). Tank shells are far above it.", new AcceptableValueRange<float>(1f, 40f)));
            MgAmmoMatch = Config.Bind("MGTracers", "AmmoMatch", "",
                "Optional: ammo names containing one of these words also count as MG ammo (comma separated). Normally not needed.");
            MgTracerMatch = Config.Bind("MGTracers", "TracerMatch", "rifle",
                "Tracer effects whose name contains one of these words are MG / rifle tracers whatever the ammo says (the game's are P_VFX_Tracer_SOV_Rifle and P_VFX_Tracer_GER_Rifle).");
            MgTailParts = Config.Bind("MGTracers", "TailParts", "P_G_VFX_TracerTrail",
                "Which parts of the MG tracer are the tail (switched off). The game's MG tracer has two: P_G_VFX_TracerTrail (the long streak) and P_G_VFX_ParticleTrail (the glowing round). Comma separated; if the wrong one disappears, swap them. Live.");
            MgTweakParts = Config.Bind("MGTracers", "TweakParts", "P_G_VFX_ParticleTrail",
                "MG tracer parts that draw the glowing round AND its tail in one effect: these are not switched off, their tail settings are changed instead (TailSettings). Live.");
            MgTailSettings = Config.Bind("MGTracers", "TailSettings", "Ribbon Life=0; Ribbon Max Size overLife=0",
                "Effect settings for the TweakParts: 'name=value; name=value' (names as listed in the log line '[MG] settings of ...'). auto = every setting named like a trail/tail length, lifetime, width, count or rate set to 0. Empty = game values. Live.");
            MgTailMatch = Config.Bind("MGTracers", "TailMatch", "trail,tail,streak,smoke",
                "Effect parts whose name contains one of these words are tail parts. Trail renderers and particle trails are always tail.");
            MgGlowScale = Config.Bind("MGTracers", "GlowScale", -1f,
                new ConfigDescription("Size of the MG tracer glow. -1 = same as [Shells] GlowScale (the look you have now). Game: 1.", new AcceptableValueRange<float>(-1f, 3f)));
            MgGlowIntensity = Config.Bind("MGTracers", "GlowIntensity", -1f,
                new ConfigDescription("MG tracer glow brightness. -1 = same as [Shells] GlowIntensity. Game: 1.", new AcceptableValueRange<float>(-1f, 5f)));

            VerboseLog = Config.Bind("Debug", "VerboseLog", false,
                "Write diagnostic lines to the log.");

            Config.Save();
            Config.SaveOnConfigSet = false;
            StartConfigWatcher();

            var harmony = new Harmony(Guid);
            try { harmony.PatchAll(typeof(ShellFx)); } catch (Exception e) { L.LogWarning("Shell patches failed: " + e.Message); }
            try
            {
                var update = AccessTools.DeclaredMethod(typeof(MEC.Timing), "Update", Type.EmptyTypes);
                if (update != null) harmony.Patch(update, postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
                else L.LogWarning("MEC.Timing.Update not found - per-frame updates disabled.");
            }
            catch (Exception e) { L.LogWarning("Could not install the per-frame update: " + e.Message); }
            // Logs quiet by default: switch off the log settings once (they can be turned back on afterwards).
            var quiet = Config.Bind("Debug", "QuietLogsApplied", false, "Set by the plugin after it switched its logging off once. Leave as is.");
            if (!quiet.Value) { VerboseLog.Value = false; quiet.Value = true; }
            V("Potato VFX " + Version + " loaded.");
        }

        internal static void V(string s) { if (VerboseLog != null && VerboseLog.Value) L.LogInfo(s); }

        // Postfix on MEC.Timing.Update (runs every frame).
        private static float _nextSlowLog;
        public static void Tick()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ReloadTick();
            MuzzleFx.Tick();
            RifleTails.Tick();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 4 && VerboseLog.Value && Time.unscaledTime >= _nextSlowLog) { _nextSlowLog = Time.unscaledTime + 1f; L.LogWarning($"[Perf] Potato VFX took {ms:0.0} ms this frame"); }
        }

        // First run: take these sections over from Quick Crew's config, where they used to live.
        private void MigrateFromQuickCrew(string[] sections)
        {
            try
            {
                var path = Config.ConfigFilePath;
                if (System.IO.File.Exists(path)) return;
                var old = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "quickcrew.tanksquad.cfg");
                if (!System.IO.File.Exists(old)) return;
                var keep = new HashSet<string>(sections);
                var outp = new System.Text.StringBuilder();
                bool on = false;
                foreach (var raw in System.IO.File.ReadAllLines(old))
                {
                    var t = raw.Trim();
                    if (t.StartsWith("[") && t.EndsWith("]")) on = keep.Contains(t.Substring(1, t.Length - 2));
                    if (on) outp.AppendLine(raw);
                }
                if (outp.Length == 0) return;
                System.IO.File.WriteAllText(path, outp.ToString());
                Config.Reload();
                V("Settings taken over from Quick Crew's config.");
            }
            catch (Exception e) { L.LogWarning("Could not take over Quick Crew settings: " + e.Message); }
        }

        // ---- live config reload: edit the .cfg while the game runs, changes apply within ~1 second ----
        private static System.IO.FileSystemWatcher _watcher;
        private static volatile bool _reloadPending;
        private static float _reloadAt = -1f;

        internal void StartConfigWatcher()
        {
            try
            {
                var path = Config.ConfigFilePath;
                _watcher = new System.IO.FileSystemWatcher(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path))
                {
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size
                };
                _watcher.Changed += (_, _) => _reloadPending = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { L.LogWarning("Config live-reload unavailable: " + e.Message); }
        }

        internal static void ReloadTick()
        {
            if (_reloadPending) { _reloadPending = false; _reloadAt = Time.unscaledTime + 0.5f; }
            if (_reloadAt < 0f || Time.unscaledTime < _reloadAt) return;
            _reloadAt = -1f;
            try { Instance?.Config.Reload(); V("Config reloaded."); ShellFx.ResetLog(); MuzzleFx.ResetLog(); }
            catch (Exception e) { L.LogWarning("Config reload failed: " + e.Message); }
        }
    }
}

namespace TankSquadPotatoVFX
{
    // Shell tracer effect: optional smoke removal and glow size. The effect objects are pooled and reused, so the game's
    // original values are remembered per component and the settings are always applied from those.
    [HarmonyPatch]
    internal static class ShellFx
    {
        private static readonly Dictionary<IntPtr, float> OrigSize = new();      // particle start size multiplier
        private static readonly Dictionary<IntPtr, float> OrigWidth = new();     // trail width multiplier
        private static readonly Dictionary<IntPtr, float> OrigRange = new();     // light range
        private static readonly Dictionary<IntPtr, Vector3> OrigScale = new();   // VFX graph transform scale
        private static readonly HashSet<string> Logged = new();

        [ThreadStatic] private static string _match;
        private static bool IsSmoke(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            foreach (var w in (_match ?? Plugin.ShellSmokeMatch.Value ?? "").Split(','))
            {
                var t = w.Trim().ToLowerInvariant();
                if (t.Length > 0 && name.Contains(t)) return true;
            }
            return false;
        }

        private static string Names(Component c)
        {
            string n = c.gameObject.name;
            try { var r = c.GetComponent<Renderer>(); if (r != null && r.sharedMaterial != null) n += " " + r.sharedMaterial.name; } catch { }
            return n;
        }

        private static readonly Dictionary<IntPtr, IntPtr> Done = new();   // projectile -> tracer it was applied to
        private static readonly HashSet<IntPtr> Hidden = new();

        private static bool IsOffMap(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile p, GameObject go)
        {
            try { if (p._IsArtyShot) return true; } catch { }
            string n = go.name.ToLowerInvariant();
            foreach (var w in (Plugin.OffMapMatch.Value ?? "").Split(','))
            {
                var t = w.Trim().ToLowerInvariant();
                if (t.Length > 0 && n.Contains(t)) return true;
            }
            return false;
        }

        // Switch every visible part of a trail effect on/off (effects, particles, trails, lights, shell mesh).
        private static void SetAll(GameObject go, bool on)
        {
            foreach (var v in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true)) if (v != null) v.enabled = on;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = on;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) if (ps != null) { var em = ps.emission; em.enabled = on; if (!on) ps.Clear(false); }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l != null) l.enabled = on;
        }
        private static bool _announced;
        private static string _doneKey;
        private static string Key() => $"{Plugin.OffMapSmoke.Value}|{Plugin.OffMapTrails.Value}|{Plugin.OffMapMatch.Value}|{Plugin.ShellRemoveSmoke.Value}|{Plugin.ShellGlowScale.Value:0.###}|{Plugin.ShellGlowIntensity.Value:0.###}|{Plugin.ShellSmokeMatch.Value}|{Plugin.MgRemoveTail.Value}|{Plugin.MgMaxCaliber.Value}|{Plugin.MgAmmoMatch.Value}|{Plugin.MgTailMatch.Value}|{Plugin.MgTracerMatch.Value}|{Plugin.MgGlowScale.Value}|{Plugin.MgGlowIntensity.Value}";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile), "SpawnTracer")]
        private static void AfterSpawn(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile __instance) => Check(__instance);

        // Runs every frame of a shell's flight; picks up the tracer even if the spawn call itself can't be hooked.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile), "TrajectoryUpdate")]
        private static void InFlight(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile __instance) => Check(__instance);

        private static void Check(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile p)
        {
            try
            {
                var go = p._TracerVFXGO;
                if (go == null) return;
                // MG tracer tails: kept off for the whole flight, every frame (cheap: the tail parts are cached per effect).
                if (Plugin.MgRemoveTail.Value && TailParts.TryGetValue(go.Pointer, out var tp)) Enforce(tp);
                // Shell smoke: also kept off for the whole flight (the game can switch it back on after the shot).
                if (SmokeParts.TryGetValue(go.Pointer, out var sp)) KeepOff(sp);
                if (Done.TryGetValue(p.Pointer, out var g) && g == go.Pointer && _doneKey == Key()) return;
                if (_doneKey != Key()) { _doneKey = Key(); Done.Clear(); RifleGo.Clear(); TailParts.Clear(); SmokeParts.Clear(); }
                Done[p.Pointer] = go.Pointer;
                if (Done.Count > 512) Done.Clear();
                if (!_announced && Plugin.VerboseLog.Value) { _announced = true; Plugin.L.LogDebug($"[Shells] tracer effects found (first: '{go.name}')"); }
                bool mg = IsMg(p, out string ammo) || IsRifleTracer(p, go);
                if (Plugin.VerboseLog.Value && Seen.Count < 40 && Seen.Add(go.name + "|" + ammo))
                    Plugin.V($"[Tracers] '{go.name}' ammo '{ammo}' -> {(mg ? (Plugin.MgRemoveTail.Value ? "MG: tail removed" : "MG: tail kept") : "shell")} | parts: {PartList(go)}");
                if (Plugin.VerboseLog.Value && Logged.Add(go.name)) { Plugin.L.LogDebug($"[Shells] tracer '{go.name}' ammo '{ammo}'{(mg ? " = MG ammo" : "")}"); Describe(go); }
                if (mg)
                {
                    // done for every tracer round (Done above skips repeats within one flight): the game re-arms its
                    // pooled effects for each shot, so a tail switched off once can come back on the next.
                    float gs = Plugin.MgGlowScale.Value < 0f ? Plugin.ShellGlowScale.Value : Plugin.MgGlowScale.Value;
                    float gi = Plugin.MgGlowIntensity.Value < 0f ? Plugin.ShellGlowIntensity.Value : Plugin.MgGlowIntensity.Value;
                    Apply(go, Plugin.ShellRemoveSmoke.Value, Math.Max(0f, gs), null, Math.Max(0f, gi), 1f);
                    Tail(go, !Plugin.MgRemoveTail.Value);
                    if (Plugin.MgRemoveTail.Value && !TailParts.ContainsKey(go.Pointer)) { if (TailParts.Count > 512) TailParts.Clear(); TailParts[go.Pointer] = Collect(go); }
                    return;
                }
                bool offMap = IsOffMap(p, go);
                bool hide = !Plugin.OffMapTrails.Value && offMap;
                if (!hide && Hidden.Remove(go.Pointer)) SetAll(go, true);       // reused for a normal shell: show again
                // Shells follow RemoveSmoke; rockets / off-site artillery follow RocketSmoke only.
                bool noSmoke = offMap ? !Plugin.OffMapSmoke.Value : Plugin.ShellRemoveSmoke.Value;
                Apply(go, noSmoke, Math.Max(0f, Plugin.ShellGlowScale.Value), null, Math.Max(0f, Plugin.ShellGlowIntensity.Value), 1f);
                if (noSmoke) { if (!SmokeParts.ContainsKey(go.Pointer)) { if (SmokeParts.Count > 512) SmokeParts.Clear(); SmokeParts[go.Pointer] = CollectSmoke(go); } }
                else SmokeParts.Remove(go.Pointer);
                if (hide) { SetAll(go, false); if (Hidden.Add(go.Pointer) && Hidden.Count == 1) Plugin.V($"[Shells] off-map trail hidden ('{go.name}')"); }
            }
            catch (Exception e) { Plugin.V("[Shells] " + e.Message); }
        }

        private static readonly HashSet<string> Seen = new();

        // ---- shell smoke parts, kept switched off every frame of the flight ----
        private class SmokeSet { public List<UnityEngine.VFX.VisualEffect> Vfx = new(); public List<ParticleSystem> Ps = new(); public List<TrailRenderer> Tr = new(); }
        private static readonly Dictionary<IntPtr, SmokeSet> SmokeParts = new();
        private static int _smokeFixes;

        private static SmokeSet CollectSmoke(GameObject go)
        {
            var s = new SmokeSet();
            foreach (var v in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                if (v != null && IsSmoke(v.gameObject.name + " " + (v.visualEffectAsset != null ? v.visualEffectAsset.name : ""))) s.Vfx.Add(v);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null && IsSmoke(Names(ps))) s.Ps.Add(ps);
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
                if (tr != null && IsSmoke(Names(tr))) s.Tr.Add(tr);
            return s;
        }

        private static void KeepOff(SmokeSet s)
        {
            foreach (var v in s.Vfx)
            {
                if (v == null || !v.enabled) continue;
                v.enabled = false;
                if (_smokeFixes++ == 0) Plugin.V($"[Shells] the game switched shell smoke back on mid-flight ('{v.gameObject.name}'); it is now kept off every frame");
            }
            foreach (var ps in s.Ps)
            {
                if (ps == null) continue;
                var em = ps.emission; if (em.enabled) { em.enabled = false; ps.Clear(false); }
                var r = ps.GetComponent<ParticleSystemRenderer>(); if (r != null && r.enabled) r.enabled = false;
            }
            foreach (var tr in s.Tr) if (tr != null && tr.enabled) { tr.enabled = false; tr.Clear(); }
        }
        private static string PartList(GameObject go)
        {
            var a = new List<string>();
            try
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    var k = new List<string>();
                    if (t.GetComponent<TrailRenderer>() != null) k.Add("trail");
                    var ps = t.GetComponent<ParticleSystem>(); if (ps != null) k.Add(ps.trails.enabled ? "particles+trails" : "particles");
                    if (t.GetComponent<LineRenderer>() != null) k.Add("line");
                    var v = t.GetComponent<UnityEngine.VFX.VisualEffect>(); if (v != null) k.Add($"vfx '{(v.visualEffectAsset != null ? v.visualEffectAsset.name : "?")}'{(v.enabled ? "" : " off")}");
                    if (t.GetComponent<MeshRenderer>() != null) k.Add("mesh");
                    if (t.GetComponent<Light>() != null) k.Add("light");
                    if (k.Count > 0) a.Add($"{t.gameObject.name}[{string.Join(",", k)}]");
                }
            }
            catch (Exception e) { a.Add("? " + e.Message); }
            return string.Join(" ", a);
        }

        // ---------- machine-gun / rifle tracers ----------
        private static readonly Dictionary<IntPtr, (bool mg, string name)> AmmoKind = new();
        private static string _ammoKey;

        private static bool IsMg(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile p, out string name)
        {
            name = "?";
            Com.DeGenerals.TankSquad.DataBase.AmmoData ad = null;
            try { ad = p._AmmoData; } catch { }
            if (ad == null) return false;
            string key = $"{Plugin.MgMaxCaliber.Value}|{Plugin.MgAmmoMatch.Value}";
            if (key != _ammoKey) { _ammoKey = key; AmmoKind.Clear(); }
            if (AmmoKind.TryGetValue(ad.Pointer, out var k)) { name = k.name; return k.mg; }
            bool mg = false;
            try { name = ad.name; } catch { }
            try
            {
                float cal = ad.Caliber;
                float mm = cal > 0f && cal < 1f ? cal * 1000f : cal;            // metres or millimetres
                name += $" ({mm:0.##} mm)";
                if (mm > 0f && mm <= Plugin.MgMaxCaliber.Value) mg = true;
            }
            catch { }
            if (!mg)
                foreach (var w in (Plugin.MgAmmoMatch.Value ?? "").Split(','))
                {
                    var t = w.Trim();
                    if (t.Length > 0 && name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) { mg = true; break; }
                }
            if (AmmoKind.Count > 256) AmmoKind.Clear();
            AmmoKind[ad.Pointer] = (mg, name);
            if (mg) Plugin.V($"[MG] '{name}' tracers: tail {(Plugin.MgRemoveTail.Value ? "removed" : "kept")}");
            return mg;
        }

        private static readonly Dictionary<IntPtr, bool> RifleGo = new();
        private static bool IsRifleTracer(Com.DeGenerals.TankSquad.Ballistics.BallisticsProjectile p, GameObject go)
        {
            try { if (p._IsInfantryWeapon) return true; } catch { }
            try { var ad = p._AmmoData; if (ad != null && ad.IsInfantryWeaponAmmo) return true; } catch { }
            if (RifleGo.TryGetValue(go.Pointer, out var r)) return r;
            r = false;
            string n = go.name ?? "";
            foreach (var w in (Plugin.MgTracerMatch.Value ?? "").Split(','))
            {
                var t = w.Trim();
                if (t.Length > 0 && n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) { r = true; break; }
            }
            if (RifleGo.Count > 512) RifleGo.Clear();
            RifleGo[go.Pointer] = r;
            if (r) Plugin.V($"[MG] tracer '{n}' counts as MG / rifle tracer (name)");
            return r;
        }

        private static bool IsTailName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            foreach (var w in (Plugin.MgTailMatch.Value ?? "").Split(','))
            {
                var t = w.Trim();
                if (t.Length > 0 && n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // Game values remembered per component, so the tail comes back exactly when the switch is turned off.
        private static readonly Dictionary<IntPtr, bool> OrigPsTrail = new();
        private static readonly Dictionary<IntPtr, bool> OrigEnabled = new();

        // The parts a tail is made of, per pooled tracer effect.
        private sealed class Parts
        {
            public List<TrailRenderer> Trails = new(); public List<ParticleSystem> PsTrails = new();
            public List<Renderer> Rends = new(); public List<UnityEngine.VFX.VisualEffect> Vfx = new();
        }
        private static readonly Dictionary<IntPtr, Parts> TailParts = new();

        private static Parts Collect(GameObject go)
        {
            var t = new Parts();
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) if (tr != null) t.Trails.Add(tr);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                t.PsTrails.Add(ps);
                if (IsTailName(Names(ps))) { var r = ps.GetComponent<ParticleSystemRenderer>(); if (r != null) t.Rends.Add(r); }
            }
            foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true)) if (lr != null && IsTailName(Names(lr))) t.Rends.Add(lr);
            foreach (var v in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                if (v != null && IsTailName(v.gameObject.name + " " + (v.visualEffectAsset != null ? v.visualEffectAsset.name : ""))) t.Vfx.Add(v);
            return t;
        }

        private static void Enforce(Parts t)
        {
            try
            {
                foreach (var tr in t.Trails) if (tr != null && tr.enabled) { tr.enabled = false; tr.Clear(); }
                foreach (var ps in t.PsTrails) if (ps != null) { var m = ps.trails; if (m.enabled) m.enabled = false; }
                foreach (var r in t.Rends) if (r != null && r.enabled) r.enabled = false;
                foreach (var v in t.Vfx) if (v != null && v.enabled) v.enabled = false;
            }
            catch { }
        }

        private static void Tail(GameObject go, bool show)
        {
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (tr == null) continue;
                if (!OrigEnabled.ContainsKey(tr.Pointer)) OrigEnabled[tr.Pointer] = tr.enabled;
                tr.enabled = show && OrigEnabled[tr.Pointer];
                if (!show) tr.Clear();
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var trails = ps.trails;                                       // the streak behind each particle
                if (!OrigPsTrail.ContainsKey(ps.Pointer)) OrigPsTrail[ps.Pointer] = trails.enabled;
                trails.enabled = show && OrigPsTrail[ps.Pointer];
                if (IsTailName(Names(ps)))                                    // a whole particle system that is the tail
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null) { if (!OrigEnabled.ContainsKey(r.Pointer)) OrigEnabled[r.Pointer] = r.enabled; r.enabled = show && OrigEnabled[r.Pointer]; }
                    var em = ps.emission; em.enabled = show;
                    if (!show) ps.Clear(false);
                }
            }
            foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr == null || !IsTailName(Names(lr))) continue;
                if (!OrigEnabled.ContainsKey(lr.Pointer)) OrigEnabled[lr.Pointer] = lr.enabled;
                lr.enabled = show && OrigEnabled[lr.Pointer];
            }
            foreach (var vfx in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            {
                if (vfx == null) continue;
                if (IsTailName(vfx.gameObject.name + " " + (vfx.visualEffectAsset != null ? vfx.visualEffectAsset.name : "")))
                { if (!OrigEnabled.ContainsKey(vfx.Pointer)) OrigEnabled[vfx.Pointer] = vfx.enabled; vfx.enabled = show && OrigEnabled[vfx.Pointer]; continue; }
                // tracer graphs often have their own trail switch / length
                foreach (var b in new[] { "Trail", "Use Trail", "Enable Trail", "Show Trail", "Trails" }) VfxBool(vfx, b, false, !show);
                foreach (var b in new[] { "Turn Off Trail", "Disable Trail", "Hide Trail" }) VfxBool(vfx, b, true, !show);
                foreach (var f in new[] { "Trail Length", "TrailLength", "Trail Width", "TrailWidth", "Trail Lifetime", "Trail Alpha" }) VfxFloat(vfx, f, show ? 1f : 0f);
            }
        }

        internal static void Apply(GameObject go, bool noSmoke, float k, string match = null, float glow = 1f, float smokeAlpha = 1f)
        {
            _match = match;
            try { ApplyInner(go, noSmoke, k, glow, smokeAlpha); } finally { _match = null; }
        }

        private static readonly Dictionary<IntPtr, (Color a, Color b)> OrigTrail = new();
        private static readonly Dictionary<IntPtr, float> OrigIntensity = new();

        // v <= 1: opacity (alpha x v). v > 1: brighter (colour x v, alpha unchanged).
        private static Color Mul(Color c, float v) => v <= 1f ? new Color(c.r, c.g, c.b, c.a * v) : new Color(c.r * v, c.g * v, c.b * v, c.a);

        // A gradient copied into plain managed data the moment it is read. Game gradient objects are never kept:
        // holding on to them and reading them later can hit freed memory.
        internal sealed class GradData
        {
            public GradientMode Mode;
            public Color[] C; public float[] CT;
            public float[] A; public float[] AT;

            public static GradData From(Gradient g)
            {
                if (g == null) return null;
                var d = new GradData { Mode = g.mode };
                var ck = g.colorKeys; var ak = g.alphaKeys;
                int nc = ck == null ? 0 : ck.Length, na = ak == null ? 0 : ak.Length;
                d.C = new Color[nc]; d.CT = new float[nc]; d.A = new float[na]; d.AT = new float[na];
                for (int i = 0; i < nc; i++) { d.C[i] = ck[i].color; d.CT[i] = ck[i].time; }
                for (int i = 0; i < na; i++) { d.A[i] = ak[i].alpha; d.AT[i] = ak[i].time; }
                return d;
            }

            // Write the scaled original keys into a gradient the game just handed us (its own arrays, edited in place).
            // v <= 1: alpha x v. v > 1: colour x v. Returns false if the shape does not match.
            public bool ApplyTo(Gradient g, float v)
            {
                if (g == null) return false;
                var ck = g.colorKeys; var ak = g.alphaKeys;
                if (ck == null || ak == null || ck.Length != C.Length || ak.Length != A.Length) return false;
                for (int i = 0; i < C.Length; i++)
                {
                    var c = C[i];
                    if (v > 1f) c = new Color(c.r * v, c.g * v, c.b * v, c.a);
                    ck[i] = new GradientColorKey(c, CT[i]);
                }
                for (int i = 0; i < A.Length; i++) ak[i] = new GradientAlphaKey(v <= 1f ? A[i] * v : A[i], AT[i]);
                g.SetKeys(ck, ak);
                return true;
            }
        }

        private sealed class MinMaxData
        {
            public ParticleSystemGradientMode Mode;
            public Color CMin, CMax;
            public GradData GMin, GMax;
        }

        private static readonly Dictionary<IntPtr, MinMaxData> OrigColor = new();

        private static MinMaxData Read(ParticleSystem.MinMaxGradient g)
        {
            var d = new MinMaxData { Mode = g.mode };
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: d.CMax = g.colorMax; break;
                case ParticleSystemGradientMode.TwoColors: d.CMin = g.colorMin; d.CMax = g.colorMax; break;
                case ParticleSystemGradientMode.TwoGradients: d.GMin = GradData.From(g.gradientMin); d.GMax = GradData.From(g.gradientMax); break;
                default: d.GMax = GradData.From(g.gradientMax); break;
            }
            return d;
        }

        private static void SetOpacity(ParticleSystem ps, float v)
        {
            var main = ps.main;
            if (!OrigColor.TryGetValue(ps.Pointer, out var d)) { d = Read(main.startColor); OrigColor[ps.Pointer] = d; }
            var g = main.startColor;          // fresh copy; only its values are replaced
            switch (d.Mode)
            {
                case ParticleSystemGradientMode.Color: g.colorMax = Mul(d.CMax, v); break;
                case ParticleSystemGradientMode.TwoColors: g.colorMin = Mul(d.CMin, v); g.colorMax = Mul(d.CMax, v); break;
                case ParticleSystemGradientMode.TwoGradients:
                    { var a = g.gradientMin; if (d.GMin != null && d.GMin.ApplyTo(a, v)) g.gradientMin = a; }
                    { var b = g.gradientMax; if (d.GMax != null && d.GMax.ApplyTo(b, v)) g.gradientMax = b; }
                    break;
                default: { var b = g.gradientMax; if (d.GMax != null && d.GMax.ApplyTo(b, v)) g.gradientMax = b; } break;
            }
            main.startColor = g;
        }

        private static void SetOpacity(TrailRenderer tr, float v)
        {
            if (!OrigTrail.ContainsKey(tr.Pointer)) OrigTrail[tr.Pointer] = (tr.startColor, tr.endColor);
            var o = OrigTrail[tr.Pointer];
            tr.startColor = Mul(o.a, v); tr.endColor = Mul(o.b, v);
        }

        private static void ApplyInner(GameObject go, bool noSmoke, float k, float glow, float smokeAlpha)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var main = ps.main;
                if (!OrigSize.ContainsKey(ps.Pointer)) OrigSize[ps.Pointer] = main.startSizeMultiplier;
                bool smoke = IsSmoke(Names(ps));
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (smoke)
                {
                    bool off = noSmoke || smokeAlpha <= 0f;
                    if (r != null) r.enabled = !off;
                    var em = ps.emission; em.enabled = !off;
                    if (off) ps.Clear(false);
                    continue;
                }
                main.startSizeMultiplier = OrigSize[ps.Pointer] * k;
            }

            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (tr == null) continue;
                if (!OrigWidth.ContainsKey(tr.Pointer)) OrigWidth[tr.Pointer] = tr.widthMultiplier;
                if (IsSmoke(Names(tr)))
                {
                    bool off = noSmoke || smokeAlpha <= 0f;
                    tr.enabled = !off; if (off) tr.Clear();
                    continue;
                }
                tr.widthMultiplier = OrigWidth[tr.Pointer] * k;
            }

            foreach (var l in go.GetComponentsInChildren<Light>(true))
            {
                if (l == null) continue;
                if (!OrigRange.ContainsKey(l.Pointer)) OrigRange[l.Pointer] = l.range;
                if (!OrigIntensity.ContainsKey(l.Pointer)) OrigIntensity[l.Pointer] = l.intensity;
                l.range = OrigRange[l.Pointer] * k;
                l.intensity = OrigIntensity[l.Pointer] * glow;
            }

            foreach (var vfx in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            {
                if (vfx == null) continue;
                var t = vfx.transform;
                if (IsSmoke(vfx.gameObject.name + " " + (vfx.visualEffectAsset != null ? vfx.visualEffectAsset.name : "")))
                { vfx.enabled = !(noSmoke || smokeAlpha <= 0f); continue; }
                if (!OrigScale.ContainsKey(t.Pointer)) OrigScale[t.Pointer] = t.localScale;
                t.localScale = OrigScale[t.Pointer] * k;
                // Trails that carry their smoke inside the same effect (e.g. rockets) have a switch for it.
                VfxBool(vfx, "Turn Off Smoke", true, noSmoke || smokeAlpha <= 0f);
            }
        }

        // VerboseLog: one description per tracer prefab, so the parts can be matched by name.
        internal static void Describe(GameObject go, string what = "tracer", string match = null)
        {
            _match = match;
            try { DescribeInner(go, what); } finally { _match = null; }
        }

        private static readonly List<UnityEngine.VFX.VisualEffect> VfxDetails = new();

        // ---- VFX Graph exposed settings, always set from the game's original value ----
        private static readonly Dictionary<(IntPtr, string), float> OrigF = new();
        private static readonly Dictionary<(IntPtr, string), Vector2> OrigV2 = new();
        private static readonly Dictionary<(IntPtr, string), bool> OrigB = new();

        internal static void VfxFloat(UnityEngine.VFX.VisualEffect v, string name, float mul, float min = 0f)
        {
            try
            {
                if (!v.HasFloat(name)) return;
                var key = (v.Pointer, name);
                if (!OrigF.ContainsKey(key)) OrigF[key] = v.GetFloat(name);
                v.SetFloat(name, Math.Max(min, OrigF[key] * mul));
            }
            catch { }
        }

        internal static void VfxVec2(UnityEngine.VFX.VisualEffect v, string name, float mul)
        {
            try
            {
                if (!v.HasVector2(name)) return;
                var key = (v.Pointer, name);
                if (!OrigV2.ContainsKey(key)) OrigV2[key] = v.GetVector2(name);
                v.SetVector2(name, OrigV2[key] * mul);
            }
            catch { }
        }

        private static readonly Dictionary<(IntPtr, string), GradData> OrigG = new();

        // Opacity of a colour gradient setting (alpha x mul).
        internal static void VfxAlpha(UnityEngine.VFX.VisualEffect v, string name, float mul)
        {
            try
            {
                if (!v.HasGradient(name)) return;
                var key = (v.Pointer, name);
                if (!OrigG.TryGetValue(key, out var d)) { d = GradData.From(v.GetGradient(name)); OrigG[key] = d; }
                if (d == null) return;
                var g = v.GetGradient(name);                 // fresh copy from the game
                if (d.ApplyTo(g, Math.Min(1f, mul))) v.SetGradient(name, g);
            }
            catch { }
        }

        internal static void VfxBool(UnityEngine.VFX.VisualEffect v, string name, bool on, bool force)
        {
            try
            {
                if (!v.HasBool(name)) return;
                var key = (v.Pointer, name);
                if (!OrigB.ContainsKey(key)) OrigB[key] = v.GetBool(name);
                v.SetBool(name, force ? on : OrigB[key]);
            }
            catch { }
        }
        internal static void ResetLog() => Logged.Clear();

        private static void DescribeVfx(UnityEngine.VFX.VisualEffect v)
        {
            try
            {
                var sys = new Il2CppSystem.Collections.Generic.List<string>();
                v.GetParticleSystemNames(sys);
                var a = new List<string>(); for (int i = 0; i < sys.Count; i++) a.Add(sys[i]);
                var sp = new Il2CppSystem.Collections.Generic.List<string>();
                v.GetSpawnSystemNames(sp);
                var b = new List<string>(); for (int i = 0; i < sp.Count; i++) b.Add(sp[i]);
                Plugin.L.LogDebug($"[Shells]     '{v.gameObject.name}' particle systems: {string.Join(", ", a)}; spawners: {string.Join(", ", b)}");
            }
            catch (Exception e) { Plugin.L.LogDebug("[Shells]     systems: " + e.Message); }
            try
            {
                var asset = v.visualEffectAsset;
                if (asset == null) return;
                var props = new Il2CppSystem.Collections.Generic.List<UnityEngine.VFX.VFXExposedProperty>();
                asset.GetExposedProperties(props);
                var a = new List<string>();
                for (int i = 0; i < props.Count; i++)
                {
                    var pr = props[i];
                    string val = "";
                    try { if (pr.type != null && pr.type.Name == "Single") val = "=" + v.GetFloat(pr.name).ToString("0.###"); } catch { }
                    a.Add($"{pr.name} ({pr.type?.Name}){val}");
                }
                Plugin.L.LogDebug($"[Shells]     '{v.gameObject.name}' settings: {(a.Count == 0 ? "none" : string.Join(", ", a))}");
            }
            catch (Exception e) { Plugin.L.LogDebug("[Shells]     settings: " + e.Message); }
        }

        private static void DescribeInner(GameObject go, string what)
        {
            VfxDetails.Clear();
            DescribeParts(go, what);
            foreach (var v in VfxDetails) DescribeVfx(v);
        }

        private static void DescribeParts(GameObject go, string what)
        {
            var L = Plugin.L;
            L.LogDebug($"[Shells] {what} '{go.name}':");
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var parts = new List<string>();
                try { var ps = t.GetComponent<ParticleSystem>(); if (ps != null) { var m = ps.main; parts.Add($"particles size x{m.startSizeMultiplier:0.###} life {m.startLifetimeMultiplier:0.##}s{(ps.trails.enabled ? " + particle trails" : "")}"); } } catch { }
                try { var tr = t.GetComponent<TrailRenderer>(); if (tr != null) parts.Add($"trail width x{tr.widthMultiplier:0.###} time {tr.time:0.##}s"); } catch { }
                try { var l = t.GetComponent<Light>(); if (l != null) parts.Add($"light range {l.range:0.##} intensity {l.intensity:0.##}"); } catch { }
                try { var v = t.GetComponent<UnityEngine.VFX.VisualEffect>(); if (v != null) { parts.Add($"vfx graph '{(v.visualEffectAsset != null ? v.visualEffectAsset.name : "?")}'"); VfxDetails.Add(v); } } catch { }
                try { var mr = t.GetComponent<MeshRenderer>(); if (mr != null) parts.Add("mesh"); } catch { }
                try { var lr = t.GetComponent<LineRenderer>(); if (lr != null) parts.Add("line"); } catch { }
                try
                {
                    var r = t.GetComponent<Renderer>();
                    if (r != null && r.sharedMaterial != null)
                        parts.Add($"material '{r.sharedMaterial.name}' shader '{(r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "?")}'");
                }
                catch { }
                string smoke = IsSmoke(t.gameObject.name + " " + string.Join(" ", parts)) ? "  <- smoke" : "";
                L.LogDebug($"[Shells]   {t.gameObject.name}{(parts.Count > 0 ? ": " + string.Join(", ", parts) : "")}{smoke}");
            }
        }
    }
}

namespace TankSquadPotatoVFX
{
    // Your tank's firing effects (muzzle blast / barrel smoke) come from the gun's on-shoot effect spawners. Every few
    // frames the effects those spawners currently have out are checked and their smoke parts switched off.
    internal static class MuzzleFx
    {
        private static IntPtr _vehicle;
        private static readonly List<VFXSpawner> Spawners = new();
        private static readonly HashSet<string> Logged = new();
        private static readonly Dictionary<IntPtr, string> Applied = new();
        internal static void ResetLog() => Logged.Clear();
        private static float _next;
        private static bool _announced;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.05f;
            try
            {
                var v = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
                if (v == null) { _vehicle = IntPtr.Zero; Spawners.Clear(); return; }
                if (v.Pointer != _vehicle) { _vehicle = v.Pointer; Applied.Clear(); Collect(v); }
                if (Spawners.Count == 0) return;

                bool front = Plugin.FrontSmoke.Value;
                foreach (var sp in Spawners)
                {
                    if (sp == null) continue;
                    var list = sp._SpawnedVFXGOs;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var go = list[i];
                        if (go == null || !go.activeInHierarchy) continue;
                        if (!_announced && Plugin.VerboseLog.Value) { _announced = true; Plugin.L.LogDebug($"[Shells] firing effects found (first: '{go.name}')"); }
                        if (Plugin.VerboseLog.Value && Logged.Add(go.name)) ShellFx.Describe(go, "firing effect", "smoke");
                        bool side = Plugin.SideSmoke.Value;
                        string key = $"{front}|{side}";
                        if (Applied.TryGetValue(go.Pointer, out var was) && was == key) continue;
                        Applied[go.Pointer] = key;
                        foreach (var vfx in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                        {
                            if (vfx == null) continue;
                            var pr = Props(vfx);
                            if (pr.Front.Count == 0 && pr.Side.Count == 0) continue;
                            vfx.enabled = true;   // (older versions could switch the whole blast off)
                            // Only the front and side smoke are switched; flash and everything else stay as the game has it.
                            foreach (var n in pr.Front) Set(vfx, n, front ? 1f : 0f);
                            foreach (var n in pr.Side) Set(vfx, n, side ? 1f : 0f);
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.V("[Shells] muzzle: " + e.Message); }
        }

        // Which exposed settings of a firing effect control its front / side smoke. The game used
        // "FRONT Particle Amount" / "SIDE Particle Amount"; if those are gone (game update), any exposed
        // setting with front/side plus amount/count/rate/spawn/smoke/particle in its name is used.
        private class P { public List<string> Front = new(), Side = new(); }
        private static readonly Dictionary<string, P> PropCache = new();

        private static P Props(UnityEngine.VFX.VisualEffect vfx)
        {
            string asset = "?";
            try { asset = vfx.visualEffectAsset != null ? vfx.visualEffectAsset.name : "?"; } catch { }
            if (PropCache.TryGetValue(asset, out var c)) return c;
            var p = new P();
            var names = new List<string>();
            string how = "listed";
            try
            {
                var l = new Il2CppSystem.Collections.Generic.List<UnityEngine.VFX.VFXExposedProperty>();
                vfx.visualEffectAsset.GetExposedProperties(l);
                for (int i = 0; i < l.Count; i++) { try { names.Add(l[i].name); } catch { } }
            }
            catch (Exception e) { how = "could not list (" + e.GetType().Name + ")"; }
            foreach (var n in new[] { "FRONT Particle Amount", "SIDE Particle Amount", "Front Particle Amount", "Side Particle Amount",
                                      "FRONT Particles Amount", "SIDE Particles Amount", "FRONT Smoke Amount", "SIDE Smoke Amount",
                                      "FRONT Amount", "SIDE Amount", "FRONT Spawn Rate", "SIDE Spawn Rate", "FRONT Particle Count", "SIDE Particle Count" })
                if (!names.Contains(n)) names.Add(n);
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || !Has(vfx, n)) continue;
                var low = n.ToLowerInvariant();
                bool qty = low.Contains("amount") || low.Contains("count") || low.Contains("rate") || low.Contains("spawn") || low.Contains("smoke") || low.Contains("particle");
                if (!qty) continue;
                if (low.Contains("front")) p.Front.Add(n);
                else if (low.Contains("side")) p.Side.Add(n);
            }
            PropCache[asset] = p;
            var have = new List<string>(); foreach (var n in names) if (Has(vfx, n)) have.Add(n);
            Plugin.V($"[Shells] firing effect '{asset}': settings {how}: {(have.Count > 0 ? string.Join(", ", have) : "(none found)")} | front smoke: {(p.Front.Count > 0 ? string.Join(", ", p.Front) : "-")} | side smoke: {(p.Side.Count > 0 ? string.Join(", ", p.Side) : "-")}");
            return p;
        }

        private static bool Has(UnityEngine.VFX.VisualEffect v, string n)
        {
            try { return v.HasFloat(n) || v.HasInt(n) || v.HasUInt(n); } catch { return false; }
        }

        private static readonly Dictionary<(IntPtr, string), int> OrigI = new();
        private static void Set(UnityEngine.VFX.VisualEffect v, string n, float mul)
        {
            try
            {
                if (v.HasFloat(n)) { ShellFx.VfxFloat(v, n, mul); return; }
                if (v.HasInt(n)) { var k = (v.Pointer, n); if (!OrigI.ContainsKey(k)) OrigI[k] = v.GetInt(n); v.SetInt(n, (int)Math.Round(OrigI[k] * mul)); return; }
                if (v.HasUInt(n)) { var k = (v.Pointer, n); if (!OrigI.ContainsKey(k)) OrigI[k] = (int)v.GetUInt(n); v.SetUInt(n, (uint)Math.Round(OrigI[k] * mul)); }
            }
            catch { }
        }

        private static void Collect(Com.DeGenerals.TankSquad.Vehicle.VehicleSettings v)
        {
            Spawners.Clear();
            try
            {
                foreach (var wf in v.GetComponentsInChildren<Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire>(true))
                {
                    var arr = wf?.model?.OnShootVFXSpawners;
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++) if (arr[i] != null) Spawners.Add(arr[i]);
                }
                Plugin.V($"[Shells] your tank has {Spawners.Count} firing effect spawner(s)");
            }
            catch (Exception e) { Plugin.V("[Shells] collect: " + e.Message); }
        }
    }
}



namespace TankSquadPotatoVFX
{
    // Machine-gun / rifle tracers (DT, DP-27, MG34 ...) are not made by the projectile code the shell section hooks:
    // the MGs use their own fast path. Their tracer is a copy of a prefab (P_VFX_Tracer_SOV_Rifle, P_VFX_Tracer_GER_Rifle),
    // so the tail is switched off on the loaded prefab itself (every copy made from it inherits that) and on the copies
    // already in the pool. Looked for every few seconds until found, then every 30 s for newly loaded ones.
    internal static class RifleTails
    {
        private static float _next;
        private static string _key;
        private static readonly Dictionary<IntPtr, Component> Off = new();     // what we switched off (to switch back)
        private static int _lastCount = -1;
        private static readonly HashSet<string> Parts = new();

        private static bool Has(string n, string list)
        {
            if (string.IsNullOrEmpty(n)) return false;
            foreach (var w in (list ?? "").Split(','))
            {
                var t = w.Trim();
                if (t.Length > 0 && n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // A part of a rifle tracer: some parent up the chain is named like 'P_VFX_Tracer_..._Rifle'.
        private static bool InRifleTracer(Transform t)
        {
            for (int i = 0; t != null && i < 5; i++, t = t.parent)
            {
                var n = t.gameObject.name;
                if (n.IndexOf("Tracer", StringComparison.OrdinalIgnoreCase) >= 0 && Has(n, Plugin.MgTracerMatch.Value)) return true;
            }
            return false;
        }

        internal static void Tick()
        {
            string key = $"{Plugin.MgRemoveTail.Value}|{Plugin.MgTracerMatch.Value}|{Plugin.MgTailParts.Value}|{Plugin.MgTweakParts.Value}|{Plugin.MgTailSettings.Value}";
            if (key != _key) { _key = key; _next = 0f; RestoreAll(); Parts.Clear(); }     // settings changed: back to the game's, then re-apply
            if (!Plugin.MgRemoveTail.Value || Time.unscaledTime < _next) return;
            int found = 0;
            try
            {
                foreach (var v in Resources.FindObjectsOfTypeAll<UnityEngine.VFX.VisualEffect>())
                {
                    if (v == null || !InRifleTracer(v.transform)) continue;
                    string n = v.gameObject.name + " " + (v.visualEffectAsset != null ? v.visualEffectAsset.name : "");
                    if (Has(v.gameObject.name, Plugin.MgTweakParts.Value)) { if (Tweak(v)) found++; continue; }   // glow + tail in one: adjust, never switch off
                    bool tail = Has(v.gameObject.name, Plugin.MgTailParts.Value);
                    string root = v.transform.parent != null ? v.transform.parent.gameObject.name : "-";
                    if (Parts.Count < 60 && Parts.Add(root + "/" + n)) Plugin.V($"[MG] rifle tracer part: '{root}' / '{v.gameObject.name}' vfx '{(v.visualEffectAsset != null ? v.visualEffectAsset.name : "?")}' -> {(tail ? "tail, switched off" : "kept (glow)")}");
                    if (!tail) continue;
                    found++;
                    if (v.enabled) { v.enabled = false; Off[v.Pointer] = v; }
                }
                foreach (var tr in Resources.FindObjectsOfTypeAll<TrailRenderer>())
                {
                    if (tr == null || !InRifleTracer(tr.transform) || !Has(tr.gameObject.name, Plugin.MgTailParts.Value)) continue;
                    found++;
                    if (tr.enabled) { tr.enabled = false; Off[tr.Pointer] = tr; }
                }
            }
            catch (Exception e) { Plugin.V("[MG] " + e.Message); }
            _next = Time.unscaledTime + (found > 0 ? 30f : 10f);
            if (found != _lastCount)
            {
                _lastCount = found;
                if (found > 0) Plugin.V($"[MG] MG tracer tails switched off ({Plugin.MgTailParts.Value}): {found} part(s) on the loaded tracer prefabs and their pooled copies");
            }
        }

        // ---- settings inside a VFX graph (the tail of a glow-and-tail effect) ----
        private static readonly Dictionary<(IntPtr, string), object> OrigVal = new();
        private static readonly HashSet<string> DescribedAssets = new();

        private static bool TailName(string n)
        {
            n = n.ToLowerInvariant();
            bool trail = n.Contains("trail") || n.Contains("tail") || n.Contains("ribbon") || n.Contains("strip");
            bool amount = n.Contains("length") || n.Contains("life") || n.Contains("width") || n.Contains("count") || n.Contains("rate") || n.Contains("spawn") || n.Contains("size") || n.Contains("alpha");
            return trail && amount;
        }

        // Returns true if anything was changed on this effect.
        private static bool Tweak(UnityEngine.VFX.VisualEffect v)
        {
            var asset = v.visualEffectAsset;
            if (asset == null) return false;
            var props = new Il2CppSystem.Collections.Generic.List<UnityEngine.VFX.VFXExposedProperty>();
            try { asset.GetExposedProperties(props); } catch { return false; }

            // Once per graph: every setting it exposes, with its value (to pick the right one by name).
            if (DescribedAssets.Add(asset.name))
            {
                var a = new List<string>();
                for (int i = 0; i < props.Count; i++)
                {
                    var pr = props[i]; string tn = pr.type?.Name ?? "?", val = "";
                    try
                    {
                        if (tn == "Single") val = "=" + v.GetFloat(pr.name).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                        else if (tn == "Int32") val = "=" + v.GetInt(pr.name);
                        else if (tn == "Boolean") val = "=" + v.GetBool(pr.name);
                        else if (tn == "Vector2") val = "=" + v.GetVector2(pr.name);
                    }
                    catch { }
                    a.Add($"{pr.name} ({tn}){val}");
                }
                Plugin.V($"[MG] settings of '{asset.name}': {(a.Count == 0 ? "none exposed" : string.Join(", ", a))}");
            }

            var want = new List<(string name, string val)>();
            string cfg = (Plugin.MgTailSettings.Value ?? "").Trim();
            if (cfg.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < props.Count; i++)
                {
                    var pr = props[i]; string tn = pr.type?.Name ?? "";
                    if (!TailName(pr.name)) continue;
                    if (tn == "Single" || tn == "Int32") want.Add((pr.name, "0"));
                    else if (tn == "Boolean") want.Add((pr.name, "false"));
                }
            }
            else foreach (var part in cfg.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) want.Add((part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }

            bool changed = false; var done = new List<string>();
            foreach (var (name, val) in want)
            {
                try
                {
                    var k = (v.Pointer, name);
                    if (v.HasFloat(name) && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    { if (!OrigVal.ContainsKey(k)) OrigVal[k] = v.GetFloat(name); v.SetFloat(name, f); changed = true; done.Add($"{name}={f}"); }
                    else if (v.HasInt(name) && int.TryParse(val, out var iv))
                    { if (!OrigVal.ContainsKey(k)) OrigVal[k] = v.GetInt(name); v.SetInt(name, iv); changed = true; done.Add($"{name}={iv}"); }
                    else if (v.HasBool(name) && bool.TryParse(val, out var b))
                    { if (!OrigVal.ContainsKey(k)) OrigVal[k] = v.GetBool(name); v.SetBool(name, b); changed = true; done.Add($"{name}={b}"); }
                }
                catch { }
            }
            if (changed) { Tweaked[v.Pointer] = v; if (TweakLogged.Add(asset.name + "|" + string.Join(",", done))) Plugin.V($"[MG] '{asset.name}' tail settings: {string.Join(", ", done)}"); }
            else if (want.Count == 0 && TweakLogged.Add(asset.name + "|none")) Plugin.V($"[MG] '{asset.name}': no tail setting found ({(cfg.Length == 0 ? "TailSettings empty" : cfg)}) - see the settings line above");
            return changed;
        }
        private static readonly Dictionary<IntPtr, UnityEngine.VFX.VisualEffect> Tweaked = new();
        private static readonly HashSet<string> TweakLogged = new();

        private static void RestoreTweaks()
        {
            foreach (var kv in OrigVal)
            {
                try
                {
                    if (!Tweaked.TryGetValue(kv.Key.Item1, out var v) || v == null) continue;
                    switch (kv.Value)
                    {
                        case float f: v.SetFloat(kv.Key.Item2, f); break;
                        case int i: v.SetInt(kv.Key.Item2, i); break;
                        case bool b: v.SetBool(kv.Key.Item2, b); break;
                    }
                }
                catch { }
            }
            OrigVal.Clear(); Tweaked.Clear(); TweakLogged.Clear();
        }

        private static void RestoreAll()
        {
            RestoreTweaks();
            foreach (var c in Off.Values)
            {
                try
                {
                    if (c == null) continue;
                    var v = c.TryCast<UnityEngine.VFX.VisualEffect>(); if (v != null) { v.enabled = true; continue; }
                    var tr = c.TryCast<TrailRenderer>(); if (tr != null) { tr.enabled = true; continue; }
                    var r = c.TryCast<Renderer>(); if (r != null) r.enabled = true;
                }
                catch { }
            }
            Off.Clear(); _lastCount = -1;
        }
    }
}
