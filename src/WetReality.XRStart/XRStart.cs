// WetReality.XRStart - erste Laufzeitmessung fuer PWS1 (Handbuch 1.5).
//
// Frage: Bringt der im Spiel eingetragene OpenXRLoader eine Session, wenn man
// ihn nur aufruft? PWS1 hat die ganze managed XR-Schicht einkompiliert und
// OpenXRLoader in XRManagerSettings eingetragen, startet ihn aber nie
// (InitManagerOnStart = false, BootingState.get_IsXR = konstant false).
//
// Die Mod tut genau eines: F8 startet den Loader (InitializeLoaderSync, dann
// StartSubsystems), F8 erneut stoppt ihn. Kein Autostart - scheitert der Start,
// bleibt das flache Spiel spielbar. Kamera, Pistole und Eingabe bleiben
// unberuehrt. Jeder Schritt meldet Erfolg UND Misserfolg; danach 10 s lang
// einmal pro Sekunde der Zustand, weil sich eine Session erst aufbaut.
//
// Die Taste kommt ueber GetAsyncKeyState, nicht UnityEngine.Input: das Spiel
// nutzt das neue Input System, ob der alte Eingabeweg aktiv ist, ist
// ungemessen.
//
// 0.1.0 hat gezeigt: die Session laeuft bis FOCUSED, das Headset bleibt
// schwarz, Camera.main ist im Hauptmenue null (Handbuch 2). 0.2.0 aendert
// nichts am Spiel und misst nur zweierlei:
//   - alle Szenenkameras (auch inaktive): Pfad, aktiv, Tag, Zielauge,
//     RenderTexture, stereoEnabled, depth, URP-renderType, Stapel. Voll
//     gelistet beim Start und immer dann, wenn sich die Liste aendert -
//     ein Zustand, der sich aufbauen kann, wird nicht einmalig abgegriffen.
//   - "[XR] SetOutput Failed." pro Frame, gezaehlt im mitgelesenen
//     Player.log, damit sich zeigt, ob die Meldung im Frametakt kommt.
//
// 0.2.0 hat gezeigt: im Auftrag zeichnet HeadTurn/PlayerCamera stereo, das
// Bild folgt dem Kopf nicht (Handbuch 2.2). 0.3.0 schreibt weiter NICHTS und
// misst, was der naechste Schritt braucht:
//   - Posequellen: XRHMD, linker/rechter XRController aus dem Input System -
//     vorhanden, getrackt, Pose, Trigger. Liefern die automatisch angehaengten
//     Action Sets Werte?
//   - Wer haelt den Kopf: lokale Pose von PlayerCamera, ihrem Elternknoten und
//     dessen Elternknoten, in DERSELBEN Zeile wie die HMD-Pose (ein Block,
//     eine Momentaufnahme). Gemessen in OnLateUpdate, hinter den
//     Update-Schreibern des Spiels. Fenster 30 s: erst Kopf, dann Maus bewegen.
//     Angekert an Camera.main (Tag MainCamera), nicht an Knotennamen.
//
// 0.4.0 hat gezeigt: Maus-Yaw auf HeadTurn.y, Pitch auf PlayerCamera.x, ein
// Spielschreiber zwischen OnUpdate und OnLateUpdate (Handbuch 2.4). Statisch
// ist PlayerCameraController.UpdateRotation aus PhysicalCharacterController.Update
// der Kandidat (Handbuch 2.5). 0.5.0 schreibt weiter NICHTS und misst im
// F9-Fenster, einmal pro Sekunde, alles in EINER Zeile desselben Frames:
//   - HeadTurn.y / PlayerCamera.x in OnUpdate (U), vor dem ersten und nach dem
//     letzten UpdateRotation (pre/post), in OnLateUpdate (L) und in
//     Application.onBeforeRender (R). Aendert sich L -> R, schreibt danach
//     noch jemand.
//   - Aufrufe von UpdateRotation und UpdateLookDirection: im Messframe und
//     pro Sekunde. Ein installierter Patch heisst nicht, dass er feuert.
//   - einmal zu Fensterbeginn: Zahl der PlayerCameraController, ob
//     m_horizontalLook/m_verticalLook dieselben Objekte wie Camera.main und
//     ihr Elternknoten sind, m_rigTransform, m_camera.
//   - im XR-Bericht: Tracking-Ursprung des XRInputSubsystem.
//
// 0.5.0 hat gezeigt: UpdateRotation schreibt HeadTurn.y/PlayerCamera.x 1x pro
// Frame absolut aus HorizontalLookRotation/VerticalLookRotation, danach
// schreibt bis onBeforeRender niemand (Handbuch 2.6). 0.6.0 SCHREIBT DEN KOPF,
// und nur den - F7 schaltet es, aus beim Start, nur bei laufendem XR.
// In OnLateUpdate, hinter UpdateRotation (PWS2-Kopfkette, PWS2-Handbuch §5):
//   HeadTurn.localRotation     = Euler(0, bodyYaw + hmdYaw, 0)
//   PlayerCamera.localRotation = Inverse(Euler(0, hmdYaw, 0)) * hmdRotation
//   HeadTurn.localPosition     = headTurnRest + Euler(0, bodyYaw, 0) * (hmd - headPoseBase)
// bodyYaw = HorizontalLookRotation, jedes Frame gelesen: die Maus dreht den
// Koerper weiter ueber das Spiel. Der Maus-Pitch (VerticalLookRotation) wird
// verworfen, der Kopf allein neigt. headTurnRest wird vor dem ersten
// Schreibzugriff je HeadTurn-Instanz erfasst, headPoseBase beim Einschalten
// (F7 aus/an = neu zentrieren). Beim Ausschalten kommt headTurnRest zurueck.
// Einmal pro Sekunde eine Zeile: der Wert des Spiels vor dem Schreiben
// (muss = bodyYaw sein, sonst liest das Spiel den eigenen Schreibzugriff
// zurueck) und das Geschriebene. F9 prueft wie in 0.5.0, ob L bis R haelt.
//
// 0.6.0 hat gezeigt: der Koerper dreht sich von selbst, mit hmdYaw x Framerate.
// UpdateRotation liest HeadTurn.localRotation.y nach HorizontalLookRotation,
// bevor es die Maus addiert - es integriert auf dem Transform und liest den
// Schreibzugriff der Mod zurueck (Handbuch 2.7). 0.6.1: bodyYaw fuehrt die
// Mod selbst, einmal aus HorizontalLookRotation uebernommen; hinein geht nur
// die Mausaenderung, gemessen als Differenz HeadTurn.y vor UpdateRotation ->
// HorizontalLookRotation danach (Prefix/Postfix, die es schon gibt).
//
// 0.6.1 hat gezeigt: kein Selbstdrehen mehr, Maus dreht den Koerper und bleibt
// stehen; Umschauen wirkt aber zittrig bei stabilen 45 Frames/s. Hypothese:
// die Pose aus OnLateUpdate ist aelter als die, mit der gerendert und
// uebergeben wird - die Reprojektion korrigiert dann falsch. 0.6.2: F6
// schaltet den Schreibpunkt zwischen OnLateUpdate (wie 0.6.1, Vorgabe) und
// Application.onBeforeRender (wie TrackedPoseDriver) - Vergleich im selben
// Lauf. Pro Sekunde: Winkel zwischen LateUpdate- und Render-Pose desselben
// Frames gegen die Kopfbewegung je Frame.
//
// 0.6.2 hat gezeigt: mit onBeforeRender "absolut fluessig" (Nutzer). Die
// LateUpdate-Pose liegt im Mittel 0,2-0,5 Grad (bis 2,4) hinter der
// Render-Pose, 20-35 % der Kopfbewegung eines Frames (Handbuch 2.8). 0.6.3:
// onBeforeRender ist die Vorgabe, F6 schaltet zurueck.
//
// 0.7.0: Pistolenmessung im F9-Fenster, schreibt NICHTS an der Pistole
// (GunProbe.cs). Der Kopf bleibt wie 0.6.3. 0.7.1: dazu Spielerposition,
// Rig/RigOffset lokal und Anker relativ zur Kamera - 0.7.0 lief im Stillstand.
// 0.8.0: F5 schreibt die Pistole - Weltpose der Assembly aus dem rechten
// Controller in onBeforeRender, nach dem Kopf (GunDrive.cs).
// 0.9.0: PWS2-Korrekturen fuer Pistole und Strahl, F4 (GunFixes.cs).
// 0.9.1: Duesenanker vom Zwilling der 3. Person (PWS2 Bit 65536, 1.103.0).
// 1.0.0: Steuerung Stufe 1 - Spruehen, Gehen, Sprint, Drehen (ControllerInput.cs).
// 1.1.0: Steuerung Stufe 2 - Knoepfe ueber die Delegates von BaseInput.
// 1.1.2: Knopf-Messzeile (was sieht OpenXR?), Stick hoch frei fuer den Teleport.
// 1.2.0: X zielt ueber den Zeigestrahl der Pistole (Pointer.cs) - falsche Hand.
// 1.3.0: X zielt mit der Off-Hand, Linie nur mit Ziel; Sprint nur im Stehen.
// 1.3.1: X loest auch GameEvents.PickUpInput aus; Strahlende an festen Collidern.
// 1.3.2: Tragen mit der linken Hand; Strahlende immer auf dem Strahl.
// 1.4.0: Vibration (Ziel, Aufnehmen/Ablegen, Dauerspruehen); Objekt drehen mit
//        linkem Griff + linkem Stick beim Tragen.
// 1.5.0: Strahl-Haptik (PWS2 SprayHaptics), Zeigestrahl beim Spruehen aus,
//        Objekt drehen doppelt so schnell.
// 1.6.0: Testumgebung (DevTools.cs): Autostart F8/F7/F5, Ladebildschirm-Weiter
//        automatisch, Cheats Guthaben + Jobs frei; Kontaktstrahl der Haptik mit
//        der Waschmaske des Spiels; Objekt drehen x2,4.
// 1.6.1: Cheat Sterne (Washer im Shop freischalten).
// 1.6.2: Sterne-Jingle alle 2 s behoben (DLC-Kampagnen nehmen keine Sterne an).
// 1.6.3: Sterne waehrend eines Jobs unberuehrt (sporadischer Ping).
// 1.6.4: Sterne nie mehr veraendert; Shop-Sperre StarsRemaining -> 0.
// 1.6.5: linker Griff ohne Tragen = Schmutz hervorheben.
// 1.7.0: Messung Menue-Canvases und Spiegelbild, F1/F2 (UiProbe.cs).
// 1.8.0: Spiel-UI im Headset - UIRoot als ScreenSpaceCamera, F10 (VrUi.cs);
//        Spiegel-Schalter DesktopMirror (UiProbe.cs).
// 1.9.0: Menue mit dem Zeigestrahl der rechten Hand, Trigger klickt (MenuPointer.cs).
// 1.9.1: Klick/Hover wie die Maus, Hover-Toleranz, Regler ziehen, Scrollen mit dem Stick.
// 1.9.2: Klick als Mausfolge ueber ExecuteEvents, kein Hover-Puls, Strahl geglaettet, Scrollweg.
// 1.9.3: Scrollen nur sichtbarer Seiten, Hover an die Elternkette, Werkzeug im Menue aus.
// 1.9.4: Treffer in der Ebene jedes Elements (Parallaxe), Scroll-Rueckfall.
// 1.9.5: Ziel = oberstes Graphic wie der GraphicRaycaster, Klick-Nachruecken, Scrollbalken.
// 1.9.6: Treffertest geometrisch statt ueber den Bildschirmpunkt (Versatz).
// 1.9.7: nur Graphics in Canvases mit GraphicRaycaster (1.9.6: kein Ziel mehr).
// 1.9.8: CanvasGroup/Maske ab dem Graphic selbst (SideMenuOverlay deckte alles zu).
// 1.9.9: keine veralteten Grenzen - Hover sofort statt nach 2-3 s.
// 1.10.0: Interaktionsreichweite aus der Hand 5 m wie PWS2 (InteractionRange).
// 1.11.0: VR-Haende des Spiels an den Controllern, Spielarme aus (VrHands.cs).
// 1.12.0: Fingerposen ueber die Animator-Parameter des Spiels, folgen den Controllern (HandPoses.cs).
// 1.13.0: Himmel - Messung und SkyFix, F2 (SkyFix.cs); rechte Hand feste Griffpose wie PWS2.
// 1.14.0: Waehlscheibe auf R3 halten, Y kurz Inventar, Y lang naechster Washer (Wheel.cs).
// 1.15.0: Scheibe waehlt per Zeiger-Kette/Stick (Select), zum Betrachter; Zeiger im Inventar.
// 1.15.1: Scheibe oeffnet am Strahlpunkt, Auswahl erst auf dem Ring, Knoten-Dump.
// 1.15.2: Ringradius aus den Slot-Knoten (Segmentabstand las 0), Shader-Messung.
// 1.15.3: Scheibe weltfest (Position + Drehung vom Oeffnen), Ring je Frame als Maximum.
// 1.15.4: Lage der Scheibe auch in Update/LateUpdate, Messung fremder Ueberschreibung.
// 1.16.0: AutoStart ohne feste Pausen, XR schon beim Laden des Levels.

