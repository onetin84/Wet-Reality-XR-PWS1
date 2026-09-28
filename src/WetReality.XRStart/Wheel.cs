// Waehlscheibe und Washer-Wechsel (XRStart 1.14.0).
//
// GEMESSEN (dump.cs, disassembliert):
//   - PWS1 hat KEINE Washer-Scheibe: RadialMenu fuehrt m_nozzleSelectionMenu,
//     m_extensionSelectionMenu, m_cleaningLiquidSelectionMenu. Geoeffnet und
//     geschlossen ueber BaseInput.m_openRadialMenu / m_closeRadialMenu
//     (Action<EquipmentType>) - PlayerInput.OpenHUDRadialMenu ordnet nur die
//     Rewired-Aktion einem EquipmentType zu (0x9EDE80).
//   - Gewaehlt wird in RadialMenu.UpdateInput(BaseInput): m_inputMode 0 Keyboard
//     (Maus), 1 ConsoleController, 2 VRController; GetOffsetJoystick liest
//     BaseInput.UiMovementRaw (+0x2C, 0x7C1A00).
//   - Taste E ist KEIN Washer-Wechsel, sondern das Inventar
//     (PlayerInput.ToggleInventoryInput: CheckAndCloseInventoryMenuIfOpen, sonst
//     oeffnen; PlayingState.ShowInventory). Den Washer-Wechsel gibt es trotzdem:
//     QuestPlayerInput.NextGun() (die Quest-Fassung des Spiels) ruft
//     BaseInput.SwitchGun(1) - in der PC-Fassung nur keiner Taste zugeordnet.
//
// BELEGUNG:
//   R3 halten   Duesenscheibe auf; rechter Stick waehlt; Loslassen = uebernehmen
//               (wie Taste halten + Maus). Waehrend offen: linker/rechter Griff =
//               vorige/naechste Scheibe (Duese, Verlaengerung, Reinigungsmittel).
//               Dauerspruehen, Schmutz-Hervorheben, Drehen, Duese-zurueck ruhen.
//   Y kurz      Inventar auf/zu (wie E) - Washer dort mit dem Zeigestrahl.
//   Y halten    naechster Washer direkt (SwitchGun +1, wie NextGun).
// 1.15.0, nach 1.14.0 Lauf 1 (19:35, Bild wheel-alignment-wrong.png): die Scheibe
//   erscheint, steht aber WELTFEST ausgerichtet, und nichts laesst sich waehlen
//   (UpdateInput feuert ~45/s). Der Konsolenzweig skaliert UiMovementRaw mit einem
//   Bildschirmwert - in VR erreicht das den Segmentabstand vermutlich nie.
//   RadialMenuCategoryBase<T>.Select(Vector3 offset) ist oeffentlich, dazu
//   GetSlotOffsetMagnitude() und IsOpen: ein POSTFIX waehlt jetzt selbst,
//   Richtung x Segmentabstand x 1,2 - aus dem ZEIGER (Nutzervorschlag) oder dem
//   rechten Stick.
//   ZEIGER-KETTE (Nutzer): beim Oeffnen ist der Strahlpunkt die Mitte (keine
//   Auswahl). Ueberschreitet der Strahl WheelPointerRadius, gilt die Richtung;
//   nach 0,15 s wird uebernommen und die NAECHSTE Scheibe geoeffnet, deren Mitte
//   wieder der aktuelle Strahlpunkt ist. Nach der dritten schliesst sie. Griffe
//   ueberspringen ohne Wahl; R3 loslassen bricht ab (uebernimmt, was steht).
//   AUSRICHTUNG: solange offen, sitzt die Scheibe in der Mitte der UIRoot-Ebene
//   und zum Betrachter gedreht (Lage vorher gemerkt, beim Schliessen zurueck).
//   Ihre Canvas wird einmal gemessen (WAEHLSCHEIBE: Canvas).
//
// 1.15.1, nach 1.15.0 Lauf 1 (19:46): die Kette laeuft, aber (a) Auswahl viel zu
//   frueh - feste 12 cm, der sichtbare Ring ist groesser: drei Scheiben in 2 s;
//   (b) die naechste Scheibe steht am selben Ort, der Zeiger zeigt schon auf ein
//   Segment, zurueck zur Mitte bestaetigt ungewollt; (c) weiter weltfest.
//   Gemessen: RadialMenu haengt UNTER UIRoot (.../Gameplay(Clone)/RadialMenu),
//   Position/Drehung = UIRoot - das Objekt steht richtig, weltfest ist ein
//   KINDKNOTEN (Kandidat m_offsetFeedback, der Kippeffekt zur Maus).
//   Jetzt: (1) jede Scheibe oeffnet mit der MITTE AM STRAHLPUNKT (Nutzervorschlag),
//   (2) gewaehlt erst auf dem sichtbaren Ring: Ausschlag in Einheiten der Scheibe
//   gegen GetSlotOffsetMagnitude, Schwelle 80 %, (3) Kindknoten beim Oeffnen und
//   1 s spaeter geloggt (welcher dreht sich?).
//
// 1.15.2, nach 1.15.1 Lauf 1 (19:58, Nutzerbild): Scheibe oeffnet am Strahlpunkt ja,
//   Kette ja - aber Auswahl weiter zu frueh und Ring weiter "weltfest". Gemessen:
//   (a) GetSlotOffsetMagnitude las 0 -> die 80-%-Schwelle griff nie, es galten die
//   festen 12 cm; (b) ALLE Knoten der Scheibe haben lrot 0 und die Weltdrehung der
//   UI-Ebene (folgt dem Kopf) - keine Transform ist weltfest. Hypothese: die
//   Ringgrafik rechnet im Bildschirmraum (Shader). Jetzt: Ringradius aus der
//   Geometrie (Abstand der Slot-Knoten von der Mitte, in Metern), Schwelle 80 %;
//   Material/Shader/Eigenschaften der Scheiben-Graphics einmal im Log.
//
// 1.15.3, nach 1.15.2 Lauf 1 (20:05): Scheiben 2 und 3 treffen den Ring, Scheibe 1
//   zu frueh; die Scheibe verzerrt beim Kopfdrehen. Gemessen: (a) Ring der
//   Duesenscheibe 0,19-0,32 m, die anderen stabil 0,38 m - gemessen WAEHREND der
//   Oeffnungsanimation (RadialRoot 0,53 -> 1); jetzt je Frame das Maximum.
//   (b) alle Shader Standard (UI/Default, TMP) - Bildschirmraum-Hypothese
//   WIDERLEGT. Die Verzerrung war MEINE: fester Weltpunkt, aber jede Frame die
//   Drehung der kopffesten UI-Ebene -> die Scheibe stand schraeg im Raum. Jetzt
//   WELTFEST, Position UND Drehung beim Oeffnen eingefroren, zum Betrachter; die
//   Zeiger-Ebene ist dieselbe.
//
// 1.15.4, nach 1.15.3 Lauf 1: Scheibe 1 waehlt auf dem Ring, Kette ja - VERZERRUNG
//   BLEIBT. Hypothese: zu spaet geschrieben. Die Mod setzte die Lage in
//   onBeforeRender; Unity baut die Canvas-Geometrie vorher (willRenderCanvases
//   nach LateUpdate) - zu sehen ist die Lage des Spiels (fester Punkt vom
//   Oeffnen, Drehung der kopffesten UI). Jetzt an drei Stellen: Postfix
//   UpdateInput (Update), OnLateUpdate, onBeforeRender; vor dem Rendern wird
//   gemessen, ob jemand dazwischen ueberschrieben hat (WAEHLSCHEIBE Lage).
//
// 1.19.0, nach Nutzerbild wheel-world-coordinates-issue.png: die Hintergrund-
//   scheibe steht richtig zur Kamera, der RING mit den Segmenten und dem
//   Auswahlkeil steht weltfest (schraeg, gespiegelt, kantig flach). Der
//   Knotendump ging nur 3 Ebenen tief - die Segmente unter Slots fehlten.
//   Hypothese: das Spiel setzt ihre WELTdrehung (rotation = Euler(0,0,a)) statt
//   der lokalen - am PC mit ungedrehter UI dasselbe, in VR an den Weltachsen.
//   Korrektur: unter der offenen Scheibe jeden Knoten, dessen Weltdrehung eine
//   REINE Z-Drehung ist, waehrend der Elternknoten es nicht ist, auf die lokale
//   Drehung Euler(0,0,a) setzen - in Update (UpdateInput-Postfix) und
//   LateUpdate, vor dem Canvas-Bau, und immer wieder, wenn das Spiel neu
//   schreibt. Jeder korrigierte Knoten einmal im Log (WAEHLSCHEIBE Welt->lokal).
//
// 1.19.1, nach 1.19.0 Lauf (Nutzer): Ring "deutlich besser" (Rest-Verzerrung
//   beim Seitenblick: spaeter). Aber schraege Segmente (Duese 1-2 Uhr, 4-5 Uhr)
//   waehlen nichts, nur waagerecht/senkrecht. Ursache: GetSlotOffsetMagnitude
//   liest 0 (gemessen 1.15.1), Select bekam Max(1, 0) x 1,2 = 1,2 EINHEITEN -
//   der Ring ist Hunderte gross. Prueft das Spiel die Achsen gegen eine
//   Totzone dieser Groesse, reichen orthogonal 1,2, schraeg je Achse 0,85 nicht.
//   Jetzt: Ausschlag = Ringradius in CANVAS-Einheiten (Meter / Skalierung der
//   Scheibe), mindestens 300.
//
// Ein Prefix auf RadialMenu.UpdateInput setzt, solange die MOD die Scheibe
// offen hat, m_inputMode = ConsoleController und UiMovementRaw = rechter Stick -
// unmittelbar vor dem Lesen, ohne Rennen mit PlayerInput.Update.

