// Strahl auf die freie Hand (XRStart 1.30.0). Port von PWS2 HandSpray.cs.
//
// Der Waschstrahl ist ein Raycast-Faecher gegen WashEquipment.m_rayMask. Hat
// die Hand einen Collider auf einer Ebene IN dieser Maske, ist sie fuer das
// Spiel ein Hindernis wie jede Wand: dessen Spritzeffekt, dessen Strahlabbruch
// - nichts davon zeichnet die Mod. Dazu:
//   - DIE ECHTE GEOMETRIE: BakeMesh der linken VR-Hand (VrHands.cs) in einen
//     nicht-konvexen MeshCollider auf einem Kindknoten "WetReality_HandHit"
//     (PWS2: der Huellquader war halb Luft, der Spritzer lag unter der Hand).
//     Der Renderer bleibt auf seiner Ebene (Ebene = Culling-Maske!).
//   - EINE FREIE EBENE (kein Name), physikalisch stumm per
//     IgnoreLayerCollision gegen alle - nur Raycasts sehen sie.
//   - HandHitWashMask: Bit in m_rayMask, beim Aus/Stopp zurueck.
//   - VIBRATION: Pruefstrahl nur gegen die Handebene ab HandHitSkip (0,01 m);
//     verfehlt er, ein RUECKWAERTSstrahl von 0,3 m voraus (die Muendung steckt
//     in der Hand - ein Strahl, der IM Collider beginnt, trifft ihn nicht).
//     Liegt vor der Hand eine Wand (Spielmaske), keine Vibration.
//   - Neu gebacken alle HandHitRebake s, solange gesprueht wird (Fingerpose).

