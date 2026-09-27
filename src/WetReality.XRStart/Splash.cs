// Startlogo im Headset (XRStart 1.22.1). Port von PWS2 Splash.cs.
//
// Einmal je XR-Start (auf dem UEBERGANG gespannt, nicht auf dem Zustand - sonst
// begaenne das Halten jeden Frame neu): SplashSeconds (2,5) voll, dann
// SplashFadeSeconds (0,75) ausblenden, SplashDistance (2,2 m) vor der Kamera,
// SplashWidth (1,9 m) breit. Ohne Camera.main wartet es, und der Zeitgeber mit.
//
// Wie PWS2: World-Space-Canvas + RawImage statt Quad - ein CanvasRenderer holt
// sich das UI-Standardmaterial selbst, kein Shader.Find. Ebene 0 (Spieler- und
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
    private MelonPreferences_Entry<float> prefSplashSeconds = null!, prefSplashFade = null!, prefSplashDistance = null!, prefSplashWidth = null!;

    private GameObject? splashHolder;
    private Texture2D? splashTexture;
    private CanvasGroup? splashGroup;
    private RectTransform? splashRect;
    private bool splashFailed, splashArmed, splashLogged;
    private float splashStartedAt = -1f;
    private float splashAspect = 1650f / 953f;

    private void InitSplash(MelonPreferences_Category cat)
    {
        prefShowSplash = cat.CreateEntry("ShowSplash", true, description: "Startlogo im Headset, sobald XR laeuft (wie PWS2)");
        prefSplashSeconds = cat.CreateEntry("SplashSeconds", 2.5f, description: "Sekunden voll sichtbar, bevor es ausblendet");
        prefSplashFade = cat.CreateEntry("SplashFadeSeconds", 0.75f, description: "Sekunden fuer das Ausblenden");
        prefSplashDistance = cat.CreateEntry("SplashDistance", 2.2f, description: "Meter vor dem Headset");
        prefSplashWidth = cat.CreateEntry("SplashWidth", 1.9f, description: "Breite in Metern");
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
        var cam = Camera.main;
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
            float scale = prefSplashWidth.Value / SplashCanvasWidth;
            t.localScale = new Vector3(scale, scale, scale);
            if (splashRect != null) splashRect.sizeDelta = new Vector2(SplashCanvasWidth, SplashCanvasWidth / splashAspect);
            if (!splashLogged)
            {
                splashLogged = true;
                LoggerInstance.Msg($"STARTLOGO: {splashTexture!.width}x{splashTexture.height}, {prefSplashWidth.Value:F2} m breit in {prefSplashDistance.Value:F2} m, Kamera '{cam.name}', {hold:F2} s + {fade:F2} s");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("STARTLOGO: " + e.GetType().Name + ": " + e.Message); splashHolder = null; }
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
            splashHolder.layer = 0;
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
            child.layer = 0;
            child.transform.SetParent(splashHolder.transform, false);
            var image = child.AddComponent<RawImage>();
            image.texture = splashTexture;
            image.raycastTarget = false;
            var cr = child.GetComponent<RectTransform>();
            if (cr != null) { cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero; }
            LoggerInstance.Msg("STARTLOGO: Canvas gebaut (World-Space, Ebene 0, RawImage)");
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
