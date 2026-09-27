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
        bool want = started && vrUiWanted && prefMenuCamera.Value && !playerCamPresent;
        if (want == menuCamOn) return;
        if (want) EnableMenuCamera(); else DisableMenuCamera(playerCamPresent ? "Spielerkamera da" : "XR/UI aus");
    }

    private void EnableMenuCamera()
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
            menuCam.gameObject.tag = "MainCamera";
            menuCam.gameObject.SetActive(true);
            menuCam.enabled = true;
            PoseMenuCamera();
            menuCamOn = true;
            menuCamFrames = 0;
            nextUiCheck = 0f;   // UIRoot sofort auf diese Kamera (VrUi.cs)
            var main = Camera.main;
            LoggerInstance.Msg($"MENUE-KAMERA: AN (keine Spielerkamera) - Camera.main='{(main == null ? "null" : main.name)}', Maske 0x{menuCam.cullingMask:X8}");
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE-KAMERA: anlegen " + e.GetType().Name + ": " + e.Message); menuCamOn = false; }
    }

    private void DisableMenuCamera(string why)
    {
        menuCamOn = false;
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
