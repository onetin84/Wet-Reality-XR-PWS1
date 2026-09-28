// Testumgebung (XRStart 1.6.0) - nur fuer die Entwicklung, jeder Teil einzeln
// in UserData/MelonPreferences.cfg, Kategorie WetReality_XRStart, abschaltbar.
// Die cfg nur bei BEENDETEM Spiel aendern - MelonLoader schreibt sie beim
// Beenden neu.
//
// AutoStart: F8, F7, F5 von selbst. Seit 1.16.0 ohne feste Pausen: XR schon beim
//   Laden des Levels (oder sobald ein Spielercharakter da ist), Kopf sobald HMD
//   getrackt und Spielerkamera da, Pistole im Frame danach. Im Hauptmenue startet
//   nichts (keine 3D-Kamera, das Headset bliebe schwarz). Einmal pro Spielstart:
//   wer mit F8 stoppt, bleibt aus.
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
    private MelonPreferences_Entry<bool> prefComfortTeleport = null!;
    private MelonPreferences_Entry<float> prefTeleportJumpSpeed = null!;
    private MelonPreferences_Entry<bool> prefTeleportSlopeWalk = null!;
    private MelonPreferences_Entry<bool> prefLadderTeleport = null!;
    private MelonPreferences_Entry<float> prefLadderTopOffset = null!;
    private MelonPreferences_Entry<float> prefTurnSpeed = null!, prefHapticIntensity = null!, prefUiScale = null!, prefUiDistance = null!;
    private MelonPreferences_Entry<bool> prefSprayHaptics = null!;
    private MelonPreferences_Entry<string> prefHandRPos = null!, prefHandRRot = null!, prefHandLPos = null!, prefHandLRot = null!;

    private void InitDevTools()
    {
        var cat = MelonPreferences.CreateCategory("WetReality_XRStart");
        prefAutoStart = cat.CreateEntry("AutoStart", true, description: "F8/F7/F5 automatisch, sobald ein Level geladen ist");
        prefSkipContinue = cat.CreateEntry("SkipLoadingContinue", true, description: "Ladebildschirm (Steuerungshilfe) automatisch mit Weiter bestaetigen");
        // AUS in der Auslieferung (Nutzer 28.09.). Die Entwicklungs-cfg behaelt ihr
        // true - eine vorhandene cfg sieht geaenderte Vorgaben nie (MelonPreferences).
        prefCheats = cat.CreateEntry("DevCheats", false, description: "Guthaben auffuellen und Jobs freischalten - schreibt in den Spielstand. Nur fuer die Entwicklung; tools/savegame-switch.ps1 schaltet zurueck.");
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
        prefSkyFix = cat.CreateEntry("SkyFix", "cubemap", description: "Himmel im Headset: cubemap (Spielhimmel mit Wolken ueber eine Wuerfeltextur, Rueckfall solid), solid (feste Farbe), skybox (erzwingen - schmiert), off (wie das Spiel)");
        prefSkyColor = cat.CreateEntry("SkyColor", "0.55,0.72,0.92", description: "Himmelsfarbe fuer SkyFix=solid, r,g,b 0..1");
        InitSky();
        InitPointerStyle(cat);   // PointerColor & Teleportziel (PointerStyle.cs)
        InitMenuCamera(cat);     // Hauptmenue im Headset (MenuCamera.cs)
        InitSplash(cat);         // Startlogo im Headset (Splash.cs)
        InitComfort(cat);        // Snap-Turn, Vignette, Teleport-Blende (Comfort.cs)
        InitImmersion(cat);      // Menue halten = Spiel-UI aus/an (Immersion.cs)
        InitGestures(cat);       // Schulter/Pistole + Griff (Gestures.cs)
        InitSpawnYaw(cat);       // Spawn-Ausrichtung messen und uebernehmen (SpawnYaw.cs)
        InitHandSpray(cat);      // Strahl auf die freie Hand (HandSpray.cs)
        InitHandedness(cat);     // Pistolenhand rechts/links (Handedness.cs)
        InitHandTint(cat);       // orange Handschuhe (HandTint.cs)
        InitWashLaser(cat);      // Ziellaser (WashLaser.cs)
        InitGrip(cat);           // Griff-Feintuning der Pistole (Grip.cs)
        // Fuer den Konfigurator (tools/frontend), Namen wie PWS2; Vorgaben = die frueheren festen Werte.
        prefTurnSpeed = cat.CreateEntry("TurnSpeed", 90f, description: "Grad pro Sekunde fuer das Drehen mit dem rechten Stick");
        prefSprayHaptics = cat.CreateEntry("SprayHaptics", true, description: "Dauervibration rechts beim Spruehen (Staerke nach Duese, Washer, Oberflaeche)");
        prefHapticIntensity = cat.CreateEntry("HapticIntensity", 1f, description: "Faktor fuer alle Spruehvibrationen, 1 = Vorgabe");
        prefUiScale = cat.CreateEntry("UiScale", 0.3627f, description: "Groesse der Spiel-UI im Headset (Skalierung der Kindknoten)");
        prefUiDistance = cat.CreateEntry("UiDistance", 2f, description: "Meter. Abstand der Spiel-UI im Headset");
        // Namen wie PWS2 - fuer den spaeteren Konfigurator.
        prefComfortTeleport = cat.CreateEntry("ComfortTeleport", false, description: "Komfort: linker Stick nach vorn teleportiert aus der linken Hand und ersetzt das Gehen. Der Ziel-Teleport auf der Pistolenhand wirkt immer.");
        prefTeleportJumpSpeed = cat.CreateEntry("TeleportJumpSpeed", 7f, description: "m/s. Waagerechtes Tempo der Sprungparabel, die die Teleportweite begrenzt (PWS2 gemessen: 7,0 mit Sprint)");
        prefTeleportSlopeWalk = cat.CreateEntry("TeleportSlopeWalk", true, description: "Treppe/Rampe als Weg (PWS2 Abschnitt 159): liegt das Ziel ueber der Kantengrenze, wird der Boden vom Fuss dorthin in 0,18-m-Schritten abgegangen; jede Stufe <= 0,45 m hoch ist ein Weg.");
        prefLadderTeleport = cat.CreateEntry("LadderTeleport", true, description: "Auf eine aufgestellte Leiter gezielt = oben ankommen statt an der Sprosse (PWS2); die eine Ausnahme von der Hoehengrenze.");
        prefLadderTopOffset = cat.CreateEntry("LadderTopOffset", 0.35f, description: "Meter weg von der Leiter oben, damit man auf dem Dach steht und nicht an der Sprosse haengt.");
        LoggerInstance.Msg($"TESTUMGEBUNG: AutoStart={prefAutoStart.Value} SkipLoadingContinue={prefSkipContinue.Value} DevCheats={prefCheats.Value} DesktopMirror={prefMirror.Value} InteractionRange={prefInteractionRange.Value:F1} ShowVrHands={prefHands.Value} HandRight {prefHandRPos.Value} / {prefHandRRot.Value} HandLeft {prefHandLPos.Value} / {prefHandLRot.Value} SkyFix={prefSkyFix.Value} SkyColor={prefSkyColor.Value} ComfortTeleport={prefComfortTeleport.Value} TeleportJumpSpeed={prefTeleportJumpSpeed.Value:F1} TeleportSlopeWalk={prefTeleportSlopeWalk.Value} PointerColor={prefPointerColor.Value} PointerAlpha={prefPointerAlpha.Value:F2} TurnSpeed={prefTurnSpeed.Value:F0} SprayHaptics={prefSprayHaptics.Value} HapticIntensity={prefHapticIntensity.Value:F2} UiScale={prefUiScale.Value:F4} UiDistance={prefUiDistance.Value:F2}");
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

    private int autoStep;            // 0 warten, 1 XR laeuft, 2 Kopf an, 3 fertig
    private float autoDeadline, nextAutoProbe;
    private static float lastLoadingAt = -100f;

    // 1.16.0: ohne feste Pausen (vorher 1 s Takt + 1 + 2 + 2 s = bis ~6 s nach dem
    // Levelstart, Nutzer: "gefuehlt 5 s"). XR startet schon, sobald ein
    // Ladezustand laeuft (Headset beim Laden kurz schwarz), sonst sobald ein
    // Spielercharakter da ist; Kopf, sobald HMD getrackt UND Spielerkamera da,
    // je Frame geprueft; Pistole im Frame danach.
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
                    bool loading = now - lastLoadingAt < 1f;
                    bool mainMenu = false;
                    if (!loading)
                    {
                        if (now < nextAutoProbe) return;
                        nextAutoProbe = now + 0.1f;
                        // 1.22.0: auch im Hauptmenue - dort zeigt es die Menuekamera (MenuCamera.cs).
                        mainMenu = prefMenuCamera.Value && InMainMenu();
                        if (!mainMenu && (UnityEngine.Object.FindObjectOfType<PlayerCameraController>() == null || Camera.main == null)) return;
                    }
                    LoggerInstance.Msg($"AUTOSTART: F8 ({(loading ? "Level laedt" : mainMenu ? "Hauptmenue" : "Spielercharakter da")})");
                    Start();
                    if (!started) { LoggerInstance.Warning("AUTOSTART: XR nicht gestartet - Rest abgebrochen, F8 von Hand"); autoStep = 3; return; }
                    autoStep = 1;
                    autoDeadline = now + 20f;
                    return;
                case 1:
                    if (!started) { autoStep = 3; return; }
                    if (!ReadHmdRotation().HasValue)
                    {
                        if (now > autoDeadline) { LoggerInstance.Warning("AUTOSTART: HMD nach 20 s nicht getrackt - F7/F5 von Hand"); autoStep = 3; }
                        return;
                    }
                    autoDeadline = now + 20f;   // HMD da - ab jetzt wird nur noch auf die Spielerkamera gewartet
                    if (Camera.main == null || now < nextAutoProbe) return;
                    nextAutoProbe = now + 0.1f;
                    if (UnityEngine.Object.FindObjectOfType<PlayerCameraController>() == null) return;
                    StartHead("AUTOSTART F7");
                    autoStep = 2;
                    return;
                case 2:
                    if (!started) { autoStep = 3; return; }
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
    // Nur das Laden eines LEVELS startet XR vorab (AutoStart) - nicht der Weg ins Hauptmenue.
    private static void LoadingLocationUpdatePostfix(LoadingLocationState __instance) { lastLoadingAt = Time.unscaledTime; NoteLoading(__instance); }

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
