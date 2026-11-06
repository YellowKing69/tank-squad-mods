// Tank Squad - Quick Crew (BepInEx 6 IL2CPP plugin) v2.2.1
// - Shortens every "hold to..." circle (crew seat switches, get outside, battle stations, crew repair, ...).
// - Shortens the multi-crew / total-control mode toggle and the switch-to-other-vehicle hold.
// - Speeds up hatch peeking (used when going to binoculars with B).
// - Shortens the hatch peek-out fade (main delay on B from a closed hatch) and the seat-change fade.
// - EXPERIMENTAL: speeds up the character animation during the hatch climb (no game setting exists for it).
// - Ctrl+1..Ctrl+5 seat hotkeys (instant seat switch, configurable).
// - Shortens timed waits inside the hatch/peek coroutines (the real ~1.8s binocular climb timer).
// - Ctrl+~ instant multi-crew toggle, Ctrl+V go inside the tank (interior camera).
// - Instant Visors (merged): fast visor/periscope entry, no visor label.
// - Squad attack: look at an enemy, Alt+1/2/3 (Alt+4 = all) orders that squad to attack it.
// - Own AI gunner: ignore infantry, AP-family ammo only.
// - Switch off the AI gunner in your own tank (no turret swinging while you command), Ctrl+G / Ctrl+9 toggle.
// - Cuts the built-in binocular aim delay and speeds up the zoom-in (WeaponZooming).
// (Graphics effects moved to Potato VFX; aim speed / smoothing / firing shake moved to Control Tweaks.)
// - Optional timing log for the binocular sequence, to see where the time goes.
// Harmony-only: no class injection, so it is safe on Unity 2022.3.62 with BepInEx be.788.
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Com.DeGenerals.TankSquad;
using Com.DeGenerals.TankSquad.Characters;
using Com.DeGenerals.TankSquad.Interactions;
using Com.DeGenerals.TankSquad.UI;
using Com.DeGenerals.TankSquad.UI.Ingame;
using Com.DeGenerals.TankSquad.Weapons;
using UnityEngine;

namespace TankSquadQuickCrew
{
    [BepInPlugin(Guid, "Quick Crew", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "quickcrew.tanksquad";
        public const string Version = "2.2.1";

        internal static ManualLogSource L;

        internal static ConfigEntry<float> HoldTimeMultiplier;
        internal static ConfigEntry<float> MinHoldTime;
        internal static ConfigEntry<float> ControlModeSwitchTime;
        internal static ConfigEntry<float> VehicleSwitchTime;
        internal static ConfigEntry<float> PeekSpeed;
        internal static ConfigEntry<float> PeekFadeMultiplier;
        internal static ConfigEntry<float> SlotChangeFadeMultiplier;
        internal static ConfigEntry<bool> VisorFastEnter;
        internal static ConfigEntry<float> VisorHoldTime;
        internal static ConfigEntry<float> VisorAnimationSpeed;
        internal static ConfigEntry<bool> VisorHideLabel;
        internal static ConfigEntry<bool> VisorIncludeGunnerSights;
        internal static ConfigEntry<bool> OwnGunnerAI;
        internal static ConfigEntry<string> GunnerToggleKey;
        internal static ConfigEntry<bool> FreezeTurretWhenOff;
        internal static ConfigEntry<bool> MainGunIgnoresInfantry;
        internal static ConfigEntry<bool> MainGunAPOnly;
        internal static ConfigEntry<bool> AlliedNoInfantry;
        internal static ConfigEntry<string> AlliedNoInfantryModels;
        internal static ConfigEntry<bool> SquadAttackEnabled;
        internal static ConfigEntry<string> SquadAttackModifier;
        internal static ConfigEntry<string> SquadAttackKeys;
        internal static ConfigEntry<string> SquadAttackAllKey;
        internal static ConfigEntry<bool> SquadAttackMove;
        internal static ConfigEntry<float> SquadAttackRange;
        internal static ConfigEntry<string> ExtraModifier;
        internal static ConfigEntry<string> MultiCrewKey;
        internal static ConfigEntry<string> InteriorKey;
        internal static ConfigEntry<bool> SeatHotkeysEnabled;
        internal static ConfigEntry<string> SeatHotkeys;
        internal static ConfigEntry<float> PeekWaitSpeed;
        internal static ConfigEntry<float> CameraBlendSpeed;
        internal static ConfigEntry<float> ClimbAnimationSpeed;
        internal static ConfigEntry<float> MaxClimbBoostSeconds;
        internal static ConfigEntry<float> AimDelayMultiplier;
        internal static ConfigEntry<float> ZoomSpeedMultiplier;
        internal static ConfigEntry<bool> ApplyToAllZoomItems;
        internal static ConfigEntry<bool> VerboseLog;

        public override void Load()
        {
            L = Log;
            Instance = this;

            HoldTimeMultiplier = Config.Bind("Holds", "HoldTimeMultiplier", 0f,
                new ConfigDescription("Hold time for all hold-to-use actions, as a multiplier. Game: 1. 0 = shortest (MinHoldTime).", new AcceptableValueRange<float>(0f, 1f)));
            MinHoldTime = Config.Bind("Holds", "MinHoldTime", 0.05f,
                "Shortest hold allowed, in seconds.");

            ControlModeSwitchTime = Config.Bind("Holds", "ControlModeSwitchTime", 0f,
                "Hold time for the multi-crew toggle, in seconds. Game: 5. -1 = game value.");
            VehicleSwitchTime = Config.Bind("Holds", "VehicleSwitchTime", -1f,
                "Hold time for switching to another vehicle, in seconds. -1 = game value.");

            PeekSpeed = Config.Bind("Peeking", "PeekSpeed", 1f,
                new ConfigDescription("Experimental, no visible effect. Game: 1.", new AcceptableValueRange<float>(1f, 10f)));

            PeekFadeMultiplier = Config.Bind("Peeking", "PeekFadeMultiplier", 0f,
                new ConfigDescription("Hatch-climb fade length, as a multiplier. Game: 1. 0 = shortest.", new AcceptableValueRange<float>(0f, 1f)));
            SlotChangeFadeMultiplier = Config.Bind("Holds", "SlotChangeFadeMultiplier", 0f,
                new ConfigDescription("Seat-change fade length, as a multiplier. Game: 1. 0 = shortest.", new AcceptableValueRange<float>(0f, 1f)));

            SeatHotkeysEnabled = Config.Bind("SeatHotkeys", "Enabled", true,
                "Ctrl+1..5 switch seats instantly.");
            SeatHotkeys = Config.Bind("SeatHotkeys", "Seats", "Commander,Gunner,Driver,RadioOperator,Loader",
                "Seats for Ctrl+1..5, comma separated: Commander, Gunner, Driver, Loader, RadioOperator. Leave an entry empty to skip that key.");

            PeekWaitSpeed = Config.Bind("Peeking", "PeekWaitSpeed", 20f,
                new ConfigDescription("Speed-up for hatch climbing (binoculars, seat changes, hatch open/close). Game: 1.", new AcceptableValueRange<float>(1f, 20f)));

            VisorFastEnter = Config.Bind("Visors", "FastEnter", true,
                "Fast visor / periscope / viewfinder entry. Game: off.");
            VisorHoldTime = Config.Bind("Visors", "HoldTime", 0f,
                "Visor hold time, in seconds. 0 = single tap.");
            VisorAnimationSpeed = Config.Bind("Visors", "AnimationSpeed", 20f,
                "Visor hand-reach speed. Game: 1.");
            VisorHideLabel = Config.Bind("Visors", "HideLabel", true,
                "Hide the visor 'Press F' label (outline stays). Game: off.");
            VisorIncludeGunnerSights = Config.Bind("Visors", "IncludeGunnerSights", true,
                "Also apply the visor settings to gunner sights.");

            OwnGunnerAI = Config.Bind("Gunner", "OwnGunnerAI", false,
                "AI gunner in your tank. false = idle, the turret stays put. Game: true.");
            GunnerToggleKey = Config.Bind("Gunner", "ToggleKey", "g,digit9",
                "Modifier + any of these keys toggles your AI gunner (comma separated). Empty = off.");

            FreezeTurretWhenOff = Config.Bind("Gunner", "FreezeTurretWhenOff", true,
                "Stop the turret tracking while your AI gunner is off. Manual aiming still works.");
            MainGunIgnoresInfantry = Config.Bind("Gunner", "IgnoreInfantry", true,
                "Your AI gunner never targets infantry. Game: off.");
            MainGunAPOnly = Config.Bind("Gunner", "APOnly", true,
                "Your AI gunner only loads AP-type ammo. Game: off.");

            AlliedNoInfantry = Config.Bind("AlliedNoInfantry", "Enabled", true,
                "Allied vehicles of the models below (yours and the AI's) never pick infantry as a target - their main gun is saved for vehicles and guns. Orders you give still count. Live.");
            AlliedNoInfantryModels = Config.Bind("AlliedNoInfantry", "Models", "SU_152",
                "Which vehicles: parts of the vehicle's name, comma separated (SU_152 = the SU-152). Live.");
            SquadAttackEnabled = Config.Bind("SquadAttack", "Enabled", true,
                "Look at an enemy and press a hotkey to order a squad to attack it.");
            SquadAttackModifier = Config.Bind("SquadAttack", "Modifier", "alt",
                "Modifier for the squad attack keys: ctrl, alt or shift.");
            SquadAttackKeys = Config.Bind("SquadAttack", "SquadKeys", "digit1,digit2,digit3",
                "Keys for squad 1, 2, 3, ... comma separated.");
            SquadAttackAllKey = Config.Bind("SquadAttack", "AllSquadsKey", "digit4",
                "Key that sends every squad. Empty = off.");
            SquadAttackMove = Config.Bind("SquadAttack", "MoveTowardTarget", false,
                "false = squad fires from where it is. true = squad drives toward the target.");
            SquadAttackRange = Config.Bind("SquadAttack", "MaxRange", 3000f,
                "How far you can point at a target, in metres.");

            ExtraModifier = Config.Bind("ExtraHotkeys", "Modifier", "ctrl",
                "Modifier for the hotkeys below and the gunner toggle: ctrl, alt or shift.");
            MultiCrewKey = Config.Bind("ExtraHotkeys", "MultiCrewToggle", "backquote",
                "Modifier + key toggles multi-crew mode. Empty = off.");
            InteriorKey = Config.Bind("ExtraHotkeys", "InteriorCamera", "v",
                "Modifier + key goes inside the tank (same as I). Empty = off.");

            CameraBlendSpeed = Config.Bind("Peeking", "CameraBlendSpeed", 1f,
                new ConfigDescription("Experimental, no visible effect. Game: 1.", new AcceptableValueRange<float>(1f, 20f)));
            ClimbAnimationSpeed = Config.Bind("Peeking", "ClimbAnimationSpeed", 1f,
                new ConfigDescription("Experimental, no visible effect. Game: 1.", new AcceptableValueRange<float>(1f, 5f)));
            MaxClimbBoostSeconds = Config.Bind("Peeking", "MaxClimbBoostSeconds", 2f,
                "Safety limit for ClimbAnimationSpeed, in seconds.");

            AimDelayMultiplier = Config.Bind("Binoculars", "AimDelayMultiplier", 0f,
                new ConfigDescription("Delay before the binoculars zoom in, as a multiplier. Game: 1. 0 = none.", new AcceptableValueRange<float>(0f, 1f)));
            ZoomSpeedMultiplier = Config.Bind("Binoculars", "ZoomSpeedMultiplier", 1f,
                new ConfigDescription("Binocular zoom speed. Game: 1.", new AcceptableValueRange<float>(0.1f, 10f)));
            ApplyToAllZoomItems = Config.Bind("Binoculars", "ApplyToAllZoomItems", false,
                "Also apply the binocular settings to scopes and sights. Game: off.");

            VerboseLog = Config.Bind("Debug", "VerboseLog", false,
                "Write diagnostic lines to the log.");

            // Drop settings that no longer exist from the file.
            try
            {
                var dict = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) as Dictionary<ConfigDefinition, string>;
                if (dict != null)
                {
                    var orphans = new List<ConfigDefinition>();
                    foreach (var k in dict.Keys)
                        if ((k.Section == "Aiming" && k.Key.StartsWith("Drag"))
                            || (k.Section == "Shells" && (k.Key == "RemoveMuzzleSmoke" || k.Key == "MuzzleSmokeMatch" || k.Key == "MuzzleSmokeOpacity"
                                || k.Key == "SmokeOpacity" || k.Key == "RemoveMuzzleBlast" || k.Key == "MuzzleSmoke" || k.Key == "AmbientSmoke"
                                || k.Key == "RingSmoke" || k.Key == "TopSmoke" || k.Key == "OffMapTrails" || k.Key == "OffMapMatch")))
                            orphans.Add(k);
                    foreach (var k in orphans) dict.Remove(k);
                }
            }
            catch { }

            // Write the file once, then stop auto-saving so a reload can never re-trigger the watcher.
            Config.Save();
            Config.SaveOnConfigSet = false;
            StartConfigWatcher();

            OwnGunnerAI.SettingChanged += (_, _) =>
            {
                V($"[Gunner] your AI gunner is now {(OwnGunnerAI.Value ? "ON" : "OFF")} (changed by {GunnerDiag.ChangeSource})");
                GunnerDiag.ChangeSource = "config file reload";
            };

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Patches));
            try { harmony.PatchAll(typeof(OwnGunnerSwitch)); harmony.PatchAll(typeof(GunnerTargetAndAmmo)); harmony.PatchAll(typeof(GunnerDiag)); }
            catch (Exception e) { L.LogWarning("Could not install gunner switch: " + e.Message); }
            try { harmony.PatchAll(typeof(PeekWaitShortener)); }
            catch (Exception e) { L.LogWarning("Could not install peek wait shortener: " + e.Message); }

