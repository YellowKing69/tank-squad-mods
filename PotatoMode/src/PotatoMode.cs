using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.Rendering;

namespace PotatoMode
{
    // Info lines only when [Debug] VerboseLog is on; warnings and errors always show.
    internal static class Quiet
    {
        internal static ConfigEntry<bool> Verbose;
        internal static void Info(ManualLogSource log, string s) { if (Verbose != null && Verbose.Value) log?.LogInfo(s); }
    }

    public enum Toggle { Leave, On, Off }
    public enum WindowMode { Leave, Exclusive, Borderless, Windowed }
    public enum Aniso { Leave, Off, On, Forced }

    [BepInPlugin(Guid, PluginName, PluginVersion)]
    public class PotatoModePlugin : BasePlugin
    {
        public const string Guid = "potatomode.vramtweaks";
        public const string PluginName = "Potato Mode";
        public const string PluginVersion = "1.4.3";

        internal static PotatoModePlugin Instance;

        // ---- Textures
        ConfigEntry<int> _mipLimit;
        ConfigEntry<Toggle> _mipStreaming;
        ConfigEntry<float> _mipBudgetMB;
        ConfigEntry<Aniso> _aniso;

        // ---- Rendering
        ConfigEntry<float> _renderScale;
        ConfigEntry<int> _msaa;
        ConfigEntry<Toggle> _hdr;
        ConfigEntry<Toggle> _realtimeProbes;
        ConfigEntry<float> _lodBias;
        ConfigEntry<int> _maxLodLevel;
        ConfigEntry<Toggle> _softParticles;
        ConfigEntry<int> _addLightsPerObject;

        // ---- Shadows
        ConfigEntry<int> _shadowRes;
        ConfigEntry<int> _shadowCascades;
        ConfigEntry<float> _shadowDistance;
        ConfigEntry<Toggle> _mainLightShadows;
        ConfigEntry<Toggle> _addLightShadows;
        ConfigEntry<Toggle> _softShadows;

        // ---- Terrain
        ConfigEntry<float> _terrDetailDensity;
        ConfigEntry<float> _terrDetailDistance;
        ConfigEntry<float> _terrTreeDistance;
        ConfigEntry<float> _terrBillboardStart;
        ConfigEntry<float> _terrMaxTrees;
        ConfigEntry<float> _terrPixelError;
        ConfigEntry<float> _terrBasemapDistance;

        // ---- Display
        ConfigEntry<int> _width;
        ConfigEntry<int> _height;
        ConfigEntry<WindowMode> _windowMode;
        ConfigEntry<int> _targetFps;
        ConfigEntry<int> _vsync;

        // ---- General
        ConfigEntry<float> _reapplyInterval;
        ConfigEntry<bool> _watchFile;
        ConfigEntry<string> _tickHooks;

        float _nextApply;
        volatile bool _reloadPending;
        float _reloadAt;
        FileSystemWatcher _watcher;
        bool _displayApplied;
        bool _tickFailed;

        readonly HashSet<string> _warned = new();
        readonly Dictionary<string, int> _changeLogCount = new();

