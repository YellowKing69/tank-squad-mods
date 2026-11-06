// Tank Squad - Control Tweaks Lite (BepInEx 6 IL2CPP plugin)
// Uses the game's own four gunner zoom levels and its own sights and range tables.
// - Turret aim speed per zoom step: even on-screen speed (ZoomScaling = fov) with an adjustable curve.
// - Normal mouse aiming smoothing; gunner-sight turret inertia.
// - Commander cupola / head-out mouse speed per zoom; steadied binoculars.
// - Gun firing camera shake strength.
// - Item hotkeys, order line look, aim pointer.
// Harmony-only: no class injection.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace TankSquadControlTweaksLite
{
    [BepInPlugin(Guid, "Control Tweaks Lite", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "controltweakslite.tanksquad";
        public const string Version = "1.0.0";

        internal static ManualLogSource L;
        internal static Plugin Instance;
        internal static ConfigEntry<string> ZoomScaling;
        internal static ConfigEntry<float> MouseSmoothing, ZoomCurve, CupolaZoomCurve;
        internal static ConfigEntry<bool> CupolaHeadOut, CupolaPorts;
        internal static ConfigEntry<float> FireShake;
        internal static ConfigEntry<float> CupolaStabilize, CupolaSmoothing;

        // Extra gunner zoom levels currently switched on (FOV).
        internal static int[] ExtraSteps()
        {
            return Array.Empty<int>();
        }
        internal static string ExtraKey() => "";
        internal static ConfigEntry<float> OrderLineWidth;
        internal static ConfigEntry<string> OrderLineValid, OrderLineInvalid;
        internal static ConfigEntry<bool> ShowAimPointer, ItemWheel, ItemWheelInvert, ItemKeysEnabled;
        internal static ConfigEntry<string> ItemList, ItemModifier;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<bool> GaOn, GaElevation;
        internal static ConfigEntry<float> GaSpeed, GaSpinUp, GaSpinCurve, GaFovWide, GaFovNarrow;

        public override void Load()
        {
            L = Log;
            Instance = this;

            ZoomScaling = Config.Bind("Aiming", "ZoomScaling", "fov",
                "game = game curve. fov = same on-screen aim speed at every zoom step.");
            ZoomCurve = Config.Bind("Aiming", "ZoomCurve", 1.9f,
                new ConfigDescription("Gunner sight and third person, with ZoomScaling = fov: 1 = same on-screen speed at every zoom, above 1 = slower when zoomed in, below 1 = faster. (The cupola has its own: Cupola/ZoomCurve; commander vision ports use the game's values.)", new AcceptableValueRange<float>(0.25f, 3f)));
            MouseSmoothing = Config.Bind("Aiming", "MouseSmoothing", 1f,
                new ConfigDescription("Normal mouse aiming smoothing, in seconds (gunner sight and third person only; never the cupola camera or the commander head-out). 0 = off (game).", new AcceptableValueRange<float>(0f, 1f)));

            FireShake = Config.Bind("Shake", "FireShake", 0.5f,
                new ConfigDescription("Camera shake when guns fire. 0 = none, 0.5 = half. Game: 1.", new AcceptableValueRange<float>(0f, 2f)));

            OrderLineWidth = Config.Bind("OrderLine", "Width", 0.5f,
                new ConfigDescription("Width of the order line from your units to the cursor. Game: 1.", new AcceptableValueRange<float>(0.1f, 5f)));
            OrderLineValid = Config.Bind("OrderLine", "ValidColor", "",
                "Colour when the order is valid (game: green), as hex RRGGBB or RRGGBBAA. Empty = game colour.");
            OrderLineInvalid = Config.Bind("OrderLine", "InvalidColor", "",
                "Colour when the order is not valid (game: red), as hex RRGGBB or RRGGBBAA. Empty = game colour.");

            CupolaZoomCurve = Config.Bind("Cupola", "ZoomCurve", 1.3f,
                new ConfigDescription("External cupola camera (commander inside, camera on top of the turret): mouse speed per zoom step, like Aiming/ZoomCurve for the gunner sight (needs ZoomScaling = fov): 1 = same on-screen speed at every zoom, above 1 = slower when zoomed in, so long-range spotting is not jumpy. -1 = game values (same turn rate at every zoom). Head-out and vision-port views: see HeadOut and VisionPorts.", new AcceptableValueRange<float>(-1f, 3f)));
            CupolaHeadOut = Config.Bind("Cupola", "HeadOut", true,
                "Also use ZoomCurve when the commander has his head out of the hatch (low / full peek, with or without binoculars), from the interior or the external cupola camera. false = the game's turn rate there.");
            CupolaPorts = Config.Bind("Cupola", "VisionPorts", true,
                "Also use ZoomCurve when looking out through a vision port / the cupola glass from inside (the zoomable view with the lens edge). false = the game's turn rate there.");
            CupolaStabilize = Config.Bind("Binoculars", "Stabilize", 1f,
                new ConfigDescription("Commander head-out of the hatch (with or without binoculars up; not the external cupola camera): how much of the tank's pitch and roll is taken out of the view. 0 = bolted to the tank (game), 1 = perfectly level. Heading still follows the tank; mouse look is untouched. Live.", new AcceptableValueRange<float>(0f, 1f)));
            CupolaSmoothing = Config.Bind("Binoculars", "Smoothing", 0f,
                new ConfigDescription("Seconds his head takes to follow the tilt that is left: eases out jolts. 0 = instant. Live.", new AcceptableValueRange<float>(0f, 2f)));

            ShowAimPointer = Config.Bind("HUD", "AimPointer", false,
                "Show the small white aim dot in the middle of the screen (commander mode). Game: on.");

            ItemList = Config.Bind("ItemHotkeys", "Items", "Wrench,Assembly,Canister,AmmoCrate",
                "On-foot items for Ctrl+1, 2, 3, ... and the mouse wheel, in order. Names: Wrench, Assembly, Disassembly, Canister, AmmoCrate, TowRope, Extinguisher, Axe, Brush, Torch, Binoculars, Flag, CamoNet, Checklist.");
            ItemKeysEnabled = Config.Bind("ItemHotkeys", "KeysEnabled", true,
                "Modifier + 1, 2, 3, ... picks the item in that position of the list (on foot only).");
            ItemModifier = Config.Bind("ItemHotkeys", "Modifier", "ctrl",
                "Modifier for the item keys: ctrl, alt or shift.");
            ItemWheel = Config.Bind("ItemHotkeys", "MouseWheel", true,
                "Mouse wheel cycles through the items in the list (on foot, while holding one of them or nothing).");
            ItemWheelInvert = Config.Bind("ItemHotkeys", "InvertWheel", false,
                "false = scrolling down goes to the next item in the list (1 -> 2), true = the other way.");

            GaOn = Config.Bind("GunnerAim", "TurretInertia", true,
                "Manual gunner-sight aiming (not vector aiming) gets turret inertia: the mouse moves a target and the turret follows it, spinning up to its real traverse speed and braking onto the target, like vector aiming. false = game behaviour (the turret follows the mouse instantly).");
            GaSpeed = Config.Bind("GunnerAim", "TraverseSpeed", 1f,
                new ConfigDescription("Top traverse speed as a multiple of the tank's real traverse speed (from its full-rotation time). 1 = real.", new AcceptableValueRange<float>(0.25f, 5f)));
            GaSpinUp = Config.Bind("GunnerAim", "SpinUpSeconds", 2f,
                new ConfigDescription("Time the turret takes to reach top speed (and to brake from it) at the narrowest zoom (see SpinUpCurve). Lower = snappier, higher = heavier.", new AcceptableValueRange<float>(0.02f, 3f)));
            GaSpinCurve = Config.Bind("GunnerAim", "SpinUpCurve", 1.7f,
                new ConfigDescription("Spin-up time follows the zoom: 0 s at SpinUpFovWide, SpinUpSeconds at SpinUpFovNarrow, along this curve (1 = straight line, above 1 = stays light until the narrower zooms). 0 = SpinUpSeconds at every zoom.", new AcceptableValueRange<float>(0f, 5f)));
            GaFovWide = Config.Bind("GunnerAim", "SpinUpFovWide", 15f,
                new ConfigDescription("Zoom (FOV) where spin-up starts: at this zoom and wider the turret turns instantly (still at its top speed). Gunner zoom levels: 25, 15, 10, 5.", new AcceptableValueRange<float>(1f, 90f)));
            GaFovNarrow = Config.Bind("GunnerAim", "SpinUpFovNarrow", 5f,
                new ConfigDescription("Zoom (FOV) with the full SpinUpSeconds. The game's narrowest gunner zoom: 5.", new AcceptableValueRange<float>(1f, 90f)));
            GaElevation = Config.Bind("GunnerAim", "AlsoElevation", true,
                "Also give gun elevation the same inertia (it uses the gun's own elevation speed).");

            VerboseLog = Config.Bind("Debug", "VerboseLog", false,
                "Write diagnostic lines to the log.");

            Config.Save();
            Config.SaveOnConfigSet = false;
            StartConfigWatcher();

            var harmony = new Harmony(Guid);
            try { harmony.PatchAll(typeof(FireShakePatch)); } catch (Exception e) { L.LogWarning("Shake patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(GameItemKeys)); } catch (Exception e) { L.LogWarning("Item key patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(AimPointer)); } catch (Exception e) { L.LogWarning("Aim pointer patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(OrderLine)); } catch (Exception e) { L.LogWarning("Order line patches failed: " + e.Message); }
            try { harmony.PatchAll(typeof(CupolaBalance)); } catch (Exception e) { L.LogWarning("Cupola balance patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(CupolaLook)); } catch (Exception e) { L.LogWarning("Cupola look patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(Binoculars)); } catch (Exception e) { L.LogWarning("Binoculars patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(GunnerAim)); } catch (Exception e) { L.LogWarning("Gunner aim patch failed: " + e.Message); }
            try { harmony.PatchAll(typeof(MouseSmooth)); } catch (Exception e) { L.LogWarning("Mouse smoothing patches failed: " + e.Message); }
            try
            {
                var update = AccessTools.DeclaredMethod(typeof(MEC.Timing), "Update", Type.EmptyTypes);
                if (update != null) harmony.Patch(update, postfix: new HarmonyMethod(typeof(Plugin), nameof(Tick)));
                else L.LogWarning("MEC.Timing.Update not found - per-frame updates disabled.");
            }
            catch (Exception e) { L.LogWarning("Could not install the per-frame update: " + e.Message); }
            V("Control Tweaks Lite " + Version + " loaded.");
        }

        internal static void V(string s) { if (VerboseLog != null && VerboseLog.Value) L.LogInfo(s); }
        internal static string ConfigDirPublic => System.IO.Path.GetDirectoryName(Instance.Config.ConfigFilePath);

        // Postfix on MEC.Timing.Update (runs every frame).
        public static void Tick()
        {
            Safe("BinocularsRestore", Binoculars.Restore);
            Safe("Reload", ReloadTick);
            Safe("AimInfo", AimInfo.Tick);
            Safe("AimTweaks", AimTweaks.Tick);
            Safe("AimPointer", AimPointer.Tick);
            Safe("ItemHotkeys", ItemHotkeys.Tick);
            Safe("CupolaBalance", CupolaBalance.Tick);
        }

        // Each feature runs on its own. If one hits a type the game no longer has (usually after a game
        // update), it is switched off with a single warning instead of throwing every frame.
        private static readonly System.Collections.Generic.HashSet<string> _dead = new();
        private static float _nextSlowLog;
        private static readonly System.Collections.Generic.Dictionary<string, int> _errs = new();
        private static void Safe(string name, Action tick)
        {
            if (_dead.Contains(name)) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { tick(); }
            catch (Exception e) when (e is TypeLoadException || e is MissingMethodException || e is MissingFieldException
                                      || e.InnerException is TypeLoadException)
            {
                _dead.Add(name);
                L.LogWarning($"[{name}] disabled: the game changed and this feature needs a rebuild ({e.GetType().Name}: {e.Message})");
            }
            catch (Exception e)
            {
                _errs.TryGetValue(name, out int n); _errs[name] = ++n;
                if (n <= 3) L.LogError($"[{name}] {e}");
                if (n == 50) { _dead.Add(name); L.LogWarning($"[{name}] disabled after repeated errors."); }
            }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 4 && VerboseLog.Value && Time.unscaledTime >= _nextSlowLog) { _nextSlowLog = Time.unscaledTime + 1f; L.LogWarning($"[Perf] Control Tweaks {name} took {ms:0.0} ms this frame"); }
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
            try { Instance?.Config.Reload(); V("Config reloaded."); }
            catch (Exception e) { L.LogWarning("Config reload failed: " + e.Message); }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // VerboseLog only: writes the turret mouse-aim values - aim mode, sensitivities and per-zoom-step aim multipliers -
    // when you get in, change aim mode/camera, or change zoom step.
    internal static class AimInfo
    {
        private static float _next;
        private static IntPtr _lastInput, _lastMode;
        private static int _lastStep = int.MinValue;
        private static float _lastFovSens = float.NaN;

        private static void L(string s) => Plugin.V("[Aim] " + s);

        internal static void Tick()
        {
            if (!Plugin.VerboseLog.Value) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.2f;
            try
            {
                var va = Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAiming.LocalPlayerInstance;
                var vc = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance;
                if (va == null || vc == null) { _lastInput = _lastMode = IntPtr.Zero; _lastStep = int.MinValue; return; }

                Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputBase input = null;
                try { input = va.Model?.CurrentAimingWeapon?.CurrentInputController; } catch { }
                Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode mode = null;
                try { mode = vc.CurrentCameraMode; } catch { }

                IntPtr ip = input?.Pointer ?? IntPtr.Zero, mp = mode?.Pointer ?? IntPtr.Zero;
                if (ip != _lastInput || mp != _lastMode)
                {
                    _lastInput = ip; _lastMode = mp; _lastStep = int.MinValue;
                    Snapshot(va, vc, input, mode);
                }

                int step = -1; float fov = 0, mult = float.NaN, fovSens = float.NaN;
                try { if (mode != null) { step = mode._CurrentCameraFOVStepIndex; fov = mode.CurrentFOV; var st = mode._CurrentCameraFOVStep; if (st != null) mult = st.AimMultiplier; } } catch { }
                try { fovSens = vc.RotationFOVBasedSensitivity; } catch { }
                if (step != _lastStep || (!float.IsNaN(fovSens) && Math.Abs(fovSens - _lastFovSens) > 0.0005f))
                {
                    _lastStep = step; _lastFovSens = fovSens;
                    L($"zoom step {step}: FOV {fov:0.#}, step AimMultiplier {mult:0.###}, camera RotationFOVBasedSensitivity {fovSens:0.###}");
                }
            }
            catch (Exception e) { Plugin.V("[Aim] " + e.Message); }
        }

        private static void Snapshot(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAiming va,
            Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController vc,
            Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputBase input,
            Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode mode)
        {
            string type = "none", aimType = "?";
            try { if (input != null) type = input.GetIl2CppType().Name; } catch { }
            try { aimType = Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingModel.CurrentAimInputType.ToString(); } catch { }
            string modeName = "?"; try { modeName = mode?.gameObject.name ?? "none"; } catch { }
            L($"---- aim input {type} ({aimType}), camera '{modeName}' ----");

            try
            {
                {
                    L($"settings: VehicleAim sens {Com.DeGenerals.TankSquad.Managers.ContentManager.MouseSensitivityVehicleAim:0.###}, VehicleTP sens {Com.DeGenerals.TankSquad.Managers.ContentManager.MouseSensitivityVehicleTP:0.###}, AimSensitivity {Com.DeGenerals.TankSquad.Managers.ContentManager.AimSensitivity:0.###}, UseVectorAiming {Com.DeGenerals.TankSquad.Managers.ContentManager.UseVectorAiming}");
                }
            }
            catch (Exception e) { L("settings: " + e.Message); }

            if (input != null)
            {
                try { L($"input: TurnRateSpeedMod {Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputBase.TurnRateSpeedMod:0.###}, AimRange {Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputBase.AimRange:0.#}"); } catch { }
            }

            try
            {
                var d = mode?._Data;
                if (d != null)
                {
                    var steps = d.CameraFOVSteps;
                    var parts = new List<string>();
                    if (steps != null) for (int i = 0; i < steps.Length; i++) { var st = steps[i]; if (st != null) parts.Add($"[{i}] FOV {st.FOVValue} x{st.AimMultiplier:0.###}"); }
                    L($"zoom steps: {(parts.Count == 0 ? "none" : string.Join(", ", parts))}");
                    if (d.HasQuickZoom && d.QuickZoomFOV != null) L($"quick zoom: FOV {d.QuickZoomFOV.FOVValue} x{d.QuickZoomFOV.AimMultiplier:0.###}");
                }
            }
            catch (Exception e) { L("zoom steps: " + e.Message); }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // Which of the tank's views the player is in.
    //   Cupola  = the external first-person camera on top of the turret (commander still inside)
    //   Binoculars = the commander head-out of the hatch (VehicleCommanderCameraMode), with or without binoculars up
    //   Visor   = commander vision ports;  Gunner = gunner sight;  Third = third-person camera
    internal enum TankView { None, Gunner, Cupola, Binoculars, Visor, Third, Other }
    internal static class Views
    {
        private static float _at = -1f; private static TankView _v; private static IntPtr _logged; private static TankView _loggedView; private static string _lastState = "";
        internal static TankView Current
        {
            get
            {
                if (Time.unscaledTime - _at < 0.1f) return _v;
                _at = Time.unscaledTime;
                try { var m = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance?.CurrentCameraMode; _v = Of(m); }
                catch { _v = TankView.None; }
                return _v;
            }
        }

        // Commander's head out of the hatch: low or full peek, or binoculars up (closed / opened hatch = inside).
        private static bool CommanderOutside()
        {
            bool outside = false; string st = "no character";
            try
            {
                var ch = Com.DeGenerals.TankSquad.Characters.CharacterInVehicleController.LocalPlayer;
                if (ch != null)
                {
                    var peek = ch.CurrentPeekState;
                    bool binos = ch.IsInBinocularsMode, peeking = ch.IsPeekingOut;
                    outside = binos || peeking || (int)peek >= 2;
                    st = $"{peek}, peeking {peeking}, binoculars {binos}";
                }
            }
            catch (Exception e) { st = "error " + e.Message; }
            if (st != _lastState) { _lastState = st; Plugin.V($"[Views] commander state: {st}"); }
            return outside;
        }

        internal static TankView Of(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode m)
        {
            if (m == null) return TankView.None;
            TankView v;
            string name = "?"; try { name = m.gameObject.name; } catch { }
            if (m.TryCast<Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode>() != null)
                // One camera ('CommanderCamera') serves both: commander inside = external cupola camera,
                // head out of the hatch (peeking) or binoculars up = binoculars view.
                v = CommanderOutside() ? TankView.Binoculars : TankView.Cupola;
            else if (m.TryCast<Com.DeGenerals.TankSquad.Vehicle.VehicleGunnerCameraMode>() != null) v = TankView.Gunner;
            else if (m.TryCast<Com.DeGenerals.TankSquad.Vehicle.VehicleTPCameraMode>() != null) v = TankView.Third;
            else
            {
                var fp = m.TryCast<Com.DeGenerals.TankSquad.Vehicle.VehicleFPCameraMode>();
                if (fp == null) v = TankView.Other;
                else
                {
                    bool visor = name.IndexOf("Visor", StringComparison.OrdinalIgnoreCase) >= 0;
                    try { visor |= fp._UseVehicleVisorsReticle; } catch { }
                    // the commander's interior camera ('CommanderInteriorCamera') stays in use when he peeks out
                    // from inside and raises the binoculars there
                    if (visor) v = TankView.Visor;
                    else if (name.IndexOf("Commander", StringComparison.OrdinalIgnoreCase) >= 0 && CommanderOutside()) v = TankView.Binoculars;
                    else v = TankView.Cupola;
                }
            }
            if (m.Pointer != _logged || v != _loggedView) { _logged = m.Pointer; _loggedView = v; Plugin.V($"[Views] {v}: '{name}' ({m.GetIl2CppType().Name})"); }
            return v;
        }
    }

    // Zoom aim speed: each camera zoom step has an aim multiplier. ZoomScaling = fov replaces the game's curve with
    // multiplier = widest step's multiplier x (FOV / widest FOV) ^ ZoomCurve.
    internal static class AimTweaks
    {
        private static readonly Dictionary<IntPtr, float> OrigStepMult = new();
        private static IntPtr _lastMode;
        private static string _lastScaling;

        private static bool Fov => string.Equals(Plugin.ZoomScaling.Value?.Trim(), "fov", StringComparison.OrdinalIgnoreCase);

        internal static void Tick()
        {
            try
            {
                var vc = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance;
                if (vc == null) { _lastMode = IntPtr.Zero; return; }
                Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode mode = null;
                try { mode = vc.CurrentCameraMode; } catch { }
                if (mode == null) return;
                string sc = Plugin.ZoomScaling.Value + "|" + Plugin.ZoomCurve.Value.ToString("0.###") + "|" + (Plugin.CupolaZoomCurve?.Value ?? -1f).ToString("0.###") + "|" + Plugin.ExtraKey() + "|" + Views.Current;
                if (mode.Pointer != _lastMode || sc != _lastScaling)
                {
                    _lastMode = mode.Pointer; _lastScaling = sc;
                    ApplyZoom(mode, vc);
                }
            }
            catch (Exception e) { Plugin.V("[Aim] tick: " + e.Message); }
        }

        private static void ApplyZoom(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode mode, Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController vc)
        {
            try
            {
                var d = mode._Data;
                if (d == null) return;
                var steps = d.CameraFOVSteps;
                var all = new List<Com.DeGenerals.TankSquad.Vehicle.VehicleCameraFOVStep>();
                if (steps != null) for (int i = 0; i < steps.Length; i++) if (steps[i] != null) all.Add(steps[i]);
                if (d.HasQuickZoom && d.QuickZoomFOV != null) all.Add(d.QuickZoomFOV);
                if (all.Count == 0) return;

                foreach (var st in all) if (!OrigStepMult.ContainsKey(st.Pointer)) OrigStepMult[st.Pointer] = st.AimMultiplier;

                // Reference = the widest step with its game multiplier.
                var reff = all[0];
                foreach (var st in all) if (st.FOVValue > reff.FOVValue) reff = st;
                float refMult = OrigStepMult[reff.Pointer], refFov = Math.Max(1, reff.FOVValue);

                string camName = "?"; try { camName = mode.gameObject.name; } catch { }
                // Binoculars and vision ports keep the game's own values; the external cupola camera has its own curve.
                var view = Views.Of(mode);
                bool cupola = view == TankView.Cupola;
                float curve = cupola ? (Plugin.CupolaZoomCurve?.Value ?? -1f) : Plugin.ZoomCurve.Value;
                string which = cupola ? "Cupola ZoomCurve" : "ZoomCurve";
                bool excluded = view == TankView.Binoculars || view == TankView.Visor || cupola;   // cupola: scaled in CupolaLook
                bool useFov = Fov && !excluded;
                var parts = new List<string>();
                foreach (var st in all)
                {
                    float m = useFov ? refMult * (float)Math.Pow(st.FOVValue / refFov, curve) : OrigStepMult[st.Pointer];
                    st.AimMultiplier = m;
                    parts.Add($"FOV {st.FOVValue} x{m:0.###}");
                }
                try { var cur = mode._CurrentCameraFOVStep; if (cur != null) vc._RotationFOVBasedSensitivity_k__BackingField = cur.AimMultiplier; } catch { }
                string name = "?"; try { name = mode.gameObject.name; } catch { }
                Plugin.V($"[Aim] '{name}' zoom aim ({(useFov ? which + " " + curve.ToString("0.##") : excluded ? "game - left to the game" : "game")}): {string.Join(", ", parts)}");
            }
            catch (Exception e) { Plugin.V("[Aim] zoom: " + e.Message); }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // Normal mouse aiming (AimingWithMouseAuto): low-pass the mouse input so camera and turret get a little momentum.
    // Movement is delayed, never lost - the smoothed deltas add up to the same total.
    [HarmonyPatch]
    internal static class MouseSmooth
    {
        private static readonly Dictionary<IntPtr, (int frame, Vector2 outV, Vector2 state)> Cam = new();
        private static float _nextLogCam, _nextLogFp;
        private static bool _camSeen;

        private static bool Active()
        {
            float t = Plugin.MouseSmoothing.Value;
            if (t <= 0f) return false;
            var v = Views.Current;
            if (v == TankView.Cupola || v == TankView.Binoculars || v == TankView.Visor) return false;     // commander views: no smoothing
            try
            {
                return Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingModel.CurrentAimInputType
                    == Com.DeGenerals.TankSquad.Vehicle.Weapon.AimInputType.AimingWithMouseAuto;
            }
            catch { return false; }
        }

        private static float Alpha() => 1f - (float)Math.Exp(-Math.Max(Time.deltaTime, 0.0001f) / Math.Max(0.001f, Plugin.MouseSmoothing.Value));

        // Camera look input (third person and sights).
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode), "HandleRotationInput")]
        private static void CamInput(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode __instance, ref Vector2 __result)
        {
            try
            {
                if (!Active()) { Cam.Clear(); return; }
                if (!_camSeen) { _camSeen = true; Plugin.V("[Aim] camera look input hooked"); }
                int f = Time.frameCount;
                var key = __instance.Pointer;
                Cam.TryGetValue(key, out var st);
                if (st.frame == f) { __result = st.outV; return; }          // called twice in one frame: same answer
                var raw = __result;
                var s = st.state + (raw - st.state) * Alpha();
                Cam[key] = (f, s, s);
                if (Plugin.VerboseLog.Value && Time.unscaledTime >= _nextLogCam && raw.sqrMagnitude > 0f)
                {
                    _nextLogCam = Time.unscaledTime + 1f;
                    Plugin.V($"[Aim] smooth camera: in ({raw.x:0.###},{raw.y:0.###}) out ({s.x:0.###},{s.y:0.###})");
                }
                __result = s;
            }
            catch { }
        }

        // Turret turn input in first-person sights: smooth the mouse input the sight mode reads.
        private static readonly Dictionary<IntPtr, (int frame, Vector2 state)> Sight = new();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputMouseAuto), "RotateInFPCameraManual")]
        private static void SightInput(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleAimingInputMouseAuto __instance)
        {
            try
            {
                if (!Active()) { Sight.Clear(); return; }
                int f = Time.frameCount;
                var key = __instance.Pointer;
                Sight.TryGetValue(key, out var st);
                var raw = __instance.mouseInput;
                if (st.frame == f) { __instance.mouseInput = st.state; return; }
                var s = st.state + (raw - st.state) * Alpha();
                Sight[key] = (f, s);
                if (Plugin.VerboseLog.Value && Time.unscaledTime >= _nextLogFp && raw.sqrMagnitude > 0f)
                {
                    _nextLogFp = Time.unscaledTime + 1f;
                    Plugin.V($"[Aim] smooth sight: in ({raw.x:0.###},{raw.y:0.###}) out ({s.x:0.###},{s.y:0.###})");
                }
                __instance.mouseInput = s;
            }
            catch { }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // Gun firing camera shake: each weapon has a camera impulse; its strength follows the velocity it is fired with,
    // so that is scaled. The "you got hit" impulse is left alone.
    [HarmonyPatch]
    internal static class FireShakePatch
    {
        private static bool _logged;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CustomCameraImpulse), nameof(CustomCameraImpulse.GenerateImpulse))]
        private static bool Prefix(CustomCameraImpulse __instance, ref Vector3 __1)
        {
            try
            {
                float k = Plugin.FireShake.Value;
                if (Math.Abs(k - 1f) < 0.001f) return true;
                var cm = Com.DeGenerals.TankSquad.Managers.CameraManager.Instance;
                var hit = cm != null ? cm._OnHitCameraImpulse : null;
                if (hit != null && hit.Pointer == __instance.Pointer) return true;     // hit shake: untouched
                if (!_logged) { _logged = true; Plugin.V($"[Shake] firing shake x{k:0.##} ('{__instance.name}')"); }
                if (k <= 0f) return false;
                __1 *= k;
                return true;
            }
            catch { return true; }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // The order line (unit -> cursor while giving an order): width and the valid / invalid colours.
    [HarmonyPatch]
    internal static class OrderLine
    {
        private static readonly Dictionary<IntPtr, float> OrigWidth = new();
        private static bool _inOrders;
        private static bool _logged;

        private static bool TryHex(string s, out Color c)
        {
            c = default;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;
            try
            {
                byte P(int i) => Convert.ToByte(s.Substring(i, 2), 16);
                c = new Color(P(0) / 255f, P(2) / 255f, P(4) / 255f, s.Length == 8 ? P(6) / 255f : 1f);
                return true;
            }
            catch { return false; }
        }

        // The game uses green for a valid order and red for an invalid one.
        private static void Recolor(ref Color c)
        {
            bool valid = c.g >= c.r;
            if (TryHex(valid ? Plugin.OrderLineValid.Value : Plugin.OrderLineInvalid.Value, out var mine))
            {
                if (!_logged) { _logged = true; Plugin.V($"[OrderLine] game colour ({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##}) -> {(valid ? "valid" : "invalid")} colour"); }
                c = mine;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Managers.AI.AIOrdersController), "SetOrderReceivingLineRendererColor")]
        private static void OrdersColor(ref Color __0) { try { Recolor(ref __0); _inOrders = true; } catch { } }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Managers.AI.AIOrdersController), "SetOrderReceivingLineRendererColor")]
        private static Exception OrdersColorEnd(Exception __exception) { _inOrders = false; return __exception; }

        // Squads can also be coloured directly (not through the orders controller).
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Squad.CombatSquad), "SetUnitsOrderReceivingLineRendererColor")]
        private static void SquadColor(ref Color __0) { try { if (!_inOrders) Recolor(ref __0); } catch { } }

        // Width: applied whenever a unit's order line is drawn to the cursor.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Units.GenericUnit), "ShowLineRendererToDestination")]
        private static void Draw(Com.DeGenerals.TankSquad.AI.Units.GenericUnit __instance, LineRenderer __0)
        {
            try
            {
                if (__0 == null) return;
                var mine = __instance.OrderReceivingVisualizer;
                if (mine == null || mine.Pointer != __0.Pointer) return;
                if (!OrigWidth.TryGetValue(__0.Pointer, out var w)) { w = __0.widthMultiplier; OrigWidth[__0.Pointer] = w; }
                float k = Plugin.OrderLineWidth.Value;
                if (Math.Abs(__0.widthMultiplier - w * k) > 0.0001f) __0.widthMultiplier = w * k;
            }
            catch { }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // The small white aim dot (GUIManager aim pointer): on/off.
    [HarmonyPatch]
    internal static class AimPointer
    {
        private static float _next;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.UI.GUIManager), "ManageAimPointer")]
        private static void Prefix(ref bool __0)
        {
            if (__0 && !Plugin.ShowAimPointer.Value) { __0 = false; Plugin.V("[HUD] aim dot kept hidden"); }
        }

        // Also covers the dot being switched on by other code.
        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            try
            {
                bool show = Plugin.ShowAimPointer.Value;
                var g = Com.DeGenerals.TankSquad.UI.GUIManager.Instance?.AimPointerParent;
                if (g == null) return;
                if (!show && g.activeSelf) g.SetActive(false);
                // Switched back on: the game shows it again the next time it changes mode.
            }
            catch { }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // While the item-key modifier is held, the game's own 1..8 item keys stay quiet (Ctrl+1 must not also pick slot 1).
    [HarmonyPatch]
    internal static class GameItemKeys
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory), "OnSwitchWeaponKey")]
        private static bool Prefix()
        {
            try
            {
                if (!Plugin.ItemKeysEnabled.Value) return true;
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb == null || !ItemHotkeys.ModifierHeldPublic(kb);
            }
            catch { return true; }
        }
    }

    // On foot: Ctrl+1..n picks a tool by type, the mouse wheel cycles through the same list.
    // Uses the game's own "switch to item of this type" call.
    internal static class ItemHotkeys
    {
        private static float _nextWheel, _nextFind, _nextSkipLog;
        private static string _lastSkip;

        private static float _nextDump;

        // What the character is carrying: slot, slot type, item, its tool type, quantity; plus what is in hand.
        private static void Dump(Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory inv)
        {
            if (Time.unscaledTime < _nextDump) return;
            _nextDump = Time.unscaledTime + 5f;
            try
            {
                string hand = "nothing";
                try { var c = inv.CurrentItem; if (c != null) hand = $"'{c.name}' ({c.HoldableType})"; } catch { }
                Plugin.V($"[Items] inventory of '{inv.gameObject.name}' - in hand: {hand}");
                var slots = inv.Slots;
                if (slots == null) { Plugin.V("[Items]   no slots list"); return; }
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i];
                    if (sl == null) { Plugin.V($"[Items]   slot {i + 1}: null"); continue; }
                    string item = "empty", type = "";
                    try { var it = sl.ItemReference; if (it != null) { item = it.name; type = it.HoldableType.ToString(); } } catch { }
                    string data = "?"; try { data = sl.ItemData != null ? sl.ItemData.name : "none"; } catch { }
                    int q = 0; try { q = sl.ItemQuantity; } catch { }
                    Plugin.V($"[Items]   slot {i + 1} ({sl.Type}): {item} {type} | data '{data}' x{q}");
                }
                foreach (var t in Types) Plugin.V($"[Items]   game finds {t}: {(Has(inv, t, out var x) ? "'" + x.name + "'" : "no")}");
            }
            catch (Exception e) { Plugin.V("[Items] dump failed: " + e.Message); }
        }

        // Always logged (rate-limited): why a key press or scroll did nothing.
        private static void Skip(string what, string why)
        {
            string m = $"[Items] {what} ignored: {why}";
            if (m == _lastSkip && Time.unscaledTime < _nextSkipLog) return;
            _lastSkip = m; _nextSkipLog = Time.unscaledTime + 2f;
            Plugin.V(m);
        }
        private static Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory _inv;
        private static string _listKey;
        private static readonly List<Com.DeGenerals.TankSquad.Items.HoldableType> Types = new();

        private static void ParseList()
        {
            string v = Plugin.ItemList.Value ?? "";
            if (v == _listKey) return;
            _listKey = v; Types.Clear();
            foreach (var w in v.Split(','))
            {
                var t = w.Trim(); if (t.Length == 0) continue;
                bool found = false;
                foreach (Com.DeGenerals.TankSquad.Items.HoldableType h in Enum.GetValues(typeof(Com.DeGenerals.TankSquad.Items.HoldableType)))
                {
                    string n = h.ToString(); int us = n.LastIndexOf('_'); if (us > 0) n = n.Substring(0, us);
                    if (string.Equals(n, t, StringComparison.OrdinalIgnoreCase)) { Types.Add(h); found = true; break; }
                }
                if (!found) Plugin.L.LogWarning($"[Items] unknown item name '{t}'");
            }
            Plugin.V($"[Items] item list: {string.Join(", ", Types)} (keys {(Plugin.ItemKeysEnabled.Value ? "on" : "off")}, wheel {(Plugin.ItemWheel.Value ? "on" : "off")})");
        }

        internal static bool ModifierHeldPublic(UnityEngine.InputSystem.Keyboard kb) => ModifierHeld(kb);
        private static bool ModifierHeld(UnityEngine.InputSystem.Keyboard kb)
        {
            switch ((Plugin.ItemModifier.Value ?? "ctrl").Trim().ToLowerInvariant())
            {
                case "alt": return kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
                case "shift": return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                default: return kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            }
        }

        private static Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory Inventory()
        {
            if (_inv != null && Time.unscaledTime < _nextFind) return _inv;
            _nextFind = Time.unscaledTime + 2f;
            _inv = null;
            try
            {
                var me = Com.DeGenerals.TankSquad.Characters.CharacterInVehicleController.LocalPlayer;
                if (me != null) _inv = me._InventoryController;
            }
            catch { }
            if (_inv == null)
            {
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType<Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory>();
                    Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory input = null, state = null, any = null;
                    var desc = new List<string>();
                    foreach (var i in all)
                    {
                        if (i == null) continue;
                        bool ia = false, sa = false, en = false;
                        try { ia = i.HasInputAuthority; } catch { }
                        try { sa = i.HasStateAuthority; } catch { }
                        try { en = i.isActiveAndEnabled; } catch { }
                        desc.Add($"'{i.gameObject.name}' input={ia} state={sa} active={en}");
                        if (ia && input == null) input = i;
                        if (sa && en && state == null) state = i;
                        if (en && any == null) any = i;
                    }
                    // Prefer the one this machine controls; a lone character is ours too.
                    _inv = input ?? (all.Length == 1 ? any : null) ?? state ?? any;
                    if (_inv != null && Plugin.VerboseLog.Value || _inv == null)
                        Plugin.V($"[Items] characters with an inventory: {all.Length} ({string.Join("; ", desc)}) -> using {(_inv != null ? "'" + _inv.gameObject.name + "'" : "none")}");
                }
                catch (Exception e) { Plugin.V("[Items] inventory search failed: " + e.Message); }
            }
            return _inv;
        }

        private static bool OnFoot()
        {
            try { if (Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer != null) return false; } catch { }
            try { var me = Com.DeGenerals.TankSquad.Characters.CharacterInVehicleController.LocalPlayer; if (me != null && me.CurrentSlot != null) return false; } catch { }
            return true;
        }

        private static bool Has(Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory inv, Com.DeGenerals.TankSquad.Items.HoldableType t, out Com.DeGenerals.TankSquad.Items.InventoryItem item)
        {
            item = null;
            try { return inv.TryGetItemOfType(t, out item) && item != null; } catch { return false; }
        }

        private static void Pick(Com.DeGenerals.TankSquad.Characters.PlayerCharacterInventory inv, Com.DeGenerals.TankSquad.Items.HoldableType t, string why)
        {
            if (!Has(inv, t, out _)) { Skip(why, $"no {t} in your inventory"); return; }
            inv.ChangeToItemOfType(t);
            Plugin.V($"[Items] {why}: {t}");
        }

        internal static void Tick()
        {
            try
            {
                if (!Plugin.ItemKeysEnabled.Value && !Plugin.ItemWheel.Value) return;
                ParseList();
                if (Types.Count == 0) return;

                var kb = UnityEngine.InputSystem.Keyboard.current;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                bool key = false; int keyIdx = -1;
                if (Plugin.ItemKeysEnabled.Value && kb != null && ModifierHeld(kb))
                {
                    var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
                    for (int i = 0; i < Math.Min(Types.Count, digits.Length); i++)
                        if (digits[i].wasPressedThisFrame) { key = true; keyIdx = i; break; }
                }
                float scroll = 0f;
                if (Plugin.ItemWheel.Value && mouse != null && Time.unscaledTime >= _nextWheel)
                {
                    scroll = mouse.scroll.ReadValue().y;
                    if (mouse.rightButton.isPressed) scroll = 0f;   // aiming: leave the wheel alone
                }
                if (!key && Math.Abs(scroll) < 0.01f) return;
                string what = key ? $"{Plugin.ItemModifier.Value}+{keyIdx + 1}" : $"wheel {(scroll < 0 ? "down" : "up")}";
                if (Cursor.visible && Cursor.lockState == CursorLockMode.None) { Skip(what, "mouse cursor is showing (menu/map)"); return; }
                if (!OnFoot()) { Skip(what, "not on foot (in a vehicle seat)"); return; }
                var inv = Inventory();
                if (inv == null) { Skip(what, "could not find your character's inventory"); return; }
                Dump(inv);
                try { if (inv.IsInputBlocked) { Skip(what, "game has item switching blocked right now"); return; } } catch { }

                if (key) { Pick(inv, Types[keyIdx], $"Ctrl+{keyIdx + 1}"); return; }

                // Wheel: only while holding one of the listed items or nothing, so weapons keep their own wheel use.
                int cur = -1; bool emptyHands = false;
                try
                {
                    var now = inv.CurrentItem;
                    if (now == null) emptyHands = true;
                    else { var ht = now.HoldableType; cur = Types.IndexOf(ht); }
                }
                catch { }
                if (cur < 0 && !emptyHands)
                {
                    string held = "?"; try { held = inv.CurrentItem?.name ?? "?"; } catch { }
                    Skip(what, $"holding '{held}', which is not in the Items list");
                    return;
                }

                bool down = scroll < 0f;
                int dir = (down ^ Plugin.ItemWheelInvert.Value) ? 1 : -1;
                int start = cur < 0 ? (dir > 0 ? -1 : 0) : cur;
                for (int step = 1; step <= Types.Count; step++)
                {
                    int i = ((start + dir * step) % Types.Count + Types.Count) % Types.Count;
                    if (i == cur) break;
                    if (Has(inv, Types[i], out _)) { Pick(inv, Types[i], "wheel"); break; }
                }
                _nextWheel = Time.unscaledTime + 0.12f;
            }
            catch (Exception e) { Plugin.L.LogWarning("[Items] " + e.Message); }
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // Head out of the commander's hatch the camera is pinned to the tank (ParentConstraint), so it pitches and rolls
    // with every lurch. A person braced in a hatch balances against that: keep the heading, counter part of the tilt,
    // and let what is left follow a little late. Done through the constraint's own rotation offset, so mouse look and
    // the cupola turning work as normal.

    [HarmonyPatch]
    internal static class CupolaBalance
    {
        private static Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode _mode;
        private static readonly Dictionary<IntPtr, Vector3> Original = new();     // constraint -> its own rotation offset
        private static readonly Dictionary<IntPtr, Vector3> OriginalPos = new();  // constraint -> its own translation offset
        private static Quaternion _tilt = Quaternion.identity;
        private static Vector3 _pos;
        private static bool _haveTilt, _havePos, _logged;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode), "Activate")]
        private static void OnActivate(Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode __instance) { _mode = __instance; _haveTilt = false; _havePos = false; }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode), "Deactivate")]
        private static void OnDeactivate(Com.DeGenerals.TankSquad.Vehicle.VehicleCommanderCameraMode __instance)
        {
            try { Restore(__instance?.Constraint); } catch { }
            if (_mode != null && __instance != null && _mode.Pointer == __instance.Pointer) _mode = null;
        }

        private static void Restore(UnityEngine.Animations.ParentConstraint c)
        {
            if (c == null || c.WasCollected) return;
            if (Original.TryGetValue(c.Pointer, out var o) && c.sourceCount > 0) c.SetRotationOffset(0, o);
            if (OriginalPos.TryGetValue(c.Pointer, out var op) && c.sourceCount > 0) c.SetTranslationOffset(0, op);
        }

        internal static void Tick()
        {
            var m = _mode;
            if (m == null || m.WasCollected) { _mode = null; return; }
            var c = m.Constraint;
            if (c == null || c.WasCollected || c.sourceCount < 1) return;
            if (!Original.ContainsKey(c.Pointer))
            {
                Original[c.Pointer] = c.GetRotationOffset(0);
                OriginalPos[c.Pointer] = c.GetTranslationOffset(0);
                if (!_logged) { _logged = true; Plugin.V($"[Cupola] constraint on '{c.gameObject.name}': rotation axes {c.rotationAxis}, weight {c.weight}, offset {c.GetRotationOffset(0)}"); }
            }
            Restore(c);     // replaced by Binoculars (steadies the final camera instead)
        }
    }
}

