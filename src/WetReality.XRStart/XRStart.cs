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

using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

[assembly: MelonInfo(typeof(WetReality.XRStart.XRStart), "Wet Reality XRStart", "0.1.0", "Tino")]
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

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("bereit - F8 startet den OpenXR-Loader des Spiels, F8 erneut stoppt ihn");
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
            $"display {display}, runtime {runtime}, {camera}");
    }
}
