// Cartoon-Umriss und Stereo-Modus (XRStart 1.35.1/1.36.0) - SpongeBob-DLC.
//
// BEFUND (Logs 1.35.0/1.35.1, SpongeBob ConchStreet): Level geladen, Ton,
// Kopf und Pistole laufen; das Headset zeigt weiter Levelauswahl/Ladebalken,
// der Balken "zappelt", links flimmert es. GEMESSEN: die PlayerCamera ist
// sauber - eye=Both, allowXR=True, Ziel Bildschirm, kein Stapel; XR gibt jeden
// Frame aus (SetOutput 1396 in 1396). Das Bild entsteht also, kommt aber nicht
// in die Augentextur: Virtual Desktop zeigt alte Swapchain-Bilder im Wechsel.
//
// URSACHE (bestaetigt 1.35.1: abgeschaltet ist das Bild da): PWS.ModulatedOutlineRenderer, ein URP-
// Bildschirmeffekt (Cartoon-Umriss, Volume PWS.ModulatedOutline - nur das
// SpongeBob-Level stellt ihn an). Er blittet Quelle -> zwei temporaere 2D-RTs
// -> zurueck; unter SinglePassInstanced ist die Augentextur ein Array, das
// geht schief (nur ein Auge, oder gar nichts).
//
// SINGLEPASS (CartoonOutline aus): solange XR laeuft, jede Umriss-Feature per
// SetActive(false) aus, beim XR-Stopp der alte Zustand zurueck. Log UMRISS.
// Mit DevMode 5 s nach jeder neuen Spielerkamera Kameraliste und XR-Zustand.
//
// CARTOON-UMRISS IN VR (1.36.0): cfg CartoonOutline (Vorgabe an)
// startet XR in MultiPass - jedes Auge ein gewoehnliches 2D-Bild, der Umriss
// darf dann laufen. Aus = SinglePassInstanced wie das Spiel (OpenXRSettings
// renderMode 1) und Umriss aus. Der Modus gilt ab dem naechsten XR-Start.
// GEMESSEN (Log 1.36.0, Shrek + SpongeBob): Umriss auf beiden Augen richtig
// (Nutzer), 44,5-45 fps wie SinglePass - die Grenze ist die 45-fps-Kappe.
// BILDRATE, Kameraliste und URP-FEATURES nur mit DevMode (Diagnosen aus).

