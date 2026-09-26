// Steuerung Stufe 1 (XRStart 1.0.0): Spruehen, Gehen, Sprint, Drehen.
//
// Regel aus CLAUDE.md: kein eigenes Eingabesystem, die Quest-Controller
// beantworten, was das Spiel ohnehin liest. PWS1 hat dieselbe Oberflaeche wie
// PWS2: BaseInput.Fire / MovementRaw / Sprint, gefuellt von PlayerInput aus
// Rewired.
//
// ANDERS ALS PWS2: kein Getter-Postfix. Die Getter sind eingeschmolzen UND
// vom Linker gefaltet (get_Fire = EventAttribute.get_IsOpcodeSet,
// get_MovementRaw = CanvasScaler.get_referenceResolution, 0 direkte
// Aufrufer, tools/find_callers.py) - ein Postfix wuerde nie feuern und die
// fremden Methoden auf derselben Adresse mitverbiegen. Stattdessen werden die
// WERTE gesetzt, in einem Postfix auf PlayerInput.Update, also direkt nach dem
// eigenen Schreibzugriff des Spiels. Setter aufzurufen ist bei gefaltetem Code
// unbedenklich: er schreibt dasselbe Feld.
//
// Tastatur und Maus bleiben: ueberschrieben wird nur, solange der Controller
// wirklich ausgelenkt ist, beim Loslassen einmal zurueckgesetzt.
//
// Belegung (PWS2-Handbuch §6):
//   rechter Trigger  Spruehen, solange gehalten (> 0,5)
//   linker Stick     Gehen (Totzone 0,15); voll ausgeschlagen (> 0,9) Sprint
//   rechter Stick X  Drehen, 90 Grad/s gleitend, Totzone 0,2 - direkt auf
//                    bodyYaw, den die Mod ohnehin fuehrt (nur mit Kopf, F7)
// F3 schaltet die Steuerung aus und an (an beim Start, wirkt nur mit XR).

using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int VK_F3 = 0x72;
    private bool f3WasDown;
    private static bool inputOn = true;

    private const float FireThreshold = 0.5f;
    private const float MoveDeadZone = 0.15f;
    private const float SprintThreshold = 0.9f;
    private const float TurnDeadZone = 0.2f;
    private const float TurnSpeed = 90f;

    // Vom letzten Frame: fuer das einmalige Zuruecksetzen beim Loslassen.
    private static bool weFire, weMove, weSprint;
    private static int piCalls, fireFrames, moveFrames, sprintFrames;
    private static float turnSum;
    private static string inputLast = "";
    private static bool inputStartedStatic;

    private void PatchControllerInput()
    {
        try
        {
            var up = HarmonyLib.AccessTools.Method(typeof(Il2CppPWS.PlayerInput), "Update");
            if (up == null) { LoggerInstance.Error("Patch PlayerInput.Update: Methode nicht gefunden"); return; }
            HarmonyInstance.Patch(up, postfix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(XRStart), nameof(PlayerInputPostfix))));
            LoggerInstance.Msg("Patch PlayerInput.Update (Postfix): installiert (ob er feuert, zeigt der Zaehler)");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("Patch PlayerInput.Update: " + e);
        }
    }

    private static AxisControl? Axis(XRController? c, string name)
        => c == null ? null : c.TryGetChildControl(name)?.TryCast<AxisControl>();

    private static Vector2Control? Stick(XRController? c)
        => c == null ? null : c.TryGetChildControl("thumbstick")?.TryCast<Vector2Control>();

    private static void PlayerInputPostfix(Il2CppPWS.PlayerInput __instance)
    {
        piCalls++;
        if (!inputOn || !inputStartedStatic) { Release(__instance); return; }
        try
        {
            var r = XRController.rightHand;
            var l = XRController.leftHand;

            // Spruehen
            var trig = Axis(r, "trigger");
            bool fire = trig != null && trig.ReadValue() > FireThreshold;
            if (fire) { __instance.Fire = true; weFire = true; fireFrames++; }
            else if (weFire) { __instance.Fire = false; weFire = false; }

            // Gehen und Sprint
            var ls = Stick(l);
            var mv = ls == null ? Vector2.zero : ls.ReadValue();
            if (mv.magnitude > MoveDeadZone)
            {
                __instance.MovementRaw = mv;
                weMove = true;
                moveFrames++;
            }
            else if (weMove) { __instance.MovementRaw = Vector2.zero; weMove = false; }

            bool sprint = mv.magnitude > SprintThreshold;
            if (sprint) { __instance.Sprint = true; weSprint = true; sprintFrames++; }
            else if (weSprint) { __instance.Sprint = false; weSprint = false; }

            inputLast = $"trigger {(trig == null ? "fehlt" : trig.ReadValue().ToString("F2"))} linker Stick {(ls == null ? "fehlt" : mv.ToString("F2"))} | " +
                $"Spiel liest Fire={__instance.Fire} MovementRaw={__instance.MovementRaw.ToString("F2")} Sprint={__instance.Sprint}";
        }
        catch { }
    }

    private static void Release(Il2CppPWS.PlayerInput pi)
    {
        try
        {
            if (weFire) { pi.Fire = false; weFire = false; }
            if (weMove) { pi.MovementRaw = Vector2.zero; weMove = false; }
            if (weSprint) { pi.Sprint = false; weSprint = false; }
        }
        catch { }
    }

    // Aus OnUpdate: Drehen auf bodyYaw. Nur solange die Mod den Kopf schreibt -
    // ohne Kopfschreiber dreht die Maus ueber das Spiel.
    private void UpdateControllerTurn()
    {
        inputStartedStatic = started;
        if (!inputOn || !started || !trackBody) return;
        try
        {
            var rs = Stick(XRController.rightHand);
            if (rs == null) return;
            float x = rs.ReadValue().x;
            if (Math.Abs(x) <= TurnDeadZone) return;
            // Aus der Totzone heraus weich einsetzen, nicht mit einem Sprung.
            float s = Math.Sign(x) * (Math.Abs(x) - TurnDeadZone) / (1f - TurnDeadZone);
            float d = s * TurnSpeed * Time.unscaledDeltaTime;
            bodyYaw = Mathf.Repeat(bodyYaw + d, 360f);
            turnSum += d;
        }
        catch { }
    }

    private void ToggleControllerInput()
    {
        inputOn = !inputOn;
        LoggerInstance.Msg($"F3: Controller-Steuerung {(inputOn ? "AN" : "AUS")}");
    }

    // Einmal pro Sekunde, im Kopf-Takt.
    private string InputStatus()
    {
        var s = $"STEUERUNG {(inputOn ? "AN" : "AUS")} | PlayerInput.Update {piCalls}x | Frames mit Fire {fireFrames} Gehen {moveFrames} Sprint {sprintFrames} | " +
            $"Drehen {turnSum:F1}° | {inputLast}";
        piCalls = fireFrames = moveFrames = sprintFrames = 0;
        turnSum = 0f;
        return s;
    }
}