        public override void Load()
        {
            Instance = this;
            Quiet.Verbose = Config.Bind("Debug", "VerboseLog", false,
                "Write what the plugin found and changed to the BepInEx log. false = the plugin logs nothing (warnings still show).");
            const string leave = " (-1 = leave the game's value)";

            // Textures
            _mipLimit = Config.Bind("Textures", "MipmapLimit", 1,
                new ConfigDescription("Drop this many top mip levels from every texture. 1 = ~75% less texture memory, 2 = ~94% less (blurry)." + leave,
                    new AcceptableValueRange<int>(-1, 3)));
            _mipStreaming = Config.Bind("Textures", "MipStreaming", Toggle.Leave,
                "Turn Unity texture mip streaming on/off. Only helps if the game's textures were built with streaming enabled.");
            _mipBudgetMB = Config.Bind("Textures", "MipStreamingBudgetMB", -1f,
                "Texture memory budget in MB when mip streaming is on (e.g. 512 on a 2 GB card)." + leave);
            _aniso = Config.Bind("Textures", "AnisotropicFiltering", Aniso.Leave,
                "Off = slightly blurrier textures at glancing angles, small GPU saving.");

            // Rendering
            _renderScale = Config.Bind("Rendering", "RenderScale", 0.8f,
                new ConfigDescription("URP only: internal render resolution multiplier. Set to -1 when the game's FSR/upscaler is on, or they will fight and flicker." + leave,
                    new AcceptableValueRange<float>(-1f, 2f)));
            _msaa = Config.Bind("Rendering", "MSAA", 1,
                "Multisample anti-aliasing samples: 1 = off, 2, 4, 8. Off saves a lot of render-target memory." + leave);
            _hdr = Config.Bind("Rendering", "HDR", Toggle.Leave,
                "URP only: HDR render targets use 2x the memory of LDR. Off may dull bloom and can clash with FSR.");
            _realtimeProbes = Config.Bind("Rendering", "RealtimeReflectionProbes", Toggle.Off,
                "Realtime reflection probes allocate and re-render cubemaps.");
            _lodBias = Config.Bind("Rendering", "LodBias", -1f,
                "Lower = switch to low-detail models sooner (e.g. 0.5)." + leave);
            _maxLodLevel = Config.Bind("Rendering", "MaximumLODLevel", -1,
                "Never use LOD levels below this index: 1 = skip the highest-detail model of every LOD group." + leave);
            _softParticles = Config.Bind("Rendering", "SoftParticles", Toggle.Leave,
                "Off = cheaper smoke/dust with harder edges where particles touch geometry.");
            _addLightsPerObject = Config.Bind("Rendering", "AdditionalLightsPerObject", -1,
                "URP only: max extra (point/spot) lights per object, 0-8. Lower = faster, fewer lit highlights." + leave);

            // Shadows
            _shadowRes = Config.Bind("Shadows", "ShadowMapResolution", 1024,
                "Shadow map size: 256, 512, 1024, 2048, 4096. A 4096 map is ~64 MB by itself." + leave);
            _shadowCascades = Config.Bind("Shadows", "ShadowCascades", 1,
                "Directional shadow cascades: 1, 2 or 4. Fewer = smaller shadow atlas and faster." + leave);
            _shadowDistance = Config.Bind("Shadows", "ShadowDistance", -1f,
                "Max shadow draw distance in metres (e.g. 50)." + leave);
            _mainLightShadows = Config.Bind("Shadows", "MainLightShadows", Toggle.Leave,
                "URP only: sun shadows. Off is the biggest single GPU saving, but no shadows from the sun at all.");
            _addLightShadows = Config.Bind("Shadows", "AdditionalLightShadows", Toggle.Leave,
                "URP only: shadows from point/spot lights (muzzle flashes, lamps). Off removes their shadow atlas.");
            _softShadows = Config.Bind("Shadows", "SoftShadows", Toggle.Leave,
                "URP only: filtered soft shadow edges. Off = harder edges, cheaper.");

            // Terrain (Unity 2022.2+ global terrain quality values)
            _terrDetailDensity = Config.Bind("Terrain", "DetailDensityScale", -1f,
                "Grass/detail density multiplier, 0-1 (e.g. 0.4)." + leave);
            _terrDetailDistance = Config.Bind("Terrain", "DetailDistance", -1f,
                "Distance in metres that grass/details are drawn (e.g. 40)." + leave);
            _terrTreeDistance = Config.Bind("Terrain", "TreeDistance", -1f,
                "Distance in metres that trees are drawn (e.g. 600)." + leave);
            _terrBillboardStart = Config.Bind("Terrain", "BillboardStart", -1f,
                "Distance in metres at which trees switch to flat billboards (e.g. 50)." + leave);
            _terrMaxTrees = Config.Bind("Terrain", "MaxFullTrees", -1f,
                "Maximum number of full-mesh trees; the rest become billboards (e.g. 50)." + leave);
            _terrPixelError = Config.Bind("Terrain", "PixelError", -1f,
                "Terrain mesh accuracy, 1-200. Higher = coarser, cheaper terrain (e.g. 20)." + leave);
            _terrBasemapDistance = Config.Bind("Terrain", "BasemapDistance", -1f,
                "Distance beyond which terrain uses its low-res composite texture (e.g. 300)." + leave);

            // Display
            _width = Config.Bind("Display", "Width", 0, "Screen width. 0 = leave. Set together with Height.");
            _height = Config.Bind("Display", "Height", 0, "Screen height. 0 = leave.");
            _windowMode = Config.Bind("Display", "WindowMode", WindowMode.Leave, "Fullscreen mode.");
            _targetFps = Config.Bind("Display", "TargetFrameRate", 0,
                "Frame-rate cap (e.g. 30 or 60). 0 = leave. Ignored while VSync is on.");
            _vsync = Config.Bind("Display", "VSync", -1, "0 = off, 1 = every vblank, 2 = every second vblank." + leave);

            // General
            _reapplyInterval = Config.Bind("General", "ReapplyIntervalSeconds", 2f,
                "How often to re-check settings, in case the game resets them (e.g. on scene load or from its own menu).");
            _watchFile = Config.Bind("General", "LiveReloadConfig", true,
                "Reload this file automatically when you save it. Changing this needs a game restart.");
            _tickHooks = Config.Bind("General", "TickHooks", "MEC.Timing:Update;AstarPath:Update",
                "Per-frame game methods to piggyback on, as Type:Method separated by ';'. The first one found is used. " +
                "This avoids injecting a MonoBehaviour into IL2CPP, which crashes on this game's Unity version. Needs a restart.");

            // GPU Instancer vegetation (bushes/trees). Must bind BEFORE Config.Save() below,
            // otherwise its settings never get written to the .cfg file.
            GpuiTweaks.Init(Config, Log);

            // Write the file once, then stop auto-saving so a live reload can never re-trigger the file watcher.
            Config.Save();
            Config.SaveOnConfigSet = false;

            if (_watchFile.Value) StartWatcher();

            SafeTick();
            InstallTickHook();

            Quiet.Info(Log, $"{PluginName} {PluginVersion} loaded. Edit {Path.GetFileName(Config.ConfigFilePath)} to tune.");
        }

