// Die VR-Haende des Spiels an den Controllern (XRStart 1.11.0), Stufe 1:
// sichtbar und am richtigen Ort, noch ohne Fingerposen. Port von PWS2 VrHands.
//
// SCHLUESSEL GEMESSEN (catalog.json, m_KeyDataString entschluesselt, 186570
// Schluessel) - NICHT die PWS2-Pfade:
//   PWS2  Assets/PWS/Content/Core/FBX/Oculus/Rig_VRHand_L.fbx
//   PWS1  Assets/PWS/Content/Core/FBX/Player/Rig_VRHand_L.fbx
// Dazu (fuer Stufe 2) .../Animation/Player/Controllers/Player_VRHand_L.controller.
// Der Klartext "Rig_VRHand_L.fbx" im Katalog ist eine Dateinamentabelle, kein
// Schluessel (PWS2 §117).
//
// Laden wie PWS2: Addressables.InstantiateAsync(object, Transform, bool, bool),
// gepollt (kein await im Melon). Halter DontDestroyOnLoad, ueberlebt Levelwechsel.
//
// POSEN wie PWS2 (Pose.cs):
//   Pistolenhand  Position Grip, Drehung Aim (pointerRotation) - wie GunDrive
//                 Versatz (0,03, 0,03, -0,14) m, Drehung (-15, 0, -80) Grad
//   freie Hand    Position und Drehung Grip
//                 Versatz (-0,04, 0, -0,08) m, Drehung (70, 20, 90) Grad
// Beide in PWS2 im Headset getrimmt; fuer PWS1 Startwerte (cfg HandTrim*).
// Versatz in der Handdrehung gerechnet, damit er sich mitdreht.
//
// RECHTES RIG PUNKTGESPIEGELT (PWS2 §143-145: Knochenskalierung -1): Unity
// kompensiert die Wicklung nicht, die Hand wirkt von innen. Stufe 1: Material der
// rechten Hand beidseitig (_Cull 0). Gemessen wird die Spiegelung im Log.
//
// SPIELARME: das Spiel zeichnet Ersthand-Arme unter der Kamera (PWS1-Handbuch 3.3:
// ein Mesh fuer beide Arme). Solange die Haende stehen, werden alle Renderer
// unter Camera.main ausgeblendet, die NICHT unter dem Pistolenanker haengen -
// jeder mit Namen im Log. Zurueck, wenn die Haende gehen.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const string HandKeyL = "Assets/PWS/Content/Core/FBX/Player/Rig_VRHand_L.fbx";
    private const string HandKeyR = "Assets/PWS/Content/Core/FBX/Player/Rig_VRHand_R.fbx";

    private GameObject? handHolder, handL, handR;
    private UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject>? handHandle;
    private int handStep;            // 0 nichts, 1 links laedt, 2 rechts laedt, 3 fertig, -1 aufgegeben
    private float handDeadline;
    private readonly List<Renderer> armsHidden = new();
    private Vector3 handRPos, handRRot, handLPos, handLRot;

    // "x,y,z" aus der cfg; bei Unlesbarem die PWS2-Vorgabe.
    private Vector3 ParseVec(string text, Vector3 fallback, string name)
    {
        try
        {
            var p = text.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length == 3) return new Vector3(float.Parse(p[0].Trim(), ci), float.Parse(p[1].Trim(), ci), float.Parse(p[2].Trim(), ci));
        }
        catch { }
        LoggerInstance.Warning($"HAENDE: {name} '{text}' unlesbar - Vorgabe {fallback.ToString("F2")}");
        return fallback;
    }
    private float nextArmsCheck;

    private void DriveVrHands()
    {
        bool want = prefHands.Value && started && writeHead;
        if (!want)
        {
            if (handHolder != null && handHolder.activeSelf) { handHolder.SetActive(false); ShowArms(); }
            return;
        }
        if (handStep >= 0 && handStep < 3) { LoadHands(); return; }
        if (handStep < 0 || handHolder == null) return;
        if (!handHolder.activeSelf) handHolder.SetActive(true);

        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            var cam = Camera.main;
            if (hmd == null || cam == null) return;
            var hmdPos = hmd.centerEyePosition.ReadValue();
            var hmdRot = hmd.centerEyeRotation.ReadValue();
            var camT = cam.transform;
            var toWorld = camT.rotation * Quaternion.Inverse(hmdRot);

            PlaceHand(handR, XRController.rightHand, true, camT, toWorld, hmdPos, handRPos, handRRot);
            PlaceHand(handL, XRController.leftHand, false, camT, toWorld, hmdPos, handLPos, handLRot);

            float now = Time.unscaledTime;
            if (now >= nextArmsCheck) { nextArmsCheck = now + 1f; HideArms(cam); }
        }
        catch (Exception e) { LoggerInstance.Warning("HAENDE: " + e.GetType().Name + ": " + e.Message); }
    }

    private static void PlaceHand(GameObject? hand, XRController? c, bool aimRotation, Transform camT, Quaternion toWorld,
        Vector3 hmdPos, Vector3 posOffset, Vector3 rotOffset)
    {
        if (hand == null) return;
        if (c == null || !c.isTracked.isPressed) { if (hand.activeSelf) hand.SetActive(false); return; }
        if (!hand.activeSelf) hand.SetActive(true);
        var grip = c.devicePosition.ReadValue();
        Quaternion rot = c.deviceRotation.ReadValue();
        if (aimRotation)
        {
            var aimCtl = c.TryGetChildControl("pointerRotation")?.TryCast<QuaternionControl>();
            if (aimCtl != null) rot = aimCtl.ReadValue();
        }
        var world = camT.position + toWorld * (grip - hmdPos);
        var r = toWorld * rot * Quaternion.Euler(rotOffset);
        hand.transform.SetPositionAndRotation(world + r * posOffset, r);
    }

    private void LoadHands()
    {
        try
        {
            if (handHolder == null)
            {
                handHolder = new GameObject("WetReality_VrHands");
                UnityEngine.Object.DontDestroyOnLoad(handHolder);
            }
            if (handHandle == null)
            {
                string key = handStep == 0 || handStep == 1 ? HandKeyL : HandKeyR;
                if (handStep == 0) handStep = 1;
                Il2CppSystem.Object k = (Il2CppSystem.String)key;
                handHandle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(k, handHolder.transform, false, true);
                handDeadline = Time.unscaledTime + 15f;
                LoggerInstance.Msg($"HAENDE: lade '{key}'");
                return;
            }
            var h = handHandle;
            if (!h.IsDone)
            {
                if (Time.unscaledTime > handDeadline) { LoggerInstance.Warning($"HAENDE: nach 15 s nicht fertig, Status {h.Status} - aufgegeben"); handStep = -1; }
                return;
            }
            if (h.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded || h.Result == null)
            {
                LoggerInstance.Warning($"HAENDE: Laden fehlgeschlagen, Status {h.Status} - {(h.OperationException == null ? "keine Ausnahme" : h.OperationException.Message)}");
                handStep = -1;
                return;
            }
            var go = h.Result;
            handHandle = null;
            if (handStep == 1) { handL = go; go.name = "WetReality_HandL"; DescribeHand(go, "links"); handStep = 2; }
            else { handR = go; go.name = "WetReality_HandR"; DescribeHand(go, "rechts"); FixMirrored(go); handStep = 3; LoggerInstance.Msg("HAENDE: beide geladen"); }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("HAENDE: Laden " + e.GetType().Name + ": " + e.Message);
            handStep = -1;
        }
    }

    private void DescribeHand(GameObject go, string side)
    {
        try
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var sb = new System.Text.StringBuilder();
            foreach (var r in rs)
            {
                if (r == null) continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                string mats = "";
                foreach (var m in r.sharedMaterials) mats += (m == null ? "none" : $"{m.name}/{(m.shader == null ? "?" : m.shader.name)}") + " ";
                sb.Append($" | '{r.name}' {r.GetIl2CppType().Name} layer {r.gameObject.layer} bones {(smr == null ? 0 : smr.bones.Length)} root '{(smr == null || smr.rootBone == null ? "-" : smr.rootBone.name)}' "
                          + $"rootScale {(smr == null || smr.rootBone == null ? "-" : smr.rootBone.lossyScale.ToString("F2"))} mats {mats.Trim()}");
            }
            var anim = go.GetComponentInChildren<Animator>(true);
            LoggerInstance.Msg($"HAENDE: {side} '{go.name}' {rs.Length} Renderer, Animator {(anim == null ? "keiner" : (anim.runtimeAnimatorController == null ? "ohne Controller" : "'" + anim.runtimeAnimatorController.name + "'"))}{sb}");
        }
        catch (Exception e) { LoggerInstance.Warning("HAENDE: Beschreibung " + e.GetType().Name); }
    }

    // Stufe 1: das punktgespiegelte Rig beidseitig zeichnen (eigene Materialinstanz).
    private void FixMirrored(GameObject go)
    {
        try
        {
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;   // Instanzen, das geteilte Material bleibt unberuehrt
                foreach (var m in mats) { if (m == null) continue; SetIntSafe(m, "_Cull", 0); n++; }
            }
            LoggerInstance.Msg($"HAENDE: rechts {n} Material(ien) beidseitig (_Cull 0) - Rig ist gespiegelt");
        }
        catch (Exception e) { LoggerInstance.Warning("HAENDE: Spiegelung " + e.GetType().Name); }
    }

    // Spielarme: alles unter der Kamera, was nicht die Pistole ist.
    private void HideArms(Camera cam)
    {
        try
        {
            var anchor = gunAnchor;
            if (anchor == null) return;   // ohne Anker waere die Pistole nicht zu unterscheiden
            var rs = cam.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || !r.enabled) continue;
                if (r.transform.IsChildOf(anchor)) continue;
                if (menuLine != null && r.Pointer == menuLine.Pointer) continue;
                r.enabled = false;
                armsHidden.Add(r);
                LoggerInstance.Msg($"HAENDE: Spielkoerper ausgeblendet '{PathOf(r.transform)}' ({r.GetIl2CppType().Name})");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("HAENDE: Arme " + e.GetType().Name + ": " + e.Message); }
    }

    private void ShowArms()
    {
        if (armsHidden.Count == 0) return;
        int n = 0;
        foreach (var r in armsHidden) { try { if (r != null) { r.enabled = true; n++; } } catch { } }
        LoggerInstance.Msg($"HAENDE: Spielkoerper wieder sichtbar ({n})");
        armsHidden.Clear();
    }
}
