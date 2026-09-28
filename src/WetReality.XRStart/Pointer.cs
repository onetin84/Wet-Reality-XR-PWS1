// Zeigestrahl der Off-Hand fuer Interaktion (XRStart 1.3.0): X nimmt, worauf
// die LINKE Hand zeigt.
//
// 1.2.0 hatte den Selektor an die Pistole gehaengt - falsch: in PWS2 zielt
// die FREIE Hand (AimRay -> OffHandForward, Aim-Pose der Off-Hand), und der
// Zeigestrahl erscheint nur, wenn ein Ziel da ist (DriveGrabPointer). Und X
// nahm trotz "zeigt auf ein Objekt" nichts auf - ungeklaert, darum misst
// diese Version beim Druck mit (DiagnosePickup).
//
// PWS1 waehlt das Ziel anders als PWS2: InteractableItemRaycastSelector (auf
// PlayerCamera) raycastet in Update entlang m_cameraTransform und meldet das
// Ergebnis ueber ItemSelectionChanged an ItemInteractionManager.m_selectedItem;
// HandlePickUpInput nimmt genau dieses. Die Mod gibt dem Selektor einen
// eigenen Zeiger-Transform auf der Aim-Pose der linken Hand - das SPIEL zielt
// dann aus der Hand, mit eigener Pruefung und eigenem Highlight. PWS2s eigene
// Zielsuche (TryAimPickup) entfaellt: SetTargetAndInteractStateImmediate gibt
// es in PWS1 nicht, und sie brauchte es nur, weil der Selektor dort nicht
// umzulenken war.
//
// Die Linie (LineRenderer, Sprites/Default) nur, solange der Selektor ein
// gueltiges Objekt haelt, von der Hand zum naechsten Punkt seines Colliders
// (Collider.ClosestPoint - transform.position luegt bei animierten Objekten).
//
// REICHWEITE (1.10.0): der Selektor sucht bis m_maxDistance - im Spiel 1000 m
// (Log seit 1.3.x), gedacht fuer den Kopfstrahl. Aus der Hand waren Leitern so
// aus jeder Entfernung greifbar (Nutzer). PWS2 sucht AimInteractionRange = 5 m
// ab der Hand (Pose.cs, Vorgabe) - hier als InteractionRange (cfg), gesetzt,
// solange der Selektor umgelenkt ist, und beim Zuruecknehmen zurueck.
//
// Aktiv, solange der Kopf geschrieben wird (F7): toWorld braucht die Kamera,
// die DriveHead eben gesetzt hat. Das Tragen (MovableItemHandler, eigenes
// m_cameraTransform) bleibt vorerst am Kopf.

