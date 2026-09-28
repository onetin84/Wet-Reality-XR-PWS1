// Hauptmenue im Headset (XRStart 1.22.0).
//
// GEMESSEN (1.20.0-Log, Hauptmenue vor dem Levelstart): einzige Kamera ist
// UIClearCamera(Clone) - Tiefe -100, Culling-Maske 0x00000000, sie loescht nur.
// UIRoot(Clone) ist ScreenSpaceOverlay; ein Overlay landet in PWS1 nur im
// Monitorbild (VrUi.cs). Das Headset zeigt darum schwarz. PWS2 hat im
// Hauptmenue eine 3D-Szene und braucht das nicht.
//
// LOESUNG: solange keine Spielerkamera (PlayerCameraController) existiert,
// eine eigene Kamera, getaggt MainCamera - damit sind VrUi.EnsureUi (UIRoot ->
// ScreenSpaceCamera, UI-Bit, Skalierung, ZTest) und der Menuezeiger (Pose aus
// Camera.main) OHNE Sonderweg dieselben wie im Level. Sie rendert nur die
// UI-Ebene (und Ebene 0: Strahl, Startlogo) vor einem dunklen Grund; die Pose kommt je Frame aus dem HMD
// (Rig im Weltursprung, Kamera = HMD-Pose, also toWorld = Identitaet).
// Erscheint die Spielerkamera (Level geladen), geht sie aus und gibt den Tag
// ab, damit Camera.main wieder die PlayerCamera ist.
//
// cfg MenuCamera (an). Aus = Verhalten bis 1.21.0 (Headset im Hauptmenue schwarz).