            // Per-frame safety tick for the climb boost (same game method PotatoMode uses; multiple postfixes are fine).
            try
            {
                var timing = typeof(MEC.Timing);   // direct reference: no assembly scan, no HarmonyX TypeLoad warning
                var update = timing == null ? null : AccessTools.DeclaredMethod(timing, "Update", Type.EmptyTypes);
                if (update != null)
                    harmony.Patch(update, postfix: new HarmonyMethod(typeof(ClimbBoost), nameof(ClimbBoost.Tick)));
                else
                    L.LogWarning("MEC.Timing.Update not found - climb boost will rely on in-game events to restore animation speed.");
            }
            catch (Exception e) { L.LogWarning("Could not install climb boost tick: " + e.Message); }
            if (VerboseLog.Value)
            {
                try { harmony.PatchAll(typeof(ControlModeProbes)); V("Multi-crew timing probes active."); }
                catch (Exception e) { L.LogWarning("Could not install multi-crew probes: " + e.Message); }
            }
            // Logs quiet by default: switch off the log settings once (they can be turned back on afterwards).
            var quiet = Config.Bind("Debug", "QuietLogsApplied", false, "Set by the plugin after it switched its logging off once. Leave as is.");
            if (!quiet.Value) { VerboseLog.Value = false; quiet.Value = true; }
            V("Quick Crew " + Version + " loaded.");
        }

        internal static void V(string s) { if (VerboseLog != null && VerboseLog.Value) L.LogInfo(s); }

        // ---- live config reload: edit the .cfg while the game runs, changes apply within ~1 second ----
        private static System.IO.FileSystemWatcher _watcher;
        private static volatile bool _reloadPending;
        private static float _reloadAt = -1f;
        internal static Plugin Instance;

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

        // Called every frame from the MEC.Timing.Update postfix.
        internal static void ReloadTick()
        {
            if (_reloadPending) { _reloadPending = false; _reloadAt = UnityEngine.Time.unscaledTime + 0.5f; }
            if (_reloadAt < 0f || UnityEngine.Time.unscaledTime < _reloadAt) return;
            _reloadAt = -1f;
            try { Instance?.Config.Reload(); V("Config reloaded."); }
            catch (Exception e) { L.LogWarning("Config reload failed: " + e.Message); }
        }
    }

    internal static class Patches
    {
        // Original values per object, so changing the config never compounds.
        private static readonly Dictionary<IntPtr, float> HoldOriginals = new();
        private static readonly Dictionary<IntPtr, (float speed, float duration)> PeekOriginals = new();

        private static float _binocStart = -1f;

        // ---------------- Hold circles ----------------
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ButtonHoldDetection), nameof(ButtonHoldDetection.StartHolding))]
        private static void StartHolding(ButtonHoldDetection __instance)
        {
            try
            {
                var key = __instance.Pointer;
                if (!HoldOriginals.TryGetValue(key, out var orig))
                {
                    orig = __instance._HoldTime;
                    HoldOriginals[key] = orig;
                }

                float want = Math.Max(Plugin.MinHoldTime.Value, orig * Plugin.HoldTimeMultiplier.Value);
                if (want > orig) want = orig; // never make a hold longer than the game's
                if (Math.Abs(__instance._HoldTime - want) > 0.0001f)
                {
                    __instance._HoldTime = want;
                    Plugin.V($"Hold ({__instance.AssignedInteractionUIType}): {orig:0.###}s -> {want:0.###}s");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("StartHolding patch failed: " + e.Message); }
        }

        // ---------------- Multi-crew / total-control toggle ----------------
        [HarmonyPrefix]
        [HarmonyPatch(typeof(OrdersMenuUIManager), nameof(OrdersMenuUIManager._SwitchTotalControlModeProcess))]
        private static void SwitchTotalControl(OrdersMenuUIManager __instance)
        {
            try
            {
                float want = Plugin.ControlModeSwitchTime.Value;
                if (want < 0f) return;
                float old = __instance._ControlModeSwitchInputTime;
                want = Math.Max(Plugin.MinHoldTime.Value, Math.Min(old, want));
                if (Math.Abs(old - want) > 0.0001f)
                {
                    __instance._ControlModeSwitchInputTime = want;
                    Plugin.V($"Control mode switch hold: {old:0.###}s -> {want:0.###}s");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Control mode patch failed: " + e.Message); }
        }

        // Also apply on key-down, in case the hold coroutine reads the value before it is created.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(OrdersMenuUIManager), nameof(OrdersMenuUIManager.OnStartTotalControlSwitch))]
        private static void StartTotalControl(OrdersMenuUIManager __instance)
        {
            SwitchTotalControl(__instance);
            ControlModeProbes.Mark($"multi-crew key DOWN (hold needed {__instance._ControlModeSwitchInputTime:0.###}s)", reset: true);
        }

        // ---------------- Switch to another vehicle ----------------
        [HarmonyPrefix]
        [HarmonyPatch(typeof(OrdersMenuUIManager), nameof(OrdersMenuUIManager._SwitchVehicle))]
        private static void SwitchVehicle(OrdersMenuUIManager __instance)
        {
            try
            {
                float want = Plugin.VehicleSwitchTime.Value;
                if (want < 0f) return;
                float old = __instance.VehicleSwitchTime;
                want = Math.Max(Plugin.MinHoldTime.Value, Math.Min(old, want));
                if (Math.Abs(old - want) > 0.0001f)
                {
                    __instance.VehicleSwitchTime = want;
                    Plugin.V($"Vehicle switch hold: {old:0.###}s -> {want:0.###}s");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Vehicle switch patch failed: " + e.Message); }
        }

        // ---------------- Peeking speed ----------------
        private static void ApplyPeek(CharacterInVehicleController c)
        {
            try
            {
                var key = c.Pointer;
                if (!PeekOriginals.TryGetValue(key, out var orig))
                {
                    orig = (c._PeekSpeedMultiplier, c._PeekingModeChangeDuration);
                    PeekOriginals[key] = orig;
                    Plugin.V($"Peek originals: speed={orig.speed:0.###} duration={orig.duration:0.###}s " +
                             $"hasAnimatorParam={c._HasPeekSpeedMultiplierParameter}");
                }

                float k = Math.Max(1f, Plugin.PeekSpeed.Value);
                float speed = (orig.speed <= 0f ? 1f : orig.speed) * k;
                float duration = orig.duration / k;
                if (Math.Abs(c._PeekSpeedMultiplier - speed) > 0.0001f) c._PeekSpeedMultiplier = speed;
                if (Math.Abs(c._PeekingModeChangeDuration - duration) > 0.0001f) c._PeekingModeChangeDuration = duration;
            }
            catch (Exception e) { Plugin.L.LogWarning("Peek patch failed: " + e.Message); }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterInVehicleController), nameof(CharacterInVehicleController.TryChangePeekState))]
        private static void TryChangePeekState(CharacterInVehicleController __instance) => ApplyPeek(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterInVehicleController), nameof(CharacterInVehicleController.SwitchSlot))]
        private static void SwitchSlot(CharacterInVehicleController __instance, CrewRoleType crewRole)
        {
            ApplyPeek(__instance);
            string state = "";
            try { state = __instance.CurrentPeekState.ToString(); } catch { }
            Plugin.V($"SwitchSlot -> {crewRole} at {Time.unscaledTime:0.000} (peek state {state})");
            // Peeked out: the character climbs back down before the seat change.
            if (state.Length > 0 && state != "ClosedHatch") ClimbBoost.Start(__instance, "climb down for seat change");
        }

        // ---------------- Fades (hatch peek-out, seat change) ----------------
        private static readonly Dictionary<IntPtr, (float fade, float visible)> FadeOriginals = new();

        private static void ScaleFade(UIFade f, float mult, string label)
        {
            try
            {
                if (f == null) return;
                var key = f.Pointer;
                if (!FadeOriginals.TryGetValue(key, out var orig))
                {
                    orig = (f.FadeTime, f.VisibleTime);
                    FadeOriginals[key] = orig;
                    Plugin.V($"[Fade] {label} originals: fadeTime={orig.fade:0.###}s visibleTime={orig.visible:0.###}s");
                }
                float m = Math.Max(0f, mult);
                float fade = Math.Max(0.05f, orig.fade * m);   // never 0: the fade divides by it
                float visible = Math.Max(0f, orig.visible * m);
                if (fade > orig.fade) fade = orig.fade;
                if (Math.Abs(f.FadeTime - fade) > 0.0001f) f.FadeTime = fade;
                if (Math.Abs(f.VisibleTime - visible) > 0.0001f) f.VisibleTime = visible;
                Stamp($"{label} fade shown (fade {fade:0.###}s, black {visible:0.###}s)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"{label} fade patch failed: {e.Message}"); }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GlobalFadesUIManager), nameof(GlobalFadesUIManager.HidePeekingOutFade))]
        private static void HidePeekingOutFade() => Stamp("peek-out fade HIDDEN (screen visible again)");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GlobalFadesUIManager), nameof(GlobalFadesUIManager.ShowPeekingOutFade))]
        private static void ShowPeekingOutFade(GlobalFadesUIManager __instance)
            => ScaleFade(__instance.PeakingOut, Plugin.PeekFadeMultiplier.Value, "Peek-out");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GlobalFadesUIManager), nameof(GlobalFadesUIManager.ShowSlotChangeFade))]
        private static void ShowSlotChangeFade(GlobalFadesUIManager __instance)
        {
            ClimbBoost.Restore("seat-change fade");
            ScaleFade(__instance.SlotChangingFade, Plugin.SlotChangeFadeMultiplier.Value, "Seat-change");
        }

        // ---------------- Binocular aim delay + zoom speed ----------------
        private static readonly Dictionary<IntPtr, (float start, float stop, float zoom)> ZoomOriginals = new();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WeaponZooming), nameof(WeaponZooming.SetWeaponAim))]
        private static void SetWeaponAim(WeaponZooming __instance, bool state)
        {
            try
            {
                string name = __instance.gameObject.name ?? "";
                bool isBinoc = name.IndexOf("Binocular", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isBinoc && !Plugin.ApplyToAllZoomItems.Value) return;

                var key = __instance.Pointer;
                if (!ZoomOriginals.TryGetValue(key, out var orig))
                {
                    orig = (__instance._StartAimDelayTime, __instance._StopAimDelayTime, __instance.ZoomInSpeed);
                    ZoomOriginals[key] = orig;
                    Plugin.V($"[Zoom] '{name}' originals: delayed={__instance._IsZoomDelayed} start={orig.start:0.###}s " +
                             $"stop={orig.stop:0.###}s zoomInSpeed={orig.zoom:0.###}");
                }

                float m = Math.Max(0f, Plugin.AimDelayMultiplier.Value);
                float start = orig.start * m, stop = orig.stop * m;
                float zoom = orig.zoom * Plugin.ZoomSpeedMultiplier.Value;

                if (Math.Abs(__instance._StartAimDelayTime - start) > 0.0001f) __instance._StartAimDelayTime = start;
                if (Math.Abs(__instance._StopAimDelayTime - stop) > 0.0001f) __instance._StopAimDelayTime = stop;
                if (orig.zoom > 0f && Math.Abs(__instance.ZoomInSpeed - zoom) > 0.0001f) __instance.ZoomInSpeed = zoom;

                Stamp($"aim {(state ? "ON" : "OFF")} on '{name}' (start delay {start:0.###}s, stop delay {stop:0.###}s, zoom {__instance.ZoomInSpeed:0.###})");
            }
            catch (Exception e) { Plugin.L.LogWarning("SetWeaponAim patch failed: " + e.Message); }
        }

        // ---------------- Binoculars (B) ----------------
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterInVehicleController), nameof(CharacterInVehicleController.InstantBinocularMode))]
        private static void InstantBinocularMode(CharacterInVehicleController __instance)
        {
            ApplyPeek(__instance);
            _binocStart = Time.unscaledTime;
            string state = __instance.CurrentPeekState.ToString();
            Plugin.V($"[Binoc] B pressed at {_binocStart:0.000} (peek state {state})");
            // Only boost when there is a climb UP ahead; from FullPeek B just lowers the binoculars.
            if (state != "FullPeek")
            {
                ClimbBoost.Start(__instance, "climb up for binoculars");
                CameraBlendWatch.Begin();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterInVehicleController), nameof(CharacterInVehicleController.OnFullPeek))]
        private static void OnFullPeek() => Stamp("full peek reached");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterInVehicleController), nameof(CharacterInVehicleController.ManageBinoculars))]
        private static void ManageBinoculars(bool state)
        {
            if (state) { ClimbBoost.Restore("binoculars up"); CameraBlendWatch.EndSoon(); }
            Stamp(state ? "binoculars ON" : "binoculars OFF");
        }

        private static void Stamp(string what)
        {
            if (!Plugin.VerboseLog.Value) return;
            float now = Time.unscaledTime;
            string since = _binocStart >= 0f ? $" (+{now - _binocStart:0.000}s after B)" : "";
            Plugin.V($"[Binoc] {what} at {now:0.000}{since}");
        }
    }
}

