// WetReality.XRStart - erste Laufzeitmessung fuer PWS1 (Handbuch 1.5).
//
// Frage: Bringt der im Spiel eingetragene OpenXRLoader eine Session, wenn man
// ihn nur aufruft? PWS1 hat die ganze managed XR-Schicht einkompiliert und
// OpenXRLoader in XRManagerSettings eingetragen, startet ihn aber nie
// (InitManagerOnStart = false, BootingState.get_IsXR = konstant false).
//
// Die Mod tut genau eines: F8 startet den Loader (InitializeLoaderSync, dann
// StartSubsystems), F8 erneut stoppt ihn. Kein Autostart - scheitert der Start,
// bleibt das flache Spiel spielbar. Kamera, Pistole und Eingabe bleiben
// unberuehrt. Jeder Schritt meldet Erfolg UND Misserfolg; danach 10 s lang
// einmal pro Sekunde der Zustand, weil sich eine Session erst aufbaut.
//
// Die Taste kommt ueber GetAsyncKeyState, nicht UnityEngine.Input: das Spiel
// nutzt das neue Input System, ob der alte Eingabeweg aktiv ist, ist
// ungemessen.
//
// 0.1.0 hat gezeigt: die Session laeuft bis FOCUSED, das Headset bleibt
// schwarz, Camera.main ist im Hauptmenue null (Handbuch 2). 0.2.0 aendert
// nichts am Spiel und misst nur zweierlei:
//   - alle Szenenkameras (auch inaktive): Pfad, aktiv, Tag, Zielauge,
//     RenderTexture, stereoEnabled, depth, URP-renderType, Stapel. Voll
//     gelistet beim Start und immer dann, wenn sich die Liste aendert -
//     ein Zustand, der sich aufbauen kann, wird nicht einmalig abgegriffen.
//   - "[XR] SetOutput Failed." pro Frame, gezaehlt im mitgelesenen
//     Player.log, damit sich zeigt, ob die Meldung im Frametakt kommt.

using System.Runtime.InteropServices;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

[assembly: MelonInfo(typeof(WetReality.XRStart.XRStart), "Wet Reality XRStart", "0.2.0", "Tino")]
[assembly: MelonGame("FuturLab", "PowerWash Simulator")]

namespace WetReality.XRStart;

public sealed class XRStart : MelonMod
{
    private const int VK_F8 = 0x77;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private bool keyWasDown;
    private bool started;
    private float reportUntil;
    private float nextReport;

