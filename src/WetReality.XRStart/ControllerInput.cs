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

            UpdateButtons(__instance, r, l);

            // Spruehen - Trigger oder der Dauerspruehen-Schalter auf dem Griff
            var trig = Axis(r, "trigger");
            // Im Menue gehoert der Trigger dem Klick - und bis zum Loslassen danach (MenuPointer.cs).
            bool fire = !menuOwnsTrigger && ((trig != null && trig.ReadValue() > FireThreshold) || fireLatched);
            if (fire) { __instance.Fire = true; weFire = true; fireFrames++; }
            else if (weFire) { __instance.Fire = false; weFire = false; }

            // Gehen und Sprint
            var ls = Stick(l);
            var mv = ls == null ? Vector2.zero : ls.ReadValue();

            // Tragen + linker Griff: der linke Stick dreht das Objekt, das Gehen
            // steht still (PWS2 DriveItemRotation). PWS1-Weg wie
            // PlayerInput.RotateClockwise/-Anticlockwise (0x9EFEA0/0x9EFF60):
            // wenn !BlockedInput, GameEvents.Rotating(-1 / +1). Pro Frame mit der
            // Auslenkung skaliert, aus der Totzone heraus weich.
            if (pi_CarryAndGrip(__instance, l))
            {
                float x = mv.x;
                if (Math.Abs(x) > TurnDeadZone && !__instance.BlockedInput)
                {
                    float s = (Math.Abs(x) - TurnDeadZone) / (1f - TurnDeadZone);
                    // Faktor 2 seit 1.5.0: 1.4.0 war im Headset "zu langsam". Die
                    // Einheit von Rotating ist ungemessen (PWS2: 90 Grad/s).
                    Il2CppPWS.GameEvents.Rotating?.Invoke(-Math.Sign(x) * s * RotateFactor);   // rechts = im Uhrzeigersinn = -1
                    rotateFrames++;
                }
                mv = Vector2.zero;   // Gehen ruht, einmal auf 0 (unten)
            }

            if (mv.magnitude > MoveDeadZone)
            {
                __instance.MovementRaw = mv;
                weMove = true;
                moveFrames++;
            }
            else if (weMove) { __instance.MovementRaw = Vector2.zero; weMove = false; }

            // PWS2 RunBlockedByStance: im Hocken und Liegen kein Sprint - das
            // Spiel richtet den Avatar sonst zum Sprinten wieder auf.
            bool standing = StandingNow();
            bool sprint = mv.magnitude > SprintThreshold && standing;
            if (sprint) { __instance.Sprint = true; weSprint = true; sprintFrames++; }
            else if (weSprint) { __instance.Sprint = false; weSprint = false; }

            inputLast = $"trigger {(trig == null ? "fehlt" : trig.ReadValue().ToString("F2"))} linker Stick {(ls == null ? "fehlt" : mv.ToString("F2"))} | " +
                $"Spiel liest Fire={__instance.Fire} MovementRaw={__instance.MovementRaw.ToString("F2")} Sprint={__instance.Sprint}";
        }
        catch { }
    }

    // ---- Stufe 2 (1.1.0): Knoepfe ueber die Delegates von BaseInput --------
    //
    // Dieselben Wege wie die Rewired-Handler des Spiels, im Maschinencode
    // gelesen (PlayerInput.JumpPressed 0x9EF430 u. a.): wenn !BlockedInput und
    // !CarryItem, dann Delegate.Invoke - SwitchNozzle mit +1/-1. Die Mod ruft
    // die Delegates selbst und haelt dieselbe Sperre ein. Die Handler direkt
    // aufzurufen hiesse, Rewireds InputActionEventData (ein grosses Struct)
    // ueber die Interop-Grenze zu reichen.
    //
    //   rechts  A            Springen (Jump)
    //           B kurz       Ducken (CrouchPressed); B >= 0,5 s: CrouchLongPressed
    //           Stick runter Duese zurueck (SwitchNozzle -1)
    //           Stick hoch   frei fuer den Ziel-Teleport (PWS2 §147) - seit 1.1.2
    //           Griff        Dauerspruehen ein/aus; jeder Griffdruck raeumt ihn
    //           R3           frei fuer die Waehlscheibe
    //   links   Trigger      Duese drehen (RotateNozzle)
    //           X            Aufnehmen/Ablegen (PickupItemPressed PickUp)
    //           L3           Verlaengerung (SwitchExtension +1)
    //           Griff        Schmutz hervorheben (1.6.5), nur ohne Tragen - beim
    //                        Tragen ist er der Dreh-Modifikator (PWS2 dirtButton).
    //                        PlayerInput.TriggerDirtHighlight (0x9EE1A0) ruft nur
    //                        WashManager.instance.TriggerDirtHighlight(), ohne Sperre.
    //           Menue        Pause (m_pauseAction) - ohne Sperre, wie das Spiel
    // Ereigniszeilen immer: welcher Knopf, was ausgeloest oder warum gesperrt.
    private const float CrouchHoldSeconds = 0.5f;
    private const float StickFlick = 0.7f, StickRearm = 0.3f;
    private static bool fireLatched;
    private static readonly Dictionary<string, bool> btnWas = new();
    private static float bDownAt = -1f;
    private static bool bLongSent, stickYArmed = true;
    private static readonly List<string> btnEvents = new();

    private static bool Edge(XRController? c, string side, string control)
    {
        bool down = false;
        try
        {
            var b = c == null ? null : c.TryGetChildControl(control)?.TryCast<ButtonControl>();
            down = b != null && b.isPressed;
        }
        catch { }
        string key = side + control;
        btnWas.TryGetValue(key, out bool was);
        btnWas[key] = down;
        return down && !was;
    }

    private static bool Held(XRController? c, string control)
    {
        try
        {
            var b = c == null ? null : c.TryGetChildControl(control)?.TryCast<ButtonControl>();
            return b != null && b.isPressed;
        }
        catch { return false; }
    }

    private static bool Free(Il2CppPWS.PlayerInput pi, string what)
    {
        if (pi.BlockedInput) { btnEvents.Add($"{what}: gesperrt (BlockedInput)"); return false; }
        if (pi.CarryItem) { btnEvents.Add($"{what}: gesperrt (traegt etwas)"); return false; }
        return true;
    }

    // Ein fehlender Control-Name liest still false - einmal je Controller
    // (neue Geraete-Id nach dem Wiederanmelden) protokollieren, was da ist.
    private static readonly HashSet<int> inventoried = new();

    private static void Inventory(XRController? c, string side, params string[] names)
    {
        if (c == null || !inventoried.Add(c.deviceId)) return;
        var sb = new System.Text.StringBuilder($"Controls {side} (id {c.deviceId}):");
        foreach (var n in names)
            sb.Append($" {n}={(c.TryGetChildControl(n) == null ? "FEHLT" : "ok")}");
        btnEvents.Add(sb.ToString());
    }

    // 1.1.1 lieferte fuer A, B, Griff, linken Trigger und X KEIN Ereignis, obwohl
    // A, B, Griff und linker Trigger im Spiel wirkten - sie kamen auf einem
    // anderen Weg an (Hypothese: Gamepad-Emulation von Virtual Desktop). Diese
    // Zeile zeigt, was OpenXR selbst von jedem Knopf sieht, einmal pro Sekunde:
    // Zustand jetzt und ob er in der Sekunde je gedrueckt war.
    private static readonly string[] ProbeR = { "primaryButton", "secondaryButton", "gripPressed", "triggerPressed", "thumbstickClicked", "primaryTouched" };
    private static readonly string[] ProbeL = { "primaryButton", "secondaryButton", "gripPressed", "triggerPressed", "thumbstickClicked", "menu" };
    private static readonly Dictionary<string, int> seenPressed = new();
    private static float gripMaxR, trigMaxL;

    private static void ProbeButtons(XRController? r, XRController? l)
    {
        foreach (var n in ProbeR) if (Held(r, n)) { seenPressed.TryGetValue("R." + n, out int k); seenPressed["R." + n] = k + 1; }
        foreach (var n in ProbeL) if (Held(l, n)) { seenPressed.TryGetValue("L." + n, out int k); seenPressed["L." + n] = k + 1; }
        var g = Axis(r, "grip");
        if (g != null) gripMaxR = Math.Max(gripMaxR, g.ReadValue());
        var t = Axis(l, "trigger");
        if (t != null) trigMaxL = Math.Max(trigMaxL, t.ReadValue());
    }

    private static string ButtonProbeStatus()
    {
        var sb = new System.Text.StringBuilder($"OpenXR-Knoepfe (Frames gedrueckt): grip-Achse rechts max {gripMaxR:F2}, trigger-Achse links max {trigMaxL:F2} |");
        if (seenPressed.Count == 0) sb.Append(" keiner gedrueckt");
        foreach (var kv in seenPressed) sb.Append($" {kv.Key}={kv.Value}");
        seenPressed.Clear();
        gripMaxR = trigMaxL = 0f;
        return sb.ToString();
    }

    private static void UpdateButtons(Il2CppPWS.PlayerInput pi, XRController? r, XRController? l)
    {
        try
        {
            ProbeButtons(r, l);
            Inventory(r, "rechts", "trigger", "thumbstick", "primaryButton", "secondaryButton", "gripPressed", "thumbstickClicked");
            Inventory(l, "links", "thumbstick", "triggerPressed", "primaryButton", "thumbstickClicked", "menu");

            if (Edge(r, "R", "primaryButton") && Free(pi, "A Springen"))
            {
                pi.Jump?.Invoke();
                btnEvents.Add("A: Jump");
            }

            // B: kurz = Ducken, lang = CrouchLongPressed, einmal je Druck.
            bool bNow = Held(r, "secondaryButton");
            if (Edge(r, "R", "secondaryButton")) { bDownAt = Time.unscaledTime; bLongSent = false; }
            if (bNow && bDownAt >= 0f && !bLongSent && Time.unscaledTime - bDownAt >= CrouchHoldSeconds)
            {
                bLongSent = true;
                if (Free(pi, "B lang")) { pi.CrouchLongPressed?.Invoke(); btnEvents.Add("B lang: CrouchLongPressed"); }
            }
            if (!bNow && bDownAt >= 0f)
            {
                if (!bLongSent && Free(pi, "B kurz")) { pi.CrouchPressed?.Invoke(); btnEvents.Add("B kurz: CrouchPressed"); }
                bDownAt = -1f;
            }

            // Rechter Stick Y: ein Schnipp je Auslenkung, erst nach der Mitte wieder.
            var rs = Stick(r);
            float y = rs == null ? 0f : rs.ReadValue().y;
            if (Math.Abs(y) < StickRearm) stickYArmed = true;
            else if (stickYArmed && y < -StickFlick && !menuActive)
            {
                // Nur runter: hoch gehoert dem Teleport (PWS2 §147), der Zyklus
                // erreicht rueckwaerts weiter jede Duese.
                stickYArmed = false;
                if (Free(pi, "Stick runter"))
                {
                    pi.SwitchNozzle?.Invoke(-1);
                    btnEvents.Add("Stick runter: SwitchNozzle(-1)");
                }
            }

            if (Edge(r, "R", "gripPressed"))
            {
                fireLatched = !fireLatched;
                btnEvents.Add($"Griff rechts: Dauerspruehen {(fireLatched ? "AN" : "AUS")}");
                Buzz(true, fireLatched ? "Dauerspruehen an" : "Dauerspruehen aus");
            }

            if (Edge(l, "L", "triggerPressed") && Free(pi, "Trigger links Duese drehen"))
            {
                pi.RotateNozzle?.Invoke();
                btnEvents.Add("Trigger links: RotateNozzle");
            }

            if (Edge(l, "L", "primaryButton"))
            {
                if (pi.BlockedInput) btnEvents.Add("X: gesperrt (BlockedInput)");
                else
                {
                    // Wie PlayerInput.PickUp (0x9EFC40): ZUERST das statische
                    // GameEvents.PickUpInput - darauf hoert die Aufnahme -, dann
                    // BaseInput.PickupItemPressed. 1.3.0 rief nur das zweite, und
                    // nichts wurde aufgenommen.
                    StartPickupDiag();
                    Il2CppPWS.GameEvents.PickUpInput?.Invoke(Il2CppPWS.PickUpInputAction.PickUp);
                    pi.PickupItemPressed?.Invoke(Il2CppPWS.PickUpInputAction.PickUp);
                    btnEvents.Add("X: GameEvents.PickUpInput + PickupItemPressed (PickUp)");
                }
            }

            if (Edge(l, "L", "gripPressed") && !pi.CarryItem)
                HighlightDirt();

            if (Edge(l, "L", "thumbstickClicked") && Free(pi, "L3 Verlaengerung"))
            {
                pi.SwitchExtension?.Invoke(1);
                btnEvents.Add("L3: SwitchExtension(1)");
            }

            if (Edge(l, "L", "menu"))
            {
                pi.m_pauseAction?.Invoke();
                btnEvents.Add("Menue: m_pauseAction");
            }
        }
        catch (Exception e)
        {
            btnEvents.Add("Knoepfe: Ausnahme " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static Il2CppPWS.WashManager? washManager;

    private static void HighlightDirt()
    {
        try
        {
            if (washManager == null) washManager = UnityEngine.Object.FindObjectOfType<Il2CppPWS.WashManager>();
            if (washManager == null) { btnEvents.Add("Griff links: kein WashManager"); return; }
            washManager.TriggerDirtHighlight();
            btnEvents.Add("Griff links: Schmutz hervorheben (WashManager.TriggerDirtHighlight)");
        }
        catch (Exception e) { btnEvents.Add("Griff links: Ausnahme " + e.GetType().Name + ": " + e.Message); washManager = null; }
    }

    // Aus OnUpdate: Ereigniszeilen der Knoepfe ausgeben (die Patches sind statisch).
    private void FlushButtonEvents()
    {
        if (btnEvents.Count == 0) return;
        foreach (var e in btnEvents) LoggerInstance.Msg("KNOPF " + e);
        btnEvents.Clear();
    }

    private static Il2CppPWS.PhysicalCharacterController? charCtl;
    private static int lastStance = -1;
    private static string stanceEvent = "";

    private static bool StandingNow()
    {
        try
        {
            if (charCtl == null) return true;   // unbekannt: das Spiel entscheidet wie bisher
            int st = (int)charCtl.CharacterCrouchState;
            if (st != lastStance)
            {
                lastStance = st;
                stanceEvent = $"Haltung {charCtl.CharacterCrouchState}{(st == 0 ? "" : " - Sprint gesperrt")}";
            }
            return st == 0;
        }
        catch { return true; }
    }

    private float nextCharResolve;

    // Aus OnUpdate: den Charakter-Controller finden und das Haltungsereignis loggen.
    private void ResolveCharacter()
    {
        if (stanceEvent.Length > 0) { LoggerInstance.Msg("STEUERUNG: " + stanceEvent); stanceEvent = ""; }
        if (charCtl != null || Time.unscaledTime < nextCharResolve) return;
        nextCharResolve = Time.unscaledTime + 1f;
        var c = headCtl != null ? headCtl.m_controller : null;
        charCtl = c != null ? c : UnityEngine.Object.FindObjectOfType<Il2CppPWS.PhysicalCharacterController>();
        if (charCtl != null) LoggerInstance.Msg($"STEUERUNG: Charakter '{charCtl.gameObject.name}', Haltung {charCtl.CharacterCrouchState}");
    }

    private static int rotateFrames;
    private const float RotateFactor = 2.4f;   // 1.5.0: 2 - "vielleicht noch 20 % schneller"
    internal static bool SprayingNow => weFire || fireLatched;
    private static bool lastCarry;

    private static bool pi_CarryAndGrip(Il2CppPWS.PlayerInput pi, XRController? l)
    {
        bool carry = pi.CarryItem;
        if (carry != lastCarry)
        {
            lastCarry = carry;
            Buzz(false, carry ? "aufgenommen" : "abgelegt");   // PWS2: pickup / place, Off-Hand
        }
        if (!carry) return false;
        var g = Axis(l, "grip");
        return g != null && g.ReadValue() > 0.6f;
    }

    // ---- Vibration (PWS2 Haptics / Buzz) ----------------------------------
    // PWS2 pulste ueber eigene OpenXR-Actions (XRBoot). Hier das Input System:
    // OculusTouchControllerOpenXR ist ein XRControllerWithRumble. Amplitude 1,
    // 0,25 s wie PWS2 (HapticAmplitude / HapticSeconds). Gepulst wird die
    // HANDELNDE Hand: Off-Hand fuer Zeigestrahl und Aufnehmen, rechts fuer das
    // Dauerspruehen.
    private const float HapticAmplitude = 1f, HapticSeconds = 0.25f;
    private static int buzzSent, buzzRefused;

    internal static void Buzz(bool right, string why)
    {
        try
        {
            var c = right ? XRController.rightHand : XRController.leftHand;
            var r = c == null ? null : c.TryCast<XRControllerWithRumble>();
            if (r == null) { buzzRefused++; if (buzzRefused <= 3) btnEvents.Add($"Vibration {why}: kein XRControllerWithRumble"); return; }
            // Rechts laeuft durch die Warteschlange der Strahl-Haptik, sonst
            // ueberschreibt die naechste Nachsendung den Impuls (PWS2).
            if (right) QueueSpray(HapticAmplitude, HapticSeconds);
            else r.SendImpulse(HapticAmplitude, HapticSeconds);
            buzzSent++;
            btnEvents.Add($"Vibration {(right ? "rechts" : "links")}: {why}");
        }
        catch (Exception e) { btnEvents.Add($"Vibration {why}: Ausnahme {e.GetType().Name}: {e.Message}"); }
    }

    private static void Release(Il2CppPWS.PlayerInput pi)
    {
        fireLatched = false;
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
        ResolveCharacter();
        if (!inputOn || !started || !trackBody || menuActive) return;   // im Menue scrollt der Stick (MenuPointer.cs)
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
        var s = $"STEUERUNG {(inputOn ? "AN" : "AUS")} | PlayerInput.Update {piCalls}x | Frames mit Fire {fireFrames} Gehen {moveFrames} Sprint {sprintFrames} Drehen-Objekt {rotateFrames} | " +
            $"Drehen {turnSum:F1}° | {inputLast} || {ButtonProbeStatus()}";
        piCalls = fireFrames = moveFrames = sprintFrames = rotateFrames = 0;
        turnSum = 0f;
        return s;
    }
}