namespace TankSquadQuickCrew
{
    // Logging only (installed when VerboseLog = true): timestamps every step of the multi-crew / total-control switch,
    // so we can see whether the wait is the hold, the network ownership handshake, or something else.
    [HarmonyPatch]
    internal static class ControlModeProbes
    {
        private static float _t0 = -1f;

        internal static void Mark(string what, bool reset = false)
        {
            if (!Plugin.VerboseLog.Value) return;
            float now = UnityEngine.Time.unscaledTime;
            if (reset || _t0 < 0f) _t0 = now;
            Plugin.V($"[MultiCrew] {what} at {now:0.000} (+{now - _t0:0.000}s)");
        }

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var vs = typeof(Com.DeGenerals.TankSquad.Vehicle.VehicleSettings);
            string[] names =
            {
                "SwitchSlotModeOwner", "SyncPlayerSlotModeOnChanged", "RequestControlOverVehicle",
                "RPC_ConfirmVehicleControlStateRequest", "RPC_ConfirmVehicleControlStateResponse",
                "TakeoverAllControllables", "ReleaseAllControllables", "OnPlayerTakeTotalControl", "OnPlayerLoseTotalControl",
                "CheckIfAllAuthoritiesChanged", "AfterAuthorityCheck", "TakeAuthorityOverAllNOs",
                "RequestOwnershipOverAI", "RequestOwnershipOverAICoroutine", "OnCrewChanged",
            };
            foreach (var n in names)
            {
                var m = AccessTools.DeclaredMethod(vs, n);
                if (m != null) yield return m; else Plugin.L.LogWarning("Probe target not found: VehicleSettings." + n);
            }
            var c = AccessTools.DeclaredMethod(typeof(CharacterInVehicleController), "OnVehicleControlModeChanged");
            if (c != null) yield return c;
            var o = AccessTools.DeclaredMethod(typeof(OrdersMenuUIManager), "_SwitchTotalControlModeProcess");
            if (o != null) yield return o;
        }

