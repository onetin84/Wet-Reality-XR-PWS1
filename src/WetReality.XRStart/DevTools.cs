// Testumgebung (XRStart 1.6.0) - nur fuer die Entwicklung, jeder Teil einzeln
// in UserData/MelonPreferences.cfg, Kategorie WetReality_XRStart, abschaltbar.
// Die cfg nur bei BEENDETEM Spiel aendern - MelonLoader schreibt sie beim
// Beenden neu.
//
// AutoStart: F8, F7, F5 von selbst, sobald ein Spielercharakter da ist (im
//   Hauptmenue gibt es keine 3D-Kamera, das Headset bliebe schwarz). Kopf erst,
//   wenn das HMD getrackt ist; die Abstaende wie im Lauf 12:17 von Hand (F8,
//   +2 s F7, +2 s F5). Einmal pro Spielstart: wer mit F8 stoppt, bleibt aus.
//
// SkipLoadingContinue: der Ladebildschirm (LoadingScreen mit Steuerungshilfe
//   und Weiter-Knopf) wartet nach dem Laden auf "Weiter". Ist
//   LoadingStateBase.WaitingForContinue gesetzt, ruft die Mod im naechsten
//   OnUpdate OnContinueClicked - derselbe Weg wie der Knopf, nicht aus dem
//   Update des Zustands heraus.
//
// DevCheats - SCHREIBT IN DEN SPIELSTAND. Original vorher gesichert:
//   savegame-backup/, zurueck mit tools/savegame-switch.ps1.
//   Guthaben: faellt es unter 1 Mio., auf 10 Mio. aufgefuellt
//     (PlayerSaveData.AddCredits) - das Spiel speichert es mit.
//   Sterne (1.6.4): die Sterne selbst bleiben UNBERUEHRT - jede Aenderung
//     spielt einen eigenen Ping (1.6.1-1.6.3: Auffuellen per AddStars). Die
//     Shop-Sperre ist GridElementShopBase<T>.StarsRemaining() =
//     max(0, RequiredStars - MainCampaign.m_stars) (disassembliert); der
//     Kaufknopf verlangt == 0, BuyEquipmentContent prueft keine Sterne. Ein
//     Postfix setzt StarsRemaining auf 0. Die Methode ist geteilter Code
//     aller Referenz-Instanzen (<object>) - der Patch gilt fuer alle Shops.
//     Die von 1.6.1-1.6.3 gespeicherten 5000 rechnet RecalculateStars einmal
//     auf den echten Wert zurueck.
//   Jobs: Status "Locked" wird beim Lesen zu "Unlocked" (Postfixe auf
//     CampaignSaveData.Get*Status, IsFreePlayUnlocked). Nur zur Laufzeit; ins
//     Spiel geht erst, was man tatsaechlich spielt. Ob die Postfixe feuern,
//     zeigen die Zaehler (IL2CPP schmilzt kleine Methoden ein).

