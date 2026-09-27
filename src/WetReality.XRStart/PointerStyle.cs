// Eine Farbe fuer alle Zeiger und der Teleport-Zielmarker (XRStart 1.17.1).
// Port von PWS2 (Pose.cs PointerTint/BeamTint, TeleportAim Marker).
//
// FARBEN (PWS2, Werte aus dem dortigen Auftrag, Konfigurator-Namen):
//   pink    255,  45, 145   #FF2D91
//   green   100, 235,  95   #64EB5F
//   blue     40, 205, 245   #28CDF5   VORGABE ("Azure blue" im Konfigurator)
//   yellow  255, 215,  55   #FFD737
//   ROT (1, 0,3, 0,25) ist KEINE Auswahl, sondern die Absage - keine der vier ist rot.
// PointerAlpha (0,75) gilt fuer die STRAHLEN (Teleportbogen, Menue-, Scheiben-,
// Greifzeiger). Der Marker nimmt die VOLLE Farbe: die Transparenz steckt in der
// Textur, ein Alpha auf der Farbe dunkelte die opaken Linien mit ab.
//
// MARKER wie PWS2: EINE erzeugte Textur 256x256, weiss mit wechselnder Alpha -
// duenner Aussenring 1,000-0,975, dicker Ring 0,955-0,845, duenner Innenring
// 0,825-0,805, Fuellung (TeleportFillAlpha) und opakes Raster (TeleportGridCells,
// ab der MITTE gerechnet) unter 0,795, Mittelpunkt 0,05. Eigene Edge() - Unitys
// SmoothStep ist ein Lerp und machte in PWS2 Ringe und Fuellung unsichtbar.
// Auf einem World-Space-Canvas, flach (-90 um X), 3 cm ueber dem Boden,
// Durchmesser TeleportMarkerSize. Geschrieben per SetPixels32 in EINEM Aufruf,
// nicht 65536x SetPixel ueber die Interop-Grenze.

