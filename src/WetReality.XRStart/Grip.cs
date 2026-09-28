// Griff-Feintuning (XRStart 1.32.0). PWS2-Schluessel GripOffsetX/Y/Z (Meter im
// eigenen Rahmen der Pistole: x rechts, y oben, z entlang des Laufs) und
// RotationOffsetPitch/Yaw/Roll (Grad), angewandt in DriveGun (GunDrive.cs):
// gunRot = Controller * Euler(pitch, yaw, roll), Position += gunRot * Versatz.
// Vorgabe 0 (PWS1 sass bisher exakt auf dem Controller). Einzustellen im
// Konfigurator (Feld GRIFF) oder live im Spiel (unten, seit 1.33.0).

using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<float> prefGripX = null!, prefGripY = null!, prefGripZ = null!,
        prefRotPitch = null!, prefRotYaw = null!, prefRotRoll = null!;

    private void InitGrip(MelonPreferences_Category cat)
    {
        prefGripX = cat.CreateEntry("GripOffsetX", 0f, description: "Meter im Rahmen der Pistole; positiv = rechts");
        prefGripY = cat.CreateEntry("GripOffsetY", 0f, description: "Meter im Rahmen der Pistole; positiv = oben");
        prefGripZ = cat.CreateEntry("GripOffsetZ", 0f, description: "Meter im Rahmen der Pistole; positiv = nach vorn entlang des Laufs");
        prefRotPitch = cat.CreateEntry("RotationOffsetPitch", 0f, description: "Grad; Neigung auf und ab");
        prefRotYaw = cat.CreateEntry("RotationOffsetYaw", 0f, description: "Grad; Drehung links und rechts");
        prefRotRoll = cat.CreateEntry("RotationOffsetRoll", 0f, description: "Grad; seitlich verdrehen");
    }

    // ---------------------------------------------------------------- Live
    // LIVE-KALIBRIERUNG (1.33.0, Port PWS2 ReadCalibrateHold/SolveCalibration):
    // Griff + primary + secondary der FREIEN Hand halten (rechtshaendig linker
    // Griff + X + Y) - die Pistole bleibt stehen; die Pistolenhand dorthin, wo
    // die Pistole sitzen soll; loslassen. Geloest wird im Rahmen DIESES Frames:
    //   Drehung = Inverse(roh) * eingefroren, Versatz = Inverse(eingefroren) *
    //   (eingefrorenePos - rohePos), Versatz auf +-25 cm geklemmt.
    // Gespeichert per MelonPreferences.Save() - der Konfigurator zeigt die Werte.
    // Waehrend der Kombination und 0,3 s danach ruhen Schmutz (Griff),
    // Aufnehmen (X) und Y (CalibrateSuppressed) - sonst oeffnete das Loslassen
    // das Inventar (PWS2: die Aufgabenliste).
    internal static bool calibrating;
    private static bool calibHeldLast;
    internal static float calibLockUntil;
    private Quaternion calibRot = Quaternion.identity;
    private Vector3 calibPos;

    internal static bool CalibrateSuppressed => calibrating || calibHeldLast || Time.unscaledTime < calibLockUntil;

    private static bool CalibrateChordHeld()
    {
        var c = OffCtl;
        if (c == null) return false;
        try
        {
            var grip = c.TryGetChildControl("grip")?.TryCast<UnityEngine.InputSystem.Controls.AxisControl>();
            var pri = c.TryGetChildControl("primaryButton")?.TryCast<UnityEngine.InputSystem.Controls.ButtonControl>();
            var sec = c.TryGetChildControl("secondaryButton")?.TryCast<UnityEngine.InputSystem.Controls.ButtonControl>();
            return grip != null && grip.ReadValue() > 0.6f && pri != null && pri.isPressed && sec != null && sec.isPressed;
        }
        catch { return false; }
    }

    private void CalibrateGrip(Quaternion rawRot, Vector3 rawPos, ref Quaternion gunRot, ref Vector3 gunPos)
    {
        bool held = !menuActive && !wheelOpen && CalibrateChordHeld();
        if (held) calibLockUntil = Time.unscaledTime + 0.3f;
        if (held && !calibHeldLast)
        {
            calibrating = true;
            calibRot = gunRot;
            calibPos = gunPos;
            Buzz(true, "Kalibrieren");
            LoggerInstance.Msg("GRIFF-KALIBRIERUNG: Pistole steht - Pistolenhand dorthin, wo die Pistole sitzen soll, dann loslassen");
        }
        if (calibrating && !held && calibHeldLast)
        {
            calibrating = false;
            SolveGrip(rawRot, rawPos);
        }
        calibHeldLast = held;
        if (calibrating) { gunRot = calibRot; gunPos = calibPos; }   // eingefroren
    }

    private void SolveGrip(Quaternion rawRot, Vector3 rawPos)
    {
        var wantRot = Quaternion.Inverse(rawRot) * calibRot;
        var wantGrip = Quaternion.Inverse(calibRot) * (calibPos - rawPos);
        float pitch = Wrap180(wantRot.eulerAngles.x), yaw = Wrap180(wantRot.eulerAngles.y), roll = Wrap180(wantRot.eulerAngles.z);
        float moved = (wantGrip - GripOffset).magnitude, turned = Quaternion.Angle(GripRotation, wantRot);
        if (moved < 0.005f && turned < 1f) { LoggerInstance.Msg($"GRIFF-KALIBRIERUNG: losgelassen ohne Bewegung - nichts geaendert ({moved * 100f:F1} cm, {turned:F1} Grad)"); return; }
        var clamped = new Vector3(Mathf.Clamp(wantGrip.x, -0.25f, 0.25f), Mathf.Clamp(wantGrip.y, -0.25f, 0.25f), Mathf.Clamp(wantGrip.z, -0.25f, 0.25f));
        if ((clamped - wantGrip).magnitude > 0.0005f) LoggerInstance.Warning($"GRIFF-KALIBRIERUNG: Versatz {wantGrip.ToString("F3")} ausserhalb +-25 cm, geklemmt - Hand naeher an die Pistole und neu kalibrieren");
        prefGripX.Value = clamped.x; prefGripY.Value = clamped.y; prefGripZ.Value = clamped.z;
        prefRotPitch.Value = pitch; prefRotYaw.Value = yaw; prefRotRoll.Value = roll;
        MelonPreferences.Save();
        Buzz(true, "Kalibriert");
        LoggerInstance.Msg($"GRIFF-KALIBRIERUNG: Versatz {clamped.ToString("F3")} m, Drehung Neigung {pitch:F1} Drehung {yaw:F1} Kippung {roll:F1} - verschoben {moved * 100f:F1} cm, gedreht {turned:F1} Grad, gespeichert");
    }

    private static float Wrap180(float d) => d > 180f ? d - 360f : d;

    private Vector3 GripOffset => new(prefGripX.Value, prefGripY.Value, prefGripZ.Value);
    private Quaternion GripRotation => Quaternion.Euler(prefRotPitch.Value, prefRotYaw.Value, prefRotRoll.Value);
}