using Il2CppPWS;
using UnityEngine;
using MelonLoader;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private IntPtr levelCamSeen = IntPtr.Zero;
    private float nextLevelCamCheck, levelCamReportAt = -1f;
    private readonly List<ModulatedOutlineRenderer> outlineOff = new();
    private MelonPreferences_Entry<bool> prefCartoonOutline = null!;
    private bool outlineMultiPass;   // beim XR-Start festgelegt
    private float fpsWindowStart = -1f, fpsWorst;
    private int fpsFrames;

    private void InitLevelCamera(MelonPreferences_Category cat)
    {
        prefCartoonOutline = cat.CreateEntry("CartoonOutline", true, description: "Cartoon-Umriss (SpongeBob-DLC) auch in VR: XR rendert dann MultiPass (kostet Bildrate). Aus = SinglePassInstanced, Umriss nur flach. Wirkt ab dem naechsten XR-Start.");
    }

    // Aus Start, VOR InitializeLoaderSync: der Modus wird beim Laderstart uebernommen.
    private void ApplyStereoMode()
    {
        outlineMultiPass = prefCartoonOutline.Value;
        try
        {
            var s = OpenXRSettings.Instance;
            if (s == null) { LoggerInstance.Warning("STEREO: OpenXRSettings.Instance null - Modus unveraendert, Umriss aus"); outlineMultiPass = false; return; }
            var want = outlineMultiPass ? OpenXRSettings.RenderMode.MultiPass : OpenXRSettings.RenderMode.SinglePassInstanced;
            var was = s.renderMode;
            s.renderMode = want;
            LoggerInstance.Msg($"STEREO: renderMode {was} -> {s.renderMode} (CartoonOutline={outlineMultiPass})");
        }
        catch (Exception e)
        {
            outlineMultiPass = false;
            LoggerInstance.Warning("STEREO: " + e.GetType().Name + ": " + e.Message + " - Umriss aus");
        }
    }

    // Aus OnUpdate: Bildrate im Level, eine Zeile je 10 s.
    private void TickFrameRate(float now)
    {
        if (!started || menuCamOn) { fpsWindowStart = -1f; return; }
        if (fpsWindowStart < 0f) { fpsWindowStart = now; fpsFrames = 0; fpsWorst = 0f; return; }
        fpsFrames++;
        fpsWorst = Math.Max(fpsWorst, Time.unscaledDeltaTime);
        float span = now - fpsWindowStart;
        if (span < 10f) return;
        string mode;
        try { mode = XRSettings.stereoRenderingMode.ToString(); } catch { mode = "?"; }
        LoggerInstance.Msg($"BILDRATE: {fpsFrames / span:F1} fps, langsamster Frame {fpsWorst * 1000f:F0} ms, Stereo {mode}, Umriss {(outlineMultiPass ? "an" : "aus")}");
        fpsWindowStart = now; fpsFrames = 0; fpsWorst = 0f;
    }

    // Aus OnUpdate, nach TickMenuCamera.
    private void TickLevelCamera()
    {
        float now = Time.unscaledTime;
        if (levelCamReportAt > 0f && now >= levelCamReportAt)
        {
            levelCamReportAt = -1f;
            lastCameraSignature = "";   // volle Liste erzwingen
            Report("Spielerkamera +5 s");
            ReportRendererFeatures();
        }
        if (dev) TickFrameRate(now);
        if (!started || now < nextLevelCamCheck) return;
        nextLevelCamCheck = now + 1f;
        try
        {
            if (!outlineMultiPass) DisableOutline();
            if (menuCamOn || !dev) return;
            var cam = Camera.main;
            if (cam == null || cam.Pointer == levelCamSeen) return;
            levelCamSeen = cam.Pointer;
            levelCamReportAt = now + 5f;
            LoggerInstance.Msg($"SPIELERKAMERA: '{PathOf(cam.transform)}' - Kameraliste in 5 s");
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("SPIELERKAMERA/UMRISS: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // Je Sekunde: das Level kann die Feature neu laden oder wieder anstellen.
    private void DisableOutline()
    {
        var all = Resources.FindObjectsOfTypeAll<ModulatedOutlineRenderer>();
        for (int i = 0; i < all.Length; i++)
        {
            var f = all[i];
            if (f == null || !f.isActive) continue;
            f.SetActive(false);
            bool known = false;
            for (int k = 0; k < outlineOff.Count; k++)
                if (outlineOff[k] != null && outlineOff[k].Pointer == f.Pointer) { known = true; break; }
            if (!known) outlineOff.Add(f);
            LoggerInstance.Msg($"UMRISS: '{f.name}' aus (Bildschirm-Umriss zerstoert das XR-Augenbild)");
        }
    }

    // Messung: alle geladenen URP-Features - falls der Umriss nicht der einzige ist.
    private void ReportRendererFeatures()
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<ScriptableRendererFeature>();
            var parts = new List<string>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) parts.Add($"'{all[i].name}' {all[i].GetIl2CppType().Name} {(all[i].isActive ? "an" : "aus")}");
            LoggerInstance.Msg($"URP-FEATURES ({parts.Count}): {string.Join(" | ", parts)}");
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("URP-FEATURES: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // Aus Stop, nach started = false.
    private void RestoreOutline()
    {
        int n = 0;
        for (int i = 0; i < outlineOff.Count; i++)
        {
            try { var f = outlineOff[i]; if (f != null) { f.SetActive(true); n++; } }
            catch { }
        }
        if (outlineOff.Count > 0) LoggerInstance.Msg($"UMRISS: {n} zurueck an (XR aus)");
        outlineOff.Clear();
        levelCamSeen = IntPtr.Zero;
    }
}
