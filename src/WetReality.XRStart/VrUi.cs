// Spiel-UI im Headset (XRStart 1.8.0) - Port von PWS2 GameUi/UiDepth.
//
// GEMESSEN (1.7.0 Lauf 1, 14:45): genau eine aktive Wurzel, UIRoot(Clone) in
// DontDestroyOnLoad, ScreenSpaceOverlay, pixelRect 1920x1080 = FENSTER, nicht
// der Augenpuffer (2148x2012). PWS1s URP legt das Overlay nur ins Monitorbild -
// darum Menue am Monitor ja, im Headset nein. (PWS2 dagegen bemass es auf den
// Augenpuffer und zeigte es doppelt.) Alle anderen "Wurzeln" im Dump sind
// abgeschaltete Unter-Canvases.
//
// Umbau wie PWS2 (dort im Headset bestaetigt):
//   - UIRoot -> ScreenSpaceCamera auf Camera.main (PlayerCamera), planeDistance
//     2 m. Ein Schreibzugriff auf die WURZEL regiert den ganzen Baum.
//   - Bit der UI-Ebene (5) in die Culling-Maske der Kamera: 0x0028E717 hat es
//     nicht; ohne das waere die UI als Geometrie unsichtbar.
//   - Skalierung 0,3627 auf die KINDknoten, nie auf die Wurzel (ein Canvas
//     treibt sein eigenes RectTransform). ScreenSpaceCamera fuellt immer das
//     ganze Sichtfeld; planeDistance macht nichts kleiner.
//   - ZTest Always auf jedem UI-Material (unity_GUIZTestMode, _ZTestMode, _ZTest,
//     OHNE HasProperty - PWS2 §103), sonst verschwindet die UI in naher
//     Geometrie. Erneut, wenn sich die Canvas-Liste aendert (Menue auf).
//   - Jede Sekunde geprueft: hat das Spiel renderMode oder worldCamera
//     zurueckgesetzt (PWS2: Menuewechsel, Levelwechsel = neue Kamera), wird neu
//     angewandt - sonst bleibt die UI weltfest stehen.
//   - Alles umkehrbar: F10 und XR-Stopp nehmen zurueck (Modus, Kamera, Abstand,
//     Maske, Skalierung, ZTest).
// Im Hauptmenue gibt es keine 3D-Kamera (Camera.main null) - seit 1.22.0 stellt
// MenuCamera.cs dort eine eigene (Tag MainCamera), und alles hier gilt unveraendert.