        private static void Prefix(System.Reflection.MethodBase __originalMethod, object __instance)
        {
            try
            {
                string who = "";
                if (__instance is UnityEngine.Component comp) who = " on '" + comp.gameObject.name + "'";
                Mark(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + who);
            }
            catch { /* logging only */ }
        }
    }
}

namespace TankSquadQuickCrew
{
    // EXPERIMENTAL: temporarily speeds up the local character's Animators during a hatch climb.
    // The climb has no speed parameter in this game, so this is the only lever. Always restored on:
    // binoculars up, seat-change fade, or after MaxClimbBoostSeconds (checked every frame via MEC.Timing.Update).
    internal static class ClimbBoost
    {
        private static readonly List<(Animator anim, float orig)> Boosted = new();
        private static float _until = -1f;
        private static float _started;
        private static string _why = "";

        internal static void Start(CharacterInVehicleController c, string why)
        {
            try
            {
                float k = Plugin.ClimbAnimationSpeed.Value;
                if (k <= 1.001f || c == null) return;
                Restore("new climb");

                var anims = c.GetComponentsInChildren<Animator>(true);
                foreach (var a in anims)
                {
                    if (a == null) continue;
                    float orig = a.speed;
                    Boosted.Add((a, orig));
                    a.speed = orig * k;
                }
                if (Boosted.Count == 0) { Plugin.V("[Climb] no Animator found on the character - nothing to speed up"); return; }

                _started = Time.unscaledTime;
                _until = _started + Math.Max(0.25f, Plugin.MaxClimbBoostSeconds.Value);
                _why = why;
                Plugin.V($"[Climb] {why}: {Boosted.Count} animator(s) x{k:0.##} at {_started:0.000}");
            }
            catch (Exception e) { Plugin.L.LogWarning("Climb boost failed: " + e.Message); Restore("error"); }
        }

        internal static void Restore(string reason)
        {
            if (Boosted.Count == 0) { _until = -1f; return; }
            foreach (var (a, orig) in Boosted)
            {
                try { if (a != null) a.speed = orig; } catch { /* animator destroyed */ }
            }
            Plugin.V($"[Climb] {_why} ended by {reason} after {Time.unscaledTime - _started:0.000}s - animation speed restored");
            Boosted.Clear();
            _until = -1f;
        }

        // Postfix on MEC.Timing.Update: cheap no-op unless a boost is active.
        private static float _nextSlowLog;
        public static void Tick()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { TickInner(); }
            finally
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (ms > 4 && Plugin.VerboseLog.Value && Time.unscaledTime >= _nextSlowLog) { _nextSlowLog = Time.unscaledTime + 1f; Plugin.L.LogWarning($"[Perf] Quick Crew took {ms:0.0} ms this frame"); }
            }
        }
        private static void TickInner()
        {
            Plugin.ReloadTick();
            VisorPatches.InstallOnce();
            ExtraHotkeys.Tick();
            SquadAttack.Tick();
            CameraBlendWatch.Tick();
            SeatHotkeys.Tick();
            if (_until < 0f) return;
            try { if (Time.unscaledTime >= _until) Restore("safety timeout"); }
            catch { Boosted.Clear(); _until = -1f; }
        }
    }
}

namespace TankSquadQuickCrew
{
    // Watches the main Cinemachine brain while you climb up for binoculars. Logs every blend it sees (VerboseLog)
    // and, if CameraBlendSpeed > 1, shortens those blends. Only active between B and shortly after binoculars are up.
    internal static class CameraBlendWatch
    {
        private static float _until = -1f, _t0;
        private static Cinemachine.CinemachineBrain _brain;
        private static IntPtr _lastBlend = IntPtr.Zero;
        private static bool _sawBlend;

        internal static void Begin()
        {
            _t0 = Time.unscaledTime;
            _until = _t0 + 5f;
            _lastBlend = IntPtr.Zero;
            _sawBlend = false;
        }

        internal static void EndSoon()
        {
            if (_until > 0f) _until = Math.Min(_until, Time.unscaledTime + 0.5f);
        }

        internal static void Tick()
        {
            if (_until < 0f) return;
            float now = Time.unscaledTime;
            if (now >= _until)
            {
                if (!_sawBlend) Plugin.V("[Camera] no Cinemachine blend happened during the climb - the wait is not a camera blend");
                _until = -1f;
                return;
            }

            try
            {
                if (_brain == null)
                {
                    var cam = Camera.main;
                    if (cam != null) _brain = cam.GetComponent<Cinemachine.CinemachineBrain>();
                    if (_brain == null) _brain = UnityEngine.Object.FindObjectOfType<Cinemachine.CinemachineBrain>();
                    if (_brain == null) return;
                }
                if (!_brain.IsBlending) return;

                var blend = _brain.ActiveBlend;
                if (blend == null) return;
                _sawBlend = true;
                if (blend.Pointer == _lastBlend) return;   // already handled this blend
                _lastBlend = blend.Pointer;

                float orig = blend.Duration;
                float k = Plugin.CameraBlendSpeed.Value;
                if (k > 1.001f && orig > 0f)
                {
                    float d = orig / k;
                    blend.Duration = d;
                    if (blend.TimeInBlend > d) blend.TimeInBlend = d;
                }
                Plugin.V($"[Camera] blend '{blend.Description}' at +{now - _t0:0.000}s after B: {orig:0.###}s -> {blend.Duration:0.###}s");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Camera blend watch failed (disabled until next B): " + e.Message);
                _until = -1f;
            }
        }
    }
}

namespace TankSquadQuickCrew
{
    // Ctrl+1..Ctrl+5: switch the local player straight to a crew seat (the same call the game makes after a seat hold completes).
    internal static class SeatHotkeys
    {
        private static string _parsedFrom;
        private static readonly CrewRoleType?[] Map = new CrewRoleType?[5];

        private static void Parse()
        {
            string raw = Plugin.SeatHotkeys.Value ?? "";
            if (raw == _parsedFrom) return;
            _parsedFrom = raw;
            var parts = raw.Split(',');
            for (int i = 0; i < Map.Length; i++)
            {
                Map[i] = null;
                if (i >= parts.Length) continue;
                var name = parts[i].Trim();
                if (name.Length == 0) continue;
                if (Enum.TryParse<CrewRoleType>(name, true, out var role)) Map[i] = role;
                else Plugin.L.LogWarning($"[Seats] Ctrl+{i + 1}: unknown seat '{name}' - ignored");
            }
        }

        internal static void Tick()
        {
            if (!Plugin.SeatHotkeysEnabled.Value) return;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return;
                if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) return;

                int idx =
                    kb.digit1Key.wasPressedThisFrame ? 0 :
                    kb.digit2Key.wasPressedThisFrame ? 1 :
                    kb.digit3Key.wasPressedThisFrame ? 2 :
                    kb.digit4Key.wasPressedThisFrame ? 3 :
                    kb.digit5Key.wasPressedThisFrame ? 4 : -1;
                if (idx < 0) return;

                Parse();
                var role = Map[idx];
                if (role == null) return;

