// PotatoMode - post-load shader cache (opt-in)
//
// After a battle has fully loaded (default 60 s), walk every material that is loaded and prepare the GPU program
// for each of its passes (Material.SetPass), a few materials per frame. That makes the graphics driver compile the
// variants the game will actually draw now, instead of the first time each one appears on screen, and the driver
// keeps them in its disk shader cache for later sessions. Pausing the game while it runs is fine (it uses real time).
// Progress goes to the log ([ShaderCache]). Done maps are written to BepInEx/config/potatomode.shadercache.txt and
// skipped next time. Replaces the old WarmupShaders (Shader.WarmupAllShaders compiled ~45000 variants at once and
// reset a GTX 1070's driver at start-up).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GPUInstancerPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PotatoMode
{
    internal static class ShaderCache
    {
        const string Section = "Rendering";
        static ManualLogSource _log;
        static ConfigEntry<bool> _on, _oncePerMap, _listMats;
        static ConfigEntry<string> _skip, _only;
        static ConfigEntry<float> _delay, _slowMs;
        static ConfigEntry<int> _perFrame;

        static string _sceneKey;
        static float _sceneAt = -1f, _startedAt, _lastReport;
        static List<Material> _todo;
        static int _next, _passes, _failed, _skippedFrames;
        static bool _done;
        static string FilePath => Path.Combine(Paths.ConfigPath, "potatomode.shadercache.txt");

        public static void Init(ConfigFile cfg, ManualLogSource log)
        {
            _log = log;
            _on = cfg.Bind(Section, "BuildShaderCache", false,
                "true = some time after a battle (or the yard) has loaded, prepare the GPU programs of every loaded material, a few per frame, so\n" +
                "things don't hitch the first time they appear. The graphics driver keeps them in its disk cache for later sessions, so each map\n" +
                "only needs it once. You can pause the game while it runs; progress is logged as [ShaderCache]. Safe replacement for WarmupShaders.");
            _delay = cfg.Bind(Section, "ShaderCacheDelaySeconds", 60f,
                "Real seconds after the map loads before it starts (lets the battle finish loading its assets). Limits: 5 to 600.");
            _perFrame = cfg.Bind(Section, "ShaderCachePerFrame", 6,
                "Materials per frame (each with all its passes). Lower = gentler on the graphics driver, takes longer. Limits: 1 to 50.");
            _slowMs = cfg.Bind(Section, "ShaderCacheSkipSlowFrameMs", 60f,
                "Skip a frame when the previous one took longer than this (ms), so it never piles onto a slow frame. Limits: 20 to 500.");
            _skip = cfg.Bind(Section, "ShaderCacheSkip", "",
                "Materials NOT to prepare: comma-separated parts of the material name OR shader name, case-insensitive (e.g. T34, Panzer, Vehicle, Tank).\n" +
                "Use ShaderCacheListMaterials once to see the real names. Empty = skip nothing.");
            _only = cfg.Bind(Section, "ShaderCacheOnly", "",
                "If set: prepare ONLY materials whose material or shader name contains one of these (e.g. Bush, Tree, Grass, Foliage). Empty = everything.");
            _listMats = cfg.Bind(Section, "ShaderCacheListMaterials", false,
                "true = when the cache starts, log every distinct material it will prepare (material name | shader name), and the ones it skips.\n" +
                "Use once to find the names for ShaderCacheSkip / ShaderCacheOnly. Long list.");
            _oncePerMap = cfg.Bind(Section, "ShaderCacheOncePerMap", true,
                "Remember finished maps in BepInEx/config/potatomode.shadercache.txt and don't repeat them. Delete that file (or set false) to run again.");
        }

        static string MapKey()
        {
            var names = new List<string>();
            try { for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) names.Add(s.name); } } catch { }
            names.Sort(StringComparer.Ordinal);
            return string.Join("+", names);
        }

        static bool Done(string key)
        {
            try { return File.Exists(FilePath) && Array.IndexOf(File.ReadAllLines(FilePath), key) >= 0; } catch { return false; }
        }

        public static void Tick()
        {
            if (_on == null) return;
            float now = Time.unscaledTime;
            string key = MapKey();
            if (key != _sceneKey) { _sceneKey = key; _sceneAt = now; _todo = null; _done = false; }
            if (!_on.Value || _done) return;

            if (_todo == null)
            {
                if (now < _sceneAt + Math.Clamp(_delay.Value, 5f, 600f)) return;
                _done = true;                                    // decide once per map load
                bool isMap = false;
                try { isMap = Terrain.activeTerrains.Length > 0 && UnityEngine.Object.FindObjectOfType<GPUIManager>() != null; } catch { }
                if (!isMap) return;                              // menus and loading scenes: nothing to do
                if (_oncePerMap.Value && Done(key)) { Quiet.Info(_log, $"[ShaderCache] '{key}' was already cached (delete potatomode.shadercache.txt to redo)."); return; }
                Collect(key);
                _done = false;
                _startedAt = now; _lastReport = now;
                return;
            }

            if (Time.unscaledDeltaTime * 1000f > Math.Clamp(_slowMs.Value, 20f, 500f)) { _skippedFrames++; return; }
            int n = Math.Clamp(_perFrame.Value, 1, 50);
            for (int i = 0; i < n && _next < _todo.Count; i++, _next++)
            {
                var m = _todo[_next];
                try
                {
                    if (m == null) continue;
                    int pc = m.passCount;
                    for (int p = 0; p < pc; p++) { if (m.SetPass(p)) _passes++; else _failed++; }
                }
                catch { _failed++; }
            }
            if (now - _lastReport >= 5f || _next >= _todo.Count)
            {
                _lastReport = now;
                Quiet.Info(_log, $"[ShaderCache] {_next}/{_todo.Count} materials ({100f * _next / Math.Max(1, _todo.Count):0}%), {_passes} passes prepared" +
                             $"{(_failed > 0 ? $", {_failed} skipped" : "")}, {now - _startedAt:0} s");
            }
            if (_next >= _todo.Count)
            {
                Quiet.Info(_log, $"[ShaderCache] done for '{_sceneKey}': {_passes} passes in {now - _startedAt:0} s ({_skippedFrames} slow frames skipped).");
                if (_oncePerMap.Value) { try { File.AppendAllLines(FilePath, new[] { _sceneKey }); } catch { } }
                _todo = null; _done = true;
            }
        }

        static List<string> Words(string v)
        {
            var l = new List<string>();
            foreach (var p in (v ?? "").Split(',')) { var t = p.Trim().ToLowerInvariant(); if (t.Length > 0) l.Add(t); }
            return l;
        }

        static void Collect(string key)
        {
            _todo = new List<Material>(); _next = 0; _passes = 0; _failed = 0; _skippedFrames = 0;
            var seen = new HashSet<string>();
            int total = 0, skipped = 0;
            var skip = Words(_skip.Value); var only = Words(_only.Value);
            var list = _listMats.Value ? new StringBuilder("[ShaderCache] materials (material | shader):") : null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                total++;
                try
                {
                    if (m == null) continue;
                    var sh = m.shader;
                    if (sh == null || !sh.isSupported) continue;
                    string kw = "";
                    try { var k = m.shaderKeywords; if (k != null && k.Length > 0) { var arr = new string[k.Length]; for (int i = 0; i < k.Length; i++) arr[i] = k[i]; Array.Sort(arr, StringComparer.Ordinal); kw = string.Join(" ", arr); } } catch { }
                    if (!seen.Add(sh.Pointer.ToInt64() + "|" + kw)) continue;     // same shader + same keywords = same variant
                    string mn = m.name ?? "", sn = sh.name ?? "";
                    string both = (mn + " | " + sn).ToLowerInvariant();
                    bool keep = (only.Count == 0 || only.Exists(w => both.Contains(w))) && !skip.Exists(w => both.Contains(w));
                    if (list != null) list.Append($"\n    {(keep ? "  " : "- ")}{mn} | {sn}");
                    if (!keep) { skipped++; continue; }
                    _todo.Add(m);
                }
                catch { }
            }
            if (list != null) Quiet.Info(_log, list.ToString());
            Quiet.Info(_log, $"[ShaderCache] '{key}': {total} materials loaded, {_todo.Count} distinct shader variants to prepare" +
                         $"{(skipped > 0 ? $", {skipped} skipped by ShaderCacheSkip/Only" : "")}. Starting.");
        }
    }
}
