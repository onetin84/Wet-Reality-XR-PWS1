// Menue mit dem Zeigestrahl bedienen (XRStart 1.9.0/1.9.1).
//
// Seit 1.8.0 steht die UI im Headset (VrUi.cs: UIRoot als ScreenSpaceCamera).
// Die Maus bedient sie nicht mehr - ihr Strahl geht durch die Stereokamera -, F10
// gibt das Overlay (und damit die Maus) zurueck.
//
// PWS1 hat NICHT das UI-Geruest von PWS2 (kein UIStateMonoBehaviour, kein
// FuturToggle, kein PwsScreenManager, kein AllowsPlayerMovement; dump.cs).
// Gemessen an dump.cs:
//   - Menuemodus: GameStateManager.CurrentScreen (GameScreen) - Game, Loading,
//     None sind kein Menue, alles andere (Menu, Options, Shop*, ...) schon.
//     Popups (Levelauswertung) sind ungemessen: jeder Wechsel steht im Log.
//   - FuturAnimatedButton erbt von UnityEngine.UI.Button.
//
// ZIEL: der kleinste sichtbare, bedienbare Selectable unter UIRoot, dessen
// Rechteck den Schnittpunkt des Strahls mit der UIRoot-Ebene enthaelt - im
// lokalen Raum des Rechtecks getestet (InverseTransformPoint + rect.Contains),
// nie per Pivot (PWS2 §90). Kandidaten alle 0,5 s neu gesammelt.
//
// 1.9.1, nach 1.9.0 Lauf 1 (15:38):
//   - KLICK WIE DIE MAUS: OnPointerClick(PointerEventData) auf Button, Toggle,
//     TMP_Dropdown, Dropdown. 1.9.0 rief FuturButton.Submit(): Popup-Knopf
//     "ABBRECHEN" 13x ohne Wirkung, und ein Toggle bekam das Submit seines
//     Eltern-FuturButton. Submit/Click nur noch als Rueckfall.
//   - HOVER WIE DIE MAUS: OnPointerEnter/OnPointerExit, dazu Select() fuer die
//     Controller-Hervorhebung. Mit Toleranz: 494 Zielwechsel im Lauf, das Ziel
//     sprang bei 2 m Abstand mit jedem Handzittern. Ein Ziel bleibt, solange der
//     Punkt hoechstens 12 % ausserhalb seines Rechtecks liegt oder 0,15 s lang.
//     Geloggt wird ein Ziel erst, wenn es 0,25 s ruhig liegt.
//   - SCHIEBEREGLER und SCROLLBALKEN: Trigger halten und ziehen - der Wert folgt
//     dem Strahl entlang der Achse des Reglers (normalizedValue / value).
//   - SCROLLEN (PWS2 DriveMenuScroll): rechter Stick y scrollt die ScrollRect
//     unter dem Strahl (kleinste, die den Punkt enthaelt). Drehen und
//     Duesenwechsel ruhen im Menue (menuActive).
//
// 1.9.2, nach 1.9.1 Lauf 1 (15:53):
//   - KLICK = DIE GANZE MAUSFOLGE: PointerDown, PointerUp, PointerClick per
//     ExecuteEvents.ExecuteHierarchy - an JEDEN Handler auf dem Objekt und
//     darueber (PointerEvents, EventTrigger, ...), nicht nur an den Button.
//     MainMenuButton_Career bekam 15x Button.OnPointerClick ohne Wirkung.
//     Execute<IPointerClickHandler/Down/Up> sind im Spiel instanziiert
//     (dump.cs). Die direkten Aufrufe bleiben Rueckfall.
//   - VIBRATION nur beim Klick und beim Anfassen eines Reglers, nicht beim
//     Ueberfahren (306 Hover-Pulse im Lauf = "Dauervibrieren", Nutzer).
//   - STRAHL GEGLAETTET: Richtung per Tiefpass, dessen Zeitkonstante mit der
//     Winkelgeschwindigkeit sinkt (langsam = ruhig, schnell = direkt) - der
//     Strahl war trotz Toleranz "zittrig".
//   - SCROLLEN nur auf Flaechen mit echtem Scrollweg: 1.9.1 nahm die kleinste
//     ('Content', Inhalt 1439 bei Sicht 1372) statt der Liste GridViewOptions.
//
// 1.9.3, nach 1.9.2 Lauf 1 (17:05):
//   - SCROLLEN: unter dem Strahl lagen VIER 'Content'-Flaechen gleicher Groesse
//     (2007/1439/1195/1927 hoch) - die Seiten aller Options-Tabs, auch der
//     unsichtbaren. Gewaehlt wird jetzt nur eine Flaeche, die mindestens einen
//     sichtbaren, bedienbaren Kandidaten (menuCandidates) enthaelt.
//   - HOVER an die ganze Elternkette (ExecuteEvents pointerEnter/Exit), wie die
//     Maus: Karriere-Kacheln bekamen Down/Up/Click und reagierten nicht.
//     Dazu einmal je Objektname die Komponenten des Klickziels und seiner
//     Eltern im Log (MENUE: Komponenten) - wer dort zuhoert, ist ungemessen.
//   - WERKZEUG AUS, solange das Menue offen ist (PWS2 ToolHide): alle Renderer
//     unter dem Anker des Spiels (EquipmentManager.m_anchorPoint, kein Name),
//     bei jedem Scan nachgezogen (Shop tauscht das Geraet), zurueck nur, was
//     die Mod abgeschaltet hat.
//
// 1.9.4, nach 1.9.3 Lauf 1 (17:23):
//   - JEDES ELEMENT IN SEINER EIGENEN EBENE: der X-Knopf des Burger-Menues war
//     nur mit sichtbarem Versatz zu treffen. 1.9.x schnitt den Strahl mit der
//     UIRoot-Ebene; liegt ein Element (animiertes Seitenmenue) davor oder
//     dahinter, trifft der Test eine andere Stelle als das Auge sieht
//     (Parallaxe). Jetzt: Strahl gegen die Ebene des Rechtecks selbst
//     (RayOnRect), der Strahl endet am getroffenen Element.
//   - SCROLLEN MIT RUECKFALL: in CareerJobDetails u. a. lag keine ScrollRect
//     unter dem Strahl (ScrollRect sitzt in PWS1 oft auf dem Inhalt selbst).
//     Dann die naechstgelegene sichtbare Liste mit Scrollweg.
//
// 1.9.5, nach 1.9.4 Lauf 1 (17:30, Nutzerbilder):
//   - ZIEL WIE DER GRAPHICRAYCASTER: das OBERSTE raycastbare Graphic unter dem
//     Strahl (Graphic.Raycast - kennt Masken und CanvasGroup.blocksRaycasts;
//     sortiert nach sortingLayer, sortingOrder, depth), Ziel = dessen naechster
//     bedienbarer Selectable darueber. Verdeckt ein Panel, gibt es kein Ziel.
//     1.9.x nahm den KLEINSTEN Selectable und wusste nichts von oben/unten:
//     bei offenem Seitenmenue klickte es den BurgerMenuButton darunter und den
//     Multiplayer-Schalter hinter dem Menue, statt des X.
//   - KLICK AB DEM GETROFFENEN GRAPHIC durch die Hierarchie, wie die Maus.
//     Die Karriere-Kacheln gehen mit der Maus (Nutzer, F10), mit der Mod nicht.
//   - NACHRUECKEN: derselbe Knopf binnen 3 s erneut geklickt -> naechster Weg
//     (ISubmitHandler, FuturButton.Submit, Button.onClick). Log nennt den Weg.
//   - SCROLLEN UEBER DEN SCROLLBALKEN, wenn keine ScrollRect Scrollweg meldet:
//     Jobdetail-Tabelle 'Content' 303/1222, aber 'Scrollbar Vertical' sichtbar.
//
// 1.9.6, nach 1.9.5 Lauf 1 (Nutzerbild many-offsets-menu.png): gleichmaessiger
//   Versatz nach rechts oben, ueberall im Menue. Ursache: Graphic.Raycast
//   bekam den Punkt ueber cam.WorldToScreenPoint - Unity rechnet ihn fuer die
//   XR-Kamera in einer anderen Pixelgroesse zurueck (Augenpuffer gegen Fenster)
//   und streckt ihn vom Ursprung unten links. Jetzt wieder rein geometrisch
//   (Strahl gegen die Ebene des Rechtecks, wie 1.9.4 ohne Versatz), dazu das,
//   was Graphic.Raycast zusaetzlich prueft, selbst: Masken der Eltern
//   (RectMask2D/Mask - Punkt auch dort innen) und CanvasGroup.blocksRaycasts.
//   KEIN Bildschirmpunkt mehr fuer die XR-Kamera.
//
// 1.9.7, nach 1.9.6 Lauf 1 (18:01): KEIN Ziel mehr, nirgends - das oberste
//   Graphic gehoerte nie zu einem Knopf. Der GraphicRaycaster trifft nur
//   Graphics, deren EIGENE Canvas (graphic.canvas) einen aktiven
//   GraphicRaycaster traegt (GraphicRegistry je Canvas) - Graphics in
//   Canvases ohne Raycaster existieren fuer die Maus nicht (Kandidat:
//   ControllerCursor, sortingOrder 31000). Dieser Filter fehlte; in 1.9.5
//   verdeckte ihn der gestreckte Bildschirmpunkt. Dazu: blockiert ein
//   Graphic ohne Knopf, steht es im Log (MENUE: verdeckt von).
//
// 1.9.9, nach 1.9.8 Lauf 1: Auswahl ohne Versatz, aber Hover "2-3 s verzoegert".
//   Die Vorauswahl pruefte gegen Grenzen, die nur alle 1,5 s gemerkt wurden -
//   nach Menueoeffnen, Animation oder Scrollen lagen die Elemente schon woanders.
//   Jetzt: jedes raycastbare Graphic (27-104) je Frame direkt geprueft, Liste
//   alle 0,3 s und sofort bei Screen- oder Canvaswechsel neu gesammelt.
//
// DER TRIGGER gehoert im Menue dem Klick: ControllerInput sprueht nicht, solange
// menuOwnsTrigger steht, und das bleibt nach dem Schliessen stehen, bis der
// Trigger losgelassen ist - sonst spruehte der Klick, der das Menue schliesst.

