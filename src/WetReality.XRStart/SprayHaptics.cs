// Haptik des Strahls (XRStart 1.5.0) - Port von PWS2 SprayHaptics /
// SprayHapticSettings (reference/, Abschnitt 102 Lauf 4).
//
// Ziel (PWS2, vom Nutzer fuer PWS1 bestaetigt): ohne Blick auf die Oberflaeche
// erfuehlen, welcher Strahl aktiv ist, ob Turbo laeuft, ob der Strahl eine
// Oberflaeche trifft - und, neu gewuenscht, welcher Reiniger in der Hand ist.
//
//   Amplitude = Basis(Strahl) x Reiniger x Kontakt x Intensitaet
//   Basis      0 = 0,42  15 = 0,34  25 = 0,26  40 = 0,18  Seife(65) = 0,27
//              sonst 0,30 (Trident)                              (PWS2-Werte)
//   Turbo      x 1,15 und Rattern 15 Hz, Tiefe 0,6               (PWS2)
//              In PWS1 ist Turbo eine DUESE (NozzleType.Turbo), kein Reiniger
//              wie in PWS2 - direkt erkennbar, keine Namensregel noetig.
//   Reiniger   Light 0,85  Medium 1,0  Heavy 1,15  Professional 1,3
//              NEU, nicht aus PWS2 - Startwerte, zum Nachstellen gedacht.
//   Kontakt    eigener Raycast entlang der Waschrichtung, die die Mod schreibt
//              (0,1 m ab Muendung, 8 m, Trigger ignoriert); ohne Treffer 0,45,
//              0,15 s geglaettet                                   (PWS2)
//              1.6.0: Maske = WashEquipment.m_rayMask (womit das Spiel
//              waescht). Mit PWS2s -5 traf der Strahl in 1.5.0 fast immer,
//              auch "in die Luft" (Lauf 2: 50x Treffer, 3x nicht) - der
//              Bericht nennt, was -5 trifft.
//   Atmen      5 %, 0,7 Hz - langsam, nicht als Pulsieren spuerbar (PWS2)
//
// DAUERPULS DURCH NACHSENDEN: ein neuer Impuls ersetzt den laufenden; alle
// 60 ms, Dauer 2,5 x Intervall, damit ein spaeter Frame keine Luecke laesst.
// Aufhoeren braucht einen eigenen Aufruf (Impuls 0).
//
// EIN SEGMENT-STAPEL fuer die Pistolenhand: Ereignisimpulse (Anlauf, Ende,
// Strahl-, Seifen-, Reinigerwechsel, Dauerspruehen an/aus) ueberlagern den
// Dauerpuls, statt von der naechsten Nachsendung ueberschrieben zu werden.
// Die Formen sind feste Zahlen (PWS2: "Formen, nicht Geschmack").