using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int VK_F10 = 0x79;
    private bool f10WasDown;
    private bool vrUiWanted = true;

    // cfg UiDistance (m) und UiScale (Kindknoten), PWS2-Namen; Vorgaben 2 und 0,3627.
    private float UiDistance => Math.Max(0.5f, prefUiDistance?.Value ?? 2f);
    private float UiScale => Math.Max(0.05f, prefUiScale?.Value ?? 0.3627f);
    private static readonly string[] ZTestProperties = { "unity_GUIZTestMode", "_ZTestMode", "_ZTest" };
    private const int CompareAlways = 8, CompareLessEqual = 4;

    private Canvas? uiRoot;
    private bool uiConverted, uiCaptured;
    private RenderMode uiOrigMode = RenderMode.ScreenSpaceOverlay;
    private Camera? uiOrigCamera;
    private float uiOrigPlane = 100f;
    private Camera? uiMaskCamera;          // Kamera, der die Mod das UI-Bit gegeben hat
    private int uiMaskAdded;               // das Bit, 0 = keins hinzugefuegt
    private readonly Dictionary<IntPtr, (Transform T, Vector3 Scale)> uiScaled = new();
    private readonly Dictionary<IntPtr, (Material M, int Previous)> uiDepthTouched = new();
    private float nextUiCheck, nextUiDepth = float.MaxValue;
    private bool uiDepthFollowup;
    private string uiDepthSignature = "";

    private void TickVrUi()
    {
        if (KeyPressed(VK_F10, ref f10WasDown, "F10"))
        {
            vrUiWanted = !vrUiWanted;
            LoggerInstance.Msg($"F10: UI im Headset {(vrUiWanted ? "AN" : "AUS (Overlay wie das Spiel)")}");
        }

        bool want = vrUiWanted && started && (writeHead || menuCamOn);   // Hauptmenue: ohne Kopfschreiber (MenuCamera.cs)
        if (!want)
        {
            if (uiConverted) RestoreUi(started ? "F10/Kopf aus" : "XR aus");
            return;
        }

        float now = Time.unscaledTime;
        if (now >= nextUiCheck)
        {
            nextUiCheck = now + 1f;
            EnsureUi();
        }
        if (uiConverted && now >= nextUiDepth)
        {
            nextUiDepth = float.MaxValue;
            ApplyUiDepth();
            // Noch einmal 2 s spaeter: die HUD-Elemente stehen beim Umstellen
            // nicht alle (PWS2 GameUi.EnsureStereo).
            if (uiDepthFollowup) { uiDepthFollowup = false; nextUiDepth = now + 2f; }
        }
    }

    // Aus ProbeCanvases: ein geoeffnetes Menue bringt neue Graphics mit.
    private void TouchUiDepth(string signature)
    {
        if (!uiConverted || signature == uiDepthSignature) return;
        uiDepthSignature = signature;
        nextMenuScan = 0f; nextGraphicScan = 0f;   // neue Knoepfe sofort treffbar (MenuPointer.cs)
        nextUiDepth = Time.unscaledTime + 0.2f;
    }

    private bool ResolveUiRoot()
    {
        if (uiRoot != null) return true;
        uiRoot = null;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Canvas>();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || c.transform.parent != null) continue;
                if (!c.name.StartsWith("UIRoot", StringComparison.Ordinal)) continue;
                if (!c.gameObject.scene.IsValid()) continue;   // Prefab-Asset, kein Szenenobjekt
                uiRoot = c;
                LoggerInstance.Msg($"VR-UI: Wurzel '{c.name}' mode={c.renderMode} layer={c.gameObject.layer} Kinder={c.transform.childCount}");
                return true;
            }
        }
        catch (Exception e) { LoggerInstance.Warning("VR-UI: Wurzelsuche " + e.GetType().Name + ": " + e.Message); }
        return false;
    }

    private void EnsureUi()
    {
        if (!ResolveUiRoot()) return;
        var cam = UiCamera;   // beim Laden die Menuekamera (MenuCamera.cs)
        if (cam == null) return;   // Hauptmenue: keine 3D-Kamera
        try
        {
            var root = uiRoot!;
            if (uiConverted)
            {
                bool modeLost = root.renderMode != RenderMode.ScreenSpaceCamera;
                bool camLost = root.worldCamera == null || root.worldCamera.Pointer != cam.Pointer;
                if (!modeLost && !camLost) { ApplyUiScale(); return; }
                LoggerInstance.Msg($"VR-UI: Umstellung verloren ({(modeLost ? "renderMode=" + root.renderMode : "worldCamera")}) - neu angewandt");
            }
            if (!uiCaptured)
            {
                uiCaptured = true;
                uiOrigMode = root.renderMode;
                uiOrigCamera = root.worldCamera;
                uiOrigPlane = root.planeDistance;
            }

            int bit = 1 << root.gameObject.layer;
            if (uiMaskCamera == null || uiMaskCamera.Pointer != cam.Pointer)
            {
                RestoreMask();
                if ((cam.cullingMask & bit) == 0)
                {
                    cam.cullingMask |= bit;
                    uiMaskCamera = cam;
                    uiMaskAdded = bit;
                    LoggerInstance.Msg($"VR-UI: Ebene {root.gameObject.layer} in die Maske von '{cam.name}', jetzt 0x{cam.cullingMask:X8}");
                }
            }

            root.worldCamera = cam;
            root.planeDistance = UiDistance;
            root.renderMode = RenderMode.ScreenSpaceCamera;
            if (!uiConverted) LoggerInstance.Msg($"VR-UI: ScreenSpaceCamera auf '{cam.name}', {UiDistance:F1} m, Skalierung {UiScale:F4} (vorher {uiOrigMode}, plane {uiOrigPlane:F1})");
            uiConverted = true;
            uiScaled.Clear();   // Kindknoten ggf. neu - Vorwerte neu erfassen
            ApplyUiScale();
            nextUiDepth = Time.unscaledTime;   // sofort und 2 s spaeter noch einmal
            uiDepthFollowup = true;
            uiDepthSignature = "";
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("VR-UI: Umstellung " + e.GetType().Name + ": " + e.Message);
            uiRoot = null;
        }
    }

    private void ApplyUiScale()
    {
        var root = uiRoot;
        if (root == null) return;
        try
        {
            var t = root.transform;
            int written = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                if (ch == null) continue;
                var key = ch.Pointer;
                if (!uiScaled.ContainsKey(key)) { uiScaled[key] = (ch, ch.localScale); }
                var want = new Vector3(UiScale, UiScale, 1f);
                if (ch.localScale != want) { ch.localScale = want; written++; }
            }
            if (written > 0) Diag($"VR-UI: Skalierung {UiScale:F4} auf {written} Kindknoten");
        }
        catch (Exception e) { LoggerInstance.Warning("VR-UI: Skalierung " + e.GetType().Name + ": " + e.Message); }
    }

    private void ApplyUiDepth()
    {
        var root = uiRoot;
        if (root == null) return;
        try
        {
            var graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            int added = 0;
            var shaders = new HashSet<string>();
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null) continue;
                Material? m;
                try { m = g.materialForRendering; } catch { continue; }
                if (m == null || uiDepthTouched.ContainsKey(m.Pointer)) continue;
                int prev;
                try { prev = m.GetInt(ZTestProperties[0]); if (prev == 0) prev = CompareLessEqual; } catch { prev = CompareLessEqual; }
                foreach (var name in ZTestProperties) { try { m.SetInt(name, CompareAlways); } catch { } }
                uiDepthTouched[m.Pointer] = (m, prev);
                added++;
                try { shaders.Add(m.shader == null ? "?" : m.shader.name); } catch { }
            }
            if (added > 0)
                Diag($"VR-UI: ZTest Always auf {added} neue Materialien ({uiDepthTouched.Count} gesamt, {graphics.Length} Graphics) - Shader {string.Join(", ", shaders)}");
        }
        catch (Exception e) { LoggerInstance.Warning("VR-UI: ZTest " + e.GetType().Name + ": " + e.Message); }
    }

    private void RestoreMask()
    {
        try
        {
            if (uiMaskCamera != null && uiMaskAdded != 0) uiMaskCamera.cullingMask &= ~uiMaskAdded;
        }
        catch { }
        uiMaskCamera = null;
        uiMaskAdded = 0;
    }

    private void RestoreUi(string why)
    {
        try
        {
            var root = uiRoot;
            if (root != null)
            {
                root.renderMode = uiOrigMode;
                root.worldCamera = uiOrigCamera;
                root.planeDistance = uiOrigPlane;
            }
            foreach (var e in uiScaled.Values) { try { if (e.T != null) e.T.localScale = e.Scale; } catch { } }
            foreach (var e in uiDepthTouched.Values)
            {
                try { if (e.M != null) foreach (var name in ZTestProperties) SetIntSafe(e.M, name, e.Previous); } catch { }
            }
            RestoreMask();
            LoggerInstance.Msg($"VR-UI: zurueckgenommen ({why}) - {uiOrigMode}, {uiScaled.Count} Kindknoten, {uiDepthTouched.Count} Materialien");
        }
        catch (Exception e) { LoggerInstance.Warning("VR-UI: Ruecknahme " + e.GetType().Name + ": " + e.Message); }
        uiScaled.Clear();
        uiDepthTouched.Clear();
        uiConverted = false;
        uiDepthSignature = "";
        nextUiDepth = float.MaxValue;
    }

    private static void SetIntSafe(Material m, string name, int value)
    {
        try { m.SetInt(name, value); } catch { }
    }
}
