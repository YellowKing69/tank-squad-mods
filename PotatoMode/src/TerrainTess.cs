// Potato Mode terrain tessellation ([Terrain] Tessellation / TessellationFactor / TessellationDistance).
// The game's "Terrain Tesselation Quality" option sets the MicroSplat terrain shader's tessellation on every terrain material:
//     Low    _TessData1.x 1  (no subdivision)   _TessData2.xy 0, 0      (nowhere)
//     Medium _TessData1.x 5                     _TessData2.xy 10, 100   (10-100 m)
//     High   _TessData1.x 10                    _TessData2.xy 20, 200   (20-200 m)
// This sets the same values, overriding the menu, so a config (or a Potato Bench preset) can choose the level.
// The game writes them again whenever its option changes, so they are checked once a second while an override is on
// (Terrain.activeTerrains is a list Unity keeps - no scene search). The game's values are put back at -1.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace PotatoMode
{
    internal static class TerrainTess
    {
        static ConfigEntry<int> _level;
        static ConfigEntry<float> _factor, _distance;
        static ManualLogSource _log;
        static float _next;
        static readonly int Tess1 = Shader.PropertyToID("_TessData1"), Tess2 = Shader.PropertyToID("_TessData2");
        static readonly Dictionary<IntPtr, (Vector4 a, Vector4 b)> _orig = new();   // game's values per material we changed

        internal static void Init(ConfigFile cfg, ManualLogSource log)
        {
            _log = log;
            const string leave = " -1 = leave it to the game's menu option (Settings > Environment > Terrain Tesselation Quality).";
            _level = cfg.Bind("Terrain", "Tessellation", -1,
                new ConfigDescription("Terrain tessellation (the ground's fine bumps and ruts, added by the graphics card): " +
                    "0 = off (menu Low), 1 = medium (subdivided 5x, 10-100 m), 2 = high (10x, 20-200 m)." + leave, new AcceptableValueRange<int>(-1, 2)));
            _factor = cfg.Bind("Terrain", "TessellationFactor", -1f,
                new ConfigDescription("Custom subdivision instead of the level's: 1 = none, 5 = medium, 10 = high, up to 32. Used on top of Tessellation (or of the menu's level when that is -1)." + leave,
                    new AcceptableValueRange<float>(-1f, 32f)));
            _distance = cfg.Bind("Terrain", "TessellationDistance", -1f,
                new ConfigDescription("Custom distance in metres out to which the ground is tessellated (medium 100, high 200); it starts at a tenth of that." + leave,
                    new AcceptableValueRange<float>(-1f, 2000f)));
            cfg.SettingChanged += (_, _) => _next = 0f;
            cfg.ConfigReloaded += (_, _) => _next = 0f;
        }

        static bool On => _level != null && (_level.Value >= 0 || _factor.Value > 0f || _distance.Value > 0f);

        internal static void Tick()
        {
            if (_level == null || (!On && _orig.Count == 0)) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 1f;

            var terrains = Terrain.activeTerrains;
            var live = new HashSet<IntPtr>();
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i]; if (t == null) continue;
                var m = t.materialTemplate; if (m == null || !m.HasProperty(Tess1) || !m.HasProperty(Tess2)) continue;
                var key = m.Pointer; if (!live.Add(key)) continue;          // terrains often share one material
                var a = m.GetVector(Tess1); var b = m.GetVector(Tess2);
                if (!On)
                {
                    if (_orig.TryGetValue(key, out var o)) { m.SetVector(Tess1, o.a); m.SetVector(Tess2, o.b); Quiet.Info(_log, $"[Terrain] tessellation back to the game's ({o.a.x:0.#}, {o.b.x:0}-{o.b.y:0} m) on '{m.name}'"); }
                    continue;
                }
                if (!_orig.ContainsKey(key)) _orig[key] = (a, b);
                // the level's values, then custom factor / distance on top
                float f = a.x, near = b.x, far = b.y;
                switch (_level.Value) { case 0: f = 1; near = 0; far = 0; break; case 1: f = 5; near = 10; far = 100; break; case 2: f = 10; near = 20; far = 200; break; }
                if (_factor.Value > 0f) f = Mathf.Clamp(_factor.Value, 1f, 32f);
                if (_distance.Value > 0f) { far = _distance.Value; near = far * 0.1f; }
                if (!Mathf.Approximately(a.x, f) || !Mathf.Approximately(b.x, near) || !Mathf.Approximately(b.y, far))
                {
                    m.SetVector(Tess1, new Vector4(f, a.y, a.z, a.w));
                    m.SetVector(Tess2, new Vector4(near, far, b.z, b.w));
                    Quiet.Info(_log, $"[Terrain] tessellation {a.x:0.#} ({b.x:0}-{b.y:0} m) -> {f:0.#} ({near:0}-{far:0} m) on '{m.name}'");
                }
            }
            if (!On) _orig.Clear();
            else { var gone = new List<IntPtr>(); foreach (var k in _orig.Keys) if (!live.Contains(k)) gone.Add(k); foreach (var k in gone) _orig.Remove(k); }
        }
    }
}
