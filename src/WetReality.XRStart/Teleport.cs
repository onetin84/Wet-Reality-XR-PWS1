// Ziel-Teleport (XRStart 1.17.0), Stufe 1. Port des Kerns von PWS2 TeleportAim
// (PWS2-Handbuch §6 "Das Teleportziel" bis "Kein Teleport in der Luft").
//
// PWS1 hat KEINE Teleport-Methode (dump.cs): kein TeleportTo; PlayerTeleport,
// BNGPlayerController, ForcePlayerTeleport gehoeren zum BNG-VR-Rahmen ohne
// Instanz im Build (Handbuch 1.4). Der Charakter faehrt ueber einen Rigidbody
// (PhysicalCharacterController.m_body) und kennt m_groundPoint, m_grounded,
// m_jumpHeight, m_navmeshCollisionLayer. Versetzt wird darum um GENAU
// Ziel - m_groundPoint: Rigidbody und Transform verschoben, Geschwindigkeit null
// - der Pivot des Spiels bleibt, wie er ist.
//
// WIE PWS2 (dort im Headset getragen):
//   - Tore: Schwelle 0,7, Dominanz |y| > 2,5 |x|, 0,15 s einschwingen; wieder
//     bewaffnet erst mit beiden Achsen in der Totzone 0,2. Halten nur ueber
//     Richtung/Totzone - gesprungen wird, wenn der Stick ZURUECKKOMMT.
//   - Beim Zielen dreht der Stick nicht (TeleportTurnLock).
//   - Wurfbogen aus der Hand, die drueckt. Reichweite = Sprungparabel:
//     V = sqrt(g (sqrt(y0^2 + R^2) - y0)), R = s * 2 v0 / g, v0 = sqrt(2 g h),
//     h = m_jumpHeight, s = TeleportJumpSpeed (PWS2 gemessen 7,0 m/s), y0 =
//     Handhoehe ueber dem Fuss.
//   - Treffer per Bisektion ueber Bool-Raycasts (kein RaycastHit - Interop).
//   - Gueltig: am Boden (m_grounded; unlesbar = am Boden), Kante <= h + 0,35 m,
//     Boden unter dem Ziel, Platzprobe CheckCapsule r 0,22 m, 0,45-1,65 m.
//   - Nach unten keine Grenze (Leiter, Dach) - ins Leere geht es nicht, der Bogen
//     muss treffen.
// NOCH NICHT (Stufe 2): Treppe als Weg (TeleportSlopeWalk), Leiter-Ziel.
//
// 1.17.1: Zielmarker wie PWS2 (PointerStyle.cs), eine Farbe fuer alle Zeiger
//   (PointerColor, Vorgabe blue), KEIN Teleport beim Spruehen (Nutzer): kein
//   Start, laufendes Zielen bricht ab.
//
// KOMFORT ComfortTeleport (cfg, aus - PWS2-Name fuer den Konfigurator): linker
// Stick nach vorn teleportiert aus der LINKEN Hand und ersetzt das Gehen.