                var player = CharacterInVehicleController.LocalPlayer;
                if (player == null) { Plugin.V($"[Seats] Ctrl+{idx + 1}: no local in-vehicle character (LocalPlayer is null)"); return; }
                if (player.IsOutsideVehicle)
                {
                    string veh = "none";
                    try { var v = player.CurrentVehicle; if (v != null) veh = v.gameObject.name; } catch { }
                    Plugin.V($"[Seats] Ctrl+{idx + 1}: character reports outside vehicle (current vehicle: {veh})");
                    return;
                }
                if (player.IsDuringPeekLevelChange) { Plugin.V($"[Seats] Ctrl+{idx + 1}: busy climbing - ignored"); return; }

                var current = player.CurrentSlot;
                if (current != null && current.CrewRole == role.Value) { Plugin.V($"[Seats] already in {role.Value}"); return; }

                Plugin.V($"[Seats] Ctrl+{idx + 1} -> {role.Value}");
                player.SwitchSlot(role.Value, false);
            }
            catch (Exception e) { Plugin.L.LogWarning("[Seats] seat hotkey failed: " + e.Message); }
        }
    }
}

namespace TankSquadQuickCrew
{
    // The hatch/peek sequences are MEC coroutines. A timed wait is "yield return Timing.WaitForSeconds(t)", which yields the
    // absolute resume time (LocalTime + t). After each MoveNext we pull that resume time closer by PeekWaitSpeed.
    // Special MEC values (NaN = wait for another coroutine, -Infinity / past times = next frame) are left untouched.
    [HarmonyPatch]
    internal static class PeekWaitShortener
    {
        private static readonly string[] StateMachines =
        {
            "_InstantFullPeek_d__235", "_InstantHide_d__234", "_HandlePeekedSlotSwitch_d__192",
            "_OpenedHatchToLowPeek_d__228", "_LowPeekToOpenedHatch_d__229", "_LowPeekToFullPeek_d__230",
            "_RefreshVehicleFunctionallitiesOnPeek_d__236",
        };

        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> CurrentProp = new();

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in StateMachines)
            {
                var t = typeof(CharacterInVehicleController).GetNestedType(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var m = t == null ? null : AccessTools.DeclaredMethod(t, "MoveNext");
                if (m == null) { Plugin.L.LogWarning("Peek coroutine not found: " + n); continue; }
                CurrentProp[t] = AccessTools.Property(t, "__2__current");
                yield return m;
            }
        }

        private static void Postfix(object __instance, bool __result, System.Reflection.MethodBase __originalMethod)
        {
            if (!__result) return;
            float k = Plugin.PeekWaitSpeed.Value;
            try
            {
                var t = __instance.GetType();
                if (!CurrentProp.TryGetValue(t, out var prop) || prop == null) return;
                float cur = (float)prop.GetValue(__instance);
                if (float.IsNaN(cur) || float.IsInfinity(cur)) return;

                float now = MEC.Timing.LocalTime;
                float wait = cur - now;
                if (wait <= 0.001f) return;                       // next-frame yield

                string name = __originalMethod.DeclaringType?.Name ?? "?";
                if (k > 1.001f)
                {
                    float shorter = wait / k;
                    prop.SetValue(__instance, now + shorter);
                    Plugin.V($"[PeekWait] {name}: wait {wait:0.###}s -> {shorter:0.###}s");
                }
                else Plugin.V($"[PeekWait] {name}: wait {wait:0.###}s (unchanged)");
            }
            catch (Exception e) { Plugin.L.LogWarning("Peek wait shortener failed: " + e.Message); }
        }
    }
}

namespace TankSquadQuickCrew
{
    // Ctrl+~ / Ctrl+V: read the keyboard directly (like the Ctrl+1..5 seat keys) and call the same game methods
    // the normal keys end up in. Avoids Input System control-scheme filtering, which can ignore runtime-added bindings.
    internal static class ExtraHotkeys
    {
        private static readonly Dictionary<string, System.Reflection.PropertyInfo> KeyProps = new();

        internal static UnityEngine.InputSystem.Controls.ButtonControl Key(UnityEngine.InputSystem.Keyboard kb, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);          // accept old "<Keyboard>/space" values too
            if (!KeyProps.TryGetValue(name, out var prop))
            {
                prop = typeof(UnityEngine.InputSystem.Keyboard).GetProperty(name + "Key",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                KeyProps[name] = prop;
                if (prop == null) Plugin.L.LogWarning($"[Hotkeys] unknown key name '{name}'");
            }
            return prop?.GetValue(kb) as UnityEngine.InputSystem.Controls.ButtonControl;
        }

        private static bool ModifierHeld(UnityEngine.InputSystem.Keyboard kb) => ModifierHeld(kb, Plugin.ExtraModifier.Value);

        internal static bool ModifierHeld(UnityEngine.InputSystem.Keyboard kb, string mod)
        {
            string m = (mod ?? "ctrl").Trim().ToLowerInvariant();
            int slash = m.LastIndexOf('/');
            if (slash >= 0) m = m.Substring(slash + 1);
            return m switch
            {
                "alt" => kb.leftAltKey.isPressed || kb.rightAltKey.isPressed,
                "shift" => kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed,
                _ => kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed,
            };
        }

        internal static void Tick()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null || !ModifierHeld(kb)) return;

                var mc = Key(kb, Plugin.MultiCrewKey.Value);
                if (mc != null && mc.wasPressedThisFrame) ToggleMultiCrew();

                var inside = Key(kb, Plugin.InteriorKey.Value);
                if (inside != null && inside.wasPressedThisFrame) GoInside();

                foreach (var kn in (Plugin.GunnerToggleKey.Value ?? "").Split(','))
                {
                    var gun = Key(kb, kn);
                    if (gun == null || !gun.wasPressedThisFrame) continue;
                    GunnerDiag.ChangeSource = "hotkey Ctrl+" + kn.Trim();
                    Plugin.OwnGunnerAI.Value = !Plugin.OwnGunnerAI.Value;
                    break;
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[Hotkeys] " + e.Message); }
        }

        private static void ToggleMultiCrew()
        {
            var v = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
            if (v == null) { Plugin.V("[Hotkeys] multi-crew: no local vehicle"); return; }
            bool before = false;
            try { before = v._CachedPlayerSlotModeOn; } catch { }
            v.SwitchSlotModeOwner();
            Plugin.V($"[Hotkeys] multi-crew toggled on '{v.gameObject.name}' (slot mode was {(before ? "on" : "off")})");
        }

        private static void GoInside()
        {
            var vc = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance;
            if (vc == null) { Plugin.V("[Hotkeys] go inside: no vehicle camera (not in a vehicle?)"); return; }
            var cam = vc.GetAvailableInteriorCamera();
            if (cam == null) { Plugin.V("[Hotkeys] go inside: this seat has no interior camera"); return; }
            vc.SwitchCamera(cam, false, false, false);
            Plugin.V("[Hotkeys] go inside: switched to interior camera");
        }
    }
}

