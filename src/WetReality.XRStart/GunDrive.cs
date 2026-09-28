// Pistole schreiben (XRStart 0.8.0) - F5 an/aus, aus beim Start, nur mit XR.
//
// Gemessen (Handbuch 2.9): PowerWasher_Assembly haengt unter dem animierten
// Armskelett (R_Hand/IKstabilizer/EquipmentAnchorRoot/EquipmentAnchor); kein
// Spielskript schreibt Anker oder Assembly lokal, nach OnLateUpdate bewegt
// niemand mehr den Anker. Die Mod schreibt deshalb die WELTPOSE der Assembly
// in onBeforeRender, hinter dem Kopf - Animator und alle LateUpdates sind
// dann gelaufen. PWS2-Kette (PWS2-Handbuch §5), noch ohne Versaetze:
//   toWorld    = camT.rotation * Inverse(hmdRotation)
//   gunRot     = toWorld * aimRotation            (Rotation aus der Aim-Pose)
//   handWorld  = camT.position + toWorld * (gripPosition - hmdPosition)
// Position aus der Grip-Pose: OpenXR legt deren Ursprung in die Handflaeche,
// den der Aim-Pose vor das Geraet (PWS2 §77).
//
// Rueckleseprobe: in OnUpdate wird die lokale Pose der Assembly gegen die im
// letzten onBeforeRender geschriebene gelesen. Weicht sie ab, hat zwischen
// Render und naechstem Update jemand geschrieben (Lehre aus 2.7).
// Beim Ausschalten kommt die lokale Ruhelage der Assembly zurueck.