using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefHandHit = null!, prefHandHitWashMask = null!;
    private MelonPreferences_Entry<float> prefHandHitAmp = null!, prefHandHitSeconds = null!, prefHandHitRange = null!, prefHandHitSkip = null!, prefHandHitRebake = null!;

    private const string HandHitNode = "WetReality_HandHit";
    private const float HandInsideDepth = 0.3f;
    private int handHitLayer = -1;
    private MeshCollider? handHitCollider;
    private Mesh? handHitMesh;
    private SkinnedMeshRenderer? handHitRenderer;
    private WashEquipment? handHitMaskOn;
    private int handHitMaskBefore;
    private bool handHitMaskWritten, handHitFailed, handHitWasHit;
    private float handHitNextBake, handHitNextBuzz, handHitNextBuild;
    private int handHitCount;

    private void InitHandSpray(MelonPreferences_Category cat)
    {
        prefHandHit = cat.CreateEntry("HandHit", true, description: "Strahl auf die freie Hand: sie vibriert, und die Hand bekommt einen Collider aus ihrem Modell, damit das Spiel dort spritzt (PWS2). Braucht ShowVrHands.");
        prefHandHitWashMask = cat.CreateEntry("HandHitWashMask", true, description: "Die Handebene in die Strahlmaske des Spiels - Spritzeffekt und Strahlabbruch an der Hand. Aus = nur Vibration.");
        prefHandHitAmp = cat.CreateEntry("HandHitAmplitude", 0.9f, description: "Vibrationsstaerke der freien Hand, solange der Strahl sie trifft");
        prefHandHitSeconds = cat.CreateEntry("HandHitSeconds", 0.25f, description: "Laenge eines Pulses, erneuert solange der Strahl auf der Hand bleibt");
        prefHandHitRange = cat.CreateEntry("HandHitRange", 2.5f, description: "Wie weit entlang des Strahls die Hand gesucht wird (m)");
        prefHandHitSkip = cat.CreateEntry("HandHitSkip", 0.01f, description: "Meter vor der Muendung uebersprungen (der Strahl fragt nur die Handebene)");
        prefHandHitRebake = cat.CreateEntry("HandHitRebake", 1f, description: "Sekunden zwischen zwei Neuberechnungen der Handform beim Spruehen (Fingerpose)");
    }

    // In onBeforeRender nach DriveSprayHaptics (Strahl und IsWashing stehen).
    private void DriveHandSpray()
    {
        bool want = prefHandHit.Value && writeGun && started && handL != null && !handHitFailed;
        var we = fixWash;
        if (!want || we == null) { ReleaseHandMask(); return; }
        try
        {
            if (!EnsureHandCollider()) return;
            // Maske: nur solange die Hand steht und die Einstellung es will.
            if (prefHandHitWashMask.Value) ApplyHandMask(we); else ReleaseHandMask();

            float now = Time.unscaledTime;
            if (sprayWashing && now >= handHitNextBake) { handHitNextBake = now + Math.Max(0.2f, prefHandHitRebake.Value); BakeHand(); }

            bool hit = sprayWashing && ProbeHand();
            if (hit && now >= handHitNextBuzz)
            {
                handHitNextBuzz = now + Math.Max(0.05f, prefHandHitSeconds.Value * 0.8f);
                try { XRController.leftHand?.TryCast<XRControllerWithRumble>()?.SendImpulse(Mathf.Clamp01(prefHandHitAmp.Value), prefHandHitSeconds.Value); } catch { }
            }
            if (hit != handHitWasHit)
            {
                handHitWasHit = hit;
                if (hit && ++handHitCount <= 5) LoggerInstance.Msg($"HAND-TREFFER: Strahl auf der freien Hand ({handHitCount}.)");
            }
        }
        catch (Exception e) { LoggerInstance.Warning("HAND-TREFFER: " + e.GetType().Name + ": " + e.Message); handHitFailed = true; ReleaseHandMask(); }
    }

    private bool EnsureHandCollider()
    {
        if (handHitCollider != null && handHitRenderer != null) return true;
        handHitCollider = null; handHitRenderer = null;   // zerstoert: alle Felder raeumen
        float now = Time.unscaledTime;
        if (now < handHitNextBuild) return false;
        handHitNextBuild = now + 1f;
        var smr = handL!.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null) { LoggerInstance.Msg("HAND-TREFFER: linke Hand ohne SkinnedMeshRenderer - warte"); return false; }
        if (handHitLayer < 0 && !PickHandLayer()) return false;
        var node = smr.transform.Find(HandHitNode);
        GameObject go;
        if (node != null) go = node.gameObject;
        else
        {
            go = new GameObject(HandHitNode);
            go.transform.SetParent(smr.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        go.layer = handHitLayer;   // nur der Kindknoten - der Renderer bleibt, wo er ist
        handHitRenderer = smr;
        var existing = go.GetComponent<MeshCollider>();
        handHitCollider = existing != null ? existing : go.AddComponent<MeshCollider>();
        handHitCollider.convex = false;
        handHitCollider.isTrigger = false;
        if (!BakeHand()) return false;
        LoggerInstance.Msg($"HAND-TREFFER: MeshCollider auf Ebene {handHitLayer} unter '{smr.name}', {handHitMesh!.vertexCount} Ecken, Renderer bleibt auf Ebene {smr.gameObject.layer}");
        return true;
    }

    private bool BakeHand()
    {
        if (handHitRenderer == null || handHitCollider == null) return false;
        if (handHitMesh == null) handHitMesh = new Mesh();
        handHitRenderer.BakeMesh(handHitMesh);
        if (handHitMesh.vertexCount == 0) { LoggerInstance.Warning("HAND-TREFFER: gebackene Hand ohne Ecken"); return false; }
        handHitCollider.sharedMesh = null;   // neu kochen
        handHitCollider.sharedMesh = handHitMesh;
        return true;
    }

    // Eine Ebene ohne Namen, stumm gegen alle anderen (nur Raycasts sehen sie).
    private bool PickHandLayer()
    {
        for (int i = 31; i >= 8; i--)
        {
            if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) continue;
            handHitLayer = i;
            for (int j = 0; j < 32; j++) Physics.IgnoreLayerCollision(i, j, true);
            LoggerInstance.Msg($"HAND-TREFFER: freie Ebene {i} gewaehlt, Kollisionen mit allen Ebenen aus");
            return true;
        }
        LoggerInstance.Warning("HAND-TREFFER: keine freie Ebene - aus");
        handHitFailed = true;
        return false;
    }

    private void ApplyHandMask(WashEquipment we)
    {
        int bit = 1 << handHitLayer;
        if (handHitMaskOn != null && handHitMaskOn.Pointer != we.Pointer) ReleaseHandMask();   // neuer Washer
        int cur = we.m_rayMask.value;
        if ((cur & bit) != 0) return;
        if (!handHitMaskWritten) handHitMaskBefore = cur;
        var lm = new LayerMask { value = cur | bit };
        we.m_rayMask = lm;
        handHitMaskOn = we;
        handHitMaskWritten = true;
        LoggerInstance.Msg($"HAND-TREFFER: Strahlmaske 0x{cur:X8} -> 0x{cur | bit:X8} (Ebene {handHitLayer})");
    }

    private void ReleaseHandMask()
    {
        if (!handHitMaskWritten) return;
        handHitMaskWritten = false;
        try
        {
            var we = handHitMaskOn;
            if (we != null && handHitLayer >= 0)
            {
                int cur = we.m_rayMask.value;
                we.m_rayMask = new LayerMask { value = cur & ~(1 << handHitLayer) };
                LoggerInstance.Msg($"HAND-TREFFER: Strahlmaske zurueck 0x{cur & ~(1 << handHitLayer):X8}");
            }
        }
        catch { }
        handHitMaskOn = null;
    }

    private bool ProbeHand()
    {
        if (!pubNozzleReady || pubNozzleDir.sqrMagnitude < 0.0001f || handHitLayer < 0) return false;
        int bit = 1 << handHitLayer;
        var dir = pubNozzleDir.normalized;
        var start = pubNozzleOrigin + dir * Math.Max(0f, prefHandHitSkip.Value);
        bool hit = Physics.Raycast(start, dir, prefHandHitRange.Value, bit, QueryTriggerInteraction.Ignore);
        if (!hit)
        {
            // Innenfall: die Muendung steckt in der Hand.
            var ahead = start + dir * HandInsideDepth;
            return Physics.Raycast(ahead, -dir, HandInsideDepth, bit, QueryTriggerInteraction.Ignore);
        }
        // Wand vor der Hand? Dann trifft der Strahl die Wand, nicht die Hand.
        var hand = handHitCollider != null ? handHitCollider.transform.position : start;
        float along = Vector3.Dot(hand - start, dir);
        int block = handHitMaskWritten ? handHitMaskBefore : sprayMask & ~bit;
        if (along > 0.15f && block != 0 && Physics.Raycast(start, dir, along - 0.1f, block, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }
}