namespace TankSquadQuickCrew
{
    // Runs on the AI turret gunner's decision points. Remembers which detector / main guns belong to YOUR tank,
    // marks "your AI gunner is deciding right now" (for the ammo rule), and skips everything when OwnGunnerAI = false.
    [HarmonyPatch]
    internal static class OwnGunnerSwitch
    {
        internal static readonly HashSet<IntPtr> OwnDetectors = new();
        internal static readonly HashSet<IntPtr> OwnMainGuns = new();
        internal static readonly HashSet<IntPtr> OwnAimings = new();
        internal static bool InOwnGunnerAI;
        // Turret slots in the game's "order mode" (multi-crew on). Diagnostics only.
        internal static readonly HashSet<IntPtr> OrderDetectors = new();
        internal static readonly HashSet<IntPtr> OrderAimings = new();
        private static IntPtr _localVehicle;
        private static bool _loggedBlock;

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var t = typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour);
            foreach (var n in new[] { "HandleBehaviour", "HandleCombat", "HandleCombatInPlace", "EnemyChanged" })
            {
                var m = AccessTools.DeclaredMethod(t, n);
                if (m != null) yield return m; else Plugin.L.LogWarning("Gunner method not found: " + n);
            }
        }

        internal static bool IsOwn(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour g)
        {
            var local = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
            if (local == null) return false;
            if (local.Pointer != _localVehicle) { _localVehicle = local.Pointer; OwnDetectors.Clear(); OwnMainGuns.Clear(); OwnAimings.Clear(); }
            var unit = g.VehicleUnit;
            var mine = unit == null ? null : unit.SettingsReference;
            if (mine == null || mine.Pointer != local.Pointer) return false;

            try { var d = g.VehicleTargetDetector; if (d != null) OwnDetectors.Add(d.Pointer); } catch { }
            try { var w = g.VehicleMainWeaponFire; if (w != null) OwnMainGuns.Add(w.Pointer); } catch { }
            try { var a = g.VehicleWeaponAiming; if (a != null) OwnAimings.Add(a.Pointer); } catch { }
            try { var w = g.VehicleAdditionalMainWeaponFire; if (w != null) OwnMainGuns.Add(w.Pointer); } catch { }
            return true;
        }

        private static readonly Dictionary<IntPtr, string> LastDecision = new();

        private static void Decide(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour g, string decision)
        {
            if (!Plugin.VerboseLog.Value) return;
            if (LastDecision.TryGetValue(g.Pointer, out var last) && last == decision) return;
            LastDecision[g.Pointer] = decision;
            string tank = "?";
            try { tank = g.VehicleUnit?.SettingsReference?.gameObject.name ?? "?"; } catch { }
            Plugin.V($"[GunnerDiag] gunner of '{tank}': {decision} at {Time.unscaledTime:0.000}");
        }

        private static bool Prefix(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour __instance)
        {
            try
            {
                if (!IsOwn(__instance))
                {
                    // Only interesting if it looks like our tank but wasn't recognised.
                    if (Plugin.VerboseLog.Value)
                    {
                        var local = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
                        string mine = null; try { mine = __instance.VehicleUnit?.SettingsReference?.gameObject.name; } catch { }
                        if (local == null) Decide(__instance, "RUNNING - game reports no local vehicle (LocalPlayer is null)");
                        else if (mine != null && mine == local.gameObject.name) Decide(__instance, "RUNNING - same tank name but not recognised as yours");
                    }
                    return true;                                   // someone else's tank: leave alone
                }
                if (GunnerTargetAndAmmo.PlayerIsGunner()) { Decide(__instance, "LEFT TO GAME - you are aiming (" + GunnerTargetAndAmmo.WhyPlayerAims() + ")"); return true; }
                if (Plugin.OwnGunnerAI.Value) { InOwnGunnerAI = true; Decide(__instance, "RUNNING - OwnGunnerAI is ON"); return true; }

                if (!_loggedBlock) { _loggedBlock = true; Plugin.V("[Gunner] AI gunner in your tank is switched off"); }
                Decide(__instance, "BLOCKED (switched off)");
                DropLock(__instance);
                return false;   // skip: no turret turning, no target picking, no firing
            }
            catch { return true; }
        }

        private static IntPtr _lastNothingLog;
        private static void Postfix() => InOwnGunnerAI = false;

        // Gunner off: the turret's aiming follows the detector's current target, so clear it (and any forced target / lock).
        private static void DropLock(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour g)
        {
            try
            {
                if (GunnerTargetAndAmmo.PlayerIsGunner()) return;
                var d = g.VehicleTargetDetector;
                if (d == null) return;
                bool had = false;
                bool forced = d.ForcedTarget != null, current = d.CurrentEnemyTarget != null;
                if (forced) { d.ClearForcedTarget(); had = true; }
                if (current) { d.ClearTarget(); had = true; }
                if (!had)
                {
                    if (Plugin.VerboseLog.Value && _lastNothingLog != g.Pointer)
                    {
                        _lastNothingLog = g.Pointer;
                        string input = "?";
                        try { var a = g.VehicleWeaponAiming; input = a == null ? "no aiming" : (a.CurrentInputController == null ? "AI (no player input)" : a.CurrentInputController.GetIl2CppType().Name); } catch { }
                        try { var me = CharacterInVehicleController.LocalPlayer; if (me != null) input += $", you using gun = {me.IsUsingVehicleWeapon}, seat = {me.CurrentSlot?.CrewRole}"; } catch { }
                        Plugin.V($"[GunnerDiag] gunner off: detector holds no target/forced target; turret aiming input = {input}");
                    }
                    return;
                }
                _lastNothingLog = IntPtr.Zero;
                try { g._TargetLockingMainWeapon?.SetIsLocked(false); } catch { }
                try { g._TargetLockingAdditionalMainWeapon?.SetIsLocked(false); } catch { }
                try { g._TargetLockingSecondaryWeapon?.SetIsLocked(false); } catch { }
                Plugin.V("[Gunner] gunner is off - dropped its target lock");
            }
            catch (Exception e) { Plugin.V("[Gunner] drop lock failed: " + e.Message); }
        }
        internal static void ForgetDecisions() => LastDecision.Clear();
        private static Exception Finalizer(Exception __exception) { InOwnGunnerAI = false; return __exception; }
    }
}

namespace TankSquadQuickCrew
{
    // Your own AI gunner: never targets infantry, and only ever loads armour-piercing ammo in the main gun.
    internal static class GunnerTargetAndAmmo
    {
        // ---------- infantry ----------
        private static bool IsInfantry(Com.DeGenerals.TankSquad.AI.ITargetable t)
        {
            if (t == null) return false;
            try { if (t.TryCast<Com.DeGenerals.TankSquad.AI.Units.HumanoidGenericUnit>() != null) return true; } catch { }
            try
            {
                var tr = t.GetMyTransform();
                if (tr == null) return false;
                if (tr.GetComponentInParent<Com.DeGenerals.TankSquad.AI.Units.VehicleUnit>() != null) return false;
                return tr.GetComponentInParent<Com.DeGenerals.TankSquad.AI.Units.HumanoidGenericUnit>() != null;
            }
            catch { return false; }
        }

        private static bool Own(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector d)
            => Plugin.MainGunIgnoresInfantry.Value && d != null && OwnGunnerSwitch.OwnDetectors.Contains(d.Pointer);

        // ---------- allied vehicles that never shoot infantry (SU-152 by default) ----------
        private static readonly Dictionary<IntPtr, (bool hit, float at, string name)> ModelCache = new();
        private static string _modelsKey; private static string[] _models = Array.Empty<string>();

        private static bool AlliedModel(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector d)
        {
            if (!Plugin.AlliedNoInfantry.Value || d == null) return false;
            float now = Time.unscaledTime;
            if (!ModelCache.TryGetValue(d.Pointer, out var c) || now - c.at > 5f)
            {
                string key = Plugin.AlliedNoInfantryModels.Value ?? "";
                if (key != _modelsKey) { _modelsKey = key; _models = key.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); }
                bool hit = false; string name = "?";
                try
                {
                    var u = d.ThisUnit;
                    if (u != null && u.CurrentFaction == Com.DeGenerals.TankSquad.AI.Units.Faction.Allied)
                    {
                        var vs = u.SettingsReference ?? u.VehicleSettingsReference;
                        name = vs != null ? vs.gameObject.name : u.gameObject.name;
                        foreach (var m in _models) if (name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    }
                }
                catch { hit = false; }
                if (ModelCache.Count > 500) ModelCache.Clear();
                if (hit && (!ModelCache.TryGetValue(d.Pointer, out var old) || !old.hit)) Plugin.V($"[NoInfantry] {name}: infantry ignored");
                ModelCache[d.Pointer] = c = (hit, now, name);
            }
            if (!c.hit) return false;
            // your own tank while YOU work the gun: never interfere
            if (OwnGunnerSwitch.OwnDetectors.Contains(d.Pointer) && PlayerIsGunner()) return false;
            return true;
        }

        // Infantry is off-limits for this detector: your own gunner's IgnoreInfantry, or an allied SU-152 (etc.).
        private static bool NoInfantry(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector d) => Own(d) || AlliedModel(d);

        // True whenever YOU control the main gun: sitting in the gunner seat, or using the vehicle weapon from another seat
        // (e.g. the Shift quick-shoot as commander).
        // Also true with multi-crew OFF (total control): then your mouse aims the turret through the gunner, so the
        // gunner switch, turret freeze and AP rule only apply in multi-crew mode.
        internal static bool PlayerIsGunner() => WhyPlayerAims() != null;

        internal static string WhyPlayerAims()
        {
            try
            {
                try
                {
                    var v = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
                    if (v != null && !v._CachedPlayerSlotModeOn) return "multi-crew off";
                }
                catch { }
                var me = CharacterInVehicleController.LocalPlayer;
                if (me == null) return null;
                try { if (me.IsUsingVehicleWeapon) return "using the gun"; } catch { }
                var slot = me.CurrentSlot;
                return slot != null && slot.CrewRole == CrewRoleType.Gunner ? "gunner seat" : null;
            }
            catch { return null; }
        }

        // Your gunner is switched off and you are not in the gunner seat: this detector must not hold any target.
        internal static bool OffAndOwn(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector d)
            => !Plugin.OwnGunnerAI.Value && d != null && OwnGunnerSwitch.OwnDetectors.Contains(d.Pointer)
               && !PlayerIsGunner();

