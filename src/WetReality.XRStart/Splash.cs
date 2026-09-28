// Startlogo im Headset (XRStart 1.22.1). Port von PWS2 Splash.cs.
//
// Einmal je XR-Start (auf dem UEBERGANG gespannt, nicht auf dem Zustand - sonst
// begaenne das Halten jeden Frame neu): SplashSeconds (2,5) voll, dann
// SplashFadeSeconds (0,75) ausblenden, SplashDistance (2,2 m) vor der Kamera,
// SplashWidth (1,9 m) breit. Ohne Camera.main wartet es, und der Zeitgeber mit.
//
// Wie PWS2: World-Space-Canvas + RawImage statt Quad - ein CanvasRenderer holt
// sich das UI-Standardmaterial selbst, kein Shader.Find. Ebene 5 seit 1.24.2 (Spieler- und
// Menuekamera rendern sie), sortingOrder 32000 ueber der Spiel-UI. Je Frame vor
// die Kamera gesetzt, NICHT an sie gehaengt: die Kamera wechselt beim Laden.
// Das Bild steckt in der DLL (EmbeddedResource WetReality.startup-logo.png,
// aus tools/frontend/assets/startup-logo.png).

using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const string SplashResource = "WetReality.startup-logo.png";
    private const float SplashCanvasWidth = 1600f;

    private MelonPreferences_Entry<bool> prefShowSplash = null!;
    private MelonPreferences_Entry<float> prefSplashSeconds = null!, prefSplashFade = null!, prefSplashDistance = null!, prefSplashWidth = null!, prefSplashCropX = null!;

    private GameObject? splashHolder;
    private Texture2D? splashTexture;
    private CanvasGroup? splashGroup;
    private RectTransform? splashRect;
    private RawImage? splashImage;
    private bool splashFailed, splashArmed, splashLogged, splashMeasured;
    private float splashStartedAt = -1f;
    private float splashAspect = 1650f / 953f;

    private void InitSplash(MelonPreferences_Category cat)
    {
        prefShowSplash = cat.CreateEntry("ShowSplash", true, description: "Startlogo im Headset, sobald XR laeuft (wie PWS2)");
        prefSplashSeconds = cat.CreateEntry("SplashSeconds", 2.5f, description: "Sekunden voll sichtbar, bevor es ausblendet");
        prefSplashFade = cat.CreateEntry("SplashFadeSeconds", 0.75f, description: "Sekunden fuer das Ausblenden");
        prefSplashDistance = cat.CreateEntry("SplashDistance", 2.2f, description: "Meter vor dem Headset");
        prefSplashWidth = cat.CreateEntry("SplashWidth", 1.9f, description: "Breite in Metern (des UNbeschnittenen Bildes)");
        // 1.24.3 (Nutzer, Bild): das Logo stand links/rechts ueber den weissen Tablet-Rahmen
        // des Menues - gemessen ~6,3 %/5,6 % je Seite. Beschnitten per uvRect, die Breite
        // schrumpft mit, damit die HOEHE bleibt und nichts gestaucht wird.
        prefSplashCropX = cat.CreateEntry("SplashCropX", 0.04f, description: "Anteil, der links UND rechts vom Startlogo abgeschnitten wird (0 bis 0,4)");
    }

    // Aus OnUpdate: auf dem Uebergang "XR laeuft" spannen.
    private void TickSplashArm()
    {
        if (!started) { splashArmed = false; if (splashStartedAt >= 0f) HideSplash(); return; }
        if (splashArmed || !prefShowSplash.Value) return;
        splashArmed = true;
        if (splashFailed) return;
        splashStartedAt = Time.unscaledTime;
        splashLogged = false;
        if (splashGroup != null) splashGroup.alpha = 1f;
        LoggerInstance.Msg("STARTLOGO: angefordert");
    }

    // Aus OnLateUpdate: die Kamera hat sich schon bewegt.
    private void TickSplash()
    {
        if (splashStartedAt < 0f || splashFailed) return;
        var cam = UiCamera;
        if (cam == null) { splashStartedAt = Time.unscaledTime; return; }   // Kamera fehlt: Zeitgeber wartet mit
        if (!EnsureSplash()) return;
        float hold = Math.Max(0f, prefSplashSeconds.Value), fade = Math.Max(0f, prefSplashFade.Value);
        float elapsed = Time.unscaledTime - splashStartedAt;
        if (elapsed >= hold + fade) { HideSplash(); LoggerInstance.Msg($"STARTLOGO: fertig nach {elapsed:F2} s"); return; }
        float alpha = elapsed > hold && fade > 0.01f ? 1f - (elapsed - hold) / fade : 1f;
        try
        {
            splashGroup!.alpha = Mathf.Clamp01(alpha);
            var eye = cam.transform;
            var t = splashHolder!.transform;
            t.position = eye.position + eye.forward * prefSplashDistance.Value;
            t.rotation = eye.rotation;
            float crop = Mathf.Clamp(prefSplashCropX.Value, 0f, 0.4f), keep = 1f - 2f * crop;
            if (splashImage != null) splashImage.uvRect = new Rect(crop, 0f, keep, 1f);
            float scale = prefSplashWidth.Value * keep / SplashCanvasWidth;
            t.localScale = new Vector3(scale, scale, scale);
            if (splashRect != null) splashRect.sizeDelta = new Vector2(SplashCanvasWidth, SplashCanvasWidth / (splashAspect * keep));   // Seitenverhaeltnis des Ausschnitts
            if (!splashLogged)
            {
                splashLogged = true;
                LoggerInstance.Msg($"STARTLOGO: {splashTexture!.width}x{splashTexture.height}, {prefSplashWidth.Value * keep:F2} m breit (je Seite {crop:P0} beschnitten) in {prefSplashDistance.Value:F2} m, Kamera '{cam.name}', {hold:F2} s + {fade:F2} s");
            }
            // Erst beim Ausblenden messen: dann ist UIRoot sicher auf die VR-Kamera umgestellt.
            if (dev && !splashMeasured && elapsed >= hold) { splashMeasured = true; MeasureSplashFrame(cam, prefSplashWidth.Value * keep, prefSplashWidth.Value / splashAspect, prefSplashDistance.Value); }
        }
        catch (Exception e) { LoggerInstance.Warning("STARTLOGO: " + e.GetType().Name + ": " + e.Message); splashHolder = null; }
    }

    // MESSUNG (1.28.0, Nutzer: Beschnitt 0,06 "etwas zu stark", das Bild soll
    // genau in den weissen Rahmen des Menues). Winkelbreite/-hoehe der
    // Rahmen-Kandidaten unter UIRoot gegen die des Logos - daraus der Beschnitt:
    // keep = Rahmenwinkel_x / Logowinkel_x_unbeschnitten.
    private void MeasureSplashFrame(Camera cam, float logoW, float logoH, float logoDist)
    {
        try
        {
            float logoAx = 2f * Mathf.Atan(logoW * 0.5f / logoDist) * Mathf.Rad2Deg;
            float logoAy = 2f * Mathf.Atan(logoH * 0.5f / logoDist) * Mathf.Rad2Deg;
            LoggerInstance.Msg($"STARTLOGO-RAHMEN: Logo {logoAx:F1} x {logoAy:F1} Grad (beschnitten)");
            if (uiRoot == null) return;
            var rts = uiRoot.GetComponentsInChildren<RectTransform>(false);
            var corners = new Vector3[4];
            var eye = cam.transform.position;
            int n = 0;
            for (int i = 0; i < rts.Length && n < 20; i++)
            {
                var rt = rts[i];
                if (rt == null) continue;
                string nm = rt.name;
                if (nm.IndexOf("Tablet", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) < 0
                    && nm.IndexOf("Border", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Background", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
                rt.GetWorldCorners(arr);
                for (int k = 0; k < 4; k++) corners[k] = arr[k];
                float ax = Vector3.Angle(corners[0] - eye, corners[3] - eye);   // unten links -> unten rechts
                float ay = Vector3.Angle(corners[0] - eye, corners[1] - eye);   // unten links -> oben links
                LoggerInstance.Msg($"STARTLOGO-RAHMEN:   '{PathOf(rt)}' {ax:F1} x {ay:F1} Grad");
                n++;
            }
        }
        catch (Exception e) { LoggerInstance.Warning("STARTLOGO-RAHMEN: " + e.GetType().Name + ": " + e.Message); }
    }

    private void HideSplash()
    {
        splashStartedAt = -1f;
        try
        {
            if (splashGroup != null) splashGroup.alpha = 0f;
            if (splashHolder != null) splashHolder.SetActive(false);
        }
        catch { splashHolder = null; splashGroup = null; splashRect = null; }
    }

    private bool EnsureSplash()
    {
        if (splashHolder != null) { if (!splashHolder.activeSelf) splashHolder.SetActive(true); return true; }
        splashHolder = null;   // zerstoerter Halter: Unity-null, nicht Muster-null
        if (!LoadSplashTexture()) return false;
        try
        {
            splashHolder = new GameObject("WetReality_Splash");
            UnityEngine.Object.DontDestroyOnLoad(splashHolder);
            splashHolder.layer = 5;   // UI-Ebene (1.24.2): die Menuekamera zeichnet beim Laden nur sie; die PlayerCamera hat das Bit durch VrUi
            var canvas = splashHolder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;   // ueber der Spiel-UI
            splashRect = splashHolder.GetComponent<RectTransform>();
            if (splashRect != null) splashRect.sizeDelta = new Vector2(SplashCanvasWidth, SplashCanvasWidth / splashAspect);
            splashGroup = splashHolder.AddComponent<CanvasGroup>();
            splashGroup.alpha = 1f;
            splashGroup.interactable = false;
            splashGroup.blocksRaycasts = false;
            var child = new GameObject("Plate");
            child.layer = 5;
            child.transform.SetParent(splashHolder.transform, false);
            var image = child.AddComponent<RawImage>();
            splashImage = image;
            image.texture = splashTexture;
            image.raycastTarget = false;
            var cr = child.GetComponent<RectTransform>();
            if (cr != null) { cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero; }
            LoggerInstance.Msg("STARTLOGO: Canvas gebaut (World-Space, Ebene 5, RawImage)");
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("STARTLOGO: Canvas " + e.GetType().Name + ": " + e.Message); splashFailed = true; splashHolder = null; return false; }
    }

    private bool LoadSplashTexture()
    {
        if (splashTexture != null) return true;
        splashTexture = null;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(SplashResource);
            if (stream == null)
            {
                LoggerInstance.Warning($"STARTLOGO: Ressource \"{SplashResource}\" fehlt. Vorhanden: {string.Join(", ", asm.GetManifestResourceNames())}");
                splashFailed = true;
                return false;
            }
            var bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length) { int step = stream.Read(bytes, read, bytes.Length - read); if (step <= 0) break; read += step; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // Il2CppStructArray ausdruecklich - die implizite Umwandlung greift nicht ueberall (PWS2).
            if (!ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes)) || tex.width <= 2)
            {
                LoggerInstance.Warning($"STARTLOGO: LoadImage lehnte {read} Bytes ab ({tex.width}x{tex.height})");
                splashFailed = true;
                return false;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            splashAspect = (float)tex.width / tex.height;
            splashTexture = tex;
            LoggerInstance.Msg($"STARTLOGO: Textur {read} Bytes -> {tex.width}x{tex.height}");
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("STARTLOGO: Textur " + e.GetType().Name + ": " + e.Message); splashFailed = true; return false; }
    }
}