using HarmonyLib;
using Il2CppPWS;
using Il2CppPWS.UI;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    internal static bool wheelOpen;
    private static EquipmentType wheelType = EquipmentType.Nozzle;
    private static readonly EquipmentType[] WheelTypes = { EquipmentType.Nozzle, EquipmentType.Extension, EquipmentType.CleaningLiquid };
    private static bool r3Was, gripLWheelWas, gripRWheelWas;
    private static float yDownAt = -1f;
    private static bool yLongSent;
    private static int wheelInputCalls;
    private const float WheelHoldSeconds = 0.25f, YHoldSeconds = 0.5f;
    private static float r3DownAt = -1f;
    private static RadialMenu? radialInst;
    private static Il2CppPWS.PlayerInput? wheelPi;
    private static Vector2 wheelWant;          // Richtung x Staerke (0..1+), vom Zeiger oder Stick
    private static bool wheelChain;            // Zeiger-Kette: automatisch weiter
    private static int wheelSelects;
    private Vector3 wheelCenter, wheelRight, wheelUp, wheelPlanePoint, wheelPlaneN;
    private bool wheelFrameSet, wheelFrameSetRing;
    private float wheelOverSince = -1f;
    private Transform? wheelMoved;
    private Vector3 wheelOrigLocalPos;
    private Quaternion wheelOrigLocalRot;
    private bool wheelMeasured;
    private const float WheelPointerRadius = 0.12f, WheelCommitSeconds = 0.15f, WheelRingShare = 0.8f;
    private static float wheelSlotMag;          // Segmentabstand der offenen Kategorie (Postfix)
    private static float wheelRingUnits;        // Ringradius in Canvas-Einheiten (aus der Messung)
    private static float wheelRingWorld;        // derselbe Radius in Welt-Metern, fuer Select (1.19.3)
    private float wheelChildDumpAt = -1f;
    private float wheelRingMeters;           // Radius der Slot-Symbole, gemessen je Scheibe
    private bool wheelShadersLogged;
    private Quaternion wheelRot = Quaternion.identity;   // eingefroren beim Oeffnen
    private static Vector3 wheelPlaceAt;
    private static Quaternion wheelPlaceRot;
    private static bool wheelPlaceValid;
    private static int wheelOverwritten, wheelPlaceChecks;
    private static readonly HashSet<string> wheelFixLogged = new();
    private static int wheelFixes;
    private float nextWheelPlaceLog;

    private void PatchWheel()
    {
        try
        {
            var m = AccessTools.Method(typeof(RadialMenu), "UpdateInput");
            if (m == null) { LoggerInstance.Error("Patch RadialMenu.UpdateInput: nicht gefunden"); return; }
            HarmonyInstance.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(XRStart), nameof(RadialUpdateInputPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(XRStart), nameof(RadialUpdateInputPostfix))));
            LoggerInstance.Msg("Patch RadialMenu.UpdateInput (Prefix+Postfix): installiert (ob er feuert, zeigt der Zaehler)");
        }
        catch (Exception e) { LoggerInstance.Error("Patch RadialMenu.UpdateInput: " + e); }
    }

    private static void RadialUpdateInputPrefix(RadialMenu __instance, BaseInput input)
    {
        wheelInputCalls++;
        radialInst = __instance;
        if (!wheelOpen) return;
        try
        {
            __instance.m_inputMode = RadialMenu.InputMode.ConsoleController;
            if (input != null) input.UiMovementRaw = Vector2.zero;   // die Wahl trifft der Postfix
        }
        catch { }
    }

    // Die Wahl nach dem Spiel: Select(Richtung x Segmentabstand) auf der offenen Kategorie.
    private static void RadialUpdateInputPostfix(RadialMenu __instance)
    {
        if (!wheelOpen) return;
        try
        {
            ApplyWheelPlacement(__instance.transform);
            wheelSlotMag = OpenSlotMag(__instance);
            if (wheelWant.sqrMagnitude < 0.0001f) return;
            var dir = wheelWant.normalized;
            if (__instance.m_nozzleSelectionMenu != null && __instance.m_nozzleSelectionMenu.IsOpen) SelectIn(__instance.m_nozzleSelectionMenu, dir);
            else if (__instance.m_extensionSelectionMenu != null && __instance.m_extensionSelectionMenu.IsOpen) SelectIn(__instance.m_extensionSelectionMenu, dir);
            else if (__instance.m_cleaningLiquidSelectionMenu != null && __instance.m_cleaningLiquidSelectionMenu.IsOpen) SelectIn(__instance.m_cleaningLiquidSelectionMenu, dir);
        }
        catch { }
    }

    private static float OpenSlotMag(RadialMenu rm)
    {
        if (rm.m_nozzleSelectionMenu != null && rm.m_nozzleSelectionMenu.IsOpen) return rm.m_nozzleSelectionMenu.GetSlotOffsetMagnitude();
        if (rm.m_extensionSelectionMenu != null && rm.m_extensionSelectionMenu.IsOpen) return rm.m_extensionSelectionMenu.GetSlotOffsetMagnitude();
        if (rm.m_cleaningLiquidSelectionMenu != null && rm.m_cleaningLiquidSelectionMenu.IsOpen) return rm.m_cleaningLiquidSelectionMenu.GetSlotOffsetMagnitude();
        return 0f;
    }

    private static void SelectIn<T>(Il2CppPWS.UI.RadialMenuCategoryBase<T> cat, Vector2 dir) where T : Il2CppPWS.BaseEquipmentData
    {
        // Ringgroesse statt des Spielwerts (der liest 0): jede Richtung weit ueber
        // jeder Totzone, auch schraeg.
        // WELTRAUM (1.19.3, disassembliert): Select nimmt das Segment mit dem
        // kleinsten 3D-Abstand zu GetSlotItemOffset = Icon.position - Slot.position,
        // also WELT-Meter. Flach sind Welt und Pixel eins; in VR steht die Scheibe
        // gedreht (Gier ~259 Grad) - ein XY-Vektor in Canvas-Einheiten trennte nur
        // oben/unten (1.19.1 "gespiegelt", 1.19.2 rechts -> 12/6 Uhr).
        // Darum: Richtung in der Scheibenebene, in die Welt gedreht, Ringradius in m.
        var t = cat.transform;
        float meters = wheelRingWorld > 0.01f ? wheelRingWorld : 0.3f;
        var world = (t.right * dir.x + t.up * dir.y).normalized * meters;
        cat.Select(world);
        wheelSelects++;
    }

    // Aus UpdateButtons (PlayerInput.Update-Postfix), vor den uebrigen Knoepfen.
    private static void DriveWheelAndY(Il2CppPWS.PlayerInput pi, XRController? r, XRController? l)
    {
        float now = Time.unscaledTime;

        // ---- R3: halten oeffnet, loslassen uebernimmt
        bool r3 = Held(r, "thumbstickClicked");
        if (r3 && !r3Was) r3DownAt = now;
        if (r3 && !wheelOpen && r3DownAt >= 0f && now - r3DownAt >= WheelHoldSeconds && !menuActive)
        {
            if (Free(pi, "R3 Waehlscheibe")) OpenWheel(pi, WheelTypes[0]);
            r3DownAt = -1f;
        }
        if (!r3 && r3Was && wheelOpen) CloseWheel(pi, "R3 losgelassen");
        r3Was = r3;
        if (wheelOpen && menuActive) CloseWheel(pi, "Menue offen");

        // ---- Griffe blaettern, solange die Scheibe offen ist
        bool gl = Held(l, "gripPressed"), gr = Held(r, "gripPressed");
        wheelPi = pi;
        if (wheelOpen && gl && !gripLWheelWas) SwitchWheel(pi, -1);
        if (wheelOpen && gr && !gripRWheelWas) SwitchWheel(pi, +1);
        gripLWheelWas = gl; gripRWheelWas = gr;

        // ---- Y: kurz Inventar, lang naechster Washer
        bool y = Held(l, "secondaryButton");
        if (y && yDownAt < 0f) { yDownAt = now; yLongSent = false; }
        // In die Griff-Kalibrierung gefallen (Grip.cs): dieser Y-Druck tut nichts, auch beim Loslassen.
        if (y && yDownAt >= 0f && CalibrateSuppressed) yLongSent = true;
        if (y && yDownAt >= 0f && !yLongSent && now - yDownAt >= YHoldSeconds)
        {
            yLongSent = true;
            if (Free(pi, "Y lang Washer"))
            {
                var sg = pi.SwitchGun;
                if (sg == null) btnEvents.Add("Y lang: SwitchGun ist null (niemand hoert zu)");
                else { sg.Invoke(1); btnEvents.Add("Y lang: SwitchGun(+1) - naechster Washer"); Buzz(false, "Washer gewechselt"); }
            }
        }
        if (!y && yDownAt >= 0f)
        {
            if (!yLongSent) self?.ToggleInventory(pi);
            yDownAt = -1f;
        }
    }

    private static void OpenWheel(Il2CppPWS.PlayerInput pi, EquipmentType type)
    {
        var a = pi.m_openRadialMenu;
        if (a == null) { btnEvents.Add("Waehlscheibe: m_openRadialMenu ist null"); return; }
        wheelType = type;
        wheelOpen = true;
        wheelWant = Vector2.zero;
        wheelChain = true;
        if (self != null) { self.wheelFrameSet = false; self.wheelFrameSetRing = false; self.wheelOverSince = -1f; }
        a.Invoke(type);
        btnEvents.Add($"Waehlscheibe AUF: {type} (UpdateInput bisher {wheelInputCalls}x)");
        Buzz(true, "Waehlscheibe");
    }

    private static void CloseWheel(Il2CppPWS.PlayerInput pi, string why)
    {
        try { pi.m_closeRadialMenu?.Invoke(wheelType); } catch (Exception e) { btnEvents.Add("Waehlscheibe zu: " + e.GetType().Name); }
        wheelOpen = false;
        try { pi.UiMovementRaw = Vector2.zero; } catch { }
        btnEvents.Add($"Waehlscheibe ZU ({why}): {wheelType} - UpdateInput {wheelInputCalls}x, Select {wheelSelects}x");
        wheelWant = Vector2.zero;
        self?.RestoreWheelPose();
        Buzz(true, "Waehlscheibe uebernommen");
    }

    private static void SwitchWheel(Il2CppPWS.PlayerInput pi, int dir)
    {
        int i = Array.IndexOf(WheelTypes, wheelType);
        var next = WheelTypes[(i + dir + WheelTypes.Length) % WheelTypes.Length];
        try { pi.m_closeRadialMenu?.Invoke(wheelType); } catch { }
        wheelOpen = false;
        OpenWheel(pi, next);
    }

    // In onBeforeRender nach dem Kopf: Zeiger-Kette und Ausrichtung der Scheibe.
    private void DriveWheelPointer()
    {
        if (!wheelOpen) return;
        try
        {
            var cam = Camera.main;
            var hmd = UnityEngine.InputSystem.InputSystem.GetDevice<XRHMD>();
            var r = WasherCtl;
            if (cam == null || hmd == null || r == null || uiRoot == null) return;
            var camT = cam.transform;
            var hmdRot = hmd.centerEyeRotation.ReadValue();
            var toWorld = camT.rotation * Quaternion.Inverse(hmdRot);
            var origin = camT.position + toWorld * (r.devicePosition.ReadValue() - hmd.centerEyePosition.ReadValue());
            var aimCtl = r.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
            var dir = (toWorld * (aimCtl != null ? aimCtl.ReadValue() : r.deviceRotation.ReadValue())) * Vector3.forward;

            // Ebene der UI; beim Oeffnen jeder Scheibe neu: Mitte = aktueller Strahlpunkt.
            var rootT = uiRoot.transform;
            if (!wheelFrameSet) { wheelPlanePoint = rootT.position; wheelPlaneN = rootT.forward; wheelRight = rootT.right; wheelUp = rootT.up; wheelRot = rootT.rotation; }
            float denom = Vector3.Dot(dir, wheelPlaneN);
            if (Math.Abs(denom) < 1e-4f) return;
            float t = Vector3.Dot(wheelPlanePoint - origin, wheelPlaneN) / denom;
            if (t <= 0f) return;
            var hit = origin + dir * t;
            if (!wheelFrameSet) { wheelCenter = hit; wheelFrameSet = true; }
            var d = hit - wheelCenter;
            var vm = new Vector2(Vector3.Dot(d, wheelRight), Vector3.Dot(d, wheelUp));   // Meter auf der Ebene
            // In Einheiten der Scheibe (Canvas-Pixel) gegen den Segmentabstand des Spiels;
            // ohne Messwert der alte feste Radius.
            // Je Frame, das Maximum: die Oeffnungsanimation skaliert die Scheibe hoch.
            float ringNow = MeasureRing(!wheelFrameSetRing);
            if (!wheelFrameSetRing) { wheelRingMeters = 0f; wheelFrameSetRing = true; }
            if (ringNow > wheelRingMeters) wheelRingMeters = ringNow;
            float unitNow = radialInst == null ? 0f : Math.Abs(radialInst.transform.lossyScale.x);
            wheelRingUnits = unitNow > 1e-7f ? wheelRingMeters / unitNow : 0f;
            wheelRingWorld = wheelRingMeters;
            Vector2 v = wheelRingMeters > 0.01f
                ? vm / (wheelRingMeters * WheelRingShare)
                : vm / WheelPointerRadius;

            // Stick hat Vorrang, wenn ausgelenkt; sonst der Zeiger. Der LINKE Stick
            // (Nutzer 1.19.3): der rechte haelt R3 gedrueckt und oeffnet die Scheibe.
            var st = OffCtl?.TryGetChildControl("thumbstick")?.TryCast<Vector2Control>();
            var sv = st == null ? Vector2.zero : st.ReadValue();
            bool stick = sv.magnitude > 0.5f;
            wheelWant = stick ? sv : (v.magnitude >= 1f ? v : Vector2.zero);

            float now = Time.unscaledTime;
            bool over = !stick && v.magnitude >= 1f;
            if (over) { if (wheelOverSince < 0f) wheelOverSince = now; }
            else wheelOverSince = -1f;
            if (over && wheelChain && now - wheelOverSince >= WheelCommitSeconds && wheelPi != null)
            {
                int i = Array.IndexOf(WheelTypes, wheelType);
                Diag($"WAEHLSCHEIBE: {wheelType} gewaehlt per Zeiger (Richtung {v.normalized.ToString("F2")}, Winkel {Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg:F0}), Ring {wheelRingMeters:F3} m = {wheelRingUnits:F0} Einheiten, Select {wheelSelects}x");
                if (i >= WheelTypes.Length - 1) CloseWheel(wheelPi, "Kette fertig");
                else
                {
                    try { wheelPi.m_closeRadialMenu?.Invoke(wheelType); } catch { }
                    wheelOpen = false;
                    RestoreWheelPose();
                    OpenWheel(wheelPi, WheelTypes[i + 1]);
                }
                return;
            }

            DrawMenuLine(origin, hit, over || stick);
            PlaceWheel(rootT, wheelCenter, wheelRot);
            if (wheelChildDumpAt > 0f && now >= wheelChildDumpAt) { wheelChildDumpAt = -1f; DumpWheelChildren("1 s nach dem Oeffnen"); }
        }
        catch (Exception e) { LoggerInstance.Warning("WAEHLSCHEIBE: " + e.GetType().Name + ": " + e.Message); }
    }

    // Die Scheibe in die Mitte der UI-Ebene, zum Betrachter gedreht.
    private void PlaceWheel(Transform rootT, Vector3 at, Quaternion rot)
    {
        var rm = radialInst;
        if (rm == null) return;
        var t = rm.transform;
        if (!wheelMeasured)
        {
            wheelMeasured = true;
            var cv = rm.GetComponentInParent<Canvas>();
            var rc = cv == null ? null : cv.rootCanvas;
            LoggerInstance.Msg($"WAEHLSCHEIBE: '{PathOf(t)}' pos {t.position.ToString("F2")} rot {t.rotation.eulerAngles.ToString("F0")} scale {t.lossyScale.ToString("F4")} | " +
                $"Canvas '{(cv == null ? "-" : cv.name)}' {(cv == null ? "-" : cv.renderMode.ToString())} Wurzel '{(rc == null ? "-" : rc.name)}' {(rc == null ? "-" : rc.renderMode.ToString())} | " +
                $"UIRoot pos {rootT.position.ToString("F2")} rot {rootT.rotation.eulerAngles.ToString("F0")} scale {rootT.lossyScale.ToString("F4")}");
        }
        if (wheelMoved == null || wheelMoved.Pointer != t.Pointer)
        {
            wheelMoved = t;
            wheelOrigLocalPos = t.localPosition;
            wheelOrigLocalRot = t.localRotation;
            DumpWheelChildren("beim Oeffnen");
            wheelChildDumpAt = Time.unscaledTime + 1f;
        }
        // Vor dem Rendern: hat seit dem letzten Schreiben jemand die Lage geaendert?
        if (wheelPlaceValid)
        {
            wheelPlaceChecks++;
            if ((t.position - wheelPlaceAt).sqrMagnitude > 1e-6f || Quaternion.Angle(t.rotation, wheelPlaceRot) > 0.1f) wheelOverwritten++;
        }
        wheelPlaceAt = at; wheelPlaceRot = rot; wheelPlaceValid = true;
        t.SetPositionAndRotation(at, rot);   // weltfest: Mitte am Strahlpunkt, Drehung vom Oeffnen
        float now = Time.unscaledTime;
        if (dev && now >= nextWheelPlaceLog)
        {
            nextWheelPlaceLog = now + 1f;
            LoggerInstance.Msg($"WAEHLSCHEIBE Lage: vor dem Rendern {wheelOverwritten} von {wheelPlaceChecks} Frames fremd ueberschrieben, Welt->lokal {wheelFixes} Korrekturen");
            wheelOverwritten = wheelPlaceChecks = 0;
            wheelFixes = 0;
        }
    }

    // Radius des sichtbaren Rings: mittlerer Weltabstand der aktiven Slot-Knoten
    // der offenen Kategorie von der Scheibenmitte. Dazu einmal die Shader.
    private float MeasureRing(bool log)
    {
        try
        {
            var rm = radialInst;
            if (rm == null) return 0f;
            Transform? slots = null;
            if (rm.m_nozzleSelectionMenu != null && rm.m_nozzleSelectionMenu.IsOpen) slots = rm.m_nozzleSelectionMenu.m_slotsRoot;
            else if (rm.m_extensionSelectionMenu != null && rm.m_extensionSelectionMenu.IsOpen) slots = rm.m_extensionSelectionMenu.m_slotsRoot;
            else if (rm.m_cleaningLiquidSelectionMenu != null && rm.m_cleaningLiquidSelectionMenu.IsOpen) slots = rm.m_cleaningLiquidSelectionMenu.m_slotsRoot;
            if (slots == null) { if (log) Diag("WAEHLSCHEIBE: keine offene Kategorie fuer den Ring - fester Radius"); return 0f; }
            float sum = 0f; int n = 0;
            var c0 = rm.transform.position;
            void Walk(Transform t, int depth)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    float d = (c.position - c0).magnitude;
                    if (depth == 1 && d > 0.001f) { sum += d; n++; }
                    if (depth == 1 && d <= 0.001f && c.childCount > 0) Walk(c, 1);   // Slot selbst in der Mitte: sein Symbol zaehlt
                }
            }
            Walk(slots, 1);
            float ring = n == 0 ? 0f : sum / n;
            if (log) Diag($"WAEHLSCHEIBE: Ring beim Oeffnen {ring:F3} m aus {n} Slot-Knoten ({wheelType}) - je Frame nachgefuehrt, Maximum gilt");
            if (!wheelShadersLogged) { wheelShadersLogged = true; LogWheelShaders(rm); }
            return ring;
        }
        catch (Exception e) { LoggerInstance.Warning("WAEHLSCHEIBE Ring: " + e.GetType().Name + ": " + e.Message); return 0f; }
    }

    private void LogWheelShaders(RadialMenu rm)
    {
        if (!dev) return;
        try
        {
            var gs = rm.GetComponentsInChildren<UnityEngine.UI.Graphic>(false);
            var seen = new HashSet<string>();
            foreach (var g in gs)
            {
                if (g == null) continue;
                var m = g.materialForRendering;
                var sh = m == null ? null : m.shader;
                string key = (sh == null ? "-" : sh.name) + "/" + (m == null ? "-" : m.name);
                if (!seen.Add(key)) continue;
                var props = new List<string>();
                if (sh != null) for (int i = 0; i < sh.GetPropertyCount(); i++) props.Add(sh.GetPropertyName(i));
                Diag($"WAEHLSCHEIBE Shader: '{g.name}' {g.GetIl2CppType().Name} Material '{(m == null ? "-" : m.name)}' Shader '{(sh == null ? "-" : sh.name)}' [{string.Join(", ", props)}]");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("WAEHLSCHEIBE Shader: " + e.GetType().Name + ": " + e.Message); }
    }

    // Welcher Knoten unter der Scheibe dreht sich weltfest? Tiefe 3, lokale und
    // Weltdrehung, dazu m_offsetFeedback beim Namen.
    private void DumpWheelChildren(string when)
    {
        if (!dev) return;
        try
        {
            var rm = radialInst;
            if (rm == null) return;
            var fb = rm.m_offsetFeedback;
            var sb = new System.Text.StringBuilder($"WAEHLSCHEIBE Knoten ({when}), Segmentabstand {wheelSlotMag:F0}, offsetFeedback '{(fb == null ? "-" : fb.name)}':");
            void Walk(Transform t, int depth)
            {
                if (depth > 5) return;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    sb.Append($" | {new string('>', depth)}'{c.name}' lpos {c.localPosition.ToString("F0")} lrot {c.localEulerAngles.ToString("F0")} wrot {c.eulerAngles.ToString("F0")} lscale {c.localScale.ToString("F2")}");
                    Walk(c, depth + 1);
                }
            }
            Walk(rm.transform, 1);
            Diag(sb.ToString());
        }
        catch (Exception e) { LoggerInstance.Warning("WAEHLSCHEIBE Knoten: " + e.GetType().Name); }
    }

    // Aus dem UpdateInput-Postfix und OnLateUpdate: dieselbe Lage vor dem Canvas-Bau.
    internal static void ApplyWheelPlacement(Transform? t)
    {
        if (!wheelOpen || t == null) return;
        if (wheelPlaceValid) { try { t.SetPositionAndRotation(wheelPlaceAt, wheelPlaceRot); } catch { } }
        FixWorldRotations(t);
    }

    private static bool PureZ(Quaternion q)
    {
        var e = q.eulerAngles;
        float x = Mathf.DeltaAngle(0f, e.x), y = Mathf.DeltaAngle(0f, e.y);
        return Math.Abs(x) < 0.5f && Math.Abs(y) < 0.5f;
    }

    // Knoten, die das Spiel in WELTkoordinaten gedreht hat, in lokale umrechnen.
    private static void FixWorldRotations(Transform root)
    {
        try
        {
            void Walk(Transform t, int depth)
            {
                if (depth > 12) return;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    var parentRot = t.rotation;
                    var rot = c.rotation;
                    if (!PureZ(parentRot) && PureZ(rot))
                    {
                        float a = rot.eulerAngles.z;
                        c.localRotation = Quaternion.Euler(0f, 0f, a);
                        wheelFixes++;
                        string key = c.name + "/" + t.name;
                        if (wheelFixLogged.Add(key)) self?.LoggerInstance.Msg($"WAEHLSCHEIBE Welt->lokal: '{c.name}' unter '{t.name}' z {a:F0} (Tiefe {depth})");
                    }
                    Walk(c, depth + 1);
                }
            }
            Walk(root, 1);
        }
        catch { }
    }

    private void RestoreWheelPose()
    {
        wheelPlaceValid = false;
        try { if (wheelMoved != null) { wheelMoved.localPosition = wheelOrigLocalPos; wheelMoved.localRotation = wheelOrigLocalRot; } } catch { }
        wheelMoved = null;
        HideMenuLine();
    }

    private void ToggleInventory(Il2CppPWS.PlayerInput pi)
    {
        try
        {
            // ERST schliessen, dann die Sperre - das offene Inventar setzt selbst
            // BlockedInput (1.14.0: Y konnte es nicht mehr schliessen).
            if (pi.CheckAndCloseInventoryMenuIfOpen()) { btnEvents.Add("Y kurz: Inventar geschlossen"); return; }
            // Im Menue ist Y ZURUECK (1.31.0, PWS2 "Y im Menue"): ESC gibt es sonst nur
            // an der Tastatur. Nie oeffnen, nur schliessen (MenuBack.cs).
            if (menuActive) { PressMenuBack(); return; }
            if (pi.BlockedInput && !menuActive) { btnEvents.Add("Y kurz: gesperrt (BlockedInput)"); return; }
            if (gameState == null) gameState = UnityEngine.Object.FindObjectOfType<GameStateManager>();
            var cur = gameState == null ? null : gameState.StateMachine?.Current;
            var ps = cur == null ? null : cur.TryCast<Il2CppPWS.States.PlayingState>();
            if (ps == null) { btnEvents.Add($"Y kurz: kein PlayingState (aktuell {(cur == null ? "null" : cur.GetIl2CppType().Name)})"); return; }
            ps.ShowInventory();
            btnEvents.Add("Y kurz: Inventar geoeffnet (PlayingState.ShowInventory)");
        }
        catch (Exception e) { btnEvents.Add("Y kurz: " + e.GetType().Name + ": " + e.Message); }
    }
}
