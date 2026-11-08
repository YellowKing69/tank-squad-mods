// Potato Mode terrain report ([Debug] TerrainReport, off by default): writes what the terrain is actually running with,
// next to the game's own menu settings for it, and writes it again whenever something changes. Use it to see which
// values a menu option (e.g. Terrain Tesselation Quality) really changes: switch the option in a battle and compare.
// Cheap: Terrain.activeTerrains is a list Unity keeps; no scene search. Checked once a second while switched on.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace PotatoMode
{
    internal static class TerrainReport
    {
        static ConfigEntry<bool> _on;
        static ManualLogSource _log;
        static float _next;
        static string _lastGame = "", _lastQuality = "";
        static readonly Dictionary<string, string> _lastTerrain = new();
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        internal static void Init(ConfigFile cfg, ManualLogSource log)
        {
            _log = log;
            _on = cfg.Bind("Debug", "TerrainReport", false,
                "true = write the terrain's actual values (mesh accuracy / pixel error, basemap, tree and grass distances, the terrain material's " +
                "tessellation values) and the game's own menu settings for them to the log, and again whenever any of them changes " +
                "(lines starting [Terrain]). Switch a menu option in a battle to see what it changes. Off by default.");
        }

        internal static void Tick()
        {
            if (_on == null || !_on.Value) { if (_lastTerrain.Count > 0) { _lastTerrain.Clear(); _lastGame = _lastQuality = ""; } return; }
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 1f;

            // the game's own menu settings
            try
            {
                var sd = Com.DeGenerals.TankSquad.Managers.ContentManager.Instance?.CurrentSettingsData;
                if (sd != null)
                {
                    string g = $"menu: Terrain Tesselation Quality {sd.TerrainTesselationQuality}, Terrain Quality '{sd.TerrainQuality}', tree distance '{sd.TerrainTreeDistance}', " +
                               $"details distance '{sd.TerrainDetailsDistance}', Grass Distance {sd.EnviroGrassDistance}, Sky Quality {sd.EnviroSkyQuality}, " +
                               $"Volumetric Clouds {(sd.EnviroVolumetricCloudsEnabled ? "on" : "off")}, LOD {sd.QualityLOD}";
                    if (g != _lastGame) { _lastGame = g; _log.LogInfo("[Terrain] " + g); }
                }
            }
            catch (Exception e) { Once("menu", e); }

            // the global override (Unity's QualitySettings.terrainPixelError etc.)
            try
            {
                string q = "quality settings: terrainPixelError " + F(QualitySettings.terrainPixelError) +
                           ", terrainBasemapDistance " + F(QualitySettings.terrainBasemapDistance) +
                           ", terrainDetailDistance " + F(QualitySettings.terrainDetailDistance) +
                           ", terrainDetailDensityScale " + F(QualitySettings.terrainDetailDensityScale) +
                           ", terrainTreeDistance " + F(QualitySettings.terrainTreeDistance) +
                           ", terrainBillboardStart " + F(QualitySettings.terrainBillboardStart) +
                           ", terrainMaxTrees " + F(QualitySettings.terrainMaxTrees) +
                           ", lodBias " + F(QualitySettings.lodBias);
                if (q != _lastQuality) { _lastQuality = q; _log.LogInfo("[Terrain] " + q); }
            }
            catch (Exception e) { Once("quality", e); }

            // each terrain in the scene
            try
            {
                var all = Terrain.activeTerrains;
                var seen = new HashSet<string>();
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null) continue;
                    string key = t.name + "#" + t.GetInstanceID();
                    seen.Add(key);
                    var sb = new StringBuilder();
                    sb.Append("terrain '").Append(t.name).Append("': pixelError ").Append(F(t.heightmapPixelError))
                      .Append(", basemapDistance ").Append(F(t.basemapDistance))
                      .Append(", detailDistance ").Append(F(t.detailObjectDistance))
                      .Append(", detailDensity ").Append(F(t.detailObjectDensity))
                      .Append(", treeDistance ").Append(F(t.treeDistance))
                      .Append(", billboardStart ").Append(F(t.treeBillboardDistance))
                      .Append(", maxFullTrees ").Append(t.treeMaximumFullLODCount)
                      .Append(", drawInstanced ").Append(t.drawInstanced)
                      .Append(", drawTrees ").Append(t.drawTreesAndFoliage);
                    Material(sb, t.materialTemplate);
                    string s = sb.ToString();
                    if (!_lastTerrain.TryGetValue(key, out var old) || old != s) { _lastTerrain[key] = s; _log.LogInfo("[Terrain] " + s); }
                }
                if (seen.Count != _lastTerrain.Count) { var gone = new List<string>(); foreach (var k in _lastTerrain.Keys) if (!seen.Contains(k)) gone.Add(k); foreach (var k in gone) _lastTerrain.Remove(k); }
            }
            catch (Exception e) { Once("terrain", e); }
        }

        // the terrain material: shader, tessellation / displacement keywords and values
        static void Material(StringBuilder sb, Material m)
        {
            if (m == null) { sb.Append(", material none"); return; }
            sb.Append(", shader '").Append(m.shader != null ? m.shader.name : "?").Append('\'');
            try
            {
                var kws = m.shaderKeywords; var hits = new List<string>();
                if (kws != null) foreach (var k in kws) if (Interesting(k)) hits.Add(k);
                if (hits.Count > 0) sb.Append(", keywords ").Append(string.Join(" ", hits));
            }
            catch { }
            try
            {
                var sh = m.shader; if (sh == null) return;
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    string name = sh.GetPropertyName(i); if (!Interesting(name)) continue;
                    var type = sh.GetPropertyType(i);
                    if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                        sb.Append(", ").Append(name).Append(' ').Append(F(m.GetFloat(name)));
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                    { var v = m.GetVector(name); sb.Append(", ").Append(name).Append(" (").Append(F(v.x)).Append(' ').Append(F(v.y)).Append(' ').Append(F(v.z)).Append(' ').Append(F(v.w)).Append(')'); }
                }
            }
            catch { }
        }

        static bool Interesting(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var u = s.ToUpperInvariant();
            return u.Contains("TESS") || u.Contains("DISPLACE") || u.Contains("PARALLAX") || u.Contains("POM");
        }

        static readonly HashSet<string> _failed = new();
        static void Once(string what, Exception e) { if (_failed.Add(what)) _log.LogWarning($"[Terrain] {what}: {e.Message}"); }
        static string F(float v) => v.ToString("0.###", CI);
    }
}
