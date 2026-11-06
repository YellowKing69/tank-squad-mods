// Gunner-sight (manual first-person) turret traverse with inertia, like vector aiming.
// The game's manual aim adds the mouse movement straight onto the turret angle:
//     weapon.CurrentAngle += turnRate * weapon.CurrentAcceleration * deltaTime * TurnRateSpeedMod * FOVRatio * sensitivity
// so the turret follows the hand instantly. Here that movement moves a target instead, and the turret drives towards
// the target: it spins up to its real traverse speed (360 / TimeForFullRotation, scalable), then brakes so it stops on
// the target without overshooting. Small mouse moves still give small, precise corrections.
// Harmony-only.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Com.DeGenerals.TankSquad.Vehicle.Weapon;

namespace TankSquadControlTweaksLite
{
    [HarmonyPatch]
    internal static class GunnerAim
    {
        private class St { public float Target, Vel, Last; public bool Init; }
        private static readonly Dictionary<IntPtr, St> _st = new();
        private static readonly Dictionary<IntPtr, float> _before = new();
        private static bool _logged;

        // Spin-up time for the current zoom: 0 at SpinUpFovWide (15) and wider, SpinUpSeconds at the narrowest
        // (SpinUpFovNarrow, 5), along a curve: t = SpinUpSeconds * ((wide - fov) / (wide - narrow)) ^ SpinUpCurve.
        private static float _fov = -1f, _fovAt = -1f;
        private static float SpinUp()
        {
            float t = Plugin.GaSpinUp.Value;
            if (Plugin.GaSpinCurve.Value <= 0f) return t;            // no curve: the same at every zoom
            if (Time.unscaledTime - _fovAt > 0.1f)
            {
                _fovAt = Time.unscaledTime;
                try
                {
                    var mode = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance?.CurrentCameraMode;
                    if (mode != null) _fov = mode.CurrentFOV;
                }
                catch { }
            }
            if (_fov <= 0f) return t;
            float wide = Plugin.GaFovWide.Value, narrow = Plugin.GaFovNarrow.Value;
            if (wide <= narrow) return t;
            float x = Mathf.Clamp01((wide - _fov) / (wide - narrow));
            return t * Mathf.Pow(x, Plugin.GaSpinCurve.Value);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(VehicleAimingInputMouseAuto), "RotateInFPCameraManual")]
        private static void Before(VehicleWeaponRotationModel weapon)
        {
            try { if (weapon != null) _before[weapon.Pointer] = weapon.CurrentAngle; } catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VehicleAimingInputMouseAuto), "RotateInFPCameraManual")]
        private static void After(VehicleWeaponRotationModel weapon, float deltaTime)
        {
            try
            {
                if (weapon == null || !Plugin.GaOn.Value) return;
                if (!_before.TryGetValue(weapon.Pointer, out float before)) return;
                bool vertical = weapon._IsVertical;
                if (vertical && !Plugin.GaElevation.Value) return;

                float after = weapon.CurrentAngle;
                float dt = Math.Max(1e-4f, deltaTime);
                float now = Time.time;
                if (!_st.TryGetValue(weapon.Pointer, out var s)) { s = new St(); _st[weapon.Pointer] = s; }
                if (!s.Init || now - s.Last > 0.5f) { s.Target = before; s.Vel = 0f; s.Init = true; }   // (re)start from where the turret is
                s.Last = now;

                bool limited = weapon.limit_flag;
                float input = limited ? after - before : Mathf.DeltaAngle(before, after);   // what the mouse asked for this frame
                s.Target += input;
                if (limited) s.Target = Mathf.Clamp(s.Target, weapon.min_angle, weapon.max_angle);

                // the turret's own top speed (deg/s), and how fast it gets there
                float full = weapon.TimeForFullRotation;
                float vmax = (full > 0.01f ? 360f / full : 30f) * Plugin.GaSpeed.Value;
                float spin = SpinUp();
                float accel = vmax / Math.Max(0.001f, spin);

                float err = limited ? s.Target - before : Mathf.DeltaAngle(before, s.Target);
                // fastest speed from which it can still brake to a stop on the target, capped at top speed
                float want = Math.Sign(err) * Math.Min(vmax, Mathf.Sqrt(2f * accel * Math.Abs(err)));
                s.Vel = Mathf.MoveTowards(s.Vel, want, accel * dt);
                float step = s.Vel * dt;
                if (Math.Abs(step) >= Math.Abs(err) || Math.Abs(err) < 1e-3f) { step = err; s.Vel = Math.Abs(err) < 1e-3f ? 0f : s.Vel; }
                float angle = before + step;
                if (limited) angle = Mathf.Clamp(angle, weapon.min_angle, weapon.max_angle);

                weapon.CurrentAngle = angle;
                weapon.ManualTurnRate = s.Vel;
                if (!_logged && Plugin.VerboseLog.Value) { _logged = true; Plugin.L.LogInfo($"[GunnerAim] manual aim with inertia: top {vmax:0.#} deg/s, spin-up {spin:0.##} s at this zoom ({(vertical ? "elevation" : "traverse")})"); }
            }
            catch { }
        }
    }
}