using Il2CppPWS;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    // cfg HapticIntensity (PWS2-Name), Vorgabe 1. SprayHaptics = false nimmt nur den
    // Dauerpuls des Strahls - Ereignisimpulse (Menue, Duese, Scheibe) bleiben.
    private float SprayIntensity => Math.Max(0f, prefHapticIntensity?.Value ?? 1f);
    private const float Jet0 = 0.42f, Jet15 = 0.34f, Jet25 = 0.26f, Jet40 = 0.18f, JetSoap = 0.27f, JetDefault = 0.30f;
    private const float TurboFactor = 1.15f, TurboHz = 15f, TurboDepth = 0.6f;
    private const float ContactFactor = 0.45f, ContactSmooth = 0.15f, ContactRange = 8f, ContactSkip = 0.1f;
    private const int DefaultMask = -5;   // Unitys DefaultRaycastLayers, nur noch zum Vergleich im Bericht
    private int sprayMask = DefaultMask;
    private const float Refresh = 0.06f, Breathe = 0.05f;

    private static readonly List<(float Until, float Amplitude, float Seconds)> spraySegments = new();
    private bool sprayWashing, sprayWashedLast, spraySending;
    private float sprayContact, sprayNextSend, sprayLastAmp, sprayNextReport;
    private int sprayNozzle = int.MinValue, sprayClass = -1;
    private bool spraySawConfig;
    private static bool pubNozzleReady;
    private static Vector3 pubNozzleOrigin, pubNozzleDir;

    // Ein Impuls auf der Pistolenhand, durch die Warteschlange (auch von Buzz).
    internal static void QueueSpray(float amplitude, float seconds)
    {
        float start = Math.Max(LastSegmentEnd(), Time.unscaledTime);
        spraySegments.Add((start + seconds, amplitude, seconds));
    }

    private static void QueueSprayGap(float seconds)
    {
        float start = Math.Max(LastSegmentEnd(), Time.unscaledTime);
        spraySegments.Add((start + seconds, 0f, seconds));
    }

    private static float LastSegmentEnd()
    {
        float last = 0f;
        foreach (var s in spraySegments) if (s.Until > last) last = s.Until;
        return last;
    }

    private static void SendRight(float amplitude, float seconds)
    {
        try
        {
            var r = XRController.rightHand?.TryCast<XRControllerWithRumble>();
            r?.SendImpulse(Mathf.Clamp01(amplitude), seconds);
        }
        catch { }
    }

    // In onBeforeRender nach der Pistole: IsWashing hat WashEquipment.LateUpdate
    // in diesem Frame gesetzt, die Waschrichtung ist veroeffentlicht.
    private void DriveSprayHaptics()
    {
        bool active = writeGun && inputOn && started;
        try
        {
            var we = fixWash;
            if (!active || we == null)
            {
                if (spraySending) { SendRight(0f, 0.01f); spraySending = false; sprayLastAmp = 0f; }
                spraySegments.Clear();
                sprayWashedLast = false;
                return;
            }

            ReadSprayConfig(we);

            sprayWashing = we.IsWashing;
            if (sprayWashing != sprayWashedLast)
            {
                sprayWashedLast = sprayWashing;
                // Anlauf- und Abschlussimpuls; der Abschluss schwaecher.
                if (sprayWashing) QueueSpray(0.50f * SprayIntensity, 0.07f);
                else QueueSpray(0.30f * SprayIntensity, 0.05f);
            }

            // Kontakt, zeitlich geglaettet statt geschaltet.
            bool hit = sprayWashing && ProbeSprayContact(sprayMask);
            float step = Mathf.Clamp01(Time.unscaledDeltaTime / ContactSmooth);
            sprayContact += ((hit ? 1f : 0f) - sprayContact) * step;

            float amplitude = SteadySprayAmplitude();
            float interval = Refresh;
            float now = Time.unscaledTime;

            while (spraySegments.Count > 0 && spraySegments[0].Until <= now) spraySegments.RemoveAt(0);

            float seconds = 0f;
            if (spraySegments.Count > 0)
            {
                amplitude = spraySegments[0].Amplitude;
                seconds = spraySegments[0].Seconds;
                interval = Math.Max(0.01f, spraySegments[0].Until - now);
            }
            else if (sprayNozzle == (int)NozzleType.Turbo)
            {
                // Das Muster bestimmt das Nachsende-Intervall - sonst Zufall statt Rattern.
                float half = 1f / Math.Max(1f, TurboHz * 2f);
                int phase = Mathf.FloorToInt(now / half) & 1;
                amplitude *= phase == 0 ? 1f : TurboDepth;
                interval = half;
            }
            else if (Breathe > 0.0001f)
            {
                amplitude *= 1f + Breathe * Mathf.Sin(now * 0.7f * 2f * Mathf.PI);
            }

            amplitude = Mathf.Clamp01(amplitude);
            if (amplitude <= 0.001f)
            {
                if (spraySending) { SendRight(0f, 0.01f); spraySending = false; sprayLastAmp = 0f; }
                return;
            }

            if (now >= sprayNextSend)
            {
                float duration = seconds > 0.001f ? seconds : interval * 2.5f;
                SendRight(amplitude, duration);
                sprayLastAmp = amplitude;
                spraySending = true;
                sprayNextSend = now + interval;
            }

            if (sprayWashing && now >= sprayNextReport)
            {
                sprayNextReport = now + 1f;
                LoggerInstance.Msg($"SPRUEH-HAPTIK: Duese {NozzleName(sprayNozzle)} Reiniger {(PowerWasherClass)sprayClass} | " +
                    $"spruehen {sprayWashing} Kontakt {sprayContact:F2} (Treffer {hit}) | Amplitude {sprayLastAmp:F3} Intervall {interval * 1000f:F0} ms | Segmente {spraySegments.Count} | " +
                    $"Maske Spiel 0x{sprayMask:X8}: {DescribeContact(sprayMask)} | Maske -5: {DescribeContact(DefaultMask)}");
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("SPRUEH-HAPTIK: " + e.GetType().Name + ": " + e.Message);
            spraySegments.Clear();
        }
    }

    private float SteadySprayAmplitude()
    {
        if (!sprayWashing || !(prefSprayHaptics?.Value ?? true)) return 0f;
        float basis = sprayNozzle switch
        {
            (int)NozzleType.Red_0 => Jet0,
            (int)NozzleType.Yellow_15 => Jet15,
            (int)NozzleType.Green_25 => Jet25,
            (int)NozzleType.White_40 => Jet40,
            (int)NozzleType.Black_65_Soap => JetSoap,
            (int)NozzleType.Turbo => Jet0 * TurboFactor,   // Turbo ist ein gebuendelter 0-Grad-Strahl
            _ => JetDefault,
        };
        float washer = sprayClass switch { 0 => 0.85f, 1 => 1.0f, 2 => 1.15f, 3 => 1.3f, _ => 1f };
        float contact = ContactFactor + (1f - ContactFactor) * Mathf.Clamp01(sprayContact);
        return basis * washer * contact * SprayIntensity;
    }

    // Trifft der Strahl etwas? Entlang DERSELBEN Richtung, die die Mod dem Spiel
    // gibt (RaycastUpdatePostfix). Die Ueberladung ohne RaycastHit: zwei Vector3
    // hinein, bool heraus - die Struct-Sperre gilt fuer RaycastHit, nicht hier.
    private static bool ProbeSprayContact(int mask, float range = ContactRange)
    {
        if (!pubNozzleReady || pubNozzleDir.sqrMagnitude < 0.0001f) return false;
        try
        {
            var dir = pubNozzleDir.normalized;
            return Physics.Raycast(pubNozzleOrigin + dir * ContactSkip, dir, range, mask, QueryTriggerInteraction.Ignore);
        }
        catch { return false; }
    }

    // Nur fuer den Bericht: Abstand und Collider des ersten Treffers OHNE
    // RaycastHit (>= 24 Byte, ueberquert die Interop-Grenze nicht - PWS2
    // Handbuch, harter Prozesstod). Abstand per Bisektion ueber die
    // Bool-Ueberladung, Collider per OverlapSphere am Trefferpunkt.
    private static string DescribeContact(int mask)
    {
        if (!ProbeSprayContact(mask)) return "kein Treffer";
        try
        {
            float lo = 0f, hi = ContactRange;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (ProbeSprayContact(mask, Math.Max(mid, 0.001f))) hi = mid; else lo = mid;
            }
            var dir = pubNozzleDir.normalized;
            var p = pubNozzleOrigin + dir * (ContactSkip + hi);
            var cols = Physics.OverlapSphere(p, 0.05f, mask, QueryTriggerInteraction.Ignore);
            var sb = new System.Text.StringBuilder($"{hi:F2} m");
            int n = cols == null ? 0 : cols.Length;
            for (int i = 0; i < Math.Min(n, 3); i++)
            {
                var c = cols![i];
                if (c == null) continue;
                var go = c.gameObject;
                var root = c.transform.root;
                sb.Append($" '{go.name}' L{go.layer}({LayerMask.LayerToName(go.layer)}) {c.GetIl2CppType().Name} unter '{(root == null ? "-" : root.name)}'");
            }
            if (n > 3) sb.Append($" +{n - 3}");
            return sb.ToString();
        }
        catch (Exception e) { return "Treffer, Beschreibung " + e.GetType().Name; }
    }

    // Duese und Reiniger, verglichen am Enumwert. Ereignisimpulse nur beim
    // WECHSEL - der erste Lesevorgang ist keiner.
    private void ReadSprayConfig(WashEquipment we)
    {
        int nozzle = int.MinValue, cls = -1;
        try { var nd = we.m_nozzleData; if (nd != null) nozzle = (int)nd.NozzleType; } catch { }
        try { var wd = we.m_powerWasherData; if (wd != null) cls = (int)wd.Class; } catch { }
        try { int m = we.m_rayMask.value; if (m != sprayMask && m != 0) { sprayMask = m; LoggerInstance.Msg($"SPRUEH-HAPTIK: Kontaktmaske = m_rayMask 0x{m:X8}"); } } catch { }
        if (nozzle == sprayNozzle && cls == sprayClass) return;

        bool first = !spraySawConfig;
        int nozzleBefore = sprayNozzle, clsBefore = sprayClass;
        sprayNozzle = nozzle;
        sprayClass = cls;
        spraySawConfig = true;
        LoggerInstance.Msg($"SPRUEH-HAPTIK: Konfiguration Duese {NozzleName(nozzle)} Reiniger {(cls < 0 ? "?" : ((PowerWasherClass)cls).ToString())} -> Basis {SteadyBaseFor(nozzle, cls):F3}");
        if (first) return;

        bool soapBefore = nozzleBefore == (int)NozzleType.Black_65_Soap, soapNow = nozzle == (int)NozzleType.Black_65_Soap;
        if (soapBefore != soapNow)
        {
            QueueSpray(0.30f * SprayIntensity, 0.05f); QueueSprayGap(0.08f); QueueSpray(0.30f * SprayIntensity, 0.05f);   // weicher Doppelimpuls
        }
        else if (cls != clsBefore)
        {
            QueueSpray(0.45f * SprayIntensity, 0.03f); QueueSprayGap(0.06f); QueueSpray(0.45f * SprayIntensity, 0.03f);   // kurzer Doppelimpuls
        }
        else
        {
            QueueSpray(0.45f * SprayIntensity, 0.04f);   // Strahlwechsel: ein kurzer, praeziser Impuls
        }
    }

    private float SteadyBaseFor(int nozzle, int cls)
    {
        int n0 = sprayNozzle, c0 = sprayClass; bool w0 = sprayWashing; float k0 = sprayContact;
        sprayNozzle = nozzle; sprayClass = cls; sprayWashing = true; sprayContact = 1f;
        float v = SteadySprayAmplitude();
        sprayNozzle = n0; sprayClass = c0; sprayWashing = w0; sprayContact = k0;
        return v;
    }

    private static string NozzleName(int n) => n == int.MinValue ? "?" : ((NozzleType)n).ToString();
}
