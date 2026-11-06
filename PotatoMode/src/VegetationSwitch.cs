// PotatoMode - vegetation switches (opt-in, everything off by default)
//
// For low-end PCs where vegetation itself is the cost. Works on the GPU Instancer managers
// (tree manager, terrain detail manager, prefab managers):
//   - lists every vegetation type (prototype) per manager with its instance count;
//   - HideTypes: switches chosen types off in the renderer (no mesh, no shadow, GPU buffers freed);
//   - HideAllDetails: switches off everything the terrain detail manager draws (grass, bushes, small plants);
//   - DetailDistance / DetailUpdateDistance / DetailDensityReduction: the detail manager's own settings
//     (separate from Unity's terrain detail distance in [Terrain], which GPUI does not use);
//     at -1 the game's value is never touched - only a value the plugin changed is put back;
//   (WarmupShaders was removed in 1.3.1: Shader.WarmupAllShaders crashed the game on a player's PC.)
// Everything is restored when the setting is cleared. Called from GpuiTweaks' scan schedule.

using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using GPUInstancerPro;
using GPUInstancerPro.TerrainModule;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PotatoMode
{
    internal static class VegetationSwitch
    {
        const string Section = "GPUI Vegetation";
        static ManualLogSource _log;

        static ConfigEntry<bool> _list, _hideAllDetails;
        static ConfigEntry<string> _hideTypes;
        static ConfigEntry<float> _detailDistance, _detailUpdateDistance;
        static ConfigEntry<int> _densityReduction;

        static readonly HashSet<string> _listed = new();
        static readonly Dictionary<(IntPtr mgr, int idx), bool> _offByUs = new();
        // The game's own detail values, recorded just before the plugin first changes them (per detail manager).
        // A setting at -1 never touches the game's value; it only puts back what the plugin changed itself.
        static readonly Dictionary<IntPtr, float> _origDist = new(), _origUpd = new();
        static readonly Dictionary<IntPtr, bool> _origDensity = new();

        public static void Init(ConfigFile cfg, ManualLogSource log)
        {
            _log = log;
            _list = cfg.Bind(Section, "ListVegetationTypes", false,
                "Log every vegetation type each GPU Instancer manager draws (trees, terrain details such as grass and bushes, objects), " +
                "with its number, name and instance count - lines starting [Vegetation]. Use the names for HideTypes. Needs [Debug] VerboseLog = true.");
            _hideTypes = cfg.Bind(Section, "HideTypes", "",
                "OPT-IN, low-end PCs only. Vegetation types NOT to draw at all, by part of the name, comma separated, case-insensitive " +
                "(e.g. bush, shrub, fern). The type is switched off in the renderer itself: no mesh, no shadow, GPU buffers freed. " +
                "This breaks the 'never change what is drawn' rule of this section on purpose: bushes still hide units from the AI " +
                "(the AI uses separate visibility volumes), you simply see through them. Empty = nothing hidden.");
            _hideAllDetails = cfg.Bind(Section, "HideAllDetails", false,
                "OPT-IN, low-end PCs only. true = draw nothing from the terrain detail manager (grass, bushes, small plants). Biggest vegetation saving.");
            _detailDistance = cfg.Bind(Section, "DetailDistance", -1f,
                "Draw distance (m) of the GPU Instancer terrain detail manager. This is NOT the same as [Terrain] DetailDistance (Unity's own, " +
                "which GPUI ignores) - that is why details can still show when everything else is off. -1 = leave. Shorter = fewer details drawn.");
            _detailUpdateDistance = cfg.Bind(Section, "DetailUpdateDistance", -1f,
                "How far (m) the camera must move before GPU Instancer rebuilds the details around it. Each rebuild is a burst of GPU work " +
                "(a spike while driving). -1 = leave. Higher = fewer rebuilds; too high and details thin out at the edge before the next rebuild.");
            _densityReduction = cfg.Bind(Section, "DetailDensityReduction", -1,
                new ConfigDescription("GPU Instancer's distance density reduction for details (fewer details further away). -1 = leave, 0 = off, 1 = on.",
                    new AcceptableValueRange<int>(-1, 1)));
        }

        static string ProtoName(GPUIPrototype p)
        {
            string n = null;
            try { n = p.name; } catch { }
            try { if (string.IsNullOrWhiteSpace(n) || n == GPUIPrototype.DEFAULT_PROTOTYPE_NAME) n = p.prefabObject != null ? p.prefabObject.name : n; } catch { }
            try { if (string.IsNullOrWhiteSpace(n)) n = p.prototypeMesh != null ? p.prototypeMesh.name : "?"; } catch { }
            return n ?? "?";
        }

        static string Kind(GPUIManager m)
        {
            try { if (m.TryCast<GPUIDetailManager>() != null) return "details"; } catch { }
            try { if (m.TryCast<GPUITreeManager>() != null) return "trees"; } catch { }
            return "objects";
        }

        // active = false ([GPUI Vegetation] Enabled = false): only put back what the plugin changed, then leave the game alone.
        public static void Scan(bool active = true)
        {
            var names = new List<string>();
            if (active)
                foreach (var part in (_hideTypes.Value ?? "").Split(','))
                { var t = part.Trim().ToLowerInvariant(); if (t.Length > 0) names.Add(t); }
            if (!active && _offByUs.Count == 0 && _origDist.Count == 0 && _origUpd.Count == 0 && _origDensity.Count == 0) return;

            var mgrs = UnityEngine.Object.FindObjectsOfType<GPUIManager>();
            if (mgrs == null) return;
            for (int mi = 0; mi < mgrs.Length; mi++)
            {
                var m = mgrs[mi]; if (m == null) continue;
                IntPtr mp = m.Pointer;
                string kind = Kind(m);
                int count; try { count = m.GetPrototypeCount(); } catch { continue; }

                if (kind == "details") DetailSettings(m.TryCast<GPUIDetailManager>(), mp, active);

                StringBuilder sb = null;
                if (active && _list.Value && _listed.Add($"{mp}:{count}:{SceneManager.GetActiveScene().handle}"))
                    sb = new StringBuilder($"[Vegetation] {kind} manager '{m.name}': {count} types");
                for (int i = 0; i < count; i++)
                {
                    GPUIPrototype p = null; try { p = m.GetPrototype(i); } catch { }
                    if (p == null) continue;
                    string n = ProtoName(p);
                    string nl = n.ToLowerInvariant();
                    bool wantOff = active && ((kind == "details" && _hideAllDetails.Value) || names.Exists(x => nl.Contains(x)));
                    bool isOn = true; try { isOn = p.isEnabled; } catch { }
                    var key = (mp, i);
                    if (wantOff && isOn) { Switch(m, p, i, false); _offByUs[key] = true; Quiet.Info(_log, $"[Vegetation] hidden: {kind} '{n}'"); }
                    else if (!wantOff && _offByUs.ContainsKey(key)) { if (!isOn) Switch(m, p, i, true); _offByUs.Remove(key); Quiet.Info(_log, $"[Vegetation] shown again: {kind} '{n}'"); }
                    if (sb != null)
                    {
                        int inst = 0; try { inst = m.GetRegisteredInstanceCount(i); } catch { }
                        sb.Append($"\n    #{i,-3} {n,-40} {inst,7} instances{(wantOff ? "  [HIDDEN]" : !isOn ? "  [off]" : "")}");
                    }
                }
                if (sb != null) Quiet.Info(_log, sb.ToString());
            }
        }

        static void Switch(GPUIManager m, GPUIPrototype p, int i, bool on)
        {
            try { p.isEnabled = on; m.OnPrototypeEnabledStatusChanged(i, on); }
            catch (Exception e) { _log.LogWarning($"[Vegetation] switch failed: {e.Message}"); }
        }

        static void DetailSettings(GPUIDetailManager dm, IntPtr mp, bool active)
        {
            if (dm == null) return;
            try
            {
                // Draw distance
                float d = active ? _detailDistance.Value : -1f;
                if (d >= 0)
                {
                    if (!_origDist.ContainsKey(mp)) { _origDist[mp] = dm.detailObjectDistance; Quiet.Info(_log, $"[Vegetation] '{dm.name}': game detail distance {dm.detailObjectDistance:0.#} m"); }
                    if (Math.Abs(dm.detailObjectDistance - d) > 0.01f) { Quiet.Info(_log, $"[Vegetation] detail distance {dm.detailObjectDistance:0.#} -> {d:0.#} m"); dm.SetDetailObjectDistance(d); }
                }
                else if (_origDist.TryGetValue(mp, out var od))
                {
                    _origDist.Remove(mp);
                    Quiet.Info(_log, $"[Vegetation] detail distance back to the game's {od:0.#} m");
                    dm.SetDetailObjectDistance(od);
                }

                // Update (rebuild) distance
                float u = active ? _detailUpdateDistance.Value : -1f;
                if (u >= 0)
                {
                    if (!_origUpd.ContainsKey(mp)) _origUpd[mp] = dm.detailUpdateDistance;
                    if (Math.Abs(dm.detailUpdateDistance - u) > 0.01f) { Quiet.Info(_log, $"[Vegetation] detail update distance {dm.detailUpdateDistance:0.#} -> {u:0.#} m"); dm.detailUpdateDistance = u; }
                }
                else if (_origUpd.TryGetValue(mp, out var ou))
                {
                    _origUpd.Remove(mp);
                    Quiet.Info(_log, $"[Vegetation] detail update distance back to the game's {ou:0.#} m");
                    dm.detailUpdateDistance = ou;
                }

                // Distance density reduction (the game's value is read from its GPUI terrains)
                int r = active ? _densityReduction.Value : -1;
                bool? cur = DensityReduction();
                if (r >= 0)
                {
                    bool want = r == 1;
                    if (!_origDensity.ContainsKey(mp) && cur.HasValue) _origDensity[mp] = cur.Value;
                    if (cur != want) { Quiet.Info(_log, $"[Vegetation] detail density reduction -> {want}"); dm.SetDistanceDensityReduction(want); }
                }
                else if (_origDensity.TryGetValue(mp, out var orr))
                {
                    _origDensity.Remove(mp);
                    if (cur != orr) { Quiet.Info(_log, $"[Vegetation] detail density reduction back to the game's {orr}"); dm.SetDistanceDensityReduction(orr); }
                }
            }
            catch (Exception e) { _log.LogWarning("[Vegetation] detail settings: " + e.Message); }
        }

        // The game's current density-reduction switch (from the first GPUI terrain), or null if none is found.
        static bool? DensityReduction()
        {
            try
            {
                var ts = UnityEngine.Object.FindObjectsOfType<GPUITerrain>();
                if (ts != null) for (int i = 0; i < ts.Length; i++) if (ts[i] != null) return ts[i].enableDetailDensityReduction;
            }
            catch { }
            return null;
        }
    }
}