using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private GameObject? pointerGo;
    private LineRenderer? pointerLine;
    private static InteractableItemRaycastSelector? selector;
    private Transform? selectorOriginal;
    private float selectorOrigMax = -1f;
    private bool selectorRedirected;
    private float nextSelectorResolve;
    private bool lastHasTarget;
    private string pointerSource = "";
    private static ItemInteractionManager? itemManager;
    private int lastUsableColliders = -1;

    // Das Tragen: dieselbe Umlenkung wie beim Selektor, auf die Halter des
    // getragenen Objekts. Drei Kandidaten mit eigenem m_cameraTransform
    // (dump.cs); welche es im Spiel gibt, sagt das Log. Umgelenkt wird nur,
    // was auf die Kamera zeigt - fremde Transforms bleiben unberuehrt.
    private readonly Dictionary<IntPtr, (string name, Func<Transform?> get, Action<Transform?> set, Transform? original)> carriers = new();
    private float nextCarrierResolve;

    private void RedirectCarriers()
    {
        float now = Time.unscaledTime;
        var cam = Camera.main;
        if (cam == null || pointerGo == null) return;
        var ptr = pointerGo.transform;
        if (now >= nextCarrierResolve)
        {
            nextCarrierResolve = now + 1f;
            foreach (var h in UnityEngine.Object.FindObjectsOfType<PickableItemHandler>()) AddCarrier("PickableItemHandler", h, () => h.m_cameraTransform, t => h.m_cameraTransform = t);
            foreach (var h in UnityEngine.Object.FindObjectsOfType<MovableItemHandler>()) AddCarrier("MovableItemHandler", h, () => h.m_cameraTransform, t => h.m_cameraTransform = t);
            foreach (var h in UnityEngine.Object.FindObjectsOfType<ItemMoverManager>()) AddCarrier("ItemMoverManager", h, () => h.m_cameraTransform, t => h.m_cameraTransform = t);
        }
        foreach (var kv in carriers)
        {
            try
            {
                var cur = kv.Value.get();
                if (cur == null || cur.Pointer != ptr.Pointer) kv.Value.set(ptr);
            }
            catch { }
        }
    }

    private void AddCarrier(string name, Component h, Func<Transform?> get, Action<Transform?> set)
    {
        if (carriers.ContainsKey(h.Pointer)) return;
        Transform? orig = null;
        try { orig = get(); } catch { }
        var cam = Camera.main;
        bool onCamera = orig != null && cam != null && orig.Pointer == cam.transform.Pointer;
        LoggerInstance.Msg($"ZEIGER: Traeger {name} auf '{h.gameObject.name}', m_cameraTransform='{Name(orig)}' {(onCamera ? "-> auf die linke Hand umgelenkt" : "- nicht die Kamera, bleibt")}");
        if (onCamera) carriers[h.Pointer] = (name, get, set, orig);
    }

    private void ReleaseCarriers()
    {
        foreach (var kv in carriers)
        {
            try { kv.Value.set(kv.Value.original); } catch { }
        }
        if (carriers.Count > 0) LoggerInstance.Msg($"ZEIGER: {carriers.Count} Traeger zurueck auf die Kamera");
        carriers.Clear();
    }

    private Transform EnsurePointer()
    {
        if (pointerGo == null)
        {
            pointerGo = new GameObject("WetReality_Pointer");
            UnityEngine.Object.DontDestroyOnLoad(pointerGo);
            pointerLine = pointerGo.AddComponent<LineRenderer>();
            pointerLine.useWorldSpace = true;
            pointerLine.positionCount = 2;
            pointerLine.startWidth = 0.006f;
            pointerLine.endWidth = 0.003f;
            pointerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pointerLine.receiveShadows = false;
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) pointerLine.material = new Material(sh);
            var c = new Color(0.3f, 0.8f, 1f, 0.75f);   // leicht durchsichtig, wie PWS2
            pointerLine.startColor = c;
            pointerLine.endColor = c;
            pointerLine.enabled = false;
            LoggerInstance.Msg($"ZEIGER: Zeiger-Transform und Linie angelegt (Shader {(sh == null ? "Sprites/Default FEHLT" : sh.name)})");
        }
        return pointerGo.transform;
    }

    // In onBeforeRender nach DriveHead, mit derselben Kamera.
    private void DriveOffHandPointer()
    {
        if (!writeHead || !inputOn) { ReleasePointer(); return; }
        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            var l = OffCtl;   // Griffzeiger aus der freien Hand
            var cam = Camera.main;
            if (hmd == null || l == null || cam == null || !l.isTracked.isPressed) { HideLine(); return; }

            var hmdPos = hmd.centerEyePosition.ReadValue();
            var hmdRot = hmd.centerEyeRotation.ReadValue();
            var grip = l.devicePosition.ReadValue();
            var aimCtl = l.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
            var aim = aimCtl != null ? aimCtl.ReadValue() : l.deviceRotation.ReadValue();
            string src = aimCtl != null ? "Aim-Pose" : "Grip-Pose (kein pointerRotation)";
            if (src != pointerSource) { pointerSource = src; LoggerInstance.Msg("ZEIGER: Off-Hand-Richtung aus " + src); }

            var camT = cam.transform;
            var toWorld = camT.rotation * Quaternion.Inverse(hmdRot);
            var origin = camT.position + toWorld * (grip - hmdPos);
            var rot = toWorld * aim;
            var p = EnsurePointer();
            p.SetPositionAndRotation(origin, rot);

            RedirectSelector();
            RedirectCarriers();
            DrawLine(origin, rot * Vector3.forward);
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("ZEIGER: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private void RedirectSelector()
    {
        float now = Time.unscaledTime;
        if (selector == null || now >= nextSelectorResolve)
        {
            nextSelectorResolve = now + 1f;
            var cam = Camera.main;
            var s = cam == null ? null : cam.GetComponent<InteractableItemRaycastSelector>();
            if (s == null) s = UnityEngine.Object.FindObjectOfType<InteractableItemRaycastSelector>();
            if (s != null && (selector == null || s.Pointer != selector.Pointer))
            {
                selector = s;
                selectorRedirected = false;
                selectorOrigMax = -1f;
                selectorOriginal = s.m_cameraTransform;
                LoggerInstance.Msg($"ZEIGER: Selektor auf '{s.gameObject.name}', m_cameraTransform='{Name(selectorOriginal)}', " +
                    $"m_maxDistance={s.m_maxDistance:F2}, Maske {s.m_LayerMask.value}, gueltig {s.m_validItemLayerMask.value}");
            }
            if (itemManager == null) itemManager = UnityEngine.Object.FindObjectOfType<ItemInteractionManager>();
        }
        if (selector == null) return;
        var cur = selector.m_cameraTransform;
        if (cur == null || cur.Pointer != pointerGo!.transform.Pointer)
        {
            if (selectorRedirected) LoggerInstance.Msg($"ZEIGER: Selektor-Transform zurueckgesetzt auf '{Name(cur)}' - neu gesetzt");
            selector.m_cameraTransform = pointerGo!.transform;
            selectorRedirected = true;
        }
        // Reichweite aus der Hand wie PWS2; nur bei Abweichung geschrieben.
        try
        {
            float want = Math.Max(0.5f, prefInteractionRange.Value);
            float have = selector.m_maxDistance;
            if (Math.Abs(have - want) > 0.001f)
            {
                if (selectorOrigMax < 0f) selectorOrigMax = have;
                selector.m_maxDistance = want;
                LoggerInstance.Msg($"ZEIGER: Reichweite {have:F2} -> {want:F2} m (InteractionRange, PWS2 AimInteractionRange)");
            }
        }
        catch { }
    }

    // Das aktuelle GUELTIGE Objekt des Selektors als Komponente. m_currentItem
    // ist ein Interface-Wrapper: keine Aufrufe darauf (PWS2 §118), erst TryCast.
    private static Component? SelectedItem()
    {
        try
        {
            var it = selector == null ? null : selector.m_currentItem;
            return it == null ? null : it.TryCast<Component>();
        }
        catch { return null; }
    }

    private void DrawLine(Vector3 origin, Vector3 forward)
    {
        // Beim Spruehen aus (PWS2 grabPointerWhileSpraying = false), OHNE das
        // Ziel zu vergessen: nach dem Loslassen pulst es nur bei einem Wechsel.
        if (SprayingNow)
        {
            if (pointerLine != null && pointerLine.enabled) pointerLine.enabled = false;
            return;
        }
        var item = SelectedItem();
        bool has = item != null;
        if (has != lastHasTarget)
        {
            lastHasTarget = has;
            LoggerInstance.Msg(has ? $"ZEIGER: Ziel '{item!.gameObject.name}' ({item.GetIl2CppType().Name})" : "ZEIGER: kein Ziel");
            Buzz(false, has ? "Ziel erfasst" : "Ziel verloren");   // PWS2 grab ready / grab lost
        }
        if (!has) { HideLine(); return; }

        // Endpunkt: der Punkt der FESTEN Collider, der dem STRAHL am naechsten
        // liegt. 1.3.0 nahm ClosestPoint zur Hand am ersten Collider - bei der
        // Leiter lag der Punkt weit ueber der Oberkante (Nutzerbild): ein
        // Trigger-Volumen oder die falsche Bezugsgroesse. Zweimal
        // angenaehert: naechster Punkt zur Hand, auf den Strahl projiziert,
        // davon wieder der naechste Punkt.
        //
        // 1.3.1: der Strahl war dann unsichtbar. Vermutlich nicht-konvexe
        // MeshCollider: deren ClosestPoint gibt den Abfragepunkt zurueck, also
        // die Hand - Laenge null. Darum seit 1.3.2: ein ClosestPoint, der den
        // Abfragepunkt selbst liefert, zaehlt nicht, und das Ende liegt IMMER
        // auf dem Strahl, in der Tiefe des besten Punkts (oder der Position).
        var target = item!.transform.position;
        int usable = 0;
        try
        {
            var cols = item.GetComponentsInChildren<Collider>();
            float best = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || !c.enabled || c.isTrigger) continue;
                var p0 = c.ClosestPoint(origin);
                if ((p0 - origin).sqrMagnitude < 1e-6f) continue;   // entartet: Abfragepunkt zurueck
                var q = origin + forward * Math.Max(0f, Vector3.Dot(p0 - origin, forward));
                var p1 = c.ClosestPoint(q);
                float d = (p1 - q).sqrMagnitude;
                if (d < best) { best = d; target = p1; }
                usable++;
            }
        }
        catch { }
        float depth = Math.Max(0.2f, Vector3.Dot(target - origin, forward));
        var end = origin + forward * depth;
        if (usable != lastUsableColliders)
        {
            lastUsableColliders = usable;
            LoggerInstance.Msg($"ZEIGER: {usable} brauchbare Collider am Ziel, Strahllaenge {depth:F2} m");
        }
        if (pointerLine == null) return;
        var bc = BeamTint();   // eine Farbe fuer alle Zeiger (PointerStyle.cs)
        pointerLine.startColor = bc;
        pointerLine.endColor = bc;
        pointerLine.SetPosition(0, origin);
        pointerLine.SetPosition(1, end);
        pointerLine.enabled = true;
    }

    private void HideLine()
    {
        if (pointerLine != null && pointerLine.enabled) pointerLine.enabled = false;
        if (lastHasTarget) { lastHasTarget = false; LoggerInstance.Msg("ZEIGER: kein Ziel"); Buzz(false, "Ziel verloren"); }
    }

    private void ReleasePointer()
    {
        HideLine();
        ReleaseCarriers();
        try
        {
            if (selectorRedirected && selector != null)
            {
                selector.m_cameraTransform = selectorOriginal;
                if (selectorOrigMax >= 0f) selector.m_maxDistance = selectorOrigMax;
                LoggerInstance.Msg($"ZEIGER: Selektor zurueck auf '{Name(selectorOriginal)}'");
            }
        }
        catch { }
        selectorRedirected = false;
        selectorOrigMax = -1f;
        selector = null;
    }

    // ---- X-Diagnose: was halten Selektor und Manager vor und nach dem Druck?
    private static int pickupDiagFrames = -1;
    private static readonly List<string> pickupDiag = new();

    private static string PickupState()
    {
        string Obj(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? o)
        {
            if (o == null) return "null";
            try { var c = o.TryCast<Component>(); return c == null ? "?" : $"'{c.gameObject.name}' ({c.GetIl2CppType().Name})"; }
            catch { return "Lesefehler"; }
        }
        try
        {
            var sel = selector == null ? "kein Selektor" : $"sieht {selector.m_lookingAtItem}, aktuell {Obj(selector.m_currentItem)}";
            var mgr = itemManager == null ? "kein Manager" : $"ausgewaehlt {Obj(itemManager.m_selectedItem)}, benutzt {Obj(itemManager.m_currentItem)}";
            return $"Selektor {sel} | Manager {mgr}";
        }
        catch (Exception e) { return "Lesefehler " + e.GetType().Name; }
    }

    // Aus UpdateButtons beim X-Druck, VOR dem Aufruf.
    private static void StartPickupDiag()
    {
        pickupDiag.Add("X vorher: " + PickupState());
        pickupDiagFrames = 30;
    }

    // Aus OnUpdate: 30 Frames nach dem Druck der Stand danach.
    private void TickPickupDiag()
    {
        if (pickupDiagFrames > 0 && --pickupDiagFrames == 0)
        {
            pickupDiag.Add("X nachher (30 Frames): " + PickupState());
            pickupDiagFrames = -1;
        }
        foreach (var d in pickupDiag) LoggerInstance.Msg("ZEIGER: " + d);
        pickupDiag.Clear();
    }
}