namespace TankSquadControlTweaksLite
{
    // External cupola camera: its zoom is a plain FOV change and its turn rate does not follow the zoom steps, so the
    // mouse look is scaled here directly: speed = widest-FOV speed x (current FOV / widest FOV) ^ Cupola ZoomCurve.
    [HarmonyPatch]
    internal static class CupolaLook
    {
        private static readonly HashSet<string> _logged = new();
        private static bool _scaled;            // HandleRotationInput was scaled during this camera's Handle
        private static Vector3 _before;
        private static bool _diag;
        private static string _lastKey = ""; private static int _zoomLogs;
        private static readonly HashSet<IntPtr> _viaInput = new();
        private static bool Scaled(TankView v) => v == TankView.Cupola || (v == TankView.Binoculars && (Plugin.CupolaHeadOut?.Value ?? true)) || (v == TankView.Visor && (Plugin.CupolaPorts?.Value ?? true));

        // Turn-speed factor for this camera at its current zoom: what the curve wants / what the game applies.
        private static float Factor(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode mode, Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController vc, string via)
        {
            float curve = Plugin.CupolaZoomCurve?.Value ?? -1f;
            if (curve <= 0f || mode == null) return 1f;
            var d = mode._Data; var steps = d?.CameraFOVSteps;
            float wide = 0f, wideMult = 1f;
            if (steps != null) for (int i = 0; i < steps.Length; i++) if (steps[i] != null && steps[i].FOVValue > wide) { wide = steps[i].FOVValue; wideMult = steps[i].AimMultiplier; }
            if (wide <= 0f) return 1f;
            // the zoom actually in use: the camera lens (the interior camera's zoom changes only the lens, not its zoom level)
            float stepFov = mode.CurrentFOV, lens = -1f;
            try { var vcam = mode.VirtualCamera; if (vcam != null) lens = vcam.m_Lens.FieldOfView; } catch { }
            float fov = lens > 0.5f ? lens : stepFov;
            if (fov <= 0f) return 1f;
            // what the game already applied for this zoom step (kept at the game's values for the cupola)
            float game = 1f; try { if (vc != null) game = vc.RotationFOVBasedSensitivity; } catch { }
            if (game <= 0f) game = 1f;
            float want = wideMult * Mathf.Pow(Mathf.Clamp(fov / wide, 0.01f, 1f), curve);
            float k = Mathf.Clamp(want / game, 0.001f, 10f);
            string name = "?"; try { name = mode.gameObject.name; } catch { }
            if (_logged.Add(name)) Plugin.V($"[Cupola] '{name}' mouse look scaled by zoom via {via} (curve {curve:0.##}): FOV {fov:0.#} of {wide:0}, game sensitivity {game:0.###}, x{k:0.###}");
            // every zoom change: what the camera reports, what is really on screen, and the factor used
            float scr = -1f; try { var c = Camera.main; if (c != null) scr = c.fieldOfView; } catch { }
            string key = $"{name}|{fov:0}|{stepFov:0}|{Mathf.Round(scr / 5f)}|{game:0.###}|{Views.Current}";
            if (key != _lastKey && _zoomLogs < 120 && Plugin.VerboseLog.Value)
            {
                _lastKey = key; _zoomLogs++;
                float real = -1f; int step = -1; float vcam = -1f;
                try { var c = Camera.main; if (c != null) real = c.fieldOfView; } catch { }
                try { step = mode._CurrentCameraFOVStepIndex; } catch { }
                try { var v = mode.VirtualCamera; if (v != null) vcam = v.m_Lens.FieldOfView; } catch { }
                Plugin.L.LogInfo($"[Cupola] '{name}' zoom: mode FOV {stepFov:0.#} (step {step}), lens {vcam:0.#}, screen {real:0.#}, game sensitivity {game:0.###}, factor x{k:0.###} via {via} ({Views.Current})");
            }
            return k;
        }