using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

[assembly: MelonInfo(typeof(WetReality.XRStart.XRStart), "Wet Reality XRStart", "1.16.0", "Tino")]
[assembly: MelonGame("FuturLab", "PowerWash Simulator")]

namespace WetReality.XRStart;

public sealed partial class XRStart : MelonMod
{
    private const int VK_F7 = 0x76;
    private const int VK_F8 = 0x77;
    private const int VK_F9 = 0x78;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private bool keyWasDown;
    private bool f9WasDown;
    private bool started;

    // F9: Referenzlauf OHNE XR. Misst das Instrument die Mausdrehung ueberhaupt?
    private const float ReferenceSeconds = 20f;
    private float refStart, refUntil, nextRefUpdate;

    // Messframe des F9-Fensters. Die Patches schreiben hinein, ausgegeben wird
    // im naechsten OnUpdate, wenn alle fuenf Punkte des Frames gelesen sind.
    private static int sampleFrame = -1;
    private static bool sampleOpen;
    private static string sU = "", sPre = "-", sPost = "-", sL = "-", sR = "-", sLook = "";
    private static int sRotCalls;
    private static int rotCalls, lookCalls, windowFrame;
    private bool beforeRenderHooked;

    // F7: Kopf schreiben. Alle vier Referenzen gehoeren zusammen und werden
    // nur gemeinsam geraeumt (Unity-null sieht ein zerstoertes Objekt, "is
    // null" nicht).
    private bool f7WasDown;
    private bool writeHead;
    private PlayerCameraController? headCtl;
    private Transform? headTurn, headCam;
    private IntPtr restFor = IntPtr.Zero;   // HeadTurn-Instanz, zu der headTurnRest gehoert
    private Vector3 headTurnRest, headPoseBase;
    private bool haveBase;
    private float nextHeadLog, nextHeadResolve;
    private int headWrites;
    private float nextInputLog;