using Il2CppPWS;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const float TpThreshold = 0.7f, TpDominance = 2.5f, TpSettle = 0.15f, TpDead = 0.2f, TpRelease = 0.3f;
    private const float TpRiseTolerance = 0.35f, TpProbeRadius = 0.22f, TpProbeLift = 0.45f, TpProbeTop = 1.65f;
    private const float TpTraceDrop = 25f, TpStep = 0.03f, TpMaxTime = 3f;

    internal static bool tpAiming, tpComfortActive, tpFromLeftStatic;   // ControllerInput: Drehen/Gehen ruhen
    private bool tpArmedR = true, tpArmedL = true;
    private float tpSinceR = -1f, tpSinceL = -1f;
    private bool tpFromLeft;
    private Vector3 tpTarget;
    private bool tpValid;
    private string tpWhy = "";
    private GameObject? tpArcGo;
    private LineRenderer? tpArc;
    private readonly List<Vector3> tpPath = new();
    private bool tpEnvelopeLogged;
    private float tpNextWhyLog;
    private string tpLastWhy = "";
    private int tpDone;

    // In onBeforeRender nach Kopf und Pistole.
    private void DriveTeleport()
    {
        if (!started || !writeHead || menuActive || wheelOpen) { EndAim(false); return; }
        try
        {
            var r = XRController.rightHand;
            var l = XRController.leftHand;
            float now = Time.unscaledTime;
            // Beim Spruehen kein Teleport (Nutzer): kein Start, Zielen bricht ab.
            if (SprayingNow) { if (tpAiming) { LoggerInstance.Msg("TELEPORT: abgebrochen - es wird gesprueht"); EndAim(false); } tpSinceR = tpSinceL = -1f; tpArmedR = tpArmedL = false; return; }

            // Pistolenhand immer; die freie Hand nur im Komfortmodus.
            bool comfort = prefComfortTeleport.Value;
            tpComfortActive = comfort;
            if (!tpAiming || !tpFromLeft) Gate(r, ref tpArmedR, ref tpSinceR, now, false);
            if (comfort && (!tpAiming || tpFromLeft)) Gate(l, ref tpArmedL, ref tpSinceL, now, true);

            if (!tpAiming) return;
            var c = tpFromLeft ? l : r;
            var st = StickOf(c);
            // Halten nur ueber Richtung und Totzone: seitlich rollen bricht nicht ab.
            if (st.magnitude < TpRelease) { EndAim(true); return; }
            if (st.y < 0f) { EndAim(false); return; }   // nach hinten gezogen = abbrechen

            AimArc(c);
        }
        catch (Exception e) { LoggerInstance.Warning("TELEPORT: " + e.GetType().Name + ": " + e.Message); EndAim(false); }
    }

    private static Vector2 StickOf(XRController? c)
    {
        try { var s = c?.TryGetChildControl("thumbstick")?.TryCast<Vector2Control>(); return s == null ? Vector2.zero : s.ReadValue(); }
        catch { return Vector2.zero; }
    }

    // Start-Tor: Schwelle, Dominanz, Einschwingzeit; bewaffnet erst nach der Totzone.
    private void Gate(XRController? c, ref bool armed, ref float since, float now, bool left)
    {
        var st = StickOf(c);
        if (Math.Abs(st.x) < TpDead && Math.Abs(st.y) < TpDead) { armed = true; since = -1f; return; }
        bool push = armed && st.y > TpThreshold && st.y > TpDominance * Math.Abs(st.x);
        if (!push) { since = -1f; return; }
        if (since < 0f) { since = now; return; }
        if (now - since < TpSettle) return;
        armed = false;
        since = -1f;
        tpAiming = true;
        tpFromLeft = left;
        tpFromLeftStatic = left;
        LoggerInstance.Msg($"TELEPORT: zielen ({(left ? "linke Hand, Komfort" : "Pistolenhand")})");
    }

    private void AimArc(XRController? c)
    {
        var hmd = InputSystem.GetDevice<XRHMD>();
        var cam = Camera.main;
        var ch = charCtl;
        if (c == null || hmd == null || cam == null || ch == null) return;

        var camT = cam.transform;
        var toWorld = camT.rotation * Quaternion.Inverse(hmd.centerEyeRotation.ReadValue());
        var origin = camT.position + toWorld * (c.devicePosition.ReadValue() - hmd.centerEyePosition.ReadValue());
        var aimCtl = c.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
        var dir = (toWorld * (aimCtl != null ? aimCtl.ReadValue() : c.deviceRotation.ReadValue())) * Vector3.forward;

        // Huelle aus dem Spiel
        float h = 1.5f; int mask = 0; bool grounded = true; Vector3 foot = ch.transform.position;
        try { h = ch.m_jumpHeight; } catch { }
        try { mask = ch.m_navmeshCollisionLayer.m_Mask; } catch { }
        try { grounded = ch.m_grounded; } catch { grounded = true; }
        try { foot = ch.m_groundPoint; } catch { }
        if (mask == 0) mask = Physics.DefaultRaycastLayers;
        float g = Math.Max(0.1f, Math.Abs(Physics.gravity.y));
        float s = Math.Max(0.1f, prefTeleportJumpSpeed.Value);
        float v0 = Mathf.Sqrt(2f * g * Math.Max(0.01f, h));
        float reach = s * 2f * v0 / g;
        float y0 = Math.Max(0f, origin.y - foot.y);
        float V = Mathf.Sqrt(g * (Mathf.Sqrt(y0 * y0 + reach * reach) - y0));
        if (!tpEnvelopeLogged)
        {
            tpEnvelopeLogged = true;
            LoggerInstance.Msg($"TELEPORT: Huelle h {h:F2} m, s {s:F2} m/s (TeleportJumpSpeed), g {g:F2}, flach {reach:F2} m, Maske 0x{mask:X8}, Handhoehe {y0:F2} m -> V {V:F2} m/s");
        }

        // Bogen: je Segment ein Bool-Strahl, beim ersten Treffer Bisektion.
        tpPath.Clear();
        tpPath.Add(origin);
        var p = origin;
        var vel = dir.normalized * V;
        bool hitSomething = false;
        Vector3 hit = origin;
        for (float t = 0f; t < TpMaxTime; t += TpStep)
        {
            var next = p + vel * TpStep + 0.5f * Physics.gravity * TpStep * TpStep;
            vel += Physics.gravity * TpStep;
            var seg = next - p;
            float len = seg.magnitude;
            if (len > 1e-5f && Physics.Raycast(p, seg / len, len, mask, QueryTriggerInteraction.Ignore))
            {
                float lo = 0f, hi = len;
                for (int i = 0; i < 12; i++) { float mid = (lo + hi) * 0.5f; if (Physics.Raycast(p, seg / len, mid, mask, QueryTriggerInteraction.Ignore)) hi = mid; else lo = mid; }
                hit = p + seg / len * hi;
                tpPath.Add(hit);
                hitSomething = true;
                break;
            }
            p = next;
            tpPath.Add(p);
            if (p.y < foot.y - TpTraceDrop) break;
        }

        tpValid = false;
        if (!hitSomething) tpWhy = "Bogen trifft nichts";
        else if (!grounded) tpWhy = "nicht am Boden";
        else
        {
            float rise = hit.y - foot.y;
            if (rise > h + TpRiseTolerance) tpWhy = $"zu hoch: {rise:F2} m > {h + TpRiseTolerance:F2} m";
            else if (!Physics.Raycast(hit + Vector3.up * 0.1f, Vector3.down, 0.3f, mask, QueryTriggerInteraction.Ignore)) tpWhy = "kein Boden (Wand?)";
            else
            {
                var bottom = hit + Vector3.up * (TpProbeLift + TpProbeRadius);
                var top = hit + Vector3.up * (TpProbeTop - TpProbeRadius);
                if (Physics.CheckCapsule(bottom, top, TpProbeRadius, mask, QueryTriggerInteraction.Ignore)) tpWhy = "kein Platz";
                else { tpValid = true; tpWhy = $"ok, {Vector3.Distance(new Vector3(hit.x, 0, hit.z), new Vector3(foot.x, 0, foot.z)):F2} m, Hoehe {rise:+0.00;-0.00} m"; }
            }
        }
        tpTarget = hit;
        float now = Time.unscaledTime;
        if (tpWhy != tpLastWhy && now >= tpNextWhyLog) { tpLastWhy = tpWhy; tpNextWhyLog = now + 0.5f; LoggerInstance.Msg("TELEPORT: " + tpWhy); }
        DrawTeleport(hitSomething);
    }

    private void EndAim(bool jump)
    {
        if (!tpAiming) { HideTeleport(); return; }
        tpAiming = false;
        HideTeleport();
        if (!jump) { LoggerInstance.Msg("TELEPORT: abgebrochen"); return; }
        if (!tpValid) { LoggerInstance.Msg("TELEPORT: kein Sprung - " + tpWhy); Buzz(tpFromLeft ? false : true, "Teleport ungueltig"); return; }
        var ch = charCtl;
        if (ch == null) return;
        try
        {
            Vector3 foot = ch.transform.position;
            try { foot = ch.m_groundPoint; } catch { }
            var delta = tpTarget - foot;
            var body = ch.m_body;
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.position = body.position + delta;
            }
            ch.transform.position = ch.transform.position + delta;
            tpDone++;
            LoggerInstance.Msg($"TELEPORT: gesprungen um {delta.ToString("F2")} ({tpWhy}) - {tpDone}. Teleport");
            Buzz(tpFromLeft ? false : true, "Teleport");
        }
        catch (Exception e) { LoggerInstance.Warning("TELEPORT: Versetzen " + e.GetType().Name + ": " + e.Message); }
    }

    private LineRenderer MakeLine(string name, out GameObject go)
    {
        go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) lr.material = new Material(sh);
        lr.enabled = false;
        return lr;
    }

    private void DrawTeleport(bool hit)
    {
        if (tpArc == null) tpArc = MakeLine("WetReality_TeleportArc", out tpArcGo);
        var beam = BeamTint();
        var col = tpValid ? beam : new Color(BlockedColor.r, BlockedColor.g, BlockedColor.b, beam.a);
        tpArc.startColor = tpArc.endColor = col;
        tpArc.startWidth = 0.012f; tpArc.endWidth = 0.008f;
        tpArc.positionCount = tpPath.Count;
        for (int i = 0; i < tpPath.Count; i++) tpArc.SetPosition(i, tpPath[i]);
        tpArc.enabled = true;
        if (!hit) { HideMarker(); return; }
        ShowMarker(tpTarget, tpValid);
    }

    private void HideTeleport()
    {
        if (tpArc != null && tpArc.enabled) tpArc.enabled = false;
        HideMarker();
    }
}