    private const string SetOutputMarker = "SetOutput Failed";
    private long logOffset = -1;
    private int lastFrame;
    private string lastCameraSignature = "";

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("bereit - F8 startet den OpenXR-Loader des Spiels, F8 erneut stoppt ihn");
        CountSetOutput();   // setzt den Offset; Meldungen vor dem Laden zaehlen nicht
        lastFrame = Time.frameCount;
    }

    public override void OnUpdate()
    {
        bool down = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
        bool pressed = down && !keyWasDown && Application.isFocused;
        keyWasDown = down;

        if (pressed)
        {
            if (started) Stop("F8");
            else Start();
        }

        if (Time.unscaledTime < reportUntil && Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + 1f;
            Report("status");
        }
    }

    public override void OnApplicationQuit()
    {
        if (started) Stop("beenden");
    }

    private void Start()
    {
        var manager = DescribeManager("vor dem Start");
        if (manager == null) return;
        LoggerInstance.Msg("vor dem Start: " + SetOutputRate());
        string pipeline;
        try
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            pipeline = rp == null ? "null" : $"'{rp.name}' ({rp.GetIl2CppType().FullName})";
        }
        catch (Exception e)
        {
            pipeline = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }
        LoggerInstance.Msg("vor dem Start: currentRenderPipeline=" + pipeline);
        lastCameraSignature = "";
        ReportCameras("vor dem Start");

        try
        {
            manager.InitializeLoaderSync();
            LoggerInstance.Msg("InitializeLoaderSync: zurueckgekehrt");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("InitializeLoaderSync: Ausnahme " + e);
            return;
        }

        var loader = manager.activeLoader;
        LoggerInstance.Msg($"nach InitializeLoaderSync: activeLoader={(loader == null ? "null" : loader.name)}, isInitializationComplete={manager.isInitializationComplete}");
        if (loader == null)
        {
            LoggerInstance.Error("kein aktiver Loader - Runtime nicht erreichbar oder Loader verweigert (Player.log pruefen)");
            return;
        }

        try
        {
            manager.StartSubsystems();
            LoggerInstance.Msg("StartSubsystems: zurueckgekehrt");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("StartSubsystems: Ausnahme " + e);
        }

        started = true;
        Report("direkt nach dem Start");
        reportUntil = Time.unscaledTime + 10f;
        nextReport = Time.unscaledTime + 1f;
    }

    private void Stop(string why)
    {
        var manager = DescribeManager("vor dem Stopp (" + why + ")");
        started = false;
        reportUntil = 0f;
        if (manager == null) return;
        try
        {
            manager.StopSubsystems();
            LoggerInstance.Msg("StopSubsystems: zurueckgekehrt");
            manager.DeinitializeLoader();
            LoggerInstance.Msg($"DeinitializeLoader: zurueckgekehrt, activeLoader={(manager.activeLoader == null ? "null" : manager.activeLoader.name)}");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("Stopp: Ausnahme " + e);
        }
    }

    private XRManagerSettings? DescribeManager(string when)
    {
        var settings = XRGeneralSettings.Instance;
        if (settings == null)
        {
            LoggerInstance.Error(when + ": XRGeneralSettings.Instance ist null");
            return null;
        }
        var manager = settings.Manager;
        if (manager == null)
        {
            LoggerInstance.Error(when + ": XRGeneralSettings.Manager ist null");
            return null;
        }

        // loaders, nicht activeLoaders: activeLoaders ist IReadOnlyList, im
        // Interop ein Interface-Wrapper - Aufrufe darauf haben bei PWS2 den
        // Prozess getoetet (PWS2-Handbuch §10). loaders ist eine konkrete List.
        var names = new List<string>();
        var loaders = manager.loaders;
        if (loaders != null)
            for (int i = 0; i < loaders.Count; i++)
                names.Add(loaders[i] == null ? "null" : loaders[i].name);

        LoggerInstance.Msg($"{when}: settings='{settings.name}', manager='{manager.name}', InitManagerOnStart={settings.InitManagerOnStart}, " +
            $"loaders=[{string.Join(", ", names)}], activeLoader={(manager.activeLoader == null ? "null" : manager.activeLoader.name)}, " +
            $"isInitializationComplete={manager.isInitializationComplete}");
        return manager;
    }

    private void Report(string when)
    {
        string display;
        try
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            var d = loader == null ? null : loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            display = d == null ? "keins" : $"running={d.running}";
        }
        catch (Exception e)
        {
            display = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }

        string runtime;
        try
        {
            runtime = $"'{OpenXRRuntime.name}' {OpenXRRuntime.version}, API {OpenXRRuntime.apiVersion}";
        }
        catch (Exception e)
        {
            runtime = "Lesefehler " + e.GetType().Name + ": " + e.Message;
        }

        var cam = Camera.main;
        string camera = cam == null ? "Camera.main=null" : $"Camera.main='{cam.name}' stereoEnabled={cam.stereoEnabled}";

        LoggerInstance.Msg($"{when}: XRSettings.enabled={XRSettings.enabled}, isDeviceActive={XRSettings.isDeviceActive}, " +
            $"device='{XRSettings.loadedDeviceName}', eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
            $"display {display}, runtime {runtime}, {camera}, {SetOutputRate()}");
        ReportCameras(when);
    }

    // "N SetOutput in F Frames" seit dem letzten Aufruf.
    private string SetOutputRate()
    {
        int frames = Time.frameCount - lastFrame;
        lastFrame = Time.frameCount;
        int n = CountSetOutput();
        return n < 0 ? $"SetOutput: Player.log unlesbar, {frames} Frames" : $"SetOutput {n} in {frames} Frames";
    }

    // Liest Player.log ab dem letzten Offset. -1 = Lesefehler, nicht "keine
    // Meldung". Ein Treffer, der genau ueber die Lesegrenze faellt, geht
    // verloren - fuer eine Rate ohne Belang.
    private int CountSetOutput()
    {
        try
        {
            string path = Application.consoleLogPath;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (logOffset < 0 || logOffset > fs.Length) logOffset = fs.Length;
            fs.Seek(logOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string text = reader.ReadToEnd();
            logOffset = fs.Length;
            int count = 0;
            for (int i = text.IndexOf(SetOutputMarker, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(SetOutputMarker, i + SetOutputMarker.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("Player.log: " + e.GetType().Name + ": " + e.Message);
            return -1;
        }
    }

    // Alle Kameras in geladenen Szenen, auch inaktive. Volle Liste nur, wenn
    // sie sich gegen den letzten Aufruf geaendert hat.
    private void ReportCameras(string when)
    {
        var lines = new List<string>();
        try
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                scenes.Add(SceneManager.GetSceneAt(i).name);
            lines.Add("Szenen: [" + string.Join(", ", scenes) + "], aktiv '" + SceneManager.GetActiveScene().name + "'");

            var all = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < all.Length; i++)
            {
                var cam = all[i];
                if (cam == null) continue;
                var go = cam.gameObject;
                var scene = go.scene;
                if (!scene.IsValid()) continue;   // Prefab-Assets, keine Szenenobjekte
                lines.Add("  " + DescribeCamera(cam, go, scene.name));
            }
        }
        catch (Exception e)
        {
            lines.Add("Kameraliste: Lesefehler " + e.GetType().Name + ": " + e.Message);
        }

        string signature = string.Join("\n", lines);
        if (signature == lastCameraSignature) return;
        lastCameraSignature = signature;
        LoggerInstance.Msg($"{when}: Kameras ({lines.Count - 1}):");
        foreach (var l in lines) LoggerInstance.Msg("  " + l);
    }

    private static string DescribeCamera(Camera cam, GameObject go, string sceneName)
    {
        var path = new StringBuilder(go.name);
        for (var t = go.transform.parent; t != null; t = t.parent)
            path.Insert(0, t.name + "/");

        var rt = cam.targetTexture;
        string target = rt == null ? "Bildschirm" : $"RT '{rt.name}' {rt.width}x{rt.height}";

        string urp;
        try
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) urp = "keine URP-Daten";
            else
            {
                var stack = data.renderType == CameraRenderType.Base ? data.cameraStack : null;
                urp = $"{data.renderType}, Stapel {(stack == null ? 0 : stack.Count)}";
            }
        }
        catch (Exception e)
        {
            urp = "URP Lesefehler " + e.GetType().Name;
        }

        return $"[{sceneName}] {path} | activeInHierarchy={go.activeInHierarchy} enabled={cam.enabled} tag={go.tag} " +
            $"eye={cam.stereoTargetEye} ziel={target} stereoEnabled={cam.stereoEnabled} depth={cam.depth} " +
            $"display={cam.targetDisplay} mask=0x{cam.cullingMask:X8} {urp}";
    }
}