using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefMenuCamera = null!;
    private static readonly Color MenuCameraBackground = new(0.02f, 0.05f, 0.09f, 1f);

    private GameObject? menuCamRig;
    private Camera? menuCam;
    internal static bool menuCamOn;
    private bool playerCamPresent;
    private float nextPlayerCamProbe;
    private int menuCamFrames;

    private void InitMenuCamera(MelonPreferences_Category cat)
    {
        prefMenuCamera = cat.CreateEntry("MenuCamera", true, description: "Hauptmenue im Headset: eigene Kamera, solange keine Spielerkamera da ist (PWS1 hat dort nur UI). Aus = Headset im Hauptmenue schwarz.");
        prefLoadingBlackout = cat.CreateEntry("LoadingBlackout", true, description: "Beim Laden eines Levels die Welt schwarz halten (nur Lade-UI), bis das Level fertig ist");
        prefLoadingHold = cat.CreateEntry("LoadingHoldSeconds", 0.5f, description: "Sekunden Schwarz nach dem Ende des Ladens, bevor die Welt erscheint");
    }

    // Aus OnUpdate, VOR TickVrUi.
    private void TickMenuCamera()
    {
        float now = Time.unscaledTime;
        if (now >= nextPlayerCamProbe)
        {
            nextPlayerCamProbe = now + 0.1f;
            try { playerCamPresent = UnityEngine.Object.FindObjectOfType<PlayerCameraController>() != null; }
            catch { playerCamPresent = false; }
        }
        // ABBLENDEN BEIM LADEN (1.24.1, Nutzer: "man sieht das Level aufbauen").
        // Solange LoadingLocationState laeuft oder der Screen Loading ist, bleibt
        // die Menuekamera AN, auch mit Spielerkamera: Tiefe 100 und SolidColor
        // loescht nach der PlayerCamera Farbe UND Tiefe - die halbe Welt ist weg,
        // Lade-UI und Startlogo bleiben. Nachlauf LoadingHoldSeconds, dann Welt.
        bool loading = prefLoadingBlackout.Value && playerCamPresent && LoadingNow(now);
        if (loading) loadingHoldUntil = now + Math.Max(0f, prefLoadingHold.Value);
        bool blackout = playerCamPresent && now < loadingHoldUntil;
        bool want = started && vrUiWanted && prefMenuCamera.Value && (!playerCamPresent || blackout);
        if (want && menuCamOn && blackout != menuCamBlackout) SetMenuCameraRole(blackout);
        // BEIM LADEN NUR DIE UI-EBENE (1.24.2, Log 1.24.1): vor der Spielerkamera
        // (Screen Loading, 8 s) zeichnete die Menuekamera Ebene 0 mit - dort liegt
        // die Level-Geometrie, und das halbe Level stand um die Lade-UI. Ebene 0
        // braucht nur der Zeigestrahl im Hauptmenue.
        if (menuCamOn && menuCam != null)
        {
            int mask = LoadingNow(now) || menuCamBlackout ? (1 << 5) : (1 << 5) | (1 << 0);
            if (menuCam.cullingMask != mask) { menuCam.cullingMask = mask; LoggerInstance.Msg($"MENUE-KAMERA: Maske 0x{mask:X8} ({(mask == (1 << 5) ? "Laden: nur UI" : "Hauptmenue: UI + Ebene 0")})"); }
        }
        if (want == menuCamOn) return;
        if (want) EnableMenuCamera(blackout);
        else DisableMenuCamera(playerCamPresent ? (menuCamBlackout ? "Level geladen" : "Spielerkamera da") : "XR/UI aus");
    }

    private MelonPreferences_Entry<bool> prefLoadingBlackout = null!;
    private MelonPreferences_Entry<float> prefLoadingHold = null!;
    private float loadingHoldUntil = -1f;
    private bool menuCamBlackout;

    // Die Kamera fuer UIRoot und Menuezeiger: die Menuekamera, solange sie an ist
    // (beim Laden gibt es sonst ZWEI Kameras, und Camera.main waere Zufall).
    internal Camera? UiCamera => menuCamOn && menuCam != null ? menuCam : Camera.main;

    private bool LoadingNow(float now)
    {
        if (now - lastLoadingAt < 0.3f) return true;   // LoadingLocationState.Update feuert
        try
        {
            if (autoGameState == null) autoGameState = UnityEngine.Object.FindObjectOfType<GameStateManager>();
            return autoGameState != null && autoGameState.CurrentScreen == GameScreen.Loading;
        }
        catch { autoGameState = null; return false; }
    }

    // Hauptmenue: MainCamera-Tag, Tiefe 0 (VrUi/Zeiger wie im Level). Abblenden:
    // KEIN Tag - Camera.main bleibt die PlayerCamera (Kopf, Pistole, Teleport) -,
    // Tiefe 100, damit sie NACH der Spielerkamera loescht.
    private void SetMenuCameraRole(bool blackout)
    {
        if (menuCam == null) return;
        menuCamBlackout = blackout;
        menuCam.gameObject.tag = blackout ? "Untagged" : "MainCamera";
        menuCam.depth = blackout ? 100f : 0f;
        menuCam.backgroundColor = blackout ? Color.black : MenuCameraBackground;
        nextUiCheck = 0f;
        LoggerInstance.Msg($"MENUE-KAMERA: {(blackout ? "ABBLENDEN (Level laedt), Tiefe 100, schwarz" : "Hauptmenue-Rolle, Tiefe 0")}");
    }

    private void EnableMenuCamera(bool blackout)
    {
        try
        {
            if (menuCamRig == null || menuCam == null)
            {
                menuCamRig = new GameObject("WetReality_MenuRig");
                UnityEngine.Object.DontDestroyOnLoad(menuCamRig);
                var go = new GameObject("WetReality_MenuCamera");
                go.transform.SetParent(menuCamRig.transform, false);
                menuCam = go.AddComponent<Camera>();
                menuCam.clearFlags = CameraClearFlags.SolidColor;
                menuCam.backgroundColor = MenuCameraBackground;
                // UI-Ebene 5 UND Default 0: Zeigestrahl (LineRenderer) und Startlogo liegen
                // auf 0 (1.22.0 nur 5: Strahl unsichtbar, Nutzer).
                menuCam.cullingMask = (1 << 5) | (1 << 0);
                menuCam.nearClipPlane = 0.05f;
                menuCam.farClipPlane = 50f;
                menuCam.depth = 0f;              // nach UIClearCamera (-100)
                menuCam.stereoTargetEye = StereoTargetEyeMask.Both;
            }
            menuCamRig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            menuCam.gameObject.SetActive(true);
            menuCam.enabled = true;
            SetMenuCameraRole(blackout);
            PoseMenuCamera();
            menuCamOn = true;
            menuCamFrames = 0;
            nextUiCheck = 0f;   // UIRoot sofort auf diese Kamera (VrUi.cs)
            var main = Camera.main;
            LoggerInstance.Msg($"MENUE-KAMERA: AN ({(blackout ? "Abblenden beim Laden" : "keine Spielerkamera")}) - Camera.main='{(main == null ? "null" : main.name)}', Maske 0x{menuCam.cullingMask:X8}");
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE-KAMERA: anlegen " + e.GetType().Name + ": " + e.Message); menuCamOn = false; }
    }

    private void DisableMenuCamera(string why)
    {
        menuCamOn = false;
        menuCamBlackout = false;
        try
        {
            if (menuCam != null)
            {
                menuCam.gameObject.tag = "Untagged";
                menuCam.enabled = false;
                menuCam.gameObject.SetActive(false);
            }
        }
        catch { menuCam = null; menuCamRig = null; }
        nextUiCheck = 0f;   // UIRoot auf die naechste Camera.main oder zurueck
        LoggerInstance.Msg($"MENUE-KAMERA: aus ({why}) nach {menuCamFrames} Frames");
    }

    // In onBeforeRender, vor dem Menuezeiger.
    private void DriveMenuCamera()
    {
        if (!menuCamOn) return;
        PoseMenuCamera();
        menuCamFrames++;
    }

    private void PoseMenuCamera()
    {
        try
        {
            if (menuCam == null) { menuCamOn = false; return; }
            var hmd = InputSystem.GetDevice<XRHMD>();
            if (hmd == null) return;
            var t = menuCam.transform;
            t.localPosition = hmd.centerEyePosition.ReadValue();
            t.localRotation = hmd.centerEyeRotation.ReadValue();
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE-KAMERA: Pose " + e.GetType().Name); }
    }

    // Autostart: XR schon im Hauptmenue (1.22.0), nicht erst beim Level.
    private GameStateManager? autoGameState;

    private bool InMainMenu()
    {
        try
        {
            if (autoGameState == null) autoGameState = UnityEngine.Object.FindObjectOfType<GameStateManager>();
            return autoGameState != null && autoGameState.CurrentScreen == GameScreen.MainMenu;
        }
        catch { autoGameState = null; return false; }
    }
}