        private static bool _loggedFreeze;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponAiming), "HandleRotations")]
        private static bool FreezeTurret(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponAiming __instance)
        {
            try
            {
                if (Plugin.OwnGunnerAI.Value || !Plugin.FreezeTurretWhenOff.Value) { _loggedFreeze = false; return true; }
                if (!OwnGunnerSwitch.OwnAimings.Contains(__instance.Pointer)) return true;
                if (PlayerIsGunner() || __instance.CurrentInputController != null) return true;   // a player is aiming: never block
                if (!_loggedFreeze) { _loggedFreeze = true; Plugin.V("[Gunner] gunner is off - turret AI rotation frozen"); }
                return false;
            }
            catch { return true; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "TargetSeeking")]
        private static bool NoSeeking(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance) => !OffAndOwn(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "TargetSpotting")]
        private static bool NoSpotting(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance) => !OffAndOwn(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "SetCurrentTarget")]
        private static bool NoCurrentTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.AI.AITarget target)
            => target == null || !OffAndOwn(__instance);      // clearing (null) is always allowed

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "SetForcedTarget")]
        private static bool NoForcedTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance) => !OffAndOwn(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "CheckIfHasProperWeapon",
            new[] { typeof(Com.DeGenerals.TankSquad.AI.ITargetable), typeof(float), typeof(bool) })]
        private static bool ProperWeaponForTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.AI.ITargetable target, ref bool __result)
        {
            try { if (NoInfantry(__instance) && IsInfantry(target)) { __result = false; return false; } } catch { }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "CheckIfHasProperWeapon",
            new[] { typeof(Com.DeGenerals.TankSquad.Weapons.WeaponSuggestedUse) })]
        private static bool ProperWeaponForUse(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.Weapons.WeaponSuggestedUse weaponSuggestedUse, ref bool __result)
        {
            try
            {
                if (NoInfantry(__instance) && weaponSuggestedUse == Com.DeGenerals.TankSquad.Weapons.WeaponSuggestedUse.AgainstInfantry)
                { __result = false; return false; }
            }
            catch { }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "SetNewTarget")]
        private static bool SetNewTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.AI.ITargetable newEnemy, bool isForced)
        {
            try
            {
                if (OffAndOwn(__instance)) return false;          // gunner off: no new targets at all
                if (!isForced && NoInfantry(__instance) && IsInfantry(newEnemy))
                {
                    Plugin.V("[Gunner] ignored an infantry target");
                    return false;
                }
            }
            catch { }
            return true;
        }

        // ---------- AP-only main gun ----------
        private static bool IsAP(Com.DeGenerals.TankSquad.Vehicle.Weapon.Ammo.AmmoStorage st)
        {
            if (st == null) return false;
            string n = st.ProjectileType.ToString();
            return n.StartsWith("ArmourPiercing") || n.StartsWith("Semi_ArmourPiercing");
        }

        private static bool Applies(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire w)
        {
            if (!Plugin.MainGunAPOnly.Value || w == null || !OwnGunnerSwitch.OwnMainGuns.Contains(w.Pointer)) return false;
            return !PlayerIsGunner();   // never interfere when YOU are the gunner
        }

        // Best AP store: the one already loaded if it is AP and has rounds, else the first non-empty AP store.
        private static int BestAPIndex(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire w, out Com.DeGenerals.TankSquad.Vehicle.Weapon.Ammo.AmmoStorage best)
        {
            best = null; int bestIdx = -1;
            string loaded = "";
            try { loaded = w._LastLoadedAmmo.ToString(); } catch { }
            for (int i = 0; i < 16; i++)
            {
                Com.DeGenerals.TankSquad.Vehicle.Weapon.Ammo.AmmoStorage st;
                try { st = w.GetAmmoStorageByIndex(i); } catch { break; }
                if (st == null) break;
                if (!IsAP(st)) continue;
                bool empty = true;
                try { empty = st.IsEmpty; } catch { }
                if (empty) continue;
                if (bestIdx < 0 || st.ProjectileType.ToString() == loaded) { best = st; bestIdx = i; }
            }
            return bestIdx;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire), "GetAmmoStorageIndexAgainstUnit")]
        private static bool AmmoAgainstUnit(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire __instance, ref int __result)
        {
            try
            {
                if (!Applies(__instance)) return true;
                int idx = BestAPIndex(__instance, out var st);
                if (idx < 0) return true;                       // no AP left: let the game decide
                __result = idx;
                Plugin.V($"[Gunner] ammo choice forced to {st.ProjectileType}");
                return false;
            }
            catch { return true; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire), "SelectAmmoStorageOwner")]
        private static void SelectStorage(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire __instance,
            ref Com.DeGenerals.TankSquad.Vehicle.Weapon.Ammo.AmmoStorage storageToSelect)
        {
            try
            {
                if (!OwnGunnerSwitch.InOwnGunnerAI || !Applies(__instance) || IsAP(storageToSelect)) return;
                if (BestAPIndex(__instance, out var ap) >= 0 && ap != null)
                {
                    Plugin.V($"[Gunner] AI wanted {storageToSelect?.ProjectileType} - loading {ap.ProjectileType} instead");
                    storageToSelect = ap;
                }
            }
            catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire), "SelectAmmoOwner")]
        private static void SelectByIndex(Com.DeGenerals.TankSquad.Vehicle.Weapon.VehicleWeaponFire __instance, ref int ammoStorageIndex)
        {
            try
            {
                if (!OwnGunnerSwitch.InOwnGunnerAI || !Applies(__instance)) return;
                var wanted = __instance.GetAmmoStorageByIndex(ammoStorageIndex);
                if (IsAP(wanted)) return;
                int idx = BestAPIndex(__instance, out var ap);
                if (idx >= 0)
                {
                    Plugin.V($"[Gunner] AI wanted {wanted?.ProjectileType} - loading {ap.ProjectileType} instead");
                    ammoStorageIndex = idx;
                }
            }
            catch { }
        }
    }
}

namespace TankSquadQuickCrew
{
    // VerboseLog diagnostics for "the gunner comes back on by itself": logs the other things that can steer your turret
    // or swap your gunner - target changes on your tank's detector (incl. forced/commander targets), gunner behaviour
    // enable/disable, and the turret slot bridge's order mode.
    internal static class GunnerDiag
    {
        internal static string ChangeSource = "config file reload";

        private static void Log(string s) { if (Plugin.VerboseLog.Value) Plugin.L.LogInfo($"[GunnerDiag] {s} at {Time.unscaledTime:0.000}"); }

        private static bool OwnDetector(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector d)
            => d != null && OwnGunnerSwitch.OwnDetectors.Contains(d.Pointer);

        private static string Name(Com.DeGenerals.TankSquad.AI.ITargetable t)
        {
            try { var tr = t?.GetMyTransform(); return tr != null ? tr.gameObject.name : "none"; } catch { return "?"; }
        }

        private static bool OwnUnit(Com.DeGenerals.TankSquad.AI.Units.VehicleUnit u)
        {
            try
            {
                var local = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
                var mine = u?.SettingsReference;
                return local != null && mine != null && mine.Pointer == local.Pointer;
            }
            catch { return false; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "SetNewTarget")]
        private static void NewTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.AI.ITargetable newEnemy, bool isForced)
        { if (Plugin.VerboseLog.Value && OwnDetector(__instance)) Log($"your gunner's detector: new target '{Name(newEnemy)}' (forced={isForced}, gunner {(Plugin.OwnGunnerAI.Value ? "ON" : "OFF")})"); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "SetForcedTarget")]
        private static void ForcedTarget(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance,
            Com.DeGenerals.TankSquad.AI.ITargetable forcedTarget, bool isSetByPlayer)
        { if (Plugin.VerboseLog.Value && OwnDetector(__instance)) Log($"your gunner's detector: FORCED target '{Name(forcedTarget)}' (set by player={isSetByPlayer})"); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector), "ClearTarget")]
        private static void Cleared(Com.DeGenerals.TankSquad.AI.Visibility.VehicleTargetDetector __instance)
        { if (Plugin.VerboseLog.Value && OwnDetector(__instance)) Log("your gunner's detector: target cleared"); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour), "OnBehaviourEnable")]
        private static void GunnerEnabled(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierCrewTurretGunnerBehaviour __instance)
        {
            if (!Plugin.VerboseLog.Value) return;
            OwnGunnerSwitch.ForgetDecisions();
            if (OwnUnit(__instance.VehicleUnit)) Log("a turret-gunner AI behaviour was ENABLED in your tank");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierTankCrewBehaviour), "OnBehaviourDisable")]
        private static void CrewDisabled(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.AISoldierTankCrewBehaviour __instance)
        {
            if (!Plugin.VerboseLog.Value || !OwnUnit(__instance.VehicleUnit)) return;
            string kind = "?"; try { kind = __instance.GetIl2CppType().Name; } catch { }
            Log($"a crew AI behaviour ({kind}) was DISABLED in your tank");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge), "OnStartOrderMode")]
        private static void OrderModeStart(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge __instance) => OrderMode(__instance, true);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge), "OnEndOrderMode")]
        private static void OrderModeEnd(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge __instance) => OrderMode(__instance, false);

        private static void OrderMode(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge b, bool on)
        {
            try
            {
                IntPtr d = IntPtr.Zero, a = IntPtr.Zero;
                try { d = b._VehicleTargetDetector?.Pointer ?? IntPtr.Zero; } catch { }
                try { a = b._VehicleWeaponAiming?.Pointer ?? IntPtr.Zero; } catch { }
                if (on) { if (d != IntPtr.Zero) OwnGunnerSwitch.OrderDetectors.Add(d); if (a != IntPtr.Zero) OwnGunnerSwitch.OrderAimings.Add(a); }
                else { OwnGunnerSwitch.OrderDetectors.Remove(d); OwnGunnerSwitch.OrderAimings.Remove(a); }
                OwnGunnerSwitch.ForgetDecisions();
                if (!Plugin.VerboseLog.Value) return;
                string slot = "?";
                try { var v = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer; if (v != null) slot = v._CachedPlayerSlotModeOn ? "on" : "off"; } catch { }
                Log($"turret slot: order mode {(on ? "STARTED" : "ENDED")} (multi-crew slot mode {slot})");
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.AI.Behaviour.Soldier.TurretSlotBehaviourBridge), "AssignBehaviour")]
        private static void BridgeAssign(bool __result) => Log($"turret slot: AI behaviour assigned (result={__result})");
    }
}