using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int VK_F5 = 0x74;
    private bool f5WasDown;
    private bool writeGun;

    // Gebundene Assembly; nur gemeinsam geraeumt.
    private Transform? gunAsm, gunAnchor;
    private IntPtr gunRestFor = IntPtr.Zero;
    private Vector3 gunRestPos;
    private Quaternion gunRestRot = Quaternion.identity;
    private float nextGunResolve, nextGunLog;
    private int gunWrites, gunSkips;
    private string gunSource = "";

    // Rueckleseprobe
    private bool gunWroteLast;
    private Vector3 gunWrotePos;
    private Quaternion gunWroteRot;
    private int gunReadbackN, gunReadbackMoved;
    private float gunReadbackMaxPos, gunReadbackMaxRot;

    private void ToggleGun()
    {
        if (writeGun) { StopGun("F5"); return; }
        if (!started) { LoggerInstance.Msg("F5: XR laeuft nicht - erst F8"); return; }
        writeGun = true;
        gunWrites = gunSkips = 0;
        nextGunLog = 0f;
        gunWroteLast = false;
        LoggerInstance.Msg("F5: Pistole schreiben AN (Welt, onBeforeRender, ohne Versaetze)");
    }

    private void StopGun(string why)
    {
        if (!writeGun) return;
        writeGun = false;
        gunWroteLast = false;
        try
        {
            if (gunAsm != null && gunAsm.Pointer == gunRestFor)
            {
                gunAsm.localPosition = gunRestPos;
                gunAsm.localRotation = gunRestRot;
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("PISTOLE-SCHREIBEN: Ruhelage nicht zurueckgesetzt - " + e.GetType().Name + ": " + e.Message);
        }
        ReleaseGunFixes();
        LoggerInstance.Msg($"Pistole schreiben AUS ({why}) nach {gunWrites} Frames, {gunSkips} ausgelassen");
        gunAsm = gunAnchor = null;
    }

    // Anker = m_anchorPoint, das unter Camera.main haengt (1. Person, per
    // Zeiger); Assembly = der PowerWasherAssembler darunter.
    private bool ResolveGun(float now)
    {
        if (gunAsm != null && gunAnchor != null && gunAsm.parent != null && gunAsm.parent.Pointer == gunAnchor.Pointer)
            return true;
        gunAsm = gunAnchor = null;
        gunWroteLast = false;
        if (now < nextGunResolve) return false;
        nextGunResolve = now + 1f;

        var cam = Camera.main;
        if (cam == null) return false;
        var camT = cam.transform;
        var mgrs = UnityEngine.Object.FindObjectsOfType<EquipmentManager>();
        for (int i = 0; i < mgrs.Length && gunAnchor == null; i++)
        {
            var a = mgrs[i].m_anchorPoint;
            if (a != null && IsUnder(a, camT)) gunAnchor = a;
        }
        if (gunAnchor == null) { LoggerInstance.Msg("PISTOLE-SCHREIBEN: kein Anker unter Camera.main - warte"); return false; }
        var asms = UnityEngine.Object.FindObjectsOfType<PowerWasherAssembler>();
        for (int i = 0; i < asms.Length && gunAsm == null; i++)
        {
            var t = asms[i].transform;
            if (IsUnder(t, gunAnchor)) gunAsm = t;
        }
        if (gunAsm == null) { LoggerInstance.Msg("PISTOLE-SCHREIBEN: keine Assembly unter dem Anker - warte"); gunAnchor = null; return false; }
        if (gunAsm.Pointer != gunRestFor)
        {
            gunRestPos = gunAsm.localPosition;
            gunRestRot = gunAsm.localRotation;
            gunRestFor = gunAsm.Pointer;
        }
        LoggerInstance.Msg($"PISTOLE-SCHREIBEN: gebunden {PathOf(gunAsm)} rest lp={gunRestPos.ToString("F3")} le={gunRestRot.eulerAngles.ToString("F1")}");
        return true;
    }

    // In onBeforeRender, nach DriveHead.
    private void DriveGun()
    {
        if (!started) { StopGun("XR aus"); return; }
        float now = Time.unscaledTime;
        if (!ResolveGun(now)) return;
        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            var ctl = WasherCtl;   // Pistolenhand (Handedness.cs)
            if (hmd == null || !hmd.isTracked.isPressed || ctl == null || !ctl.isTracked.isPressed)
            {
                gunSkips++;
                gunWroteLast = false;
                if (now >= nextGunLog)
                {
                    nextGunLog = now + 1f;
                    LoggerInstance.Msg($"PISTOLE-SCHREIBEN f={Time.frameCount}: {(ctl == null ? "kein rechter Controller" : hmd == null ? "kein HMD" : "nicht getrackt")} - Frame ausgelassen");
                }
                return;
            }

            var hmdPos = hmd.centerEyePosition.ReadValue();
            var hmdRot = hmd.centerEyeRotation.ReadValue();
            var gripPos = ctl.devicePosition.ReadValue();
            var aim = ctl.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
            Quaternion aimRot;
            if (aim != null) { aimRot = aim.ReadValue(); gunSource = "pointerRotation"; }
            else { aimRot = ctl.deviceRotation.ReadValue(); gunSource = "deviceRotation (kein pointerRotation)"; }

            var camT = Camera.main!.transform;
            var toWorld = camT.rotation * Quaternion.Inverse(hmdRot);
            var gunRot = toWorld * aimRot;
            var handWorld = camT.position + toWorld * (gripPos - hmdPos);
            // Griff-Feintuning (1.32.0, PWS2-Schluessel): Drehung und Versatz im
            // EIGENEN Rahmen der Pistole; Vorgabe 0 = wie bisher.
            var rawRot = gunRot;
            var rawPos = handWorld;
            gunRot = gunRot * GripRotation;
            handWorld += gunRot * GripOffset;
            // Live-Kalibrierung (Grip.cs): waehrend der Kombination bleibt die
            // Pistole eingefroren; beim Loslassen wird der Griff geloest.
            CalibrateGrip(rawRot, rawPos, ref gunRot, ref handWorld);

            gunAsm!.SetPositionAndRotation(handWorld, gunRot);
            gunWrotePos = gunAsm.localPosition;
            gunWroteRot = gunAsm.localRotation;
            gunWroteLast = true;
            gunWrites++;
            pubAsmPos = handWorld;       // fuer den RaycastUpdate-Postfix (GunFixes.cs)
            pubAsmRot = gunRot;
            gunPublished = true;

            if (now >= nextGunLog)
            {
                nextGunLog = now + 1f;
                LoggerInstance.Msg($"PISTOLE-SCHREIBEN f={Time.frameCount} n={gunWrites} skip={gunSkips} | Quelle {gunSource} | " +
                    $"grip={gripPos.ToString("F3")} aim={aimRot.eulerAngles.ToString("F1")} hmd={hmdPos.ToString("F3")} | " +
                    $"Welt p={handWorld.ToString("F3")} e={gunRot.eulerAngles.ToString("F1")} | lokal lp={gunWrotePos.ToString("F3")} le={gunWroteRot.eulerAngles.ToString("F1")} | " +
                    $"Rueckleseprobe {gunReadbackMoved}/{gunReadbackN} Frames veraendert, max {gunReadbackMaxPos * 1000f:F1}mm/{gunReadbackMaxRot:F2}°");
                LoggerInstance.Msg(FixStatus());
                gunReadbackN = gunReadbackMoved = 0;
                gunReadbackMaxPos = gunReadbackMaxRot = 0f;
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Error("PISTOLE-SCHREIBEN: Ausnahme, ausgeschaltet - " + e);
            StopGun("Ausnahme");
        }
    }

    // Am Anfang von OnUpdate: steht noch, was die Mod im letzten Render schrieb?
    private void CheckGunReadback()
    {
        if (!writeGun || !gunWroteLast || gunAsm == null) return;
        try
        {
            float dp = (gunAsm.localPosition - gunWrotePos).magnitude;
            float dr = AngleDeg(gunWroteRot, gunAsm.localRotation);
            gunReadbackN++;
            if (dp > 1e-5f || dr > 1e-3f)
            {
                gunReadbackMoved++;
                gunReadbackMaxPos = Math.Max(gunReadbackMaxPos, dp);
                gunReadbackMaxRot = Math.Max(gunReadbackMaxRot, dr);
            }
        }
        catch { }
    }
}