using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<string> prefPointerColor = null!;
    private MelonPreferences_Entry<float> prefPointerAlpha = null!, prefMarkerSize = null!, prefFillAlpha = null!;
    private MelonPreferences_Entry<int> prefGridCells = null!;

    private static readonly Color PointerPink = new(1f, 0.176f, 0.569f, 1f);
    private static readonly Color PointerGreen = new(0.392f, 0.922f, 0.373f, 1f);
    private static readonly Color PointerBlue = new(0.157f, 0.804f, 0.961f, 1f);
    private static readonly Color PointerYellow = new(1f, 0.843f, 0.216f, 1f);
    internal static readonly Color BlockedColor = new(1f, 0.3f, 0.25f, 1f);
    private string loggedPointerColor = "";

    private void InitPointerStyle(MelonPreferences_Category cat)
    {
        prefPointerColor = cat.CreateEntry("PointerColor", "blue", description: "Farbe von Teleportbogen, Teleportziel, Menue- und Greifzeiger: pink, green, blue oder yellow. Ein gesperrtes Ziel bleibt ROT.");
        prefPointerAlpha = cat.CreateEntry("PointerAlpha", 0.75f, description: "Deckkraft der Zeigestrahlen (Teleportbogen, Menue-, Scheiben-, Greifzeiger)");
        prefMarkerSize = cat.CreateEntry("TeleportMarkerSize", 0.45f, description: "Meter. DURCHMESSER des Teleportziels am Boden");
        prefGridCells = cat.CreateEntry("TeleportGridCells", 8, description: "Rasterzellen quer ueber das Teleportziel");
        prefFillAlpha = cat.CreateEntry("TeleportFillAlpha", 0.22f, description: "Deckkraft der Fuellung im Teleportziel; 0 laesst nur Ringe, Raster und Punkt");
    }

    internal Color PointerTint()
    {
        string name = (prefPointerColor?.Value ?? "blue").Trim().ToLowerInvariant();
        switch (name)
        {
            case "pink": return PointerPink;
            case "green": return PointerGreen;
            case "blue": return PointerBlue;
            case "yellow": return PointerYellow;
        }
        if (name != loggedPointerColor) { loggedPointerColor = name; LoggerInstance.Warning($"PointerColor \"{name}\" ist nicht pink, green, blue oder yellow - nehme blue."); }
        return PointerBlue;
    }

    internal Color BeamTint()
    {
        var t = PointerTint();
        return new Color(t.r, t.g, t.b, Mathf.Clamp01(prefPointerAlpha?.Value ?? 0.75f));
    }

    // ---------------------------------------------------------------- Marker

    private const int MarkerTex = 256;
    private const float MarkerCanvas = 256f;
    private GameObject? markerHolder;
    private RawImage? markerImage;
    private Texture2D? markerTexture;
    private int bakedCells = -1;
    private float bakedFill = float.NaN;
    private bool markerFailed;

    private static float Edge(float from, float to, float value)
    {
        if (Mathf.Approximately(from, to)) return value < from ? 0f : 1f;
        var t = Mathf.Clamp01((value - from) / (to - from));
        return t * t * (3f - 2f * t);
    }

    private static float Band(float inner, float outer, float edge, float r)
        => Mathf.Min(Edge(inner - edge, inner + edge, r), 1f - Edge(outer - edge, outer + edge, r));

    private bool BakeMarker(int cells, float fill)
    {
        try
        {
            var tex = new Texture2D(MarkerTex, MarkerTex, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float half = (MarkerTex - 1) * 0.5f;
            cells = Mathf.Clamp(cells, 2, 32);
            fill = Mathf.Clamp01(fill);
            float lineHalfCells = 1.1f / ((float)MarkerTex / cells);
            float edge = 1.2f * 2f / MarkerTex;
            var px = new Color32[MarkerTex * MarkerTex];
            for (int y = 0; y < MarkerTex; y++)
            {
                for (int x = 0; x < MarkerTex; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r <= 1f + edge)
                    {
                        if (fill > 0f) a = fill * (1f - Edge(0.795f - edge, 0.795f + edge, r));
                        if (r < 0.795f - edge)
                        {
                            float u = dx * 0.5f * cells, v = dy * 0.5f * cells;
                            float du = Math.Abs(u - Mathf.Round(u)), dv = Math.Abs(v - Mathf.Round(v));
                            a = Math.Max(a, 1f - Edge(lineHalfCells * 0.7f, lineHalfCells * 1.3f, Math.Min(du, dv)));
                        }
                        a = Math.Max(a, Band(0.805f, 0.825f, edge, r));
                        a = Math.Max(a, Band(0.845f, 0.955f, edge, r));
                        a = Math.Max(a, Band(0.975f, 1.000f, edge, r));
                        a = Math.Max(a, 1f - Edge(0.05f - edge, 0.05f + edge, r));
                    }
                    px[y * MarkerTex + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            if (markerTexture != null) UnityEngine.Object.Destroy(markerTexture);
            markerTexture = tex;
            bakedCells = cells;
            bakedFill = fill;
            LoggerInstance.Msg($"TELEPORT: Zieltextur {MarkerTex}x{MarkerTex}, {cells} Rasterzellen, Fuellung {fill:F2}, drei Ringe");
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("TELEPORT: Zieltextur " + e.GetType().Name + ": " + e.Message); markerFailed = true; return false; }
    }

    private bool EnsureMarker()
    {
        if (markerFailed) return false;
        int cells = prefGridCells.Value;
        float fill = prefFillAlpha.Value;
        bool stale = markerTexture == null || bakedCells != Mathf.Clamp(cells, 2, 32) || !Mathf.Approximately(bakedFill, Mathf.Clamp01(fill));
        if (stale && !BakeMarker(cells, fill)) return false;
        if (markerHolder != null && markerImage != null) { if (stale) markerImage.texture = markerTexture; return true; }
        try
        {
            markerHolder = new GameObject("WetReality_TeleportMarker");
            UnityEngine.Object.DontDestroyOnLoad(markerHolder);
            markerHolder.layer = 0;
            var canvas = markerHolder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32100;
            var rect = markerHolder.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(MarkerCanvas, MarkerCanvas);
            var child = new GameObject("Marker");
            child.layer = 0;
            child.transform.SetParent(markerHolder.transform, false);
            markerImage = child.AddComponent<RawImage>();
            markerImage.raycastTarget = false;
            markerImage.texture = markerTexture;
            var cr = child.GetComponent<RectTransform>();
            if (cr != null) { cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero; }
            markerHolder.SetActive(false);
            LoggerInstance.Msg("TELEPORT: Zielmarker gebaut (World-Space-Canvas, Ebene 0, 3 cm ueber dem Boden)");
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning("TELEPORT: Zielmarker " + e.GetType().Name + ": " + e.Message); markerFailed = true; markerHolder = null; markerImage = null; return false; }
    }

    private void ShowMarker(Vector3 target, bool valid)
    {
        if (!EnsureMarker()) return;
        try
        {
            var t = markerHolder!.transform;
            t.position = target + new Vector3(0f, 0.03f, 0f);
            t.rotation = Quaternion.Euler(-90f, 0f, 0f);   // Canvas zeigt nach +Z; -90 um X legt es mit der Flaeche nach oben
            float scale = Math.Max(0.05f, prefMarkerSize.Value) / MarkerCanvas;
            t.localScale = new Vector3(scale, scale, scale);
            markerImage!.color = valid ? PointerTint() : BlockedColor;
            if (!markerHolder.activeSelf) markerHolder.SetActive(true);
        }
        catch (Exception e) { LoggerInstance.Warning("TELEPORT: Marker zeigen " + e.GetType().Name); markerHolder = null; markerImage = null; }
    }

    private void HideMarker()
    {
        try { if (markerHolder != null && markerHolder.activeSelf) markerHolder.SetActive(false); } catch { markerHolder = null; markerImage = null; }
    }
}