using Il2CppPWS;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    internal static bool menuOwnsTrigger, menuActive;
    private GameStateManager? gameState;
    private string lastScreen = "";
    private bool menuMode;
    private GameObject? menuLineGo;
    private LineRenderer? menuLine;
    private readonly List<Selectable> menuCandidates = new();
    private readonly List<ScrollRect> menuScrolls = new();
    private float nextMenuScan;
    private Selectable? menuHover;
    private float menuHoverLostAt = -1f, menuHoverSince;
    private string menuHoverLogged = "";
    private bool menuTrigWas;
    private Selectable? menuDrag;      // Schieberegler/Scrollbalken, solange der Trigger haelt
    private string menuStatus = "";
    private float nextMenuLog;
    private PointerEventData? menuPed;
    private Vector3 menuDirSmooth;
    private bool menuDirHave;
    private string scrollChosen = "";
    private Vector3 rayO, rayD;
    private readonly List<(Graphic G, float MinX, float MinY, float MaxX, float MaxY)> menuGraphics = new();
    private GameObject? menuHoverGo;   // das getroffene Graphic - Start des Klicks
    private IntPtr lastClickPtr = IntPtr.Zero;
    private float lastClickAt;
    private int clickStage;
    private float nextGraphicScan, nextScanCostLog;
    private double scanCostMax;
    private readonly Dictionary<IntPtr, bool> canvasHasRaycaster = new();
    private string lastBlocker = "";
    // F11: oberstes Graphic (1.9.5+) oder kleinster Selectable (1.9.4) - A/B im selben Lauf.
    private const int VK_F11 = 0x7A;
    private bool f11WasDown, menuTopmost = true;
    private bool rayValid;
    private readonly List<Renderer> toolHidden = new();
    private readonly HashSet<string> componentsLogged = new();

    private const float MenuTriggerOn = 0.6f, MenuTriggerOff = 0.3f;
    private const float HoverMargin = 0.12f, HoverGrace = 0.15f, HoverLogCalm = 0.25f;
    private const float ScrollDeadZone = 0.2f, ScrollViewportsPerSecond = 1.5f;
    // Glaettung: unter SmoothSlowDeg/s wirkt TauSlow, ab SmoothFastDeg/s TauFast.
    private const float SmoothSlowDeg = 20f, SmoothFastDeg = 150f, TauSlow = 0.12f, TauFast = 0.01f;

    // In onBeforeRender nach dem Kopf: dieselbe Kamera wie der Zeiger der Off-Hand.
    private void DriveMenuPointer()
    {
        if (KeyPressed(VK_F11, ref f11WasDown, "F11")) { menuTopmost = !menuTopmost; LoggerInstance.Msg($"F11: Menue-Ziel {(menuTopmost ? "oberstes Graphic (1.9.7)" : "kleinster Knopf (1.9.4)")}"); }
        bool active = started && writeHead && uiConverted && uiRoot != null && InMenuScreen();
        if (active != menuMode)
        {
            menuMode = active;
            menuActive = active;
            LoggerInstance.Msg(active ? $"MENUE: Zeigestrahl AN (Screen {lastScreen})" : $"MENUE: Zeigestrahl aus (Screen {lastScreen})");
            if (!active) { SetHover(null, Vector3.zero); HideMenuLine(); menuCandidates.Clear(); menuScrolls.Clear(); menuDrag = null; menuDirHave = false; ShowTool(); }
            else HideTool();
        }

        var r = XRController.rightHand;
        float trig = 0f;
        try { var a = r?.TryGetChildControl("trigger")?.TryCast<AxisControl>(); if (a != null) trig = a.ReadValue(); } catch { }
        if (menuMode) menuOwnsTrigger = true;
        else if (menuOwnsTrigger && trig < MenuTriggerOff) menuOwnsTrigger = false;
        if (!menuMode) { menuTrigWas = trig > MenuTriggerOn; return; }

        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            var cam = Camera.main;
            if (hmd == null || r == null || cam == null || !r.isTracked.isPressed) { HideMenuLine(); return; }

            var hmdPos = hmd.centerEyePosition.ReadValue();
            var hmdRot = hmd.centerEyeRotation.ReadValue();
            var grip = r.devicePosition.ReadValue();
            var aimCtl = r.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
            var aim = aimCtl != null ? aimCtl.ReadValue() : r.deviceRotation.ReadValue();
            var camT = cam.transform;
            var toWorld = camT.rotation * Quaternion.Inverse(hmdRot);
            var origin = camT.position + toWorld * (grip - hmdPos);
            var dir = SmoothMenuDir((toWorld * aim) * Vector3.forward);
            rayO = origin; rayD = dir; rayValid = true;

            var rootT = uiRoot!.transform;
            var n = rootT.forward;
            float denom = Vector3.Dot(dir, n);
            Vector3 end = origin + dir * UiDistance;
            bool onPlane = false;
            if (Math.Abs(denom) > 1e-4f)
            {
                float t = Vector3.Dot(rootT.position - origin, n) / denom;
                if (t > 0f && t < 20f) { end = origin + dir * t; onPlane = true; }
            }

            float now = Time.unscaledTime;
            if (now >= nextMenuScan) { nextMenuScan = now + 0.3f; ScanMenuCandidates(); HideTool(); }

            bool down = menuTrigWas ? trig > MenuTriggerOff : trig > MenuTriggerOn;

            // Beim Ziehen haelt der Regler das Ziel fest, auch wenn der Strahl
            // sein Rechteck verlaesst - wie die Maus.
            if (menuDrag != null)
            {
                if (!down) { LoggerInstance.Msg("MENUE: Ziehen Ende " + Caption(menuDrag)); menuDrag = null; }
                else if (onPlane) DragValue(menuDrag, end);
            }
            else
            {
                // Rueckfall: filtert der Raycaster-Test alles weg, die Auswahl von 1.9.4.
                var hit = !onPlane ? null : (menuTopmost && menuGraphics.Count > 0) ? HitTopmost(end, cam) : HitCandidate(end);
                if (hit == null && menuHover != null)
                {
                    // Toleranz: kurz daneben oder knapp am Rand zaehlt noch.
                    bool near = onPlane && Contains(menuHover, end, HoverMargin);
                    if (near) { hit = menuHover; menuHoverLostAt = -1f; }
                    else if (menuHoverLostAt < 0f) { menuHoverLostAt = now; hit = menuHover; }
                    else if (now - menuHoverLostAt < HoverGrace) hit = menuHover;
                }
                else menuHoverLostAt = -1f;
                if (!Same(hit, menuHover)) SetHover(hit, end);

                if (down && !menuTrigWas && menuHover != null)
                {
                    if (IsDraggable(menuHover)) { menuDrag = menuHover; LoggerInstance.Msg("MENUE: Ziehen " + Caption(menuDrag)); Buzz(true, "Menue-Regler"); DragValue(menuDrag, end); }
                    else ClickMenu(menuHover, end, menuHoverGo);
                }
            }
            menuTrigWas = down;

            if (menuHover != null && now - menuHoverSince >= HoverLogCalm)
            {
                string c = Caption(menuHover);
                if (c != menuHoverLogged) { menuHoverLogged = c; LoggerInstance.Msg("MENUE: Ziel " + c); }
            }
            menuStatus = menuHover == null ? "kein Ziel" : Caption(menuHover);

            if (onPlane) DriveMenuScroll(end, r);
            var shown = menuDrag != null ? menuDrag : menuHover;
            if (shown != null) { var rt0 = shown.transform.TryCast<RectTransform>(); if (rt0 != null && RayOnRect(rt0, out var pe)) end = pe; }
            DrawMenuLine(origin, end, shown != null);

            if (now >= nextMenuLog)
            {
                nextMenuLog = now + 5f;
                LoggerInstance.Msg($"MENUE: Screen {lastScreen}, {menuCandidates.Count} Kandidaten, {menuScrolls.Count} Scrollflaechen, Ziel {menuStatus}, Strahl {(onPlane ? "trifft die UI-Ebene" : "an der UI-Ebene vorbei")}");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: " + e.GetType().Name + ": " + e.Message); }
    }

    private static bool Same(Selectable? a, Selectable? b)
        => (a == null && b == null) || (a != null && b != null && a.Pointer == b.Pointer);

    private bool InMenuScreen()
    {
        try
        {
            if (gameState == null) gameState = UnityEngine.Object.FindObjectOfType<GameStateManager>();
            if (gameState == null) return false;
            var s = gameState.CurrentScreen;
            string name = s.ToString();
            if (name != lastScreen) { LoggerInstance.Msg($"MENUE: Screen {(lastScreen.Length == 0 ? "-" : lastScreen)} -> {name}"); lastScreen = name; nextMenuScan = 0f; nextGraphicScan = 0f; }
            return s != GameScreen.Game && s != GameScreen.Loading && s != GameScreen.None;
        }
        catch { gameState = null; return false; }
    }

    private void ScanMenuCandidates()
    {
        menuCandidates.Clear();
        menuScrolls.Clear();
        try
        {
            var all = uiRoot!.GetComponentsInChildren<Selectable>(false);
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                if (s == null || !s.isActiveAndEnabled || !s.IsInteractable()) continue;
                var g = s.targetGraphic;
                if (g != null)
                {
                    var cr = g.canvasRenderer;
                    if (cr != null && (cr.cull || cr.GetInheritedAlpha() < 0.01f)) continue;
                }
                menuCandidates.Add(s);
            }
            // Mit den Knoepfen: nur die Liste, keine Grenzen (die gelten je Frame).
            float nowS = Time.unscaledTime;
            if (nowS >= nextGraphicScan)
            {
                nextGraphicScan = nowS + 0.25f;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ScanMenuGraphics();
                double ms = sw.Elapsed.TotalMilliseconds;
                scanCostMax = Math.Max(scanCostMax, ms);
                if (nowS >= nextScanCostLog) { nextScanCostLog = nowS + 10f; LoggerInstance.Msg($"MENUE: Graphic-Scan {menuGraphics.Count} raycastbar, {ms:F1} ms (max {scanCostMax:F1} ms)"); scanCostMax = 0; }
            }
            var scrolls = uiRoot!.GetComponentsInChildren<ScrollRect>(false);
            for (int i = 0; i < scrolls.Length; i++)
            {
                var sr = scrolls[i];
                if (sr != null && sr.isActiveAndEnabled && sr.content != null) menuScrolls.Add(sr);
            }
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: Kandidaten " + e.GetType().Name + ": " + e.Message); }
    }

    // Liegt der Punkt im Rechteck, um margin (Anteil der Groesse) erweitert?
    private bool Contains(Component c, Vector3 world, float margin)
    {
        try
        {
            var rt = c.transform.TryCast<RectTransform>();
            if (rt == null) return false;
            var local = rt.InverseTransformPoint(RayOnRect(rt, out var pe) ? pe : world);
            var rect = rt.rect;
            float mx = rect.width * margin, my = rect.height * margin;
            return local.x >= rect.xMin - mx && local.x <= rect.xMax + mx && local.y >= rect.yMin - my && local.y <= rect.yMax + my;
        }
        catch { return false; }
    }

    // Kleinstes Rechteck, das den Punkt enthaelt: bei verschachtelten Knoepfen
    // (Kachel mit Unterknopf) gewinnt der innere.
    private Selectable? HitCandidate(Vector3 world)
    {
        Selectable? best = null;
        float bestArea = float.MaxValue;
        foreach (var s in menuCandidates)
        {
            try
            {
                if (s == null || !Contains(s, world, 0f)) continue;
                var rt = s.transform.TryCast<RectTransform>();
                var rect = rt!.rect;
                var ls = rt.lossyScale;
                float area = rect.width * rect.height * Math.Abs(ls.x * ls.y);
                if (area < bestArea) { bestArea = area; best = s; }
            }
            catch { }
        }
        return best;
    }

    private PointerEventData Ped(Vector3 world, GameObject? go)
    {
        var es = EventSystem.current;
        if (menuPed == null) menuPed = new PointerEventData(es);
        var p = menuPed;
        try
        {
            var cam = Camera.main;
            if (cam != null) { var sp = cam.WorldToScreenPoint(world); p.position = new Vector2(sp.x, sp.y); p.pressPosition = p.position; }
        }
        catch { }
        p.button = PointerEventData.InputButton.Left;
        p.clickCount = 1;
        p.eligibleForClick = true;
        if (go != null) { p.pointerPress = go; p.rawPointerPress = go; p.pointerEnter = go; }
        return p;
    }

    private void SetHover(Selectable? next, Vector3 world)
    {
        var prev = menuHover;
        if (prev != null)
        {
            try { prev.OnPointerExit(Ped(world, null)); } catch { }
            ChainEvent(prev.gameObject, world, false);
        }
        menuHover = next;
        menuHoverSince = Time.unscaledTime;
        menuHoverLostAt = -1f;
        if (next == null) return;
        try { next.OnPointerEnter(Ped(world, next.gameObject)); } catch { }
        ChainEvent(next.gameObject, world, true);
        try { next.Select(); } catch { }
        // Kein Puls beim Ueberfahren - nur beim Bedienen (Nutzer, 1.9.1).
    }

    private void ClickMenu(Selectable s, Vector3 world, GameObject? from)
    {
        string cap = Caption(s);
        try
        {
            LogComponents(s);
            var ped = Ped(world, s.gameObject);
            string how;
            // Nachruecken: derselbe Knopf binnen 3 s erneut = voriger Weg ohne Wirkung.
            float nowC = Time.unscaledTime;
            clickStage = (s.Pointer == lastClickPtr && nowC - lastClickAt < 3f) ? clickStage + 1 : 0;
            lastClickPtr = s.Pointer;
            lastClickAt = nowC;
            if (clickStage > 0 && s.TryCast<Button>() != null && EscalateClick(s, ped, cap)) return;
            var start = from != null ? from : s.gameObject;
            string? viaEvents = MouseClick(start, ped);
            if (viaEvents != null)
            {
                LoggerInstance.Msg($"MENUE: Klick {cap} -> {viaEvents}");
                Buzz(true, "Menue-Klick");
                nextMenuScan = 0f; nextGraphicScan = 0f;
                return;
            }
            var tmd = s.TryCast<Il2CppTMPro.TMP_Dropdown>();
            var dd = tmd == null ? s.TryCast<Dropdown>() : null;
            var tg = s.TryCast<Toggle>();
            var b = s.TryCast<Button>();
            if (tmd != null) { tmd.OnPointerClick(ped); how = "TMP_Dropdown.OnPointerClick"; }
            else if (dd != null) { dd.OnPointerClick(ped); how = "Dropdown.OnPointerClick"; }
            else if (tg != null) { tg.OnPointerClick(ped); how = $"Toggle.OnPointerClick -> {tg.isOn}"; }
            else if (b != null) { b.OnPointerClick(ped); how = "Button.OnPointerClick"; }
            else
            {
                // Rueckfall fuer das, was kein Mausklick-Ziel ist.
                var go = s.gameObject;
                var mb = go.GetComponent<Il2CppFuturLab.ManagedButtonBase>();
                var fb = go.GetComponent<Il2CppFuturLab.FuturButton>();
                if (mb != null) { mb.Click(); how = "ManagedButtonBase.Click (Rueckfall)"; }
                else if (fb != null) { fb.Submit(); how = "FuturButton.Submit (Rueckfall)"; }
                else { LoggerInstance.Msg($"MENUE: Klick {cap} - {s.GetIl2CppType().Name} nicht bedienbar"); return; }
            }
            LoggerInstance.Msg($"MENUE: Klick {cap} -> {how}");
            Buzz(true, "Menue-Klick");
            nextMenuScan = 0f; nextGraphicScan = 0f;   // ein Klick oeffnet oft neue Knoepfe (Dropdown-Liste, Popup)
        }
        catch (Exception e) { LoggerInstance.Warning($"MENUE: Klick {cap} - {e.GetType().Name}: {e.Message}"); }
    }

    private static bool IsDraggable(Selectable s) => s.TryCast<Slider>() != null || s.TryCast<Scrollbar>() != null;

    // Der Wert folgt dem Strahl entlang der Achse des Reglers, im Rechteck des
    // Reglers selbst gemessen (0 am Anfang, 1 am Ende der Achse).
    private void DragValue(Selectable s, Vector3 world)
    {
        try
        {
            var rt = s.transform.TryCast<RectTransform>();
            if (rt == null) return;
            var local = rt.InverseTransformPoint(RayOnRect(rt, out var pe) ? pe : world);
            var rect = rt.rect;
            var sl = s.TryCast<Slider>();
            if (sl != null)
            {
                var d = sl.direction;
                bool vertical = d == Slider.Direction.BottomToTop || d == Slider.Direction.TopToBottom;
                float t = vertical ? Mathf.InverseLerp(rect.yMin, rect.yMax, local.y) : Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                if (d == Slider.Direction.RightToLeft || d == Slider.Direction.TopToBottom) t = 1f - t;
                sl.normalizedValue = t;
                return;
            }
            var sb = s.TryCast<Scrollbar>();
            if (sb != null)
            {
                var d = sb.direction;
                bool vertical = d == Scrollbar.Direction.BottomToTop || d == Scrollbar.Direction.TopToBottom;
                float t = vertical ? Mathf.InverseLerp(rect.yMin, rect.yMax, local.y) : Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                if (d == Scrollbar.Direction.RightToLeft || d == Scrollbar.Direction.TopToBottom) t = 1f - t;
                sb.value = t;
            }
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: Ziehen " + e.GetType().Name + ": " + e.Message); menuDrag = null; }
    }

    private float scrollLoggedAt;

    // Rechter Stick y scrollt die kleinste ScrollRect unter dem Strahl.
    private void DriveMenuScroll(Vector3 world, XRController r)
    {
        try
        {
            var st = r.TryGetChildControl("thumbstick")?.TryCast<Vector2Control>();
            if (st == null) return;
            float y = st.ReadValue().y;
            if (Math.Abs(y) <= ScrollDeadZone) return;
            ScrollRect? best = null;
            float bestArea = float.MaxValue, ch = 0f, vh = 0f;
            var seen = new System.Text.StringBuilder();
            foreach (var sr in menuScrolls)
            {
                if (sr == null || !Contains(sr, world, 0f)) continue;
                var c = sr.content;
                var v = sr.viewport != null ? sr.viewport : sr.transform.TryCast<RectTransform>();
                float h = c == null ? 0f : c.rect.height, w = v == null ? h : v.rect.height;
                seen.Append($" '{sr.name}' {h:F0}/{w:F0}");
                if (h - w <= Math.Max(5f, w * 0.05f)) continue;   // kein echter Scrollweg
                if (!HasVisibleCandidate(c)) { seen.Append(" (unsichtbar)"); continue; }   // Seite eines anderen Tabs
                var rt = sr.transform.TryCast<RectTransform>();
                float area = rt == null ? float.MaxValue : rt.rect.width * rt.rect.height;
                if (area < bestArea) { bestArea = area; best = sr; ch = h; vh = w; }
            }
            if (best == null)
            {
                // Rueckfall: naechstgelegene sichtbare Liste mit Scrollweg.
                float bestD = float.MaxValue;
                foreach (var sr in menuScrolls)
                {
                    if (sr == null) continue;
                    var c = sr.content;
                    var v = sr.viewport != null ? sr.viewport : sr.transform.TryCast<RectTransform>();
                    float h = c == null ? 0f : c.rect.height, w = v == null ? h : v.rect.height;
                    if (h - w <= Math.Max(5f, w * 0.05f) || !HasVisibleCandidate(c)) continue;
                    var vt = v != null ? v : sr.transform;
                    float d = (vt.position - world).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = sr; ch = h; vh = w; }
                }
                if (best != null) seen.Append(" -> Rueckfall naechste");
            }
            if (best == null && ScrollByScrollbar(world, y)) return;
            string chosen = best == null ? "keine" : best.name;
            if (chosen != scrollChosen) { scrollChosen = chosen; LoggerInstance.Msg($"MENUE: Scrollflaeche {chosen} (unter dem Strahl, Inhalt/Sicht:{seen})"); }
            if (best == null) return;
            float s = Math.Sign(y) * (Math.Abs(y) - ScrollDeadZone) / (1f - ScrollDeadZone);
            // Stick hoch = nach oben = normalizedPosition waechst.
            float step = s * ScrollViewportsPerSecond * Time.unscaledDeltaTime * vh / (ch - vh);
            best.verticalNormalizedPosition = Mathf.Clamp01(best.verticalNormalizedPosition + step);
            float now = Time.unscaledTime;
            if (now - scrollLoggedAt > 2f) { scrollLoggedAt = now; LoggerInstance.Msg($"MENUE: Scrollen '{best.name}' -> {best.verticalNormalizedPosition:F2} (Inhalt {ch:F0}, Sicht {vh:F0})"); }
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: Scrollen " + e.GetType().Name + ": " + e.Message); }
    }

    // Raycastbare Graphics mit ihren Grenzen in UIRoot-Koordinaten, fuer die
    // grobe Vorauswahl je Frame (die genaue Pruefung macht Graphic.Raycast).
    private void ScanMenuGraphics()
    {
        menuGraphics.Clear();
        canvasHasRaycaster.Clear();
        var root = uiRoot!.transform;
        var gs = uiRoot!.GetComponentsInChildren<Graphic>(false);
        for (int i = 0; i < gs.Length; i++)
        {
            var g = gs[i];
            try
            {
                if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.depth < 0) continue;
                if (!RaycastableCanvas(g.canvas)) continue;   // fuer die Maus unsichtbar
                var cr = g.canvasRenderer;
                // Unsichtbar gezeichnet = kein Deckel fuer den Strahl (SideMenuOverlay, 1.9.7).
                if (cr != null && (cr.cull || cr.GetInheritedAlpha() < 0.01f)) continue;
                menuGraphics.Add((g, 0f, 0f, 0f, 0f));
            }
            catch { }
        }
    }

    // Das oberste Graphic unter dem Strahl, wie der GraphicRaycaster sortiert;
    // Ziel ist sein naechster bedienbarer Selectable. Kein Selectable darueber
    // = das Graphic verdeckt nur (Panel, Hintergrund) -> kein Ziel.
    private Selectable? HitTopmost(Vector3 world, Camera cam)
    {
        menuHoverGo = null;
        Graphic? top = null;
        int tl = int.MinValue, to = int.MinValue, td = int.MinValue;
        foreach (var e in menuGraphics)
        {
            try
            {
                var g = e.G;
                if (g == null) continue;
                if (!GeometricHit(g, world)) continue;
                var cv = g.canvas;
                int l = cv == null ? 0 : cv.sortingLayerID == 0 ? 0 : SortingLayer.GetLayerValueFromID(cv.sortingLayerID);
                int o = cv == null ? 0 : cv.sortingOrder;
                int d = g.depth;
                if (l > tl || (l == tl && (o > to || (o == to && d > td)))) { top = g; tl = l; to = o; td = d; }
            }
            catch { }
        }
        if (top == null) { lastBlocker = ""; return null; }
        var t = top.transform;
        for (int i = 0; i < 12 && t != null; i++, t = t.parent)
        {
            var sel = t.GetComponent<Selectable>();
            if (sel == null) continue;
            foreach (var c in menuCandidates)
                if (c != null && c.Pointer == sel.Pointer) { menuHoverGo = top.gameObject; return sel; }
            NoteBlocker(top, $"Selectable '{sel.name}' nicht bedienbar");
            return null;   // der naechste Selectable ist nicht bedienbar (gesperrt/unsichtbar)
        }
        NoteBlocker(top, "kein Selectable darueber");
        return null;
    }

    private void NoteBlocker(Graphic g, string why)
    {
        try
        {
            var p = g.transform.parent;
            var cv = g.canvas;
            string key = $"'{g.name}' unter '{(p == null ? "-" : p.name)}' {g.GetIl2CppType().Name}, Canvas '{(cv == null ? "-" : cv.name)}' order {(cv == null ? 0 : cv.sortingOrder)} depth {g.depth} - {why}";
            if (key == lastBlocker) return;
            lastBlocker = key;
            LoggerInstance.Msg("MENUE: verdeckt von " + key);
        }
        catch { }
    }

    // Traegt die Canvas selbst einen aktiven GraphicRaycaster? Je Scan gemerkt.
    private bool RaycastableCanvas(Canvas? cv)
    {
        if (cv == null) return false;
        var key = cv.Pointer;
        if (canvasHasRaycaster.TryGetValue(key, out var has)) return has;
        has = false;
        try { var gr = cv.GetComponent<GraphicRaycaster>(); has = gr != null && gr.enabled && cv.enabled; } catch { }
        canvasHasRaycaster[key] = has;
        return has;
    }

    // Was Graphic.Raycast prueft, ohne Bildschirmpunkt: Rechteck (in seiner
    // eigenen Ebene), Masken der Eltern, CanvasGroup.blocksRaycasts.
    private bool GeometricHit(Graphic g, Vector3 world)
    {
        var rt = g.rectTransform;
        if (!Contains(g, world, 0f)) return false;
        var p = RayOnRect(rt, out var pe) ? pe : world;
        bool groupsDone = false;
        var stop = uiRoot == null ? null : uiRoot.transform;
        // Ab dem Graphic SELBST, wie Graphic.Raycast: SideMenuOverlay traegt seine
        // sperrende CanvasGroup am eigenen Objekt (1.9.7: Deckel ueber allem).
        Transform? t = rt;
        for (int i = 0; i < 24 && t != null && (stop == null || t.Pointer != stop.Pointer); i++, t = t.parent)
        {
            var go = t.gameObject;
            if (go.GetComponent<RectMask2D>() != null || go.GetComponent<Mask>() != null)
            {
                var mrt = t.TryCast<RectTransform>();
                if (mrt != null)
                {
                    var l = mrt.InverseTransformPoint(p);
                    var r = mrt.rect;
                    if (l.x < r.xMin || l.x > r.xMax || l.y < r.yMin || l.y > r.yMax) return false;
                }
            }
            if (!groupsDone)
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg != null && cg.enabled)
                {
                    if (!cg.blocksRaycasts) return false;
                    if (cg.ignoreParentGroups) groupsDone = true;
                }
            }
        }
        return true;
    }

    // Naechster Weg, wenn der vorige Klick auf denselben Knopf nichts bewirkt hat.
    private bool EscalateClick(Selectable s, PointerEventData ped, string cap)
    {
        try
        {
            var go = s.gameObject;
            string how;
            switch (clickStage)
            {
                case 1: ExecuteEvents.Execute(go, ped, ExecuteEvents.submitHandler); how = "ISubmitHandler (wie Gamepad A)"; break;
                case 2:
                    var fb = go.GetComponent<Il2CppFuturLab.FuturButton>();
                    if (fb == null) return false;
                    fb.Submit(); how = "FuturButton.Submit"; break;
                case 3:
                    var b = s.TryCast<Button>();
                    if (b == null) return false;
                    b.onClick.Invoke(); how = $"Button.onClick.Invoke ({b.onClick.GetPersistentEventCount()} feste Ziele)"; break;
                default: clickStage = 0; return false;
            }
            LoggerInstance.Msg($"MENUE: Klick {cap} erneut (Stufe {clickStage}) -> {how}");
            Buzz(true, "Menue-Klick");
            nextMenuScan = 0f; nextGraphicScan = 0f;
            return true;
        }
        catch (Exception e) { LoggerInstance.Warning($"MENUE: Klick {cap} Stufe {clickStage} - {e.GetType().Name}: {e.Message}"); return false; }
    }

    // Rueckfall: der naechste sichtbare Scrollbalken mit Spielraum (size < 1).
    private bool ScrollByScrollbar(Vector3 world, float y)
    {
        Scrollbar? best = null;
        float bestD = float.MaxValue;
        foreach (var c in menuCandidates)
        {
            try
            {
                var sb = c == null ? null : c.TryCast<Scrollbar>();
                if (sb == null || sb.size >= 0.99f) continue;
                var dd = sb.direction;
                if (dd != Scrollbar.Direction.BottomToTop && dd != Scrollbar.Direction.TopToBottom) continue;
                float d = (sb.transform.position - world).sqrMagnitude;
                if (d < bestD) { bestD = d; best = sb; }
            }
            catch { }
        }
        if (best == null) return false;
        float s = Math.Sign(y) * (Math.Abs(y) - ScrollDeadZone) / (1f - ScrollDeadZone);
        float span = Math.Max(0.01f, 1f - best.size);
        float step = s * ScrollViewportsPerSecond * Time.unscaledDeltaTime * best.size / span;
        if (best.direction == Scrollbar.Direction.TopToBottom) step = -step;
        best.value = Mathf.Clamp01(best.value + step);
        string name = best.name + "/" + (best.transform.parent == null ? "" : best.transform.parent.name);
        if (name != scrollChosen) { scrollChosen = name; LoggerInstance.Msg($"MENUE: Scrollen ueber den Scrollbalken '{name}' (size {best.size:F2})"); }
        return true;
    }

    // Schnittpunkt des Menue-Strahls mit der Ebene DIESES Rechtecks.
    private bool RayOnRect(RectTransform rt, out Vector3 p)
    {
        p = default;
        if (!rayValid) return false;
        var n = rt.forward;
        float denom = Vector3.Dot(rayD, n);
        if (Math.Abs(denom) < 1e-4f) return false;
        float t = Vector3.Dot(rt.position - rayO, n) / denom;
        if (t <= 0f || t > 20f) return false;
        p = rayO + rayD * t;
        return true;
    }

    private bool HasVisibleCandidate(RectTransform? content)
    {
        if (content == null) return false;
        foreach (var s in menuCandidates)
        {
            try { if (s != null && s.transform.IsChildOf(content)) return true; } catch { }
        }
        return false;
    }

    // Enter/Exit an die Eltern des Ziels bis unter UIRoot, wie die Maus sie
    // entlang der Hierarchie meldet. Das Ziel selbst hat SetHover schon bedient.
    private void ChainEvent(GameObject go, Vector3 world, bool enter)
    {
        try
        {
            var ped = Ped(world, enter ? go : null);
            var t = go.transform.parent;
            var stop = uiRoot == null ? null : uiRoot.transform;
            for (int i = 0; i < 8 && t != null && (stop == null || t.Pointer != stop.Pointer); i++, t = t.parent)
            {
                if (enter) ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.pointerEnterHandler);
                else ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.pointerExitHandler);
            }
        }
        catch { }
    }

    private void LogComponents(Selectable s)
    {
        try
        {
            string key = s.gameObject.name + "/" + (s.transform.parent == null ? "" : s.transform.parent.name);
            if (!componentsLogged.Add(key)) return;
            var sb = new System.Text.StringBuilder();
            var t = s.transform;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
            {
                var comps = t.GetComponents<Component>();
                var names = new List<string>();
                for (int k = 0; k < comps.Length; k++) if (comps[k] != null) names.Add(comps[k].GetIl2CppType().Name);
                sb.Append($" | [{i}] '{t.name}': {string.Join(", ", names)}");
            }
            LoggerInstance.Msg("MENUE: Komponenten" + sb);
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: Komponenten " + e.GetType().Name); }
    }

    private void HideTool()
    {
        try
        {
            var anchor = gunAnchor;
            if (anchor == null) return;
            var rs = anchor.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                toolHidden.Add(r);
                n++;
            }
            if (n > 0) LoggerInstance.Msg($"MENUE: Werkzeug ausgeblendet, {n} Renderer ({toolHidden.Count} gesamt) unter '{anchor.name}'");
        }
        catch (Exception e) { LoggerInstance.Warning("MENUE: Werkzeug ausblenden " + e.GetType().Name + ": " + e.Message); }
    }

    private void ShowTool()
    {
        if (toolHidden.Count == 0) return;
        int n = 0;
        foreach (var r in toolHidden) { try { if (r != null) { r.enabled = true; n++; } } catch { } }
        LoggerInstance.Msg($"MENUE: Werkzeug wieder sichtbar, {n} von {toolHidden.Count} Renderern");
        toolHidden.Clear();
    }

    // Die Mausfolge ueber das EventSystem. null = ging nicht, dann Rueckfall.
    private string? MouseClick(GameObject go, PointerEventData ped)
    {
        try
        {
            var down = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            var target = down != null ? down : go;
            ped.pointerPress = target;
            ped.rawPointerPress = go;
            var up = ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
            var click = ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);
            if (click == null && down == null) return null;
            return $"Mausfolge (Down {(down == null ? "-" : "'" + down.name + "'")}, Up {(up == null ? "-" : "'" + up.name + "'")}, Click {(click == null ? "-" : "'" + click.name + "'")})";
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("MENUE: Mausfolge " + e.GetType().Name + ": " + e.Message + " - Rueckfall");
            return null;
        }
    }

    // Tiefpass auf die Strahlrichtung; die Zeitkonstante faellt mit der
    // Winkelgeschwindigkeit, damit Zielen ruhig und Schwenken direkt bleibt.
    private Vector3 SmoothMenuDir(Vector3 raw)
    {
        float dt = Math.Max(1e-4f, Time.unscaledDeltaTime);
        if (!menuDirHave) { menuDirSmooth = raw; menuDirHave = true; return raw; }
        float deg = Vector3.Angle(menuDirSmooth, raw) / dt;
        float f = Mathf.InverseLerp(SmoothSlowDeg, SmoothFastDeg, deg);
        float tau = Mathf.Lerp(TauSlow, TauFast, f);
        float k = 1f - (float)Math.Exp(-dt / tau);
        menuDirSmooth = Vector3.Slerp(menuDirSmooth, raw, k).normalized;
        return menuDirSmooth;
    }

    private static string Caption(Selectable s)
    {
        try
        {
            string text = "";
            var tmp = s.GetComponentInChildren<Il2CppTMPro.TMP_Text>();
            if (tmp != null) text = tmp.text ?? "";
            if (text.Length > 30) text = text.Substring(0, 30);
            var p = s.transform.parent;
            return $"'{s.gameObject.name}'{(p == null ? "" : " unter '" + p.name + "'")} {s.GetIl2CppType().Name}{(text.Length > 0 ? " \"" + text.Replace("\n", " ") + "\"" : "")}";
        }
        catch { return "?"; }
    }

    private void DrawMenuLine(Vector3 from, Vector3 to, bool hasTarget)
    {
        if (menuLineGo == null)
        {
            menuLineGo = new GameObject("WetReality_MenuPointer");
            UnityEngine.Object.DontDestroyOnLoad(menuLineGo);
            menuLine = menuLineGo.AddComponent<LineRenderer>();
            menuLine.useWorldSpace = true;
            menuLine.positionCount = 2;
            menuLine.startWidth = 0.006f;
            menuLine.endWidth = 0.004f;
            menuLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            menuLine.receiveShadows = false;
            var sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var m = new Material(sh);
                // Ueber allem, wie die UI (sonst verschwaende der Strahl hinter ihr).
                foreach (var name in ZTestProperties) SetIntSafe(m, name, CompareAlways);
                m.renderQueue = 5000;
                menuLine.material = m;
            }
        }
        if (menuLine == null) return;
        var c = hasTarget ? new Color(1f, 0.35f, 0.75f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.6f);
        menuLine.startColor = c;
        menuLine.endColor = c;
        menuLine.SetPosition(0, from);
        menuLine.SetPosition(1, to);
        menuLine.enabled = true;
    }

    private void HideMenuLine()
    {
        if (menuLine != null && menuLine.enabled) menuLine.enabled = false;
    }
}