        // External cupola camera (and any camera that reads the mouse through the shared routine).
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode), "HandleRotationInput")]
        private static void After(Com.DeGenerals.TankSquad.Vehicle.VehicleCameraMode __instance, Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController vehicleCamera, ref Vector2 __result)
        {
            try
            {
                if (__instance == null || !Scaled(Views.Of(__instance))) return;
                _viaInput.Add(__instance.Pointer);
                float k = Factor(__instance, vehicleCamera, "input");
                if (k == 1f) return;
                __result *= k;
                _scaled = true;
            }
            catch { }
        }

        // Interior commander camera: it turns with its own code, so the look angle it adds this frame is scaled instead.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleFPCameraMode), "Handle")]
        private static void FpBefore(Com.DeGenerals.TankSquad.Vehicle.VehicleFPCameraMode __instance)
        {
            _scaled = false;
            try { if (__instance != null) _before = __instance.InputAngles; } catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleFPCameraMode), "Handle")]
        private static void FpAfter(Com.DeGenerals.TankSquad.Vehicle.VehicleFPCameraMode __instance)
        {
            try
            {
                if (__instance == null || _scaled) return;                          // already scaled through the input routine
                if (_viaInput.Contains(__instance.Pointer)) return;                  // this camera reads the mouse through the input routine
                if (!Scaled(Views.Of(__instance))) return;
                Vector3 now = __instance.InputAngles;
                Vector3 dA = new Vector3(Mathf.DeltaAngle(_before.x, now.x), Mathf.DeltaAngle(_before.y, now.y), Mathf.DeltaAngle(_before.z, now.z));
                if (dA.sqrMagnitude < 1e-10f) return;
                if (!_diag) { _diag = true; Plugin.V($"[Cupola] interior camera look angle moved by {dA} this frame"); }
                float k = Factor(__instance, __instance._VehicleCameraController, "angles");
                if (k == 1f) return;
                __instance.InputAngles = _before + dA * k;
            }
            catch { }
        }
    }

}
