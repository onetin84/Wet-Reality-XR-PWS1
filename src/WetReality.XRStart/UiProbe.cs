// Messung Menue und Spiegelbild (XRStart 1.7.0) - schreibt NICHTS an der UI.
//
// MENUE: Das Pausenmenue oeffnet, ist am Monitor zu sehen, im Headset nicht.
// In PWS2 war ein ScreenSpaceOverlay-Canvas im Headset DOPPELT und im Gesicht
// zu sehen (PWS2-Handbuch §10, §104) - hier gar nicht. Also zeichnet PWS1 die
// UI vermutlich anders (UIClearCamera(Clone) steht im Kameradump). Gemessen
// wird, was PWS1 tatsaechlich hat: jede aktive Wurzel-Canvas mit Modus,
// Kamera, Ebene, Sortierung, Groesse - voll gelistet, sobald sich die Liste
// aendert (Menue auf/zu), nicht einmalig.
//
// SPIEGEL: PWS1 spiegelt das Headsetbild auf den Monitor, PWS2 nicht, obwohl
// XRBoot dort XRSettings.showDeviceView und gameViewRenderMode setzt. In einer
// SRP zeichnet die Pipeline den Spiegel selbst, im Modus, den das
// Display-Subsystem als bevorzugt meldet (XRDisplaySubsystem.
// GetPreferredMirrorBlitMode). Hypothese: das managed OpenXR-Paket (in PWS1
// einkompiliert, in PWS2 nachgebaut) setzt diesen Wert. Gemessen:
//   - beim XR-Start Ausgangswerte beider Schalter;
//   - F1: gameViewRenderMode weiter (None, LeftEye, RightEye, BothEyes);
//   - F2: PreferredMirrorBlitMode weiter (Default 0, LeftEye -1, SideBySide -3,
//     None -6 - die Werte von UnityEngine.XR.XRMirrorViewBlitMode);
//   - je 1 s nach dem Setzen zurueckgelesen: haelt der Wert, oder stellt ihn
//     jemand zurueck? Was der Monitor zeigt, sagt der Nutzer.
//
// GEMESSEN (1.7.0 Lauf 2, 15:14): in Unity 2020.3 sind gameViewRenderMode und
// PreferredMirrorBlitMode DERSELBE Wert - F1 LeftEye liest -1 zurueck, RightEye
// -2, BothEyes -3, None -6, und F2 umgekehrt; niemand setzt zurueck, F1 und F2
// wirken am Monitor gleich (Nutzer). Beim Start aber: gameViewRenderMode None
// bei PreferredMirrorBlitMode 0 und running=False - VOR dem laufenden Display
// sind sie nicht gekoppelt. Darum schreibt DesktopMirror (1.8.0) erst bei
// running und gleicht jede halbe Sekunde per Ruecklesung ab, statt einmal zu
// schreiben. F1/F2 uebersteuern bis zum naechsten XR-Start.

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int VK_F1 = 0x70, VK_F2 = 0x71;
    private bool f1WasDown, f2WasDown;
    private float nextCanvasProbe;
    private string lastCanvasSignature = "";
    private bool mirrorLogged;
    private float mirrorReadbackAt = -1f;
    private string mirrorReadbackWhat = "";
    private bool mirrorManual;
    private float nextMirrorDrive;
    private int mirrorApplied = int.MinValue;

    private static readonly GameViewRenderMode[] MirrorModes =
        { GameViewRenderMode.None, GameViewRenderMode.LeftEye, GameViewRenderMode.RightEye, GameViewRenderMode.BothEyes };
    private static readonly int[] BlitModes = { 0, -1, -3, -6 };
    private static readonly string[] BlitNames = { "Default", "LeftEye", "SideBySide", "None" };

    private void TickUiProbe()
    {
        float now = Time.unscaledTime;
        if (now >= nextCanvasProbe)
        {
            nextCanvasProbe = now + 1f;
            ProbeCanvases();
        }

        if (started && !mirrorLogged)
        {
            mirrorLogged = true;
            LoggerInstance.Msg("SPIEGEL Ausgang: " + MirrorState());
        }
        if (!started) { mirrorLogged = false; mirrorManual = false; mirrorApplied = int.MinValue; }
        if (started && !mirrorManual && now >= nextMirrorDrive) { nextMirrorDrive = now + 0.5f; DriveMirror(); }

        if (mirrorReadbackAt > 0f && now >= mirrorReadbackAt)
        {
            mirrorReadbackAt = -1f;
            LoggerInstance.Msg($"SPIEGEL nach 1 s ({mirrorReadbackWhat}): " + MirrorState());
        }

        if (KeyPressed(VK_F1, ref f1WasDown, "F1"))
        {
            try
            {
                int i = Array.IndexOf(MirrorModes, XRSettings.gameViewRenderMode);
                var next = MirrorModes[(i + 1) % MirrorModes.Length];
                XRSettings.gameViewRenderMode = next;
                mirrorManual = true;
                mirrorReadbackWhat = "F1 gameViewRenderMode=" + next;
                LoggerInstance.Msg($"SPIEGEL F1: gameViewRenderMode -> {next} | " + MirrorState());
            }
            catch (Exception e) { LoggerInstance.Warning("SPIEGEL F1: " + e.GetType().Name + ": " + e.Message); }
            mirrorReadbackAt = now + 1f;
        }

        // F2 gehoert seit 1.13.0 dem Himmel (SkyFix.cs); die Blit-Messung ist erledigt.
    }

    // Abgleich statt Einmal-Schreiben (siehe Kopf).
    private void DriveMirror()
    {
        try
        {
            var d = Display();
            if (d == null || !d.running) return;
            string w = (prefMirror.Value ?? "left").Trim().ToLowerInvariant();
            int want = w switch { "off" => -6, "right" => -2, "both" => -3, _ => -1 };
            int have = d.GetPreferredMirrorBlitMode();
            if (have == want) { if (mirrorApplied != want) { mirrorApplied = want; LoggerInstance.Msg($"SPIEGEL DesktopMirror={w}: steht ({want}) | " + MirrorState()); } return; }
            d.SetPreferredMirrorBlitMode(want);
            LoggerInstance.Msg($"SPIEGEL DesktopMirror={w}: {have} -> {want} geschrieben | " + MirrorState());
        }
        catch (Exception e) { LoggerInstance.Warning("SPIEGEL DesktopMirror: " + e.GetType().Name + ": " + e.Message); mirrorManual = true; }
    }

    private static XRDisplaySubsystem? Display()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        return loader == null ? null : loader.GetLoadedSubsystem<XRDisplaySubsystem>();
    }

    private static string MirrorState()
    {
        string s;
        try { s = $"gameViewRenderMode={XRSettings.gameViewRenderMode} showDeviceView={XRSettings.showDeviceView}"; }
        catch (Exception e) { s = "XRSettings Lesefehler " + e.GetType().Name; }
        try
        {
            var d = Display();
            s += d == null ? " | kein XRDisplaySubsystem" : $" | PreferredMirrorBlitMode={d.GetPreferredMirrorBlitMode()} running={d.running}";
        }
        catch (Exception e) { s += " | Display Lesefehler " + e.GetType().Name; }
        return s;
    }

    // Aktive Wurzel-Canvases. Die Signatur enthaelt nur, was ein Menue
    // umschaltet (Name, aktiv, Modus, Kamera); Werte wie scaleFactor stehen
    // nur in der vollen Liste, damit sie nicht jede Sekunde neu listen.
    private void ProbeCanvases()
    {
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
            var roots = new List<Canvas>();
            var sig = new System.Text.StringBuilder();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || !c.isRootCanvas) continue;
                roots.Add(c);
            }
            roots.Sort((a, b) => a.sortingOrder != b.sortingOrder ? a.sortingOrder.CompareTo(b.sortingOrder) : string.CompareOrdinal(a.name, b.name));
            foreach (var c in roots)
            {
                var cam = c.worldCamera;
                sig.Append($"{c.name}|{c.enabled}|{c.renderMode}|{(cam == null ? "-" : cam.name)};");
            }
            string signature = sig.ToString();
            if (signature == lastCanvasSignature) return;
            lastCanvasSignature = signature;
            TouchUiDepth(signature);   // Menue auf: neue Graphics (VrUi.cs)

            LoggerInstance.Msg($"CANVAS: {roots.Count} aktive Wurzel-Canvases, XR {(started ? "an" : "aus")}, Szene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'");
            foreach (var c in roots) LoggerInstance.Msg("  " + DescribeCanvas(c));
            ReportCameras("CANVAS-Wechsel");
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("CANVAS: Lesefehler " + e.GetType().Name + ": " + e.Message);
            lastCanvasSignature = "";
        }
    }

    private static string DescribeCanvas(Canvas c)
    {
        try
        {
            var go = c.gameObject;
            var t = c.transform;
            string path = go.name;
            var p = t.parent;
            for (int i = 0; i < 4 && p != null; i++, p = p.parent) path = p.name + "/" + path;
            var cam = c.worldCamera;
            string camText = cam == null ? "keine"
                : $"'{cam.name}' enabled={cam.enabled} aktiv={cam.gameObject.activeInHierarchy} stereo={cam.stereoEnabled} eye={cam.stereoTargetEye} depth={cam.depth} mask=0x{cam.cullingMask:X8} ziel={(cam.targetTexture == null ? "Bildschirm" : "RT")}";
            var grp = go.GetComponent<CanvasGroup>();
            var r = c.pixelRect;
            int children = 0;
            try { children = go.GetComponentsInChildren<Canvas>(true).Length - 1; } catch { }
            return $"[{go.scene.name}] {path} | enabled={c.enabled} mode={c.renderMode} kamera {camText} | planeDistance={c.planeDistance:F2} " +
                   $"sortingOrder={c.sortingOrder} sortingLayer='{c.sortingLayerName}' overrideSorting={c.overrideSorting} layer={go.layer}({LayerMask.LayerToName(go.layer)}) " +
                   $"targetDisplay={c.targetDisplay} scaleFactor={c.scaleFactor:F3} pixelRect={r.width:F0}x{r.height:F0} " +
                   $"pos={t.position.ToString("F3")} scale={t.lossyScale.ToString("F4")} | CanvasGroup {(grp == null ? "keine" : $"alpha={grp.alpha:F2}")} | Unter-Canvases {children}";
        }
        catch (Exception e) { return c.name + ": Lesefehler " + e.GetType().Name + ": " + e.Message; }
    }
}
