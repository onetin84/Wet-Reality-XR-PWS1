// Koerperzonen-Gesten (XRStart 1.27.0). Port von PWS2 DriveBodyZones (PWS2-
// Handbuch "Gesten", §96/§110): Reichweite statt Knopf, die Stickklicks bleiben.
//
//   Pistolenhand hinter die eigene SCHULTER + Griff rechts = naechster Washer
//     (BaseInput.SwitchGun(+1), wie Y lang).
//   Freie Hand an die PISTOLE + Griff links = Verlaengerung
//     (BaseInput.SwitchExtension(+1), wie L3); beide Haende pulsen.
//   Pistolenhand an die rechte HUEFTE + Griff rechts = Seife aus dem Holster
//     (1.29.0, Nutzer): auf die Seifenduese (NozzleType.Black_65_Soap) und beim
//     naechsten Mal zurueck zur gemerkten Duese. PWS1 hat keine Kategorie wie
//     PWS2 - Seife ist eine Duese; das ausgeruestete Reinigungsmittel des
//     AKTUELLEN Washers nimmt das Spiel selbst. Gesetzt ueber
//     ConfigurationManager.UpdateNozzle(NozzleData), die Duese aus
//     CurrentConfiguration.m_compatibleNozzles (nur was zum Washer passt).
//     Kein Mittel ausgeruestet oder keine Seifenduese = nichts.
//
// GERECHNET im Tracking-Raum gegen die WAAGERECHTE Kopf-Vorwaertsachse:
// local = Inverse(Euler(0, torsoYaw, 0)) * (Hand - HMD). Die kuenstliche
// Koerperdrehung faellt heraus - die Zone ist "hinter meiner ECHTEN Schulter".
// Beim Blick fast senkrecht kollabiert die Projektion: dann gilt der LETZTE
// gueltige Gierwinkel (genau dann, wenn man ueber die Schulter greift).
// Zonen wie PWS2: Schulter (0,20 / -0,15 / -0,20) r 0,20; Pistole 0,15 m vor
// der Pistolenhand r 0,15; Sperre 0,4 s je Geste.
//
// VORRANG (PWS2): offenes Menue/Scheibe = keine Geste. Griff rechts bei
// laufendem Dauerspruehen raeumt NUR die Rastung (ControllerInput.cs). Griff
// links beim Tragen bleibt Dreh-Modifikator, ausserhalb der Zone Schmutz.

