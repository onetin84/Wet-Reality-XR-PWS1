// Komfort: Snap-Turn, Vignette, Teleport-Blende (XRStart 1.23.0).
// Port von PWS2 (Pose.cs Drehen/DriveVignette, Vignette.cs), dieselben
// cfg-Namen und Vorgaben, damit der PWS2-Konfigurator passt.
//
// SNAP-TURN (SnapTurn, aus; SnapAngle 45): rechter Stick X springt um
// SnapAngle, EIN Sprung je Auslenkung - wieder bewaffnet erst in der Totzone
// (nicht beim Teleport-Zielen: der Stick kommt danach von selbst zurueck).
// Aufruf aus ControllerInput.UpdateControllerTurn.
//
// VIGNETTE (ComfortVignette, aus): kopfgefuehrte schwarze Flaeche, Anteil
// = Bewegung (linker Stick, wie ans Spiel gegeben) und, mit VignetteTurn, die
// gleitende Drehung; ein Snap-Sprung gibt 0,14 s Puls. VignetteStrength 0,7,
// VignetteInner 0,55 als Anteil des HALBEN SICHTFELDS (PWS2 Abschnitt 152:
// r = tan(FOV/2) / tan(70) - sonst lag die Rampe ausserhalb des Blickfelds),
// FadeIn 0,1 s, FadeOut 0,3 s, VignetteDistance 0,5 m, gedeckelt hinter der
// Near-Plane + 0,06 (PWS2: naeher = weggeschnitten, unsichtbar).
// TELEPORT-BLENDE (TeleportBlinkSeconds 0,12): Dreieck nach Schwarz beim
// Sprung, auch ohne Vignette - sie gehoert zum Teleport.
//
// Bau wie Splash.cs: World-Space-Canvas + RawImage, Ebene 0, sortingOrder 32500
// (ueber dem Startlogo), je Frame vor die Kamera gesetzt. Textur 128x128
// erzeugt, per SetPixels32 in EINEM Aufruf (PWS2: 16384x SetPixel).

