// Fingerposen der VR-Haende (XRStart 1.12.0), Stufe 2. Port von PWS2 HandPose,
// Weg "animator": dort im Headset getragen, die Clips sind humanoid (nicht
// sampelbar) und der FBX-Klon bringt seinen Avatar mit.
//
// SCHLUESSEL GEMESSEN (PWS1 catalog.json, identisch mit PWS2):
//   Assets/PWS/Content/Core/Animation/Player/Controllers/Player_VRHand_L/R.controller
// Typisiert als RuntimeAnimatorController laden - PWS2 §117: als GameObject
// wird der Schluessel abgewiesen ("exists as multiple Types").
//
// SCHNITTSTELLE (PWS2 1.44.0 gemessen, fuer PWS1 Hypothese - das Log listet, was
// der Controller hier wirklich fuehrt):
//   Index, Middle, Ring, Pinky, Thumb   Float 0 (offen) .. 1 (geschlossen)
//   Grip                                Bool  - "diese Hand haelt die Pistole"
//   IsOffhand                           Bool
//
// ANIMIERT statt statisch (PWS2 hatte feste Posen; Nutzerwunsch "inkl.
// Animationen"):
//   rechts  Grip = true, alle Finger 0 - GENAU wie PWS2 (1.12.0 setzte die
//           Finger auf 1 und den Zeigefinger auf den Trigger: die Finger-Ebenen
//           ueberdeckten die Griffpose, die Hand blieb offen; Nutzer: feste
//           Griffpose ohne Trigger-Zeigefinger, wie PWS2)
//   links   IsOffhand = true, Index = linker Trigger, Middle/Ring/Pinky/Thumb =
//           linker Griff; beim Tragen alle 1
// Gesetzt nur bei Aenderung > 0,02. Die Wurzel bleibt der Mod: applyRootMotion aus.

using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const string PoseKeyL = "Assets/PWS/Content/Core/Animation/Player/Controllers/Player_VRHand_L.controller";
    private const string PoseKeyR = "Assets/PWS/Content/Core/Animation/Player/Controllers/Player_VRHand_R.controller";
    private static readonly string[] FingerParams = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

    private sealed class PoseSide
    {
        internal UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<RuntimeAnimatorController>? Handle;
        internal bool Requested, Done;
        internal float Deadline;
        internal RuntimeAnimatorController? Controller;
        internal IntPtr BoundTo = IntPtr.Zero;   // Animator, dem der Controller gegeben wurde
        internal readonly HashSet<string> Params = new();
        internal readonly Dictionary<string, float> Last = new();
        internal bool LastGrip, LastOff, HaveBools;
    }

    private readonly PoseSide poseL = new(), poseR = new();

    private void DriveHandPoses()
    {
        if (!prefHands.Value || handStep != 3) return;
        LoadPose(poseL, PoseKeyL, "links");
        LoadPose(poseR, PoseKeyR, "rechts");
        try
        {
            var r = XRController.rightHand;
            var l = XRController.leftHand;
            float lTrig = AxisOf(l, "trigger"), lGrip = AxisOf(l, "grip");
            bool carry = lastCarry;   // ControllerInput: Tragen erkannt
            ApplyPose(poseR, handR, "rechts", true, false,
                new[] { 0f, 0f, 0f, 0f, 0f });
            float g = carry ? 1f : lGrip, t = carry ? 1f : lTrig;
            ApplyPose(poseL, handL, "links", false, true,
                new[] { t, g, g, g, g });
        }
        catch (Exception e) { LoggerInstance.Warning("HANDPOSE: " + e.GetType().Name + ": " + e.Message); }
    }

    private static float AxisOf(XRController? c, string name)
    {
        try { var a = c?.TryGetChildControl(name)?.TryCast<AxisControl>(); return a == null ? 0f : a.ReadValue(); }
        catch { return 0f; }
    }

    private void LoadPose(PoseSide side, string key, string label)
    {
        if (side.Done) return;
        try
        {
            if (!side.Requested)
            {
                side.Requested = true;
                side.Deadline = Time.unscaledTime + 15f;
                Il2CppSystem.Object k = (Il2CppSystem.String)key;
                side.Handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<RuntimeAnimatorController>(k);
                LoggerInstance.Msg($"HANDPOSE: lade Controller {label}");
                return;
            }
            var h = side.Handle;
            if (h == null) { side.Done = true; LoggerInstance.Warning($"HANDPOSE: {label} Handle null"); return; }
            if (!h.IsDone)
            {
                if (Time.unscaledTime > side.Deadline) { side.Done = true; LoggerInstance.Warning($"HANDPOSE: {label} nach 15 s nicht fertig, Status {h.Status}"); }
                return;
            }
            side.Done = true;
            if (h.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded || h.Result == null)
            {
                LoggerInstance.Warning($"HANDPOSE: {label} Controller fehlgeschlagen, Status {h.Status} - {(h.OperationException == null ? "keine Ausnahme" : h.OperationException.Message)}");
                return;
            }
            side.Controller = h.Result;
            LoggerInstance.Msg($"HANDPOSE: {label} Controller '{side.Controller.name}' geladen, {side.Controller.animationClips.Length} Clips");
        }
        catch (Exception e) { side.Done = true; LoggerInstance.Warning($"HANDPOSE: {label} Laden {e.GetType().Name}: {e.Message}"); }
    }

    private void ApplyPose(PoseSide side, GameObject? hand, string label, bool grip, bool offHand, float[] fingers)
    {
        if (hand == null || side.Controller == null || !hand.activeInHierarchy) return;
        var anim = hand.GetComponentInChildren<Animator>(true);
        if (anim == null) return;

        if (anim.Pointer != side.BoundTo)
        {
            side.BoundTo = anim.Pointer;
            side.Params.Clear(); side.Last.Clear(); side.HaveBools = false;
            anim.runtimeAnimatorController = side.Controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // die Hand haengt ausserhalb der Kamerahierarchie
            anim.enabled = true;
            var ps = anim.parameters;
            var names = new List<string>();
            for (int i = 0; i < ps.Length; i++) { var p = ps[i]; if (p == null) continue; side.Params.Add(p.name); names.Add($"{p.name}:{p.type}"); }
            var av = anim.avatar;
            LoggerInstance.Msg($"HANDPOSE: {label} Animator gebunden - Avatar {(av == null ? "KEINER" : $"'{av.name}' human {av.isHuman} valid {av.isValid}")}, Parameter [{string.Join(", ", names)}]");
        }

        if (!side.HaveBools || side.LastGrip != grip || side.LastOff != offHand)
        {
            if (side.Params.Contains("Grip")) anim.SetBool("Grip", grip);
            if (side.Params.Contains("IsOffhand")) anim.SetBool("IsOffhand", offHand);
            side.LastGrip = grip; side.LastOff = offHand; side.HaveBools = true;
        }
        for (int i = 0; i < FingerParams.Length && i < fingers.Length; i++)
        {
            var name = FingerParams[i];
            if (!side.Params.Contains(name)) continue;
            float v = Mathf.Clamp01(fingers[i]);
            if (side.Last.TryGetValue(name, out var was) && Math.Abs(was - v) < 0.02f) continue;
            anim.SetFloat(name, v);
            side.Last[name] = v;
        }
    }
}
