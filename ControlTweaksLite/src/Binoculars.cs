// Commander head-out of the hatch (with or without binoculars; not the external cupola camera, which is the same
// game camera with the commander inside): steadies the final camera, after the game, its animation,
// constraints and Cinemachine have placed it for the frame.
// - Tilt: the camera's rotation is split into the tank's part and the rest (mouse look, head animation). The tank's
//   part is replaced by a levelled, smoothed version: heading still follows the tank, Stabilize cancels that share of
//   the tank's pitch/roll, and what is left follows with Smoothing delay. Mouse look is untouched.
// Before the next frame the camera is put back exactly as the game left it, so the game never sees our change.
// Harmony-only.
using System;
using HarmonyLib;
using UnityEngine;

namespace TankSquadControlTweaksLite
{
    [HarmonyPatch]
    internal static class Binoculars
    {
        private static Transform _cam;
        private static Vector3 _savedLocalPos, _writtenLocalPos;
        private static Quaternion _savedLocalRot, _writtenLocalRot;
        private static bool _written, _have, _logged;
        private static Quaternion _tilt;
        private static int _frame = -1;

        // diagnostics: for the first seconds of each binocular spell, how much the parts of the camera move
        private static float _diagStart = -1f, _diagNext;
        private static Quaternion _prevRest, _prevTilt, _prevOut; private static bool _prevHave;
        private static float _maxRest, _maxTilt, _maxOut, _maxFix; private static int _nMec, _nBrain;
        private static string _mode = "?";

        private static void Diag(Quaternion rest, Quaternion tilt, Quaternion outRot, Quaternion inRot, bool fromBrain)
        {
            float now = Time.unscaledTime;
            if (_diagStart < 0f)
            {
                _diagStart = now; _diagNext = now + 1f; _prevHave = false; _maxRest = _maxTilt = _maxOut = _maxFix = 0f; _nMec = _nBrain = 0;
                try { _mode = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance.CurrentCameraMode.gameObject.name; } catch { _mode = "?"; }
            }
            if (now - _diagStart > 8f || !Plugin.VerboseLog.Value) return;
            if (fromBrain) _nBrain++; else _nMec++;
            if (_prevHave)
            {
                _maxRest = Mathf.Max(_maxRest, Quaternion.Angle(_prevRest, rest));
                _maxTilt = Mathf.Max(_maxTilt, Quaternion.Angle(_prevTilt, tilt));
                _maxOut = Mathf.Max(_maxOut, Quaternion.Angle(_prevOut, outRot));
            }
            _maxFix = Mathf.Max(_maxFix, Quaternion.Angle(inRot, outRot));
            _prevRest = rest; _prevTilt = tilt; _prevOut = outRot; _prevHave = true;
            if (now >= _diagNext)
            {
                _diagNext = now + 1f;
                Plugin.L.LogInfo($"[Binoculars] '{_mode}' last second: look/head part moved up to {_maxRest:0.00} deg/frame, tank tilt {_maxTilt:0.00} deg/frame, final camera {_maxOut:0.00} deg/frame, correction up to {_maxFix:0.0} deg (passes: after MEC {_nMec}, after Cinemachine {_nBrain})");
                _maxRest = _maxTilt = _maxOut = _maxFix = 0f; _nMec = _nBrain = 0;
            }
        }

        // Put the camera back as the game left it (only if the game has not set it again itself).
        internal static void Restore()
        {
            if (!_written) return;
            _written = false;
            try
            {
                if (_cam == null || _cam.WasCollected) return;
                if ((_cam.localPosition - _writtenLocalPos).sqrMagnitude < 1e-10f && Quaternion.Angle(_cam.localRotation, _writtenLocalRot) < 1e-3f)
                { _cam.localPosition = _savedLocalPos; _cam.localRotation = _savedLocalRot; }
            }
            catch { }
        }

        // MEC's LateUpdate (after animation and constraints) ...
        [HarmonyPostfix, HarmonyPatch(typeof(MEC.Timing), "LateUpdate")]
        private static void AfterMec() { try { Apply(false); } catch { } }

        // ... and after Cinemachine, if it is the one placing the camera.
        [HarmonyPostfix, HarmonyPatch(typeof(Cinemachine.CinemachineBrain), "LateUpdate")]
        private static void AfterBrain(Cinemachine.CinemachineBrain __instance)
        {
            try
            {
                if (_cam == null || __instance == null || __instance.OutputCamera == null) return;
                if (__instance.OutputCamera.transform.Pointer != _cam.Pointer) return;
                Apply(true);
            }
            catch { }
        }

        private static void Apply(bool fromBrain)
        {
            if (Views.Current != TankView.Binoculars) { Restore(); _have = false; _diagStart = -1f; return; }
            float k = Mathf.Clamp01(Plugin.CupolaStabilize.Value), tau = Mathf.Max(0f, Plugin.CupolaSmoothing.Value);
            if (k <= 0f) { Restore(); _have = false; return; }

            var vc = Com.DeGenerals.TankSquad.Vehicle.VehicleCameraController.LocalPlayerInstance;
            var me = Com.DeGenerals.TankSquad.Vehicle.VehicleSettings.LocalPlayer;
            if (vc == null || me == null) return;
            Transform cam = null;
            try { cam = vc.CachedCameraTransform; } catch { }
            if (cam == null) { try { cam = vc.CameraReference?.transform; } catch { } }
            if (cam == null) return;
            var rb = me.VehicleDriveReference?.model?.rigidbody;
            var tank = rb != null ? rb.transform : me.transform;

            // Once per frame; if Cinemachine places the camera after MEC, its pass redoes it from the fresh pose.
            if (_frame == Time.frameCount && !fromBrain) return;
            if (_frame == Time.frameCount && fromBrain) Restore();
            _frame = Time.frameCount;

            if (_cam == null || _cam.Pointer != cam.Pointer) { Restore(); _cam = cam; _have = false; }
            if (!_logged)
            {
                _logged = true;
                bool brain = false; try { var b = cam.GetComponent<Cinemachine.CinemachineBrain>(); brain = b != null && b.enabled; } catch { }
                Plugin.V($"[Binoculars] steadying camera '{cam.name}' (parent '{(cam.parent != null ? cam.parent.name : "-")}', Cinemachine {(brain ? "yes" : "no")})");
            }

            // the pose the game produced
            Vector3 pc = cam.position; Quaternion rc = cam.rotation;
            _savedLocalPos = cam.localPosition; _savedLocalRot = cam.localRotation;

            Quaternion T = tank.rotation;
            Vector3 f = T * Vector3.forward; var fh = new Vector3(f.x, 0f, f.z);
            if (fh.sqrMagnitude < 1e-6f) return;
            Quaternion level = Quaternion.LookRotation(fh.normalized, Vector3.up);
            Quaternion tilt = Quaternion.Inverse(level) * T;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);

            if (!_have) { _tilt = tilt; _have = true; }
            else _tilt = tau > 0f ? Quaternion.Slerp(_tilt, tilt, 1f - Mathf.Exp(-dt / tau)) : tilt;

            Quaternion steadyTank = level * Quaternion.Slerp(Quaternion.identity, _tilt, 1f - k);
            Quaternion rest = Quaternion.Inverse(T) * rc;                                // mouse look + head, relative to the tank
            cam.rotation = steadyTank * rest;
            try { Diag(rest, tilt, cam.rotation, rc, fromBrain); } catch { }

            _writtenLocalPos = cam.localPosition; _writtenLocalRot = cam.localRotation;
            _written = true;
        }
    }
}