namespace TankSquadQuickCrew
{
    // ---- Instant Visors (merged in from a former separate plugin) ----
    // Keeps the game's visor interaction (hand-reach animation -> hold -> enter view) but makes it fast, and hides the
    // "NAME / Press F to interact" label for visors (the outline stays).
    // Installed on the first frame, after all plugins have loaded.
    internal static class VisorPatches
    {
        private static bool _done;
        private static readonly Dictionary<IntPtr, Com.DeGenerals.TankSquad.Interactions.Interactables.VehicleInteractableConfigurator> ByInteractable = new();
        private static readonly HashSet<IntPtr> Tuned = new();
        private static bool _suppressLabel;

        internal static void InstallOnce()
        {
            if (_done) return;
            _done = true;
            try
            {
                new Harmony(Plugin.Guid + ".visors").PatchAll(typeof(VisorPatches));
                Plugin.V("[Visors] instant visors active.");
            }
            catch (Exception e) { Plugin.L.LogWarning("[Visors] could not install: " + e.Message); }
        }

        private static void Register(Com.DeGenerals.TankSquad.Interactions.Interactables.VehicleInteractableConfigurator cfg)
        {
            try
            {
                if (cfg == null) return;
                if (!Plugin.VisorIncludeGunnerSights.Value && cfg.IsGunnerWeapon && !cfg.IsViewfinder && !cfg.IsPeriscope) return;
                var it = cfg.InteractableReference;
                if (it != null) ByInteractable[it.Pointer] = cfg;

                var ho = cfg.InteractionObject;
                string name = cfg.gameObject.name;
                if (ho == null) { Plugin.V($"[Visors] [{name}] no InteractionObject"); return; }
                if (!Plugin.VisorFastEnter.Value || !Tuned.Add(ho.Pointer)) return;

                if (ho.HoldedEvent != null)
                {
                    float old = ho.HoldedEvent.DurationTime;
                    ho.HoldedEvent.DurationTime = Math.Max(0f, Plugin.VisorHoldTime.Value);
                    Plugin.V($"[Visors] [{name}] hold {old:0.###}s -> {ho.HoldedEvent.DurationTime:0.###}s");
                }

                float speed = Plugin.VisorAnimationSpeed.Value;
                var evs = ho.events;
                if (speed > 1f && evs != null)
                {
                    for (int i = 0; i < evs.Length; i++)
                    {
                        var e = evs[i];
                        if (e == null) continue;
                        float old = e.time;
                        e.time = old / speed;
                        Plugin.V($"[Visors] [{name}] event {i} time {old:0.###} -> {e.time:0.###} pause={e.pause}");
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("[Visors] register failed: " + e.Message); }
        }

        private static bool IsVisor(IntPtr interactable)
        {
            if (interactable == IntPtr.Zero) return false;
            if (!ByInteractable.TryGetValue(interactable, out var cfg)) return false;
            if (cfg == null) { ByInteractable.Remove(interactable); return false; }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Interactions.Interactables.VehicleInteractableConfigurator),
            nameof(Com.DeGenerals.TankSquad.Interactions.Interactables.VehicleInteractableConfigurator.Initialize))]
        private static void ConfiguratorInitialize(Com.DeGenerals.TankSquad.Interactions.Interactables.VehicleInteractableConfigurator __instance)
            => Register(__instance);

        // While a visor's OnRayEnter runs, block the label/hint call it makes (the outline runs before it and stays).
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Interactions.InteractableLocal),
            nameof(Com.DeGenerals.TankSquad.Interactions.InteractableLocal.OnRayEnter))]
        private static void OnRayEnterPre(Com.DeGenerals.TankSquad.Interactions.InteractableLocal __instance)
        {
            try { _suppressLabel = Plugin.VisorHideLabel.Value && IsVisor(__instance.Pointer); }
            catch { _suppressLabel = false; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.Interactions.InteractableLocal),
            nameof(Com.DeGenerals.TankSquad.Interactions.InteractableLocal.OnRayEnter))]
        private static void OnRayEnterPost() => _suppressLabel = false;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Com.DeGenerals.TankSquad.UI.InteractionUIManager),
            nameof(Com.DeGenerals.TankSquad.UI.InteractionUIManager.ShowInteractionLabel))]
        private static bool ShowInteractionLabel()
        {
            if (!_suppressLabel) return true;
            Plugin.V("[Visors] label suppressed");
            return false;
        }
    }
}

namespace TankSquadQuickCrew
{
    // Look at an enemy (any view, incl. binoculars) and press Modifier+key: that squad gets a direct attack order on the
    // unit under the centre of the screen - no squad menu, no target-picking step.
    internal static class SquadAttack
    {
        private static bool _loggedSquads;

        internal static void Tick()
        {
            if (!Plugin.SquadAttackEnabled.Value) return;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null || !ExtraHotkeys.ModifierHeld(kb, Plugin.SquadAttackModifier.Value)) return;

                int squadNo = -1;                     // 1-based; 0 = all squads
                var keys = (Plugin.SquadAttackKeys.Value ?? "").Split(',');
                for (int i = 0; i < keys.Length && squadNo < 0; i++)
                {
                    var k = ExtraHotkeys.Key(kb, keys[i]);
                    if (k != null && k.wasPressedThisFrame) squadNo = i + 1;
                }
                if (squadNo < 0)
                {
                    var all = ExtraHotkeys.Key(kb, Plugin.SquadAttackAllKey.Value);
                    if (all != null && all.wasPressedThisFrame) squadNo = 0;
                }
                if (squadNo < 0) return;

                Order(squadNo);
            }
            catch (Exception e) { Plugin.L.LogWarning("[Squads] " + e.Message); }
        }

        private static List<Com.DeGenerals.TankSquad.AI.Squad.CombatSquad> MySquads()
        {
            var result = new List<Com.DeGenerals.TankSquad.AI.Squad.CombatSquad>();
            var oc = Com.DeGenerals.TankSquad.Managers.AI.AIOrdersController.Instance;
            var list = oc == null ? null : oc._ControlledSquads;
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var sq = list[i];
                if (sq == null) continue;
                bool alive = true;
                try { alive = sq.HasUnitsInSquad; } catch { }
                if (alive) result.Add(sq);
            }
            result.Sort((a, b) => a.UIID.CompareTo(b.UIID));   // same order as the squad menu numbers
            return result;
        }

        private static Com.DeGenerals.TankSquad.AI.Units.GenericUnit LookedAtEnemy(Com.DeGenerals.TankSquad.AI.Squad.CombatSquad forSquad)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, Math.Max(10f, Plugin.SquadAttackRange.Value));
            if (hits == null) return null;

            string myFaction = forSquad.Faction.ToString();
            Com.DeGenerals.TankSquad.AI.Units.GenericUnit best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                var col = h.collider;
                if (col == null || h.distance >= bestDist) continue;
                var unit = col.GetComponentInParent<Com.DeGenerals.TankSquad.AI.Units.GenericUnit>();
                if (unit == null) continue;
                bool alive = true;
                try { alive = unit.IsAlive; } catch { }
                if (!alive || unit.CurrentFaction.ToString() == myFaction) continue;   // friendly / dead: look further
                best = unit; bestDist = h.distance;
            }
            return best;
        }

        private static void Order(int squadNo)
        {
            var squads = MySquads();
            if (squads.Count == 0) { Plugin.V("[Squads] you have no squads under your command"); return; }
            if (!_loggedSquads)
            {
                _loggedSquads = true;
                for (int i = 0; i < squads.Count; i++)
                    Plugin.V($"[Squads] squad {i + 1} = '{squads[i].SquadName}'");
            }

            var targets = new List<Com.DeGenerals.TankSquad.AI.Squad.CombatSquad>();
            if (squadNo == 0) targets.AddRange(squads);
            else if (squadNo <= squads.Count) targets.Add(squads[squadNo - 1]);
            else { Plugin.V($"[Squads] there is no squad {squadNo}"); return; }

            var enemy = LookedAtEnemy(targets[0]);
            if (enemy == null) { Plugin.V("[Squads] no enemy under the centre of the screen"); return; }

            bool move = Plugin.SquadAttackMove.Value;
            foreach (var sq in targets)
            {
                try
                {
                    sq.CommandAttackOnUnit(enemy, move, true);
                    Plugin.V($"[Squads] '{sq.SquadName}' -> attack '{enemy.gameObject.name}' ({(move ? "move in" : "hold position")})");
                }
                catch (Exception e) { Plugin.L.LogWarning($"[Squads] order to '{sq.SquadName}' failed: {e.Message}"); }
            }
        }
    }
}
