// Spawn-Ausrichtung (XRStart 1.28.0): Messung und Abhilfe in einem Lauf.
//
// BEFUND (Logs): seit 1.16.0 (Autostart vor dem Spawn) uebernimmt der
// Kopfschreiber bodyYaw 0,0; vorher je Level 140-350. Nutzer: nach dem Laden
// 180 Grad verdreht (Haus -> Strasse). 1.24.1 hing an UpdateLookDirection - im
// Lauf KEIN Aufruf. Der Spawn geht ueber PWS.MoveToSpawnPointAtBoot:
// MoveToSpawnPositionClientRpc(position, orientation) -> SetTransform ->
// SetOrientation(orientation) (0x939790, disassembliert: Transform-Icalls auf
// m_orientationTransform +0x58). VERMUTUNG: das ist der HeadTurn-Knoten oder
// liegt darueber - und der Kopfschreiber ueberschreibt ihn im naechsten Frame.
//
// MESSUNG: je Aufruf eine Zeile - Methode, orientation, Pfad und Welt-Gierung
// von m_orientationTransform, ob es HeadTurn ist oder dessen Vorfahr.
// ABHILFE: DriveHead setzt beim naechsten Frame bodyYaw so, dass HeadTurn in
// der Welt nach "orientation" zeigt: bodyYaw = orientation - Eltern-Gierung -
// HMD-Gierung. Auch wenn der Spawn VOR der Uebernahme kommt, wird er bei der
// Uebernahme verrechnet. cfg SpawnYawFix (an).

using HarmonyLib;
using Il2CppPWS;
using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefSpawnYawFix = null!;
    private static bool spawnYawPending;
    private static float spawnYaw;
    private static readonly List<string> spawnLog = new();

    private void InitSpawnYaw(MelonPreferences_Category cat)
    {
        prefSpawnYawFix = cat.CreateEntry("SpawnYawFix", true, description: "Nach dem Laden in die vom Spiel vorgesehene Richtung schauen (Spawn-Ausrichtung uebernehmen)");
        foreach (var m in new[] { "SetOrientation", "SetTransform", "SetOrientationClientRpc", "MoveToSpawnPositionClientRpc" })
        {
            try
            {
                var target = AccessTools.Method(typeof(MoveToSpawnPointAtBoot), m);
                if (target == null) { LoggerInstance.Warning($"SPAWN: {m} nicht gefunden"); continue; }
                HarmonyInstance.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(XRStart), nameof(SpawnPostfix))));
                LoggerInstance.Msg($"SPAWN: Patch {m} installiert");
            }
            catch (Exception e) { LoggerInstance.Warning($"SPAWN: Patch {m} " + e.GetType().Name + ": " + e.Message); }
        }
    }

    // Harmony: __originalMethod nennt die Methode, __args die Argumente.
    private static void SpawnPostfix(MoveToSpawnPointAtBoot __instance, System.Reflection.MethodBase __originalMethod, object[] __args)
    {
        try
        {
            float orientation = float.NaN;
            foreach (var a in __args) if (a is float f) orientation = f;
            string target = "-";
            float targetYaw = float.NaN;
            try
            {
                var t = __instance.m_orientationTransform;
                if (t != null) { target = PathOfStatic(t); targetYaw = t.eulerAngles.y; }
            }
            catch { }
            spawnLog.Add($"SPAWN: {__originalMethod.Name}(orientation {orientation:F1}) - m_orientationTransform '{target}' Welt-Gierung {targetYaw:F1}, trackBody {trackBody}");
            if (!float.IsNaN(orientation)) { spawnYaw = orientation; spawnYawPending = true; }
        }
        catch (Exception e) { spawnLog.Add("SPAWN: Postfix " + e.GetType().Name); }
    }

    private static string PathOfStatic(Transform t)
    {
        var s = t.name;
        var p = t.parent;
        for (int i = 0; i < 6 && p != null; i++, p = p.parent) s = p.name + "/" + s;
        return s;
    }

    // Aus OnUpdate: die gesammelten Zeilen ins Log (der Postfix ist statisch).
    private void FlushSpawnLog()
    {
        if (spawnLog.Count == 0) return;
        foreach (var l in spawnLog) LoggerInstance.Msg(l);
        spawnLog.Clear();
    }

    // Aus DriveHead, nach der Uebernahme: Spawn-Ausrichtung verrechnen.
    private void ApplySpawnYaw(Transform headTurn, float hmdYaw)
    {
        if (!spawnYawPending || !prefSpawnYawFix.Value) return;
        spawnYawPending = false;
        float parentYaw = headTurn.parent == null ? 0f : headTurn.parent.eulerAngles.y;
        float before = bodyYaw;
        bodyYaw = Mathf.Repeat(spawnYaw - parentYaw - hmdYaw, 360f);
        haveRead = false;
        LoggerInstance.Msg($"SPAWN: Ausrichtung {spawnYaw:F1} uebernommen - HeadTurn-Eltern '{(headTurn.parent == null ? "-" : headTurn.parent.name)}' Gierung {parentYaw:F1}, HMD {hmdYaw:F1}, bodyYaw {before:F1} -> {bodyYaw:F1}");
    }
}