        // ---------------- tick plumbing ----------------
        void InstallTickHook()
        {
            var harmony = new Harmony(Guid);
            var postfix = new HarmonyMethod(typeof(PotatoModePlugin), nameof(TickPostfix));

            foreach (var entry in _tickHooks.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) { Log.LogWarning($"Ignoring malformed tick hook '{entry}' (expected Type:Method)."); continue; }

                var type = FindType(parts[0]);
                var method = type == null ? null : AccessTools.DeclaredMethod(type, parts[1], Type.EmptyTypes);
                if (method == null) { Quiet.Info(Log, $"Tick hook '{entry}' not found, trying next."); continue; }

                try
                {
                    harmony.Patch(method, postfix: postfix);
                    Quiet.Info(Log, $"Tick hook installed on {entry}.");
                    return;
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Could not patch '{entry}': {e.GetType().Name}: {e.Message}");
                }
            }

            Log.LogWarning("No tick hook could be installed - settings were applied once at startup only, " +
                           "and the game may override some of them later.");
        }

        // Looks a type up by full name in each loaded assembly. Unlike AccessTools.TypeByName this
        // never enumerates every type, so it avoids the noisy TypeLoadException warnings that
        // IL2CPP interop assemblies (e.g. UnityEngine.CoreModule) produce when fully scanned.
        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* dynamic / unloadable assembly */ }
            }
            return null;
        }

        public static void TickPostfix() => Instance?.SafeTick();

        void SafeTick()
        {
            try { Tick(); }
            catch (Exception e)
            {
                // Never let an exception escape into the game's own Update.
                if (!_tickFailed) { _tickFailed = true; Log.LogError($"Tick failed (further errors suppressed): {e}"); }
            }

            // Scans on its own schedule after each scene change (and after a config change); catches its own errors.
            try { GpuiTweaks.Tick(); } catch { /* already logged inside GpuiTweaks */ }
        }

        void StartWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(Config.ConfigFilePath), Path.GetFileName(Config.ConfigFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Changed += (_, _) => _reloadPending = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { Log.LogWarning($"Config live-reload unavailable: {e.Message}"); }
        }

        internal void Tick()
        {
            float now = Time.unscaledTime;

            if (_reloadPending)
            {
                _reloadPending = false;
                _reloadAt = now + 0.5f; // let the editor finish writing
            }
            if (_reloadAt > 0f && now >= _reloadAt)
            {
                _reloadAt = 0f;
                try
                {
                    Config.Reload(); // also tells GpuiTweaks to re-apply (ConfigReloaded event)
                    _changeLogCount.Clear();
                    Quiet.Info(Log, "Config reloaded.");
                }
                catch (Exception e) { Log.LogWarning($"Config reload failed: {e.Message}"); }
                _displayApplied = false;
                _nextApply = 0f;
            }

            if (now < _nextApply) return;
            _nextApply = now + Math.Max(0.25f, _reapplyInterval.Value);

            if (!_displayApplied) { ApplyDisplay(); _displayApplied = true; }
            ApplyQuality();
            ApplyTerrain();
            ApplyUrp();
        }

        // ---------------- pipeline-agnostic quality settings ----------------
        void ApplyQuality()
        {
            if (_mipLimit.Value >= 0)
                Set("Texture mip limit", () => QualitySettings.globalTextureMipmapLimit,
                    v => QualitySettings.globalTextureMipmapLimit = v, _mipLimit.Value);

            if (_mipStreaming.Value != Toggle.Leave)
                Set("Mip streaming", () => QualitySettings.streamingMipmapsActive,
                    v => QualitySettings.streamingMipmapsActive = v, _mipStreaming.Value == Toggle.On);

            if (_mipBudgetMB.Value > 0)
                Set("Mip streaming budget MB", () => QualitySettings.streamingMipmapsMemoryBudget,
                    v => QualitySettings.streamingMipmapsMemoryBudget = v, _mipBudgetMB.Value);

            if (_aniso.Value != Aniso.Leave)
                Set("Anisotropic filtering", () => QualitySettings.anisotropicFiltering,
                    v => QualitySettings.anisotropicFiltering = v,
                    _aniso.Value switch
                    {
                        Aniso.Off => AnisotropicFiltering.Disable,
                        Aniso.On => AnisotropicFiltering.Enable,
                        _ => AnisotropicFiltering.ForceEnable
                    });

            if (_msaa.Value >= 1)
                Set("MSAA (built-in)", () => QualitySettings.antiAliasing,
                    v => QualitySettings.antiAliasing = v, _msaa.Value <= 1 ? 0 : _msaa.Value);

            if (_realtimeProbes.Value != Toggle.Leave)
                Set("Realtime reflection probes", () => QualitySettings.realtimeReflectionProbes,
                    v => QualitySettings.realtimeReflectionProbes = v, _realtimeProbes.Value == Toggle.On);

            if (_lodBias.Value > 0)
                Set("LOD bias", () => QualitySettings.lodBias, v => QualitySettings.lodBias = v, _lodBias.Value);

            if (_maxLodLevel.Value >= 0)
                Set("Maximum LOD level", () => QualitySettings.maximumLODLevel,
                    v => QualitySettings.maximumLODLevel = v, _maxLodLevel.Value);

            if (_softParticles.Value != Toggle.Leave)
                Set("Soft particles", () => QualitySettings.softParticles,
                    v => QualitySettings.softParticles = v, _softParticles.Value == Toggle.On);

            // Only ever lower the built-in shadow resolution - never raise it above what the game set.
            if (_shadowRes.Value > 0 && ToBuiltinShadowRes(_shadowRes.Value) < QualitySettings.shadowResolution)
                Set("Shadow resolution (built-in)", () => QualitySettings.shadowResolution,
                    v => QualitySettings.shadowResolution = v, ToBuiltinShadowRes(_shadowRes.Value));

            if (_shadowCascades.Value > 0)
                Set("Shadow cascades (built-in)", () => QualitySettings.shadowCascades,
                    v => QualitySettings.shadowCascades = v, _shadowCascades.Value);

            if (_shadowDistance.Value > 0)
                Set("Shadow distance (built-in)", () => QualitySettings.shadowDistance,
                    v => QualitySettings.shadowDistance = v, _shadowDistance.Value);

            if (_vsync.Value >= 0)
                Set("VSync", () => QualitySettings.vSyncCount, v => QualitySettings.vSyncCount = v, _vsync.Value);

            if (_targetFps.Value != 0)
                Set("Target frame rate", () => Application.targetFrameRate,
                    v => Application.targetFrameRate = v, _targetFps.Value);
        }

        static ShadowResolution ToBuiltinShadowRes(int px) =>
            px <= 512 ? ShadowResolution.Low :
            px <= 1024 ? ShadowResolution.Medium :
            px <= 2048 ? ShadowResolution.High : ShadowResolution.VeryHigh;

        // Terrain quality values live on QualitySettings in Unity 2022.2+. Set via reflection so a
        // missing property only logs a warning instead of breaking the build.
        void ApplyTerrain()
        {
            var qs = typeof(QualitySettings);
            if (_terrDetailDensity.Value >= 0)
                SetMember(null, qs, "Terrain detail density", _terrDetailDensity.Value, "terrainDetailDensityScale");
            if (_terrDetailDistance.Value >= 0)
                SetMember(null, qs, "Terrain detail distance", _terrDetailDistance.Value, "terrainDetailDistance");
            if (_terrTreeDistance.Value >= 0)
                SetMember(null, qs, "Terrain tree distance", _terrTreeDistance.Value, "terrainTreeDistance");
            if (_terrBillboardStart.Value >= 0)
                SetMember(null, qs, "Terrain billboard start", _terrBillboardStart.Value, "terrainBillboardStart");
            if (_terrMaxTrees.Value >= 0)
                SetMember(null, qs, "Terrain max full trees", _terrMaxTrees.Value, "terrainMaxTrees");
            if (_terrPixelError.Value > 0)
                SetMember(null, qs, "Terrain pixel error", _terrPixelError.Value, "terrainPixelError");
            if (_terrBasemapDistance.Value >= 0)
                SetMember(null, qs, "Terrain basemap distance", _terrBasemapDistance.Value, "terrainBasemapDistance");
        }

        void ApplyDisplay()
        {
            try
            {
                FullScreenMode mode = _windowMode.Value switch
                {
                    WindowMode.Exclusive => FullScreenMode.ExclusiveFullScreen,
                    WindowMode.Borderless => FullScreenMode.FullScreenWindow,
                    WindowMode.Windowed => FullScreenMode.Windowed,
                    _ => Screen.fullScreenMode
                };

                if (_width.Value > 0 && _height.Value > 0)
                {
                    Screen.SetResolution(_width.Value, _height.Value, mode);
                    Quiet.Info(Log, $"Resolution set to {_width.Value}x{_height.Value} ({mode}).");
                }
                else if (_windowMode.Value != WindowMode.Leave)
                {
                    Screen.fullScreenMode = mode;
                    Quiet.Info(Log, $"Window mode set to {mode}.");
                }
            }
            catch (Exception e) { WarnOnce("Display", e); }
        }

        // ---------------- URP (via reflection, so the plugin also runs on built-in games) ----------------
        Type _urpType;
        MethodInfo _tryCastUrp;
        bool _urpSearched;

        object GetUrpAsset()
        {
            RenderPipelineAsset rp;
            try { rp = GraphicsSettings.currentRenderPipeline; }
            catch (Exception e) { WarnOnce("currentRenderPipeline", e); return null; }
            if (rp == null) return null;

            if (!_urpSearched)
            {
                _urpSearched = true;
                _urpType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");

                if (_urpType != null)
                {
                    _tryCastUrp = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast))!.MakeGenericMethod(_urpType);
                    Quiet.Info(Log, "URP detected - URP settings active.");
                }
                else
                {
                    Quiet.Info(Log, $"Render pipeline is '{rp.GetIl2CppType().FullName}', not URP - URP-only settings ignored.");
                }
            }

            if (_tryCastUrp == null) return null;
            try { return _tryCastUrp.Invoke(rp, null); }
            catch (Exception e) { WarnOnce("URP cast", e); return null; }
        }

        void ApplyUrp()
        {
            var urp = GetUrpAsset();
            if (urp == null) return;
            var t = urp.GetType();

            if (_renderScale.Value > 0)
                SetMember(urp, t, "Render scale (URP)", _renderScale.Value, "renderScale", "m_RenderScale");

            if (_msaa.Value >= 1)
                SetMember(urp, t, "MSAA (URP)", _msaa.Value, "msaaSampleCount", "m_MSAA");

            if (_hdr.Value != Toggle.Leave)
                SetMember(urp, t, "HDR (URP)", _hdr.Value == Toggle.On, "supportsHDR", "m_SupportsHDR");

            if (_addLightsPerObject.Value >= 0)
                SetMember(urp, t, "Additional lights per object (URP)", _addLightsPerObject.Value,
                    "maxAdditionalLightsCount", "m_AdditionalLightsPerObjectLimit");

            // Only ever lower shadow map resolution - never raise it above what the game itself chose
            // (in matches the game uses a 512 atlas, which the old code was pushing up to 1024).
            if (_shadowRes.Value > 0)
            {
                if (GetIntMember(urp, t, "mainLightShadowmapResolution", "m_MainLightShadowmapResolution") is int mainCur
                    && _shadowRes.Value < mainCur)
                    SetMember(urp, t, "Main light shadow res (URP)", _shadowRes.Value,
                        "mainLightShadowmapResolution", "m_MainLightShadowmapResolution");

                if (GetIntMember(urp, t, "additionalLightsShadowmapResolution", "m_AdditionalLightsShadowmapResolution") is int addCur
                    && _shadowRes.Value < addCur)
                    SetMember(urp, t, "Additional lights shadow res (URP)", _shadowRes.Value,
                        "additionalLightsShadowmapResolution", "m_AdditionalLightsShadowmapResolution");
            }

            if (_shadowCascades.Value > 0)
                SetMember(urp, t, "Shadow cascades (URP)", _shadowCascades.Value, "shadowCascadeCount", "m_ShadowCascadeCount");

            if (_shadowDistance.Value > 0)
                SetMember(urp, t, "Shadow distance (URP)", _shadowDistance.Value, "shadowDistance", "m_ShadowDistance");

            if (_mainLightShadows.Value != Toggle.Leave)
                SetMember(urp, t, "Main light shadows (URP)", _mainLightShadows.Value == Toggle.On,
                    "supportsMainLightShadows", "m_MainLightShadowsSupported");

            if (_addLightShadows.Value != Toggle.Leave)
                SetMember(urp, t, "Additional light shadows (URP)", _addLightShadows.Value == Toggle.On,
                    "supportsAdditionalLightShadows", "m_AdditionalLightShadowsSupported");

            if (_softShadows.Value != Toggle.Leave)
                SetMember(urp, t, "Soft shadows (URP)", _softShadows.Value == Toggle.On,
                    "supportsSoftShadows", "m_SoftShadowsSupported");
        }

        // ---------------- helpers ----------------
        void Set<T>(string label, Func<T> get, Action<T> set, T want)
        {
            try
            {
                var cur = get();
                if (EqualityComparer<T>.Default.Equals(cur, want)) return;
                set(want);
                LogChange(label, cur, want);
            }
            catch (Exception e) { WarnOnce(label, e); }
        }

        // Sets a property by name on an instance (obj != null) or a static type (obj == null).
        // Tries each name in turn: the public property first, then the serialized m_ field,
        // which Il2CppInterop exposes as a writable property.
        void SetMember(object obj, Type type, string label, object want, params string[] names)
        {
            var flags = BindingFlags.Public | (obj == null ? BindingFlags.Static : BindingFlags.Instance);
            foreach (var name in names)
            {
                var p = type.GetProperty(name, flags);
                if (p == null || !p.CanWrite || !p.CanRead) continue;
                try
                {
                    object target = p.PropertyType.IsEnum
                        ? Enum.ToObject(p.PropertyType, want)
                        : Convert.ChangeType(want, p.PropertyType);
                    var cur = p.GetValue(obj);
                    if (Equals(cur, target)) return;
                    p.SetValue(obj, target);
                    LogChange(label, cur, target);
                    return;
                }
                catch (Exception e)
                {
                    WarnOnce($"{label} via {name}", e.InnerException ?? e);
                }
            }
            WarnOnce(label, null, $"no writable member found ({string.Join(", ", names)})");
        }

        // Reads an int-like property by the first name that exists; null if none can be read.
        static int? GetIntMember(object obj, Type type, params string[] names)
        {
            foreach (var name in names)
            {
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanRead) continue;
                try { return Convert.ToInt32(p.GetValue(obj)); }
                catch { /* try the next name */ }
            }
            return null;
        }

        void LogChange(string label, object from, object to)
        {
            _changeLogCount.TryGetValue(label, out var n);
            _changeLogCount[label] = n + 1;
            if (n < 3)
                Quiet.Info(Log, $"{label}: {from} -> {to}");
            else if (n == 3)
                Log.LogWarning($"{label}: the game keeps resetting this; still re-applying, but no longer logging it. " +
                               "If you see flicker, set this one to -1/Leave.");
        }

        void WarnOnce(string label, Exception e, string msg = null)
        {
            if (!_warned.Add(label)) return;
            Log.LogWarning($"Could not apply '{label}': {msg ?? e?.GetType().Name + ": " + e?.Message}");
        }
    }
}
