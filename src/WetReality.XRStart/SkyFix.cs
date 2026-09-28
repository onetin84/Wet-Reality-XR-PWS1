// Himmel im Headset (XRStart 1.13.0): Messung und Abhilfe in einem Lauf.
//
// GEMELDET: Himmel/Umgebung schwarz, am Rand ziehen Schlieren/Geisterbilder mit
// der Geometrie. Das ist das Bild eines NICHT GELOESCHTEN Farbpuffers: der
// Himmel zeichnet im Stereo-Pfad nichts, und was dort steht, ist der Vorframe.
// Welcher Teil versagt (Skybox-Shader, eigene Himmelskuppel, clearFlags), ist
// ungemessen - der Kameradump nannte clearFlags bisher nicht.
//
// MESSUNG einmal je Kamera: clearFlags, backgroundColor, RenderSettings.skybox
// (Material + Shader), Nebel, XRSettings.stereoRenderingMode, dazu bis zu zehn
// Renderer mit "sky" im Objekt- oder Shadernamen.
//
// ABHILFE SkyFix (cfg): "cubemap" (Vorgabe seit 1.25.0, SkyCube.cs) - Spielhimmel
// flach in eine Wuerfeltextur, Rueckfall solid; "solid" - die XR-Kamera loescht mit SkyColor,
// keine Schlieren, einfarbiger Himmel; "skybox" - clearFlags Skybox erzwungen;
// "off" - wie das Spiel. F2 schaltet live durch (off -> solid -> skybox), jeder
// Wechsel im Log. Nur mit laufendem XR; XR-Stopp stellt den Spielwert her.

using UnityEngine;
using UnityEngine.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private static readonly string[] SkyModes = { "off", "solid", "skybox", "cubemap" };
    private string skyMode = "cubemap";
    private Color skyColor = new(0.55f, 0.72f, 0.92f, 1f);
    private Camera? skyCam;
    private CameraClearFlags skyOrigFlags;
    private Color skyOrigColor;
    private bool skyApplied;
    private IntPtr skyMeasuredFor = IntPtr.Zero;
    private float nextSkyCheck;

    private void InitSky()
    {
        skyMode = (prefSkyFix.Value ?? "cubemap").Trim().ToLowerInvariant();
        if (Array.IndexOf(SkyModes, skyMode) < 0) skyMode = "cubemap";
        var v = ParseVec(prefSkyColor.Value, new Vector3(0.55f, 0.72f, 0.92f), "SkyColor");
        skyColor = new Color(v.x, v.y, v.z, 1f);
    }

    private void TickSky()
    {
        if (KeyPressed(VK_F2, ref f2WasDown, "F2"))
        {
            int i = Array.IndexOf(SkyModes, skyMode);
            skyMode = SkyModes[(i + 1) % SkyModes.Length];
            LoggerInstance.Msg($"F2: Himmel {skyMode}");
            RestoreSky("F2");
            nextSkyCheck = 0f;
        }

        if (!started) { RestoreSky("XR aus"); return; }
        float now = Time.unscaledTime;
        if (now < nextSkyCheck) return;
        nextSkyCheck = now + 1f;

        var cam = Camera.main;
        if (cam == null) return;
        // Die Menuekamera (Hauptmenue) hat ihren eigenen Grund - kein Himmel (1.25.0).
        if (menuCam != null && cam.Pointer == menuCam.Pointer) { RestoreSky("Menuekamera"); return; }
        if (skyCam != null && skyCam.Pointer != cam.Pointer) RestoreSky("neue Kamera");
        if (cam.Pointer != skyMeasuredFor) { skyMeasuredFor = cam.Pointer; MeasureSky(cam); }

        try
        {
            if (skyMode == "off") return;
            if (!skyApplied)
            {
                skyCam = cam;
                skyOrigFlags = cam.clearFlags;
                skyOrigColor = cam.backgroundColor;
                skyApplied = true;
            }
            // cubemap: gelingt der Bau nicht, gilt solid (Rueckfall, im Log).
            bool cube = skyMode == "cubemap" && EnsureSkyCube(cam);
            // Der Wuerfel (SkyCube.cs) zeichnet den Himmel selbst - die Kamera loescht fest.
            var want = skyMode == "skybox" ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (cam.clearFlags != want) { cam.clearFlags = want; LoggerInstance.Msg($"HIMMEL: '{cam.name}' clearFlags {skyOrigFlags} -> {want}{(cube ? " (Wuerfeltextur)" : "")}"); }
            if (want == CameraClearFlags.SolidColor && cam.backgroundColor != skyColor) cam.backgroundColor = skyColor;
        }
        catch (Exception e) { LoggerInstance.Warning("HIMMEL: " + e.GetType().Name + ": " + e.Message); }
    }

    private void RestoreSky(string why)
    {
        if (!skyApplied) return;
        try
        {
            if (skyCam != null) { skyCam.clearFlags = skyOrigFlags; skyCam.backgroundColor = skyOrigColor; }
            StopSkyCube();
            LoggerInstance.Msg($"HIMMEL: zurueck ({why}) - clearFlags {skyOrigFlags}");
        }
        catch { }
        skyApplied = false;
        skyCam = null;
    }

    private void MeasureSky(Camera cam)
    {
        try
        {
            var sky = RenderSettings.skybox;
            string skyText = sky == null ? "keine" : $"'{sky.name}' Shader '{(sky.shader == null ? "?" : sky.shader.name)}'";
            string stereo;
            try { stereo = XRSettings.stereoRenderingMode.ToString(); } catch { stereo = "?"; }
            LoggerInstance.Msg($"HIMMEL: Kamera '{cam.name}' clearFlags {cam.clearFlags} background {cam.backgroundColor} farClip {cam.farClipPlane:F0} | " +
                               $"RenderSettings.skybox {skyText} | Nebel {RenderSettings.fog} ({RenderSettings.fogMode}, Farbe {RenderSettings.fogColor}) | Stereo {stereo}");
            var rs = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int n = 0;
            for (int i = 0; i < rs.Length && n < 10; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                string on = r.name;
                var m = r.sharedMaterial;
                string sh = m == null || m.shader == null ? "" : m.shader.name;
                if (on.IndexOf("sky", StringComparison.OrdinalIgnoreCase) < 0 && sh.IndexOf("sky", StringComparison.OrdinalIgnoreCase) < 0) continue;
                LoggerInstance.Msg($"HIMMEL:   Renderer '{PathOf(r.transform)}' {r.GetIl2CppType().Name} enabled {r.enabled} layer {r.gameObject.layer} Shader '{sh}' Queue {(m == null ? -1 : m.renderQueue)}");
                n++;
            }
            if (n == 0) LoggerInstance.Msg("HIMMEL:   kein Renderer mit 'sky' im Namen oder Shader");
        }
        catch (Exception e) { LoggerInstance.Warning("HIMMEL: Messung " + e.GetType().Name + ": " + e.Message); }
    }
}