using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefSnapTurn = null!, prefComfortVignette = null!, prefVignetteTurn = null!;
    private MelonPreferences_Entry<float> prefSnapAngle = null!, prefVignetteStrength = null!, prefVignetteInner = null!,
        prefVignetteFadeIn = null!, prefVignetteFadeOut = null!, prefVignetteDistance = null!, prefTeleportBlink = null!;

    private const float VigHalfAngle = 70f, VigNearMargin = 0.06f, VigCanvas = 1000f;
    private const int VigTex = 128;

    private bool snapArmed = true;
    private float turnPulseUntil;
    private float vigTurnDemand;
    internal static float vigMoveDemand;   // aus dem PlayerInput-Patch (ControllerInput.cs)

    private GameObject? vigHolder;
    private Texture2D? vigTexture;
    private RawImage? vigRing, vigPlate;
    private bool vigFailed, vigLogged;
    private float vigCurrent, vigBakedInner = float.NaN, vigBakedVisible = float.NaN;
    private float blinkStart = -1f, blinkSpan;

    private void InitComfort(MelonPreferences_Category cat)
    {
        prefSnapTurn = cat.CreateEntry("SnapTurn", false, description: "Rechter Stick X springt um SnapAngle statt gleitend zu drehen");
        prefSnapAngle = cat.CreateEntry("SnapAngle", 45f, description: "Grad je Sprung (Konfigurator: 15, 30, 45, 60)");
        prefComfortVignette = cat.CreateEntry("ComfortVignette", false, description: "Dunkelt den Blickrand beim Gehen und beim gleitenden Drehen ab");
        prefVignetteStrength = cat.CreateEntry("VignetteStrength", 0.7f, description: "Wie schwarz der Rand bei voller Bewegung wird, 0 bis 1");
        prefVignetteInner = cat.CreateEntry("VignetteInner", 0.55f, description: "Wo das Abdunkeln beginnt, als Anteil des halben Sichtfelds (0 = ab der Mitte, 0,9 = nur ganz am Rand)");
        prefVignetteFadeIn = cat.CreateEntry("VignetteFadeIn", 0.1f, description: "Sekunden von klar bis voll");
        prefVignetteFadeOut = cat.CreateEntry("VignetteFadeOut", 0.3f, description: "Sekunden von voll zurueck bis klar");
        prefVignetteTurn = cat.CreateEntry("VignetteTurn", true, description: "Auch gleitendes Drehen dunkelt ab; Snap-Turn gibt einen kurzen Puls");
        prefVignetteDistance = cat.CreateEntry("VignetteDistance", 0.5f, description: "Meter vor dem Auge (hinter der Near-Plane gedeckelt)");
        prefTeleportBlink = cat.CreateEntry("TeleportBlinkSeconds", 0.12f, description: "Sekunden Schwarz beim Teleport-Sprung, 0 = aus; wirkt auch ohne Vignette");
    }

    // ------------------------------------------------------------ Drehen

    // Aus UpdateControllerTurn, NACH den Sperren (Menue, Scheibe, Teleport,
    // Stick nach vorn). x = rechter Stick X. Liefert die Drehung dieses Frames.
    private float ComfortTurn(float x, float deadZone)
    {
        if (Math.Abs(x) <= deadZone) { snapArmed = true; return 0f; }
        if (prefSnapTurn.Value)
        {
            if (!snapArmed) return 0f;
            snapArmed = false;
            if (prefVignetteTurn.Value) turnPulseUntil = Time.unscaledTime + 0.14f;
            float d = Math.Sign(x) * prefSnapAngle.Value;
            Diag($"SNAP-TURN {(x < 0f ? "links" : "rechts")} {prefSnapAngle.Value:F0} Grad");
            return d;
        }
        // Aus der Totzone heraus weich einsetzen, nicht mit einem Sprung.
        float s = (Math.Abs(x) - deadZone) / (1f - deadZone);
        if (prefVignetteTurn.Value) vigTurnDemand = Math.Max(vigTurnDemand, s);
        return Math.Sign(x) * s * TurnSpeed * Time.unscaledDeltaTime;
    }

    // ------------------------------------------------------------ Vignette

    internal void VignetteBlink(float seconds)
    {
        if (seconds <= 0.001f) return;
        blinkStart = Time.unscaledTime;
        blinkSpan = seconds;
    }

    // In onBeforeRender, nach Kopf und Teleport: die Kamera steht final.
    private void DriveVignette()
    {
        float demand = Math.Max(vigMoveDemand, vigTurnDemand);
        vigMoveDemand = 0f; vigTurnDemand = 0f;   // verbraucht
        if (Time.unscaledTime < turnPulseUntil) demand = 1f;
        if (!started || !writeHead) demand = 0f;

        bool blinking = blinkStart >= 0f && Time.unscaledTime - blinkStart < blinkSpan;
        if (!blinking) blinkStart = -1f;

        float want = prefComfortVignette.Value ? Mathf.Clamp01(demand) * Mathf.Clamp01(prefVignetteStrength.Value) : 0f;
        float rate = want > vigCurrent ? prefVignetteFadeIn.Value : prefVignetteFadeOut.Value;
        vigCurrent = rate <= 0.001f ? want : Mathf.MoveTowards(vigCurrent, want, Time.unscaledDeltaTime / rate);

        if (vigCurrent <= 0.001f && !blinking)
        {
            vigCurrent = 0f;
            try { if (vigHolder != null && vigHolder.activeSelf) vigHolder.SetActive(false); } catch { vigHolder = null; }
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;
        try
        {
            float near = 0.01f;
            try { near = cam.nearClipPlane; } catch { }
            float dist = Math.Max(prefVignetteDistance.Value, near + VigNearMargin);
            float fov = 90f;
            try { float r = cam.fieldOfView; if (r > 20f && r < 170f) fov = r; } catch { }
            float visible = Mathf.Clamp(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(VigHalfAngle * Mathf.Deg2Rad), 0.1f, 1f);
            float inner = prefVignetteInner.Value;
            if (!EnsureVignette(inner, visible)) return;

            var eye = cam.transform;
            var t = vigHolder!.transform;
            t.position = eye.position + eye.forward * dist;
            t.rotation = eye.rotation;
            float width = 2f * dist * Mathf.Tan(VigHalfAngle * Mathf.Deg2Rad);
            float scale = width / VigCanvas;
            t.localScale = new Vector3(scale, scale, scale);
            vigRing!.color = new Color(0f, 0f, 0f, vigCurrent);
            // Dreieck: schnell nach Schwarz und genauso schnell zurueck.
            float blinkAlpha = 0f;
            if (blinking && blinkSpan > 0.001f) { float ph = (Time.unscaledTime - blinkStart) / blinkSpan; blinkAlpha = 1f - Math.Abs(ph * 2f - 1f); }
            vigPlate!.color = new Color(0f, 0f, 0f, Mathf.Clamp01(blinkAlpha));
            if (!vigLogged)
            {
                vigLogged = true;
                LoggerInstance.Msg($"VIGNETTE: {VigTex}x{VigTex}, {width:F2} m breit in {dist:F2} m (verlangt {prefVignetteDistance.Value:F2}, nearClip {near:F3}), FOV {fov:F1} -> sichtbar bis r {visible:F3}, Rampe {Mathf.Clamp(inner, 0f, 0.95f) * visible:F3} bis {visible:F3}, Kamera '{cam.name}'");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("VIGNETTE: " + e.GetType().Name + ": " + e.Message); vigHolder = null; vigRing = null; vigPlate = null; }
    }

    private bool EnsureVignette(float inner, float visible)
    {
        if (vigFailed) return false;
        if (vigTexture == null || !Mathf.Approximately(vigBakedInner, inner) || !Mathf.Approximately(vigBakedVisible, visible))
        {
            if (!BakeVignette(inner, visible)) return false;
            if (vigRing != null) vigRing.texture = vigTexture;
        }
        if (vigHolder != null) { if (!vigHolder.activeSelf) vigHolder.SetActive(true); return true; }
        vigHolder = null; vigRing = null; vigPlate = null;   // zerstoert: ALLE Felder raeumen
        try
        {
            vigHolder = new GameObject("WetReality_Vignette");
            UnityEngine.Object.DontDestroyOnLoad(vigHolder);
            vigHolder.layer = 0;
            var canvas = vigHolder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32500;   // ueber dem Startlogo (32000)
            var rect = vigHolder.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(VigCanvas, VigCanvas);
            var group = vigHolder.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;   // sonst finge die Flaeche jeden Zeiger ab
            vigRing = AddVignettePlane("Vignette", vigTexture);
            vigPlate = AddVignettePlane("Blink", null);
            LoggerInstance.Msg("VIGNETTE: Canvas gebaut (World-Space, Ebene 0, Vignette + Blende)");
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("VIGNETTE: Canvas " + e.GetType().Name + ": " + e.Message); vigFailed = true; vigHolder = null; return false; }
    }

    private RawImage AddVignettePlane(string name, Texture2D? tex)
    {
        var child = new GameObject(name);
        child.layer = 0;
        child.transform.SetParent(vigHolder!.transform, false);
        var img = child.AddComponent<RawImage>();
        img.raycastTarget = false;
        img.color = new Color(0f, 0f, 0f, 0f);
        if (tex != null) img.texture = tex;
        var cr = child.GetComponent<RectTransform>();
        if (cr != null) { cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero; }
        return img;
    }

    private bool BakeVignette(float inner, float visible)
    {
        try
        {
            if (vigTexture != null) UnityEngine.Object.Destroy(vigTexture);
            vigTexture = null;
            var tex = new Texture2D(VigTex, VigTex, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float edge = Mathf.Clamp(inner, 0f, 0.95f) * visible;
            float span = Math.Max(0.02f, visible - edge);
            float half = (VigTex - 1) * 0.5f;
            var px = new Color32[VigTex * VigTex];
            for (int y = 0; y < VigTex; y++)
                for (int x = 0; x < VigTex; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float ramp = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - edge) / span);
                    float a = ramp * ramp * (3f - 2f * ramp);   // weiche Kante, kein sichtbarer Ring
                    px[y * VigTex + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            vigTexture = tex;
            vigBakedInner = inner;
            vigBakedVisible = visible;
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("VIGNETTE: Textur " + e.GetType().Name + ": " + e.Message); vigFailed = true; return false; }
    }
}