using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefGestureZones = null!;
    private MelonPreferences_Entry<float> prefHipX = null!, prefHipY = null!, prefHipZ = null!, prefHipR = null!;
    private MelonPreferences_Entry<float> prefShoulderX = null!, prefShoulderY = null!, prefShoulderZ = null!, prefShoulderR = null!,
        prefWasherForward = null!, prefWasherR = null!, prefGestureCooldown = null!;

    private static bool gesturesOn = true;
    private static Vector3 shoulderCentre = new(0.20f, -0.15f, -0.20f);
    private static float shoulderRadius = 0.2f, washerForward = 0.15f, washerRadius = 0.15f, gestureCooldown = 0.4f;
    private static float torsoYaw;
    private static bool torsoYawValid;
    private static bool inShoulderZone, inWasherZone, inHipZone;
    private static float nextShoulderGesture, nextWasherGesture, nextHipGesture;
    private static Vector3 hipCentre = new(0.22f, -0.60f, 0f);
    private static float hipRadius = 0.2f, shoulderDist = 9f, hipDist = 9f;
    private static Il2CppPWS.NozzleData? nozzleBeforeSoap;

    private void InitGestures(MelonPreferences_Category cat)
    {
        prefGestureZones = cat.CreateEntry("GestureZones", true, description: "Pistolenhand hinter die Schulter + Griff rechts = naechster Washer; freie Hand an die Pistole + Griff links = Verlaengerung (PWS2). Stickklicks bleiben.");
        prefShoulderX = cat.CreateEntry("ShoulderZoneX", 0.20f, description: "Meter rechts vom Kopf (gierungsgerichtet). Mitte der Schulterzone");
        prefShoulderY = cat.CreateEntry("ShoulderZoneY", -0.15f, description: "Meter ueber dem Kopf; negativ = darunter, wo die Schulter ist");
        prefShoulderZ = cat.CreateEntry("ShoulderZoneZ", -0.20f, description: "Meter vor dem Kopf; negativ = dahinter");
        prefShoulderR = cat.CreateEntry("ShoulderZoneRadius", 0.2f, description: "Meter. Hinter die eigene Schulter greift man blind - darum grosszuegig");
        prefHipX = cat.CreateEntry("HipZoneX", 0.22f, description: "Meter rechts vom Kopf (gierungsgerichtet). Mitte der Hueftzone (Seife)");
        prefHipY = cat.CreateEntry("HipZoneY", -0.60f, description: "Meter ueber dem Kopf; negativ = darunter");
        prefHipZ = cat.CreateEntry("HipZoneZ", 0f, description: "Meter vor dem Kopf");
        prefHipR = cat.CreateEntry("HipZoneRadius", 0.2f, description: "Meter. Ueber 0,225 ueberlappt sie die Schulterzone (PWS2)");
        prefWasherForward = cat.CreateEntry("WasherZoneForward", 0.15f, description: "Meter entlang der Pistolen-Vorwaertsachse, von der Hand zur Muendung");
        prefWasherR = cat.CreateEntry("WasherZoneRadius", 0.15f, description: "Meter um diesen Punkt");
        prefGestureCooldown = cat.CreateEntry("GestureCooldown", 0.4f, description: "Sekunden, bevor dieselbe Geste wieder feuern kann");
        SyncGesturePrefs();
    }

    // Aus OnUpdate: die statischen Kopien fuer den PlayerInput-Patch.
    private void SyncGesturePrefs()
    {
        gesturesOn = prefGestureZones.Value;
        shoulderCentre = new Vector3(prefShoulderX.Value, prefShoulderY.Value, prefShoulderZ.Value);
        shoulderRadius = prefShoulderR.Value;
        hipCentre = new Vector3(prefHipX.Value, prefHipY.Value, prefHipZ.Value);
        hipRadius = prefHipR.Value;
        washerForward = prefWasherForward.Value;
        washerRadius = prefWasherR.Value;
        gestureCooldown = prefGestureCooldown.Value;
    }

    // Aus UpdateButtons, VOR den Griffen: Zonen dieses Frames bestimmen.
    private static void UpdateGestureZones(XRController? r, XRController? l)
    {
        bool shoulder = false, washer = false, hip = false;
        Vector3 local = Vector3.zero, washerLocal = Vector3.zero;
        try
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            if (gesturesOn && !menuActive && !wheelOpen && hmd != null && r != null)
            {
                var hmdPos = hmd.centerEyePosition.ReadValue();
                var fwd = hmd.centerEyeRotation.ReadValue() * Vector3.forward;
                var flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude > 0.01f) { torsoYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg; torsoYawValid = true; }
                if (torsoYawValid)
                {
                    var hand = r.devicePosition.ReadValue();
                    local = Quaternion.Inverse(Quaternion.Euler(0f, torsoYaw, 0f)) * (hand - hmdPos);
                    // Linkshaendig spiegeln die Koerperzonen mit der Pistolenhand (PWS2 §108).
                    var sideV = new Vector3(HandSide, 1f, 1f);
                    shoulderDist = (local - Vector3.Scale(shoulderCentre, sideV)).magnitude;
                    hipDist = (local - Vector3.Scale(hipCentre, sideV)).magnitude;
                    shoulder = shoulderDist <= shoulderRadius;
                    hip = hipDist <= hipRadius;
                    if (l != null)
                    {
                        var centre = hand + r.deviceRotation.ReadValue() * new Vector3(0f, 0f, washerForward);
                        washerLocal = l.devicePosition.ReadValue() - centre;
                        washer = washerLocal.magnitude <= washerRadius;
                    }
                }
            }
        }
        catch { shoulder = washer = hip = false; }
        if (shoulder != inShoulderZone) { inShoulderZone = shoulder; btnEvents.Add($"Zone Schulter {(shoulder ? "BETRETEN" : "verlassen")} local {local.ToString("F2")}"); }
        if (hip != inHipZone) { inHipZone = hip; btnEvents.Add($"Zone Huefte {(hip ? "BETRETEN" : "verlassen")} local {local.ToString("F2")}"); }
        if (washer != inWasherZone) { inWasherZone = washer; btnEvents.Add($"Zone Pistole {(washer ? "BETRETEN" : "verlassen")} Abstand {washerLocal.magnitude:F2}"); }
    }

    // Griff rechts in der Schulter- ODER Hueftzone; die naehere Mitte gewinnt
    // (PWS2: ein Druck, eine Aktion). true = verbraucht (keine Rastung).
    private static bool TryBodyGesture(Il2CppPWS.PlayerInput pi)
    {
        if (inHipZone && (!inShoulderZone || hipDist < shoulderDist)) return TryHipGesture(pi);
        return TryShoulderGesture(pi);
    }

    private static bool TryHipGesture(Il2CppPWS.PlayerInput pi)
    {
        float now = Time.unscaledTime;
        if (now < nextHipGesture) return true;
        nextHipGesture = now + gestureCooldown;
        if (pi.BlockedInput) { btnEvents.Add("Geste Huefte: gesperrt (BlockedInput)"); return true; }
        try
        {
            var em = UnityEngine.Object.FindObjectOfType<Il2CppPWS.EquipmentManager>();
            var cm = em == null ? null : em.ConfigurationManager;
            var conf = cm == null ? null : cm.CurrentConfiguration;
            if (conf == null) { btnEvents.Add("Geste Huefte: keine Washer-Konfiguration"); return true; }
            var current = conf.m_nozzle;
            bool onSoap = current != null && current.NozzleType == Il2CppPWS.NozzleType.Black_65_Soap;
            if (onSoap)
            {
                // Zurueck ins Holster: die gemerkte Duese, sofern sie noch zum Washer passt.
                var back = nozzleBeforeSoap;
                nozzleBeforeSoap = null;
                if (back == null || !CompatibleWith(conf, back)) { btnEvents.Add("Geste Huefte: Seife aktiv, keine gemerkte Duese - nichts"); return true; }
                cm!.UpdateNozzle(back);
                Buzz(true, "Seife weg");
                btnEvents.Add($"Geste Huefte: Seife weg -> {back.NozzleType}");
                return true;
            }
            if (conf.m_cleaningLiquid == null) { btnEvents.Add("Geste Huefte: kein Reinigungsmittel ausgeruestet - nichts"); return true; }
            Il2CppPWS.NozzleData? soap = null;
            var e = conf.m_compatibleNozzles.Values.GetEnumerator();
            while (e.MoveNext()) { var n = e.Current; if (n != null && n.NozzleType == Il2CppPWS.NozzleType.Black_65_Soap) { soap = n; break; } }
            if (soap == null) { btnEvents.Add("Geste Huefte: keine Seifenduese fuer diesen Washer - nichts"); return true; }
            nozzleBeforeSoap = current;
            cm!.UpdateNozzle(soap);
            Buzz(true, "Seife");
            btnEvents.Add($"Geste Huefte: Seife ({conf.CleaningLiquidName}) - vorher {(current == null ? "-" : current.NozzleType.ToString())}");
        }
        catch (Exception ex) { btnEvents.Add("Geste Huefte: " + ex.GetType().Name + ": " + ex.Message); }
        return true;
    }

    private static bool CompatibleWith(Il2CppPWS.WasherConfiguration conf, Il2CppPWS.NozzleData n)
    {
        var e = conf.m_compatibleNozzles.Values.GetEnumerator();
        while (e.MoveNext()) if (e.Current != null && e.Current.Pointer == n.Pointer) return true;
        return false;
    }

    private static bool TryShoulderGesture(Il2CppPWS.PlayerInput pi)
    {
        if (!inShoulderZone) return false;
        float now = Time.unscaledTime;
        if (now < nextShoulderGesture) return true;
        nextShoulderGesture = now + gestureCooldown;
        if (pi.BlockedInput) { btnEvents.Add("Geste Schulter: gesperrt (BlockedInput)"); return true; }
        pi.SwitchGun?.Invoke(1);
        Buzz(true, "Geste Schulter");
        btnEvents.Add("Geste Schulter + Griff rechts: SwitchGun(1)");
        return true;
    }

    // Griff links an der Pistole. true = verbraucht (kein Schmutz-Hervorheben).
    private static bool TryWasherGesture(Il2CppPWS.PlayerInput pi)
    {
        if (!inWasherZone) return false;
        float now = Time.unscaledTime;
        if (now < nextWasherGesture) return true;
        nextWasherGesture = now + gestureCooldown;
        if (pi.BlockedInput) { btnEvents.Add("Geste Pistole: gesperrt (BlockedInput)"); return true; }
        pi.SwitchExtension?.Invoke(1);
        Buzz(true, "Geste Pistole");   // der Ruck geht durch das Geraet: beide Haende
        Buzz(false, "Geste Pistole");
        btnEvents.Add("Geste Pistole + Griff links: SwitchExtension(1)");
        return true;
    }
}