using HarmonyLib;
using Il2CppPWS;
using Il2CppPWS.States;
using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefAutoStart = null!, prefSkipContinue = null!, prefCheats = null!;
    private MelonPreferences_Entry<string> prefMirror = null!;
    private MelonPreferences_Entry<float> prefInteractionRange = null!;
    private MelonPreferences_Entry<bool> prefHands = null!;
    private MelonPreferences_Entry<string> prefSkyFix = null!, prefSkyColor = null!;
    private MelonPreferences_Entry<string> prefHandRPos = null!, prefHandRRot = null!, prefHandLPos = null!, prefHandLRot = null!;

    private void InitDevTools()
    {
        var cat = MelonPreferences.CreateCategory("WetReality_XRStart");
        prefAutoStart = cat.CreateEntry("AutoStart", true, description: "F8/F7/F5 automatisch, sobald ein Level geladen ist");
        prefSkipContinue = cat.CreateEntry("SkipLoadingContinue", true, description: "Ladebildschirm (Steuerungshilfe) automatisch mit Weiter bestaetigen");
        prefCheats = cat.CreateEntry("DevCheats", true, description: "Guthaben auffuellen und Jobs freischalten - schreibt in den Spielstand");
        prefMirror = cat.CreateEntry("DesktopMirror", "left", description: "Headsetbild auf dem Monitor: left, right, both oder off");
        prefInteractionRange = cat.CreateEntry("InteractionRange", 5f, description: "Meter ab der linken Hand, in denen Objekte zum Aufnehmen gefunden werden (PWS2: 5)");
        prefHands = cat.CreateEntry("ShowVrHands", true, description: "VR-Haende des Spiels an den Controllern, Spielarme ausgeblendet");
        prefHandRPos = cat.CreateEntry("HandRightPos", "0.03,0.03,-0.14", description: "Pistolenhand Versatz in Metern (x rechts, y oben, z vorn), in der Handdrehung");
        prefHandRRot = cat.CreateEntry("HandRightRot", "-15,0,-80", description: "Pistolenhand Drehung in Grad (x, y, z)");
        prefHandLPos = cat.CreateEntry("HandLeftPos", "-0.04,0,-0.08", description: "Freie Hand Versatz in Metern (x rechts, y oben, z vorn)");
        prefHandLRot = cat.CreateEntry("HandLeftRot", "70,20,90", description: "Freie Hand Drehung in Grad (x, y, z)");
        handRPos = ParseVec(prefHandRPos.Value, new Vector3(0.03f, 0.03f, -0.14f), "HandRightPos");
        handRRot = ParseVec(prefHandRRot.Value, new Vector3(-15f, 0f, -80f), "HandRightRot");
        handLPos = ParseVec(prefHandLPos.Value, new Vector3(-0.04f, 0f, -0.08f), "HandLeftPos");
        handLRot = ParseVec(prefHandLRot.Value, new Vector3(70f, 20f, 90f), "HandLeftRot");
        prefSkyFix = cat.CreateEntry("SkyFix", "solid", description: "Himmel im Headset: solid (feste Farbe, keine Schlieren), skybox (erzwingen), off (wie das Spiel)");
        prefSkyColor = cat.CreateEntry("SkyColor", "0.55,0.72,0.92", description: "Himmelsfarbe fuer SkyFix=solid, r,g,b 0..1");
        InitSky();
        LoggerInstance.Msg($"TESTUMGEBUNG: AutoStart={prefAutoStart.Value} SkipLoadingContinue={prefSkipContinue.Value} DevCheats={prefCheats.Value} DesktopMirror={prefMirror.Value} InteractionRange={prefInteractionRange.Value:F1} ShowVrHands={prefHands.Value} HandRight {prefHandRPos.Value} / {prefHandRRot.Value} HandLeft {prefHandLPos.Value} / {prefHandLRot.Value} SkyFix={prefSkyFix.Value} SkyColor={prefSkyColor.Value}");
        cheatsOn = prefCheats.Value;
        skipContinueOn = prefSkipContinue.Value;

        PatchPostfix(typeof(LoadingStateBase), "Update", nameof(LoadingUpdatePostfix));
        PatchPostfix(typeof(LoadingLocationState), "Update", nameof(LoadingLocationUpdatePostfix));
        if (cheatsOn)
        {
            foreach (var m in new[] { "GetCareerJobStatus", "GetFreePlayStatus", "GetSpecialJobStatus", "GetChallengeStatus", "GetLevelStatus" })
                PatchPostfix(typeof(CampaignSaveData), m, nameof(JobStatusPostfix));
            PatchPostfix(typeof(CampaignSaveData), "IsFreePlayUnlocked", nameof(FreePlayUnlockedPostfix));
            PatchPostfix(typeof(GridElementShopBase<PowerWasherData>), "StarsRemaining", nameof(StarsRemainingPostfix));
        }
    }

    private void PatchPostfix(Type type, string method, string postfix)
    {
        try
        {
            var target = AccessTools.Method(type, method);
            if (target == null) { LoggerInstance.Error($"Patch {type.Name}.{method}: Methode nicht gefunden"); return; }
            HarmonyInstance.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(XRStart), postfix)));
            LoggerInstance.Msg($"Patch {type.Name}.{method}: installiert (ob er feuert, zeigt der Zaehler)");
        }
        catch (Exception e)
        {
            LoggerInstance.Error($"Patch {type.Name}.{method}: " + e);
        }
    }

    private void TickDevTools()
    {
        TickAutoStart();
        TickLoadingContinue();
        TickCheats();
    }

    // ---------------------------------------------------------------- AutoStart

    private int autoStep;            // 0 warten auf Level, 1 XR laeuft, 2 Kopf an, 3 fertig
    private float autoAt, autoDeadline, nextAutoProbe;

    private void TickAutoStart()
    {
        if (autoStep >= 3 || !prefAutoStart.Value) return;
        float now = Time.unscaledTime;
        try
        {
            switch (autoStep)
            {
                case 0:
                    if (started) { autoStep = 3; return; }   // von Hand gestartet
                    if (now < nextAutoProbe) return;
                    nextAutoProbe = now + 1f;
                    if (autoAt <= 0f)
                    {
                        if (UnityEngine.Object.FindObjectOfType<PlayerCameraController>() == null || Camera.main == null) return;
                        autoAt = now + 1f;
                        LoggerInstance.Msg("AUTOSTART: Spielercharakter da - XR in 1 s");
                        return;
                    }
                    if (now < autoAt) return;
                    LoggerInstance.Msg("AUTOSTART: F8");
                    Start();
                    if (!started) { LoggerInstance.Warning("AUTOSTART: XR nicht gestartet - Rest abgebrochen, F8 von Hand"); autoStep = 3; return; }
                    autoStep = 1;
                    autoAt = now + 2f;
                    autoDeadline = now + 20f;
                    return;
                case 1:
                    if (!started) { autoStep = 3; return; }
                    if (now < autoAt) return;
                    if (!ReadHmdRotation().HasValue)
                    {
                        if (now > autoDeadline) { LoggerInstance.Warning("AUTOSTART: HMD nach 20 s nicht getrackt - F7/F5 von Hand"); autoStep = 3; }
                        return;
                    }
                    StartHead("AUTOSTART F7");
                    autoStep = 2;
                    autoAt = now + 2f;
                    return;
                case 2:
                    if (!started) { autoStep = 3; return; }
                    if (now < autoAt) return;
                    if (!writeGun) { LoggerInstance.Msg("AUTOSTART: F5"); ToggleGun(); }
                    autoStep = 3;
                    return;
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("AUTOSTART: " + e.GetType().Name + ": " + e.Message + " - Rest von Hand");
            autoStep = 3;
        }
    }

    // --------------------------------------------------------- Ladebildschirm

    private static bool skipContinueOn;
    private static LoadingStateBase? pendingContinue;
    private static IntPtr continuedFor = IntPtr.Zero;
    private static int loadingUpdates;

    private static void LoadingUpdatePostfix(LoadingStateBase __instance) => NoteLoading(__instance);
    private static void LoadingLocationUpdatePostfix(LoadingLocationState __instance) => NoteLoading(__instance);

    private static void NoteLoading(LoadingStateBase s)
    {
        loadingUpdates++;
        if (!skipContinueOn || pendingContinue != null) return;
        try
        {
            if (s.Pointer == continuedFor || !s.WaitingForContinue) return;
            pendingContinue = s;
        }
        catch { }
    }

    private void TickLoadingContinue()
    {
        var s = pendingContinue;
        if (s == null) return;
        pendingContinue = null;
        try
        {
            continuedFor = s.Pointer;
            string name = s.GetIl2CppType().Name;
            s.OnContinueClicked();
            LoggerInstance.Msg($"LADEBILDSCHIRM: {name} wartete auf Weiter - automatisch bestaetigt ({loadingUpdates} Updates im Ladezustand)");
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("LADEBILDSCHIRM: Weiter fehlgeschlagen - " + e.GetType().Name + ": " + e.Message);
        }
        loadingUpdates = 0;
    }

    // ------------------------------------------------------------------ Cheats

    private static bool cheatsOn;
    private static int statusCalls, statusUnlocked, freePlayCalls, freePlayUnlocked;
    private int statusCallsLogged = -1;
    private float nextCheat, nextCheatLog;
    private SaveManager? saveManager;
    private const float CreditsFloor = 1_000_000f, CreditsFill = 10_000_000f;
    private const ushort StarsFill = 5000;   // Fuellwert von 1.6.1-1.6.3, nur noch zum Zuruecksetzen
    private static int starsRemainingCalls, starsRemainingZeroed;
    private bool starsReset;

    private static void StarsRemainingPostfix(ref int __result)
    {
        starsRemainingCalls++;
        if (!cheatsOn || __result <= 0) return;
        __result = 0;
        starsRemainingZeroed++;
    }

    private static void JobStatusPostfix(ref GameJobStatus __result)
    {
        statusCalls++;
        if (!cheatsOn || __result != GameJobStatus.Locked) return;
        __result = GameJobStatus.Unlocked;
        statusUnlocked++;
    }

    private static void FreePlayUnlockedPostfix(ref bool __result)
    {
        freePlayCalls++;
        if (!cheatsOn || __result) return;
        __result = true;
        freePlayUnlocked++;
    }

    private void TickCheats()
    {
        if (!cheatsOn) return;
        float now = Time.unscaledTime;
        if (now < nextCheat) return;
        nextCheat = now + 2f;
        try
        {
            if (saveManager == null) saveManager = UnityEngine.Object.FindObjectOfType<SaveManager>();
            var ps = saveManager == null ? null : saveManager.PlayerSave;
            if (ps != null)
            {
                float before = ps.Credits;
                if (before < CreditsFloor)
                {
                    ps.AddCredits(CreditsFill - before);
                    LoggerInstance.Msg($"CHEAT: Guthaben {before:F0} -> {ps.Credits:F0} (Slot {ps.SlotIndex})");
                }
                if (!starsReset)
                {
                    var list = ps.m_campaigns;
                    int n = list == null ? 0 : list.Count;
                    starsReset = n > 0;
                    for (int i = 0; i < n; i++)
                    {
                        var c = list![i];
                        if (c == null || c.Stars < StarsFill) continue;
                        ushort had = c.Stars;
                        c.RecalculateStars();
                        LoggerInstance.Msg($"CHEAT: Sterne der Kampagne {c.CampaignUniqueId} zurueckgerechnet {had} -> {c.Stars} (Fuellwert von 1.6.1-1.6.3)");
                    }
                }
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("CHEAT: Guthaben - " + e.GetType().Name + ": " + e.Message);
            saveManager = null;
        }
        if (statusCalls + freePlayCalls + starsRemainingCalls != statusCallsLogged && now >= nextCheatLog)
        {
            nextCheatLog = now + 10f;
            statusCallsLogged = statusCalls + freePlayCalls + starsRemainingCalls;
            LoggerInstance.Msg($"CHEAT: Job-Status {statusCalls} Aufrufe, {statusUnlocked} Locked->Unlocked | IsFreePlayUnlocked {freePlayCalls} Aufrufe, {freePlayUnlocked} false->true | StarsRemaining {starsRemainingCalls} Aufrufe, {starsRemainingZeroed} auf 0");
        }
    }
}