    // F6: Schreibpunkt onBeforeRender (Vorgabe seit 0.6.3, im Headset
    // "absolut fluessig") oder OnLateUpdate (wie 0.6.1, zittert).
    private static XRStart? self;
    private const int VK_F6 = 0x75;
    private bool f6WasDown;
    private bool writeAtRender = true;
    private Quaternion lateRot, lastRenderRot;
    private int lateRotFrame = -1;
    private bool haveRenderRot;
    private float poseGapSum, poseGapMax, headStepSum;
    private int poseGapN;
    private float nextXrUpdate;
    private float reportUntil;
    private float nextReport;
    private float startTime;
    private const float ReportSeconds = 30f;

    private const string SetOutputMarker = "SetOutput Failed";
    private long logOffset = -1;
    private int lastFrame;
    private string lastCameraSignature = "";

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("bereit - F8 startet den OpenXR-Loader des Spiels, F8 erneut stoppt ihn, F7 = Kopf schreiben an/aus (nur mit XR), F6 = Schreibpunkt LateUpdate/Render, F5 = Pistole schreiben an/aus (nur mit XR), F4 = PWS2-Korrekturen an/aus, F3 = Controller-Steuerung an/aus, F9 = 20 s Kopfmessung, F1 = Spiegel gameViewRenderMode, F2 = Himmel off/solid/skybox, F10 = UI im Headset an/aus");
        CountSetOutput();   // setzt den Offset; Meldungen vor dem Laden zaehlen nicht
        lastFrame = Time.frameCount;
        HookDeviceChanges();
        PatchCameraController();
        PatchGunWriters();
        PatchGunFixes();
        PatchControllerInput();
        PatchWheel();
        self = this;
        HookBeforeRender();   // seit 0.6.2 auch Schreibpunkt, nicht nur F9-Messung
        InitDevTools();       // Autostart, Ladebildschirm, Cheats (DevTools.cs)
        // Das 0x0001-Bit kann von einem frueheren Druck stehen (auch in einem
        // anderen Programm) - einmal ablesen, damit F8 nicht von selbst startet.
        foreach (int vk in new[] { VK_F1, VK_F2, VK_F10, VK_F11, VK_F3, VK_F4, VK_F5, VK_F6, VK_F7, VK_F8, VK_F9 }) GetAsyncKeyState(vk);
    }

    // Nur Zaehler und Lesungen, kein Eingriff: Prefix gibt nichts zurueck,
    // die Originalmethode laeuft immer.
    private void PatchCameraController()
    {
        PatchOne(nameof(PlayerCameraController.UpdateRotation), nameof(RotationPrefix), nameof(RotationPostfix));
        PatchOne(nameof(PlayerCameraController.UpdateLookDirection), nameof(LookPrefix), null);
    }

    private void PatchOne(string method, string prefix, string? postfix)
    {
        try
        {
            var target = AccessTools.Method(typeof(PlayerCameraController), method);
            if (target == null) { LoggerInstance.Error($"Patch {method}: Methode nicht gefunden"); return; }
            HarmonyInstance.Patch(target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(XRStart), prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(XRStart), postfix)));
            LoggerInstance.Msg($"Patch {method}: installiert (ob er feuert, zeigt der Zaehler)");
        }
        catch (Exception e)
        {
            LoggerInstance.Error($"Patch {method}: " + e);
        }
    }

    // Koerper-Yaw beim Kopfschreiben. UpdateRotation liest als Erstes
    // HeadTurn.localRotation.eulerAngles.y nach HorizontalLookRotation (Icall
    // get_localRotation_Injected, Handbuch 2.7) - also den Kopf-Yaw, den die
    // Mod im Vorframe geschrieben hat. Nur die Differenz vor/nach
    // UpdateRotation ist die Maus; nur sie geht in bodyYaw.
    private static bool trackBody;
    private static float bodyYaw, readYaw, mouseYawSum;
    private static bool haveRead;

    private static void RotationPrefix(PlayerCameraController __instance)
    {
        if (trackBody)
        {
            try
            {
                var h = __instance.m_horizontalLook;
                haveRead = h != null;
                if (haveRead) readYaw = h!.localEulerAngles.y;
            }
            catch { haveRead = false; }
        }
        rotCalls++;
        if (!sampleOpen || Time.frameCount != sampleFrame) return;
        sRotCalls++;
        if (sRotCalls == 1) sPre = ReadLook(__instance);
    }

    private static void RotationPostfix(PlayerCameraController __instance)
    {
        if (trackBody && haveRead)
        {
            float d = Mathf.DeltaAngle(readYaw, __instance.HorizontalLookRotation);
            bodyYaw = Mathf.Repeat(bodyYaw + d, 360f);
            mouseYawSum += d;
            haveRead = false;
        }
        ApplyLookPitch(__instance);   // PWS2 Bit 512, nur mit Pistole und F4
        if (!sampleOpen || Time.frameCount != sampleFrame) return;
        sPost = ReadLook(__instance);
    }

    private static void LookPrefix(float horizontalLook, float verticalLook)
    {
        lookCalls++;
        if (sampleOpen && Time.frameCount == sampleFrame)
            sLook += $" args=({horizontalLook:F2},{verticalLook:F2})";
    }

    // Die Knoten, die der Controller selbst haelt - nicht die von Camera.main.
    // Ob es dieselben sind, meldet ReportController zu Fensterbeginn.
    private static string ReadLook(PlayerCameraController c)
    {
        try
        {
            var h = c.m_horizontalLook;
            var v = c.m_verticalLook;
            return $"y={(h == null ? "null" : h.localEulerAngles.y.ToString("F1"))} x={(v == null ? "null" : v.localEulerAngles.x.ToString("F1"))}";
        }
        catch (Exception e) { return "Lesefehler " + e.GetType().Name; }
    }

    // Dieselben zwei Winkel, angekert an Camera.main: Kamera.x und Eltern.y.
    private static string ReadHead()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return "Camera.main=null";
            var t = cam.transform;
            var p = t.parent;
            return $"y={(p == null ? "kein Eltern" : p.localEulerAngles.y.ToString("F1"))} x={t.localEulerAngles.x:F1}";
        }
        catch (Exception e) { return "Lesefehler " + e.GetType().Name; }
    }

    private void HookBeforeRender()
    {
        if (beforeRenderHooked) return;
        try
        {
            Application.add_onBeforeRender(new System.Action(OnBeforeRender));
            beforeRenderHooked = true;
            LoggerInstance.Msg("onBeforeRender: angemeldet");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("onBeforeRender: nicht angemeldet - " + e);
        }
    }

    private static void OnBeforeRender()
    {
        self?.BeforeRender();
        // Nur der erste Aufruf des Messframes: das ist der Stand, mit dem
        // gerendert wird.
        if (sampleOpen && Time.frameCount == sampleFrame && sR == "-")
        {
            sR = ReadHead();
            sGunR = ReadGun();
        }
    }

    // Kopfpose zum zweiten Mal im Frame, kurz vor dem Rendern. Das Input
    // System aktualisiert getrackte Geraete vor dem Rendern noch einmal; der
    // Unterschied zur OnLateUpdate-Pose ist das, was der Schreibpunkt
    // LateUpdate zu alt ist. Geschrieben wird hier nur mit F6 = Render.
    private void BeforeRender()
    {
        if (writeHead) MeasurePoseGap();
        if (writeHead && writeAtRender) DriveHead();
        if (writeHead) DriveOffHandPointer();   // X zielt mit der linken Hand (Pointer.cs)
        DriveMenuPointer();                     // Menue: Strahl rechts, Trigger klickt (MenuPointer.cs)
        DriveWheelPointer();                    // Waehlscheibe: Zeiger-Kette und Ausrichtung (Wheel.cs)
        // Pistole nach dem Kopf: sie rechnet aus der Kamera, die der Kopf eben gesetzt hat.
        if (writeGun) { DriveGun(); ApplyGunFixes(); }
        DriveVrHands();   // nach Kopf und Pistole, dieselbe Kamera (VrHands.cs)
        DriveHandPoses(); // Parameter wirken im naechsten Animator-Takt (HandPoses.cs)
        DriveSprayHaptics();   // haelt auch an, wenn die Pistole aus ist (SprayHaptics.cs)
    }

    private void MeasurePoseGap()
    {
        var r = ReadHmdRotation();
        if (r.HasValue)
        {
            if (lateRotFrame == Time.frameCount)
            {
                float d = AngleDeg(lateRot, r.Value);
                poseGapSum += d;
                poseGapMax = Math.Max(poseGapMax, d);
                poseGapN++;
            }
            if (haveRenderRot) headStepSum += AngleDeg(lastRenderRot, r.Value);
            lastRenderRot = r.Value;
            haveRenderRot = true;
        }
    }

    private static Quaternion? ReadHmdRotation()
    {
        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            if (hmd == null || !hmd.isTracked.isPressed) return null;
            return hmd.centerEyeRotation.ReadValue();
        }
        catch { return null; }
    }

    // In C# gerechnet, nicht ueber die Interop-Grenze.
    private static float AngleDeg(Quaternion a, Quaternion b)
    {
        double dot = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        return (float)(2.0 * Math.Acos(Math.Min(1.0, dot)) * 180.0 / Math.PI);
    }

    // Einmal zu Fensterbeginn: wie viele Controller, und halten sie dieselben
    // Knoten wie Camera.main?
    private void ReportController()
    {
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<PlayerCameraController>();
            var cam = Camera.main;
            LoggerInstance.Msg($"PlayerCameraController: {all.Length} aktiv, Camera.main={(cam == null ? "null" : cam.name)}");
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                var h = c.m_horizontalLook;
                var v = c.m_verticalLook;
                var rig = c.m_rigTransform;
                var cc = c.m_camera;
                bool vIsCam = cam != null && v != null && v.Pointer == cam.transform.Pointer;
                bool hIsParent = cam != null && h != null && cam.transform.parent != null && h.Pointer == cam.transform.parent.Pointer;
                bool camIsMain = cam != null && cc != null && cc.Pointer == cam.Pointer;
                LoggerInstance.Msg($"  [{i}] '{c.gameObject.name}' enabled={c.enabled} | m_horizontalLook='{Name(h)}' ==Camera.main.parent: {hIsParent} | " +
                    $"m_verticalLook='{Name(v)}' ==Camera.main: {vIsCam} | m_rigTransform='{Name(rig)}' | m_camera='{Name(cc)}' ==Camera.main: {camIsMain} | " +
                    $"HorizontalLookRotation={c.HorizontalLookRotation:F1} VerticalLookRotation={c.VerticalLookRotation:F1}");
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Error("PlayerCameraController: Lesefehler " + e);
        }
    }

    private static string Name(Component? c) => c == null ? "null" : c.name;

    // Gibt den abgeschlossenen Messframe aus: eine Zeile, ein Frame.
    private void FlushSample()
    {
        if (!sampleOpen || Time.frameCount == sampleFrame) return;
        sampleOpen = false;
        int frames = Time.frameCount - windowFrame;
        LoggerInstance.Msg($"ref t+{Time.unscaledTime - refStart:F0}s KOPF f={sampleFrame} XR={(started ? "an" : "aus")} | U {sU} | pre {sPre} | post {sPost} | L {sL} | R {sR} | " +
            $"UpdateRotation {sRotCalls} im Frame, {rotCalls} in {frames} Frames | UpdateLookDirection {lookCalls} in {frames} Frames{sLook}");
        FlushGun(sampleFrame, frames);
        rotCalls = lookCalls = 0;
        windowFrame = Time.frameCount;
        if (Time.unscaledTime >= refUntil) gunProbeOn = false;   // Fensterende: Patches wieder nur Durchlauf
    }

    // Flanke der Taste. 0x8000 = jetzt unten, 0x0001 = seit der letzten
    // Abfrage gedrueckt - faengt einen kurzen Druck, waehrend der Hauptthread
    // hing (0.5.0 und 0.7.1 kam F9 nie an, ohne Spur im Log). Ohne Fokus
    // wird der Druck gemeldet statt still verworfen.
    private bool KeyPressed(int vk, ref bool wasDown, string name)
    {
        short st = GetAsyncKeyState(vk);
        bool down = (st & 0x8000) != 0;
        bool edge = (down && !wasDown) || (!down && (st & 0x0001) != 0);
        wasDown = down;
        if (!edge) return false;
        if (!Application.isFocused)
        {
            LoggerInstance.Msg($"{name}: gedrueckt, aber Spielfenster ohne Fokus - ignoriert");
            return false;
        }
        return true;
    }

    public override void OnUpdate()
    {
        CheckGunReadback();   // vor allem anderen: der Stand zwischen Render und diesem Update
        UpdateControllerTurn();
        FlushButtonEvents();
        TickPickupDiag();
        TickDevTools();
        TickUiProbe();
        TickVrUi();
        TickSky();
        if (started && Time.unscaledTime >= nextInputLog)
        {
            nextInputLog = Time.unscaledTime + 1f;
            LoggerInstance.Msg(InputStatus());
        }
        if (KeyPressed(VK_F8, ref keyWasDown, "F8"))
        {
            if (started) Stop("F8");
            else Start();
        }

        if (KeyPressed(VK_F9, ref f9WasDown, "F9"))
        {
            refStart = Time.unscaledTime;
            refUntil = refStart + ReferenceSeconds;
            nextRefUpdate = refStart;
            LoggerInstance.Msg($"F9: Kopfmessung {ReferenceSeconds:F0} s, XR {(started ? "LAEUFT" : "aus")} - Maus bewegen, mit XR auch den Kopf");
            HookBeforeRender();
            ReportController();
            ReportGun();
            rotCalls = lookCalls = 0;
            windowFrame = Time.frameCount;
        }

        if (KeyPressed(VK_F7, ref f7WasDown, "F7"))
        {
            if (writeHead) StopWriting("F7");
            else StartHead("F7");
        }

        if (KeyPressed(VK_F5, ref f5WasDown, "F5"))
            ToggleGun();

        if (KeyPressed(VK_F3, ref f3WasDown, "F3"))
            ToggleControllerInput();

        if (KeyPressed(VK_F4, ref f4WasDown, "F4"))
        {
            fixesOn = !fixesOn;
            LoggerInstance.Msg($"F4: PWS2-Korrekturen {(fixesOn ? "AN" : "AUS")}");
        }

        if (KeyPressed(VK_F6, ref f6WasDown, "F6"))
        {
            writeAtRender = !writeAtRender;
            LoggerInstance.Msg($"F6: Schreibpunkt jetzt {(writeAtRender ? "onBeforeRender" : "OnLateUpdate")}");
        }

        // Erst den Messframe des letzten Durchgangs ausgeben, dann ggf. einen
        // neuen oeffnen. U ist der Stand vor den Update-Schreibern des Spiels.
        FlushSample();
        float now = Time.unscaledTime;
        if (now < refUntil && now >= nextRefUpdate)
        {
            nextRefUpdate = now + 1f;
            sampleFrame = Time.frameCount;
            sampleOpen = true;
            sU = ReadHead();
            sGunU = ReadGun();
            sGunL = sGunR = "-";
            sPre = sPost = sL = sR = "-";
            sLook = "";
            sRotCalls = 0;
        }
        if (now < reportUntil && now >= nextXrUpdate)
        {
            nextXrUpdate = now + 1f;
            LogHead($"xr t+{now - startTime:F0}s U");
        }
    }

    // Hinter den Update-Schreibern des Spiels: ein Bericht vor dem Schreiber
    // liest einen Fremdwert.
    public override void OnLateUpdate()
    {
        if (radialInst != null) ApplyWheelPlacement(radialInst.transform);   // vor dem Canvas-Bau (Wheel.cs)
        float now = Time.unscaledTime;
        if (now < reportUntil && now >= nextReport)
        {
            nextReport = now + 1f;
            Report($"status t+{now - startTime:F0}s");
        }
        // Erst schreiben, dann L lesen: L ist dann das Geschriebene, und F9
        // zeigt mit R, ob es bis zum Rendern haelt.
        if (writeHead)
        {
            var r = ReadHmdRotation();
            if (r.HasValue) { lateRot = r.Value; lateRotFrame = Time.frameCount; }
            if (!writeAtRender) DriveHead();
        }
        if (sampleOpen && Time.frameCount == sampleFrame)
        {
            sL = ReadHead();
            sGunL = ReadGun();
        }
    }

    private void DriveHead()
    {
        if (!started) { StopWriting("XR aus"); return; }
        float now = Time.unscaledTime;
        if (!ResolveHead(now)) return;

        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            if (hmd == null || !hmd.isTracked.isPressed)
            {
                if (now >= nextHeadLog)
                {
                    nextHeadLog = now + 1f;
                    LoggerInstance.Msg($"KOPF-SCHREIBEN f={Time.frameCount}: HMD {(hmd == null ? "fehlt" : "nicht getrackt")} - Frame ausgelassen");
                }
                return;
            }
            var pos = hmd.centerEyePosition.ReadValue();
            var rot = hmd.centerEyeRotation.ReadValue();
            if (!haveBase)
            {
                headPoseBase = pos;
                haveBase = true;
                LoggerInstance.Msg($"KOPF-SCHREIBEN: Basis hmd={pos.ToString("F3")}, headTurnRest={headTurnRest.ToString("F3")}");
            }

            // Vor dem ersten Schreiben (je Einschalten und je Controller) ist
            // HorizontalLookRotation noch der reine Blick des Spiels.
            if (!trackBody)
            {
                bodyYaw = headCtl!.HorizontalLookRotation;
                mouseYawSum = 0f;
                haveRead = false;
                trackBody = true;
                LoggerInstance.Msg($"KOPF-SCHREIBEN: bodyYaw uebernommen {bodyYaw:F1}");
            }
            float gameYaw = headTurn!.localEulerAngles.y;   // Stand des Spiels, vor dem Schreiben
            float hmdYaw = rot.eulerAngles.y;
            var yaw = Quaternion.Euler(0f, hmdYaw, 0f);

            headTurn.localRotation = Quaternion.Euler(0f, bodyYaw + hmdYaw, 0f);
            headCam!.localRotation = Quaternion.Inverse(yaw) * rot;
            headTurn.localPosition = headTurnRest + Quaternion.Euler(0f, bodyYaw, 0f) * (pos - headPoseBase);
            headWrites++;
            headPitchPub = Mathf.DeltaAngle(0f, headCam.localEulerAngles.x);
            headPitchPublished = true;

            if (now >= nextHeadLog)
            {
                nextHeadLog = now + 1f;
                // Spiel vorher = bodyYaw + hmdYaw des Vorframes + Maus: das
                // Spiel hat den eigenen Schreibzugriff gelesen. mausYaw ist die
                // Summe seit der letzten Zeile, die in bodyYaw eingeht.
                LoggerInstance.Msg($"KOPF-SCHREIBEN f={Time.frameCount} n={headWrites} | Spiel vorher y={gameYaw:F1} H={headCtl!.HorizontalLookRotation:F1} " +
                    $"bodyYaw={bodyYaw:F1} mausYaw/s={mouseYawSum:F1} Maus-Pitch(verworfen)={headCtl.VerticalLookRotation:F1} | hmd pos={pos.ToString("F3")} rot={rot.eulerAngles.ToString("F1")} | " +
                    $"geschrieben HeadTurn lp={headTurn.localPosition.ToString("F3")} le={headTurn.localEulerAngles.ToString("F1")} PlayerCamera le={headCam.localEulerAngles.ToString("F1")} | " +
                    $"Kamera we={headCam.eulerAngles.ToString("F1")} wp={headCam.position.ToString("F2")}");
                // Schreibpunkt und wie alt die LateUpdate-Pose gegen die
                // Render-Pose ist, verglichen mit der Kopfbewegung selbst.
                LoggerInstance.Msg($"KOPF-TAKT f={Time.frameCount} Schreibpunkt={(writeAtRender ? "onBeforeRender" : "OnLateUpdate")} | " +
                    $"Pose LateUpdate->Render: mittel {(poseGapN == 0 ? 0f : poseGapSum / poseGapN):F3}° max {poseGapMax:F3}° in {poseGapN} Frames | " +
                    $"Kopfbewegung {headStepSum:F1}°/s, je Frame {(poseGapN == 0 ? 0f : headStepSum / poseGapN):F3}°");
                mouseYawSum = 0f;
                poseGapSum = poseGapMax = headStepSum = 0f;
                poseGapN = 0;
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Error("KOPF-SCHREIBEN: Ausnahme, ausgeschaltet - " + e);
            StopWriting("Ausnahme");
        }
    }

    // Kopfknoten aus dem Controller des Spiels, nicht aus Namen. Neue
    // HeadTurn-Instanz (Levelwechsel) -> Ruhelage neu, vor dem ersten Schreiben.
    private bool ResolveHead(float now)
    {
        if (headCtl != null && headTurn != null && headCam != null) return true;
        ClearHead();
        if (now < nextHeadResolve) return false;
        nextHeadResolve = now + 1f;

        var ctl = UnityEngine.Object.FindObjectOfType<PlayerCameraController>();
        var h = ctl == null ? null : ctl.m_horizontalLook;
        var v = ctl == null ? null : ctl.m_verticalLook;
        if (ctl == null || h == null || v == null)
        {
            LoggerInstance.Msg($"KOPF-SCHREIBEN: kein Kopf (Controller {(ctl == null ? "fehlt" : "ohne Knoten")}) - warte");
            return false;
        }
        headCtl = ctl;
        headTurn = h;
        headCam = v;
        if (h.Pointer != restFor)
        {
            headTurnRest = h.localPosition;
            restFor = h.Pointer;
            haveBase = false;
        }
        LoggerInstance.Msg($"KOPF-SCHREIBEN: gebunden HeadTurn='{h.name}' PlayerCamera='{v.name}' rest={headTurnRest.ToString("F3")}");
        return true;
    }

    // Auch bodyYaw gehoert zum gebundenen Controller: ein neuer liest neu ein.
    private void ClearHead()
    {
        trackBody = false;
        haveRead = false;
        headCtl = null;
        headTurn = null;
        headCam = null;
    }

    private void StartHead(string why)
    {
        if (writeHead) return;
        if (!started) { LoggerInstance.Msg($"{why}: XR laeuft nicht - erst F8"); return; }
        writeHead = true;
        haveBase = false;           // neu zentrieren beim ersten Frame
        headWrites = 0;
        nextHeadLog = 0f;
        LoggerInstance.Msg($"{why}: Kopf schreiben AN (Maus-Pitch verworfen, Maus-Yaw = Koerper)");
    }

    // Rotation gibt das Spiel im naechsten UpdateRotation selbst zurueck, die
    // Position schreibt es nie - die kommt hier zurueck.
    private void StopWriting(string why)
    {
        if (!writeHead) return;
        writeHead = false;
        try
        {
            if (headTurn != null && headTurn.Pointer == restFor)
                headTurn.localPosition = headTurnRest;
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("KOPF-SCHREIBEN: Ruhelage nicht zurueckgesetzt - " + e.GetType().Name + ": " + e.Message);
        }
        headPitchPublished = false;   // ohne Kopfschreiber gibt es keine HMD-Neigung fuer Bit 512
        ReleasePointer();
        LoggerInstance.Msg($"Kopf schreiben AUS ({why}) nach {headWrites} Frames");
        ClearHead();
    }

    // Jedes Geraet, das das Input System anlegt oder entfernt - auch Controller,
    // die erst spaeter binden. Aufzaehlen ueber InputSystem.devices waere ein
    // Struct-Rueckgabewert (ReadOnlyArray) ueber die Interop-Grenze; das
    // Ereignis braucht keinen. Aus demselben Grund keine usages (ebenfalls ReadOnlyArray).
    private void HookDeviceChanges()
    {
        try
        {
            System.Action<UnityEngine.InputSystem.InputDevice, InputDeviceChange> handler = (device, change) =>
            {
                try
                {
                    LoggerInstance.Msg($"Geraet {change}: '{device.displayName}' layout={device.layout} id={device.deviceId}");
                }
                catch (Exception e)
                {
                    LoggerInstance.Warning($"Geraet {change}: Lesefehler {e.GetType().Name}: {e.Message}");
                }
            };
            InputSystem.add_onDeviceChange(handler);
            LoggerInstance.Msg("onDeviceChange: angemeldet");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("onDeviceChange: nicht angemeldet - " + e);
        }
    }

    public override void OnApplicationQuit()
    {
        if (started) Stop("beenden");
    }

    private void Start()
    {
        var manager = DescribeManager("vor dem Start");
        if (manager == null) return;
        LoggerInstance.Msg("vor dem Start: " + SetOutputRate());
        string pipeline;
        try
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            pipeline = rp == null ? "null" : $"'{rp.name}' ({rp.GetIl2CppType().FullName})";
        }
        catch (Exception e)
        {
            pipeline = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }
        LoggerInstance.Msg("vor dem Start: currentRenderPipeline=" + pipeline);
        lastCameraSignature = "";
        ReportCameras("vor dem Start");

        try
        {
            manager.InitializeLoaderSync();
            LoggerInstance.Msg("InitializeLoaderSync: zurueckgekehrt");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("InitializeLoaderSync: Ausnahme " + e);
            return;
        }

        var loader = manager.activeLoader;
        LoggerInstance.Msg($"nach InitializeLoaderSync: activeLoader={(loader == null ? "null" : loader.name)}, isInitializationComplete={manager.isInitializationComplete}");
        if (loader == null)
        {
            LoggerInstance.Error("kein aktiver Loader - Runtime nicht erreichbar oder Loader verweigert (Player.log pruefen)");
            return;
        }

        try
        {
            manager.StartSubsystems();
            LoggerInstance.Msg("StartSubsystems: zurueckgekehrt");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("StartSubsystems: Ausnahme " + e);
        }

        started = true;
        startTime = Time.unscaledTime;
        Report("direkt nach dem Start");
        reportUntil = Time.unscaledTime + ReportSeconds;
        nextReport = nextXrUpdate = Time.unscaledTime + 1f;
    }

    private void Stop(string why)
    {
        StopGun("XR-Stopp " + why);
        StopWriting("XR-Stopp " + why);
        var manager = DescribeManager("vor dem Stopp (" + why + ")");
        started = false;
        reportUntil = 0f;
        if (manager == null) return;
        try
        {
            manager.StopSubsystems();
            LoggerInstance.Msg("StopSubsystems: zurueckgekehrt");
            manager.DeinitializeLoader();
            LoggerInstance.Msg($"DeinitializeLoader: zurueckgekehrt, activeLoader={(manager.activeLoader == null ? "null" : manager.activeLoader.name)}");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("Stopp: Ausnahme " + e);
        }
    }

    private XRManagerSettings? DescribeManager(string when)
    {
        var settings = XRGeneralSettings.Instance;
        if (settings == null)
        {
            LoggerInstance.Error(when + ": XRGeneralSettings.Instance ist null");
            return null;
        }
        var manager = settings.Manager;
        if (manager == null)
        {
            LoggerInstance.Error(when + ": XRGeneralSettings.Manager ist null");
            return null;
        }

        // loaders, nicht activeLoaders: activeLoaders ist IReadOnlyList, im
        // Interop ein Interface-Wrapper - Aufrufe darauf haben bei PWS2 den
        // Prozess getoetet (PWS2-Handbuch §10). loaders ist eine konkrete List.
        var names = new List<string>();
        var loaders = manager.loaders;
        if (loaders != null)
            for (int i = 0; i < loaders.Count; i++)
                names.Add(loaders[i] == null ? "null" : loaders[i].name);

        LoggerInstance.Msg($"{when}: settings='{settings.name}', manager='{manager.name}', InitManagerOnStart={settings.InitManagerOnStart}, " +
            $"loaders=[{string.Join(", ", names)}], activeLoader={(manager.activeLoader == null ? "null" : manager.activeLoader.name)}, " +
            $"isInitializationComplete={manager.isInitializationComplete}");
        return manager;
    }

    private void Report(string when)
    {
        string display;
        try
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            var d = loader == null ? null : loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            display = d == null ? "keins" : $"running={d.running}";
        }
        catch (Exception e)
        {
            display = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }

        string runtime;
        try
        {
            runtime = $"'{OpenXRRuntime.name}' {OpenXRRuntime.version}, API {OpenXRRuntime.apiVersion}";
        }
        catch (Exception e)
        {
            runtime = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }

        var cam = Camera.main;
        string camera = cam == null ? "Camera.main=null" : $"Camera.main='{cam.name}' stereoEnabled={cam.stereoEnabled}";

        LoggerInstance.Msg($"{when}: XRSettings.enabled={XRSettings.enabled}, isDeviceActive={XRSettings.isDeviceActive}, " +
            $"device='{XRSettings.loadedDeviceName}', eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
            $"display {display}, runtime {runtime}, {camera}, {SetOutputRate()}");
        ReportCameras(when);
        ReportPoses(when);
    }

    // Eine Zeile: HMD, beide Controller, Kopfknoten. Dieselbe Momentaufnahme.
    private void ReportPoses(string when)
    {
        var sb = new StringBuilder(when + ": POSE");

        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            if (hmd == null) sb.Append(" | HMD: kein XRHMD-Geraet");
            else
                sb.Append($" | HMD '{hmd.displayName}' layout={hmd.layout} tracked={Btn(hmd.isTracked)} state={Int(hmd.trackingState)} " +
                          $"centerEye pos={V(hmd.centerEyePosition)} rot={Q(hmd.centerEyeRotation)}");
        }
        catch (Exception e) { sb.Append(" | HMD Lesefehler " + e.GetType().Name + ": " + e.Message); }

        AppendController(sb, "L", () => XRController.leftHand);
        AppendController(sb, "R", () => XRController.rightHand);

        try
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            var input = loader == null ? null : loader.GetLoadedSubsystem<XRInputSubsystem>();
            sb.Append(input == null ? " | XRInputSubsystem: keins"
                : $" | XRInputSubsystem running={input.running} origin={input.GetTrackingOriginMode()} supported={input.GetSupportedTrackingOriginModes()}");
        }
        catch (Exception e) { sb.Append(" | XRInputSubsystem Lesefehler " + e.GetType().Name + ": " + e.Message); }

        LoggerInstance.Msg(sb.ToString());
        LogHead(when + " L");
    }

    // Kamera und ihre Ahnen, eine Zeile, eine Momentaufnahme. Angekert an
    // Camera.main, nicht an Knotennamen. forward ist die Blickrichtung als
    // Vektor - unabhaengig von der Euler-Zerlegung.
    private void LogHead(string when)
    {
        var sb = new StringBuilder($"{when}: KOPF f={Time.frameCount} ts={Time.timeScale:F2} focus={Application.isFocused}");
        try
        {
            var cam = Camera.main;
            if (cam == null) sb.Append(" | Camera.main=null");
            else
            {
                var t = cam.transform;
                sb.Append($" | {t.name}: lp={t.localPosition.ToString("F3")} le={t.localEulerAngles.ToString("F1")} fwd={t.forward.ToString("F3")} we={t.eulerAngles.ToString("F1")}");
                int depth = 0;
                for (var p = t.parent; p != null && depth < 4; p = p.parent, depth++)
                    sb.Append($" | {p.name}: lp={p.localPosition.ToString("F3")} le={p.localEulerAngles.ToString("F1")}");
            }
        }
        catch (Exception e) { sb.Append(" | Lesefehler " + e.GetType().Name + ": " + e.Message); }
        LoggerInstance.Msg(sb.ToString());
    }

    private static void AppendController(StringBuilder sb, string side, Func<XRController?> get)
    {
        try
        {
            var c = get();
            if (c == null) { sb.Append($" | {side}: kein XRController"); return; }
            string trigger;
            var ctl = c.TryGetChildControl("trigger");
            var axis = ctl == null ? null : ctl.TryCast<AxisControl>();
            trigger = axis == null ? "kein trigger" : $"trigger={axis.ReadValue():F2}";
            sb.Append($" | {side} '{c.displayName}' layout={c.layout} tracked={Btn(c.isTracked)} " +
                      $"pos={V(c.devicePosition)} rot={Q(c.deviceRotation)} {trigger}");
        }
        catch (Exception e) { sb.Append($" | {side} Lesefehler " + e.GetType().Name + ": " + e.Message); }
    }

    private static string V(Vector3Control? c) => c == null ? "keine" : c.ReadValue().ToString("F3");
    private static string Q(QuaternionControl? c) => c == null ? "keine" : c.ReadValue().eulerAngles.ToString("F1");
    private static string Btn(ButtonControl? c) => c == null ? "?" : c.isPressed.ToString();
    private static string Int(IntegerControl? c) => c == null ? "?" : c.ReadValue().ToString();

    // "N SetOutput in F Frames" seit dem letzten Aufruf.
    private string SetOutputRate()
    {
        int frames = Time.frameCount - lastFrame;
        lastFrame = Time.frameCount;
        int n = CountSetOutput();
        return n < 0 ? $"SetOutput: Player.log unlesbar, {frames} Frames" : $"SetOutput {n} in {frames} Frames";
    }

    // Liest Player.log ab dem letzten Offset. -1 = Lesefehler, nicht "keine
    // Meldung". Ein Treffer, der genau ueber die Lesegrenze faellt, geht
    // verloren - fuer eine Rate ohne Belang.
    private int CountSetOutput()
    {
        try
        {
            string path = Application.consoleLogPath;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (logOffset < 0 || logOffset > fs.Length) logOffset = fs.Length;
            fs.Seek(logOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string text = reader.ReadToEnd();
            logOffset = fs.Length;
            int count = 0;
            for (int i = text.IndexOf(SetOutputMarker, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(SetOutputMarker, i + SetOutputMarker.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("Player.log: " + e.GetType().Name + ": " + e.Message);
            return -1;
        }
    }

    // Alle Kameras in geladenen Szenen, auch inaktive. Volle Liste nur, wenn
    // sie sich gegen den letzten Aufruf geaendert hat.
    private void ReportCameras(string when)
    {
        var lines = new List<string>();
        try
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                scenes.Add(SceneManager.GetSceneAt(i).name);
            lines.Add("Szenen: [" + string.Join(", ", scenes) + "], aktiv '" + SceneManager.GetActiveScene().name + "'");

            var all = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < all.Length; i++)
            {
                var cam = all[i];
                if (cam == null) continue;
                var go = cam.gameObject;
                var scene = go.scene;
                if (!scene.IsValid()) continue;   // Prefab-Assets, keine Szenenobjekte
                lines.Add("  " + DescribeCamera(cam, go, scene.name));
            }
        }
        catch (Exception e)
        {
            lines.Add("Kameraliste: Lesefehler " + e.GetType().Name + ": " + e.Message);
        }

        string signature = string.Join("\n", lines);
        if (signature == lastCameraSignature) return;
        lastCameraSignature = signature;
        LoggerInstance.Msg($"{when}: Kameras ({lines.Count - 1}):");
        foreach (var l in lines) LoggerInstance.Msg("  " + l);
    }

    private static string DescribeCamera(Camera cam, GameObject go, string sceneName)
    {
        var path = new StringBuilder(go.name);
        for (var t = go.transform.parent; t != null; t = t.parent)
            path.Insert(0, t.name + "/");

        var rt = cam.targetTexture;
        string target = rt == null ? "Bildschirm" : $"RT '{rt.name}' {rt.width}x{rt.height}";

        string urp;
        try
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) urp = "keine URP-Daten";
            else
            {
                var stack = data.renderType == CameraRenderType.Base ? data.cameraStack : null;
                urp = $"{data.renderType}, Stapel {(stack == null ? 0 : stack.Count)}";
            }
        }
        catch (Exception e)
        {
            urp = "URP Lesefehler " + e.GetType().Name;
        }

        return $"[{sceneName}] {path} | activeInHierarchy={go.activeInHierarchy} enabled={cam.enabled} tag={go.tag} " +
            $"eye={cam.stereoTargetEye} ziel={target} stereoEnabled={cam.stereoEnabled} depth={cam.depth} " +
            $"display={cam.targetDisplay} mask=0x{cam.cullingMask:X8} {urp}";
    }
}
