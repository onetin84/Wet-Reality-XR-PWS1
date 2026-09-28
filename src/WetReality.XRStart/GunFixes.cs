// PWS2-Korrekturen fuer Pistole und Strahl (XRStart 0.9.0).
//
// 0.8.0 im Headset (Nutzer): Ausrichtung folgt dem Controller, die Pistole
// "klebt" aber nah am Koerper und zittert; der Strahl zielt auf die
// Bildmitte, knickt mit Versatz in Blickrichtung ab. Das Log sagt: der
// TRANSFORM steht richtig (Rueckleseprobe 0,0 mm) - es ist nicht die Pose.
// Dieselben Symptome hatte PWS2; die Loesungen stehen im PWS2-Quellcode
// (reference/, GameInput.cs, GunRender.cs, Pose.cs, Endmaske WashAimSkip
// 230289). Hier auf die PWS1-Namen uebertragen, alle zusammen hinter F4:
//
//   _LockFOV = 0        PWS2 Bit 32768. Der Pistolenshader verschiebt die
//                       Vertices mit dem Sichtfeld (Viewmodel-FOV-Lock); der
//                       globale Float ist ein SCHALTER, nur 0 ist aus. Id aus
//                       CharacterVisuals.m_fovLockPropID. Pro Frame in
//                       onBeforeRender, das Spiel setzt ihn selbst.
//   PositionToFOV aus   PWS2 Bit 16. WashEquipment.m_positionToFOV.enabled,
//                       alle 0,5 s neu aufgeloest (Unity-null nach Umbau).
//   SetWashDirection    PWS2 Bit 1. Parameterloser Prefix, gibt false: der
//                       Blick-Zweig der Richtung laeuft nicht.
//   RaySpawnPoint       PWS2 Bit 128. localRotation = Euler(RaySpawnPointRotation)
//                       statt Identitaet (1.35.0): die Duese zeigt, wohin ihr
//                       Locator zeigt, plus der Faecherwinkel des Tridents.
//                       Bei NozzleType.Turbo zusaetzlich Kippen und Kreisen -
//                       beides macht sonst SetWashDirection, das wir ueberspringen.
//   Raycaster           PWS2 Bit 256. Postfix auf RaycastUpdate schreibt
//                       Origin/Direction jedes m_nozzleRaycasters[i] - MIT
//                       Zurueckschreiben (instances[i] = r): der Indexer gibt
//                       eine Kopie des Structs (PWS2 §72, sechs Anlaeufe).
//                       Richtung und Ursprung aus der im letzten Render
//                       geschriebenen Pistolenpose (veroeffentlicht, nicht am
//                       Transform gelesen) plus der Lage der Duese in der
//                       Assembly - ein Frame Nachlauf, wie in PWS2.
//   VerticalLookRotation PWS2 Bit 512. Nach UpdateRotation = HMD-Neigung,
//                       sonst rechnet das Spiel mit dem Maus-Pitch.
// Nur aktiv, solange die Pistole geschrieben wird. Den seitlichen Duesenversatz
// (PWS2 Bit 65536): seit 0.9.1 vom Zwilling der 3. Person, siehe ClampNozzleAnchor.

using Il2CppPWS;
using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int VK_F4 = 0x73;
    private bool f4WasDown;
    private static bool fixesOn = true;
    private static bool FixActive => fixesOn && gunPublished;

    // Veroeffentlicht von DriveGun, gelesen vom RaycastUpdate-Postfix.
    private static bool gunPublished;
    private static Vector3 pubAsmPos;
    private static Quaternion pubAsmRot = Quaternion.identity;

    private static int swdCalls, swdSkipped, rcCalls, rcWrites, rcNozzles, pitchWrites;
    private static string rcLast = "";
    private static float lastPitchWritten;

    private WashEquipment? fixWash;
    private PositionToFOV? fixP2F;
    private float nextFixResolve;
    private bool p2fDisabledByUs;
    private int lockId = -1;
    private bool lockCaptured;
    private float lockRest, lockReadBefore;
    private bool lockWritten;

    private void PatchGunFixes()
    {
        try
        {
            var swd = HarmonyLib.AccessTools.Method(typeof(WashEquipment), "SetWashDirection");
            var rc = HarmonyLib.AccessTools.Method(typeof(WashEquipment), "RaycastUpdate");
            if (swd == null || rc == null) { LoggerInstance.Error($"Patch Korrekturen: SetWashDirection {(swd == null ? "fehlt" : "ok")}, RaycastUpdate {(rc == null ? "fehlt" : "ok")}"); }
            if (swd != null)
                HarmonyInstance.Patch(swd, prefix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(XRStart), nameof(SetWashDirectionPrefix))));
            if (rc != null)
                HarmonyInstance.Patch(rc, postfix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(XRStart), nameof(RaycastUpdatePostfix))));
            LoggerInstance.Msg("Patch Korrekturen: SetWashDirection (Prefix), RaycastUpdate (Postfix) installiert");
        }
        catch (Exception e)
        {
            LoggerInstance.Error("Patch Korrekturen: " + e);
        }
    }

    // Parameterlos: nichts ueberquert die Interop-Grenze (PWS2 §64).
    private static bool SetWashDirectionPrefix()
    {
        swdCalls++;
        if (!FixActive) return true;
        swdSkipped++;
        return false;
    }

    private static void RaycastUpdatePostfix(WashEquipment __instance)
    {
        rcCalls++;
        if (!FixActive) return;
        try
        {
            var arr = __instance.m_nozzleRaycasters;
            if (arr == null) return;
            int n = Math.Min(arr.Length, Math.Max(__instance.m_activeNozzleCount, 1));
            var asm = gunAsmStatic;
            if (asm == null) return;
            var sb = new System.Text.StringBuilder();
            var settings = __instance.WasherSettings;
            bool turbo = settings != null && settings.m_nozzleType == NozzleType.Turbo;
            if (turbo) AdvanceTurbo(__instance, settings!);
            for (int i = 0; i < n; i++)
            {
                var r = arr[i];
                if (r == null) continue;
                var anchor = r.NozzleAnchor;
                var rs = anchor == null ? null : anchor.RaySpawnPoint;
                if (rs == null) continue;
                rs.localRotation = NozzleLocalRotation(r.RaySpawnPointRotation, turbo ? settings : null, __instance.m_turboRotation);   // PWS2 Bit 128
                if (rs.parent != null) ClampNozzleAnchor(rs.parent, r.NozzleAnchorOffset, r.RaySpawnPointRotation);   // PWS2 Bit 65536, vor relPos
                // Lage der Duese in der Assembly - starr, unabhaengig davon, wo
                // die Animation die Assembly gerade hingesetzt hat.
                var relPos = asm.InverseTransformPoint(rs.position);
                var relDir = asm.InverseTransformDirection(rs.forward);
                var origin = pubAsmPos + pubAsmRot * relPos;
                var dir = pubAsmRot * relDir;
                if (i == 0) sb.Append($"spiel o={r.Origin.ToString("F3")} d={r.Direction.ToString("F3")} -> o={origin.ToString("F3")} d={dir.ToString("F3")} | Duese lp={rs.parent?.localPosition.ToString("F3")}");
                if (i == 0) { pubNozzleOrigin = origin; pubNozzleDir = dir; pubNozzleReady = true; }   // fuer den Kontaktstrahl der Haptik
                r.Origin = origin;
                r.Direction = dir;
                arr[i] = r;   // ZURUECKSCHREIBEN - der Indexer gab eine Kopie
                rcWrites++;
            }
            rcNozzles = n;
            rcTurbo = turbo ? $"Turbo {__instance.m_turboRotation:F0} Grad (Winkel {settings!.TurboAngle:F1}, {settings.TurboSpeed:F2} U/s)" : "";
            if (sb.Length > 0) rcLast = sb.ToString();
        }
        catch { }
    }

    // Nachbau von SetWashDirection + <SetWashDirection>g__ApplyTurboRotation
    // (disassembliert 1.35.0, tools/disasm_SetWashDirection.txt,
    // disasm_ApplyTurboRotation.txt). Das Spiel setzt je Duese
    //   rs.rotation = Zielrichtung * Euler(RaySpawnPointRotation)
    // und bei NozzleType.Turbo danach
    //   m_turboRotation = Repeat(m_turboRotation + TurboSpeed * 360 * dt, 360)
    //   rs.localRotation *= Euler(TurboAngle, 0, 0)
    //   rs.RotateAround(rs.forward VOR dem Kippen, m_turboRotation)
    // Die Zielrichtung ist bei uns die Pistole (Locator), also lokal
    //   Euler(spawn) * AngleAxis(turbo, forward) * Euler(TurboAngle, 0, 0).
    private static int turboFrame = -1;
    private static string rcTurbo = "";

    private static void AdvanceTurbo(WashEquipment w, WasherClassNozzleSettings s)
    {
        if (Time.frameCount == turboFrame) return;   // einmal je Frame, wie SetWashDirection
        turboFrame = Time.frameCount;
        w.m_turboRotation = Mathf.Repeat(w.m_turboRotation + s.TurboSpeed * 360f * Time.deltaTime, 360f);
    }

    private static Quaternion NozzleLocalRotation(Vector3 spawnRot, WasherClassNozzleSettings? turbo, float turboDeg)
    {
        var q = Quaternion.Euler(spawnRot);
        if (turbo != null) q = q * Quaternion.AngleAxis(turboDeg, Vector3.forward) * Quaternion.Euler(turbo.TurboAngle, 0f, 0f);
        return q;
    }

    // Aus RotationPostfix: nach UpdateRotation die Neigung des HMD statt der Maus.
    private static void ApplyLookPitch(PlayerCameraController c)
    {
        if (!FixActive) return;
        try
        {
            // NICHT am Transform gelesen: UpdateRotation hat PlayerCamera.x eben
            // mit dem Maus-Pitch ueberschrieben. Die Neigung, die DriveHead im
            // letzten Render geschrieben hat, ist veroeffentlicht.
            if (!headPitchPublished) return;
            float x = headPitchPub;
            c.VerticalLookRotation = x;
            lastPitchWritten = x;
            pitchWrites++;
        }
        catch { }
    }

    // PWS2 Bit 65536 in der Endfassung 1.103.0 - der UNVERSCHOBENE ZWILLING.
    // PositionToFOV hat den Duesenanker der ersten Person verschoben, bevor
    // wir es abschalten; der Wert bleibt eingebacken (0.9.0 gemessen: lp
    // (0.086, -0.097, -0.004), PWS2 im VR-FOV (0.084, -0.080)). Die dritte
    // Person traegt dieselbe Kette OHNE PositionToFOV und damit den verfassten
    // Wert. Pfad Assembly -> Anker aus der lebenden Kette, nicht aus Namen
    // (Lokator- und Klonnamen wechseln mit Duese und Verlaengerung). Kein
    // Zwilling (in PWS1 bisher immer): seitlich auf den ENTWORFENEN Versatz
    // klemmen, z bleibt. SetAnchorPoints setzt localPosition = NozzleAnchorOffset
    // (+ Kameramodus-Zuschlag); beim Trident traegt er den seitlichen Abstand
    // der drei Strahlen - bis 1.34.0 auf 0 geklemmt, alle drei auf einer Linie.
    private static Transform? thirdAsm;
    private static readonly Dictionary<IntPtr, Transform?> anchorTwins = new();
    private static readonly HashSet<IntPtr> anchorLogged = new();
    private static string anchorLog = "";

    private static void ClampNozzleAnchor(Transform anchor, Vector3 designed, Vector3 spawnRot)
    {
        var asm = gunAsmStatic;
        if (asm == null) return;
        var lp = anchor.localPosition;
        Vector3 target;
        if (!anchorTwins.TryGetValue(anchor.Pointer, out var twin) || (twin != null && twin == null))
        {
            twin = FindTwin(anchor, asm, out var path);
            anchorTwins[anchor.Pointer] = twin;
            if (anchorLogged.Add(anchor.Pointer))
                anchorLog += (anchorLog.Length > 0 ? "\n  " : "") + $"Duesenanker {path}: 1. Person {lp.ToString("F3")} | Entwurf {designed.ToString("F3")} Faecher {spawnRot.ToString("F1")} | 3. Person {(twin == null ? "NICHT GEFUNDEN - seitlich auf Entwurf geklemmt" : twin.localPosition.ToString("F3"))}";
        }
        target = twin == null ? new Vector3(designed.x, designed.y, lp.z) : twin.localPosition;
        if (lp != target) anchor.localPosition = target;
    }

    private static Transform? FindTwin(Transform anchor, Transform asm, out string path)
    {
        path = anchor.name;
        for (var n = anchor.parent; n != null; n = n.parent)
        {
            if (n.Pointer == asm.Pointer)
            {
                var t3 = ThirdPersonAssembly(asm);
                return t3 == null ? null : t3.Find(path);
            }
            path = n.name + "/" + path;
        }
        path = "(nicht unter der Assembly) " + path;
        return null;
    }

    // Der Assembler, der NICHT unter Camera.main haengt.
    private static Transform? ThirdPersonAssembly(Transform firstPerson)
    {
        if (thirdAsm != null) return thirdAsm;
        var cam = Camera.main;
        var asms = UnityEngine.Object.FindObjectsOfType<PowerWasherAssembler>();
        for (int i = 0; i < asms.Length; i++)
        {
            var t = asms[i].transform;
            if (t.Pointer == firstPerson.Pointer) continue;
            if (cam != null && IsUnder(t, cam.transform)) continue;
            thirdAsm = t;
            return t;
        }
        return null;
    }

    private static Transform? gunAsmStatic;

    // Von DriveHead veroeffentlicht: PlayerCamera.x nach dem Schreiben, vorzeichenbehaftet.
    private static bool headPitchPublished;
    private static float headPitchPub;

    // In onBeforeRender nach DriveGun: Shader-Schalter und PositionToFOV.
    private void ApplyGunFixes()
    {
        gunAsmStatic = gunAsm;
        float now = Time.unscaledTime;
        bool on = FixActive;
        try
        {
            if (lockId < 0)
            {
                var vis = UnityEngine.Object.FindObjectOfType<CharacterVisuals>();
                if (vis != null)
                {
                    lockId = vis.m_fovLockPropID;
                    LoggerInstance.Msg($"KORREKTUR: _LockFOV id {lockId}, Wert {Shader.GetGlobalFloat(lockId):F3}, toggle id {vis.m_fovTogglePropID}");
                }
            }
            if (lockId >= 0)
            {
                lockReadBefore = Shader.GetGlobalFloat(lockId);
                if (!lockCaptured) { lockRest = lockReadBefore; lockCaptured = true; }
                if (on) { Shader.SetGlobalFloat(lockId, 0f); lockWritten = true; }
                else if (lockWritten) { Shader.SetGlobalFloat(lockId, lockRest); lockWritten = false; }
            }

            if (fixWash == null || fixP2F == null || now >= nextFixResolve)
            {
                nextFixResolve = now + 0.5f;
                fixWash = gunAsm == null ? null : gunAsm.GetComponentInParent<WashEquipment>();
                if (fixWash == null) fixWash = UnityEngine.Object.FindObjectOfType<WashEquipment>();
                var p = fixWash == null ? null : fixWash.m_positionToFOV;
                if (p != null && (fixP2F == null || p.Pointer != fixP2F.Pointer))
                {
                    if (fixP2F != null) LoggerInstance.Msg("KORREKTUR: PositionToFOV ersetzt - Waescher neu gebaut");
                    fixP2F = p;
                    p2fDisabledByUs = false;
                }
            }
            if (fixP2F != null)
            {
                if (on && fixP2F.enabled) { fixP2F.enabled = false; p2fDisabledByUs = true; }
                else if (!on && p2fDisabledByUs) { fixP2F.enabled = true; p2fDisabledByUs = false; }
            }

            // Auch vor dem Rendern: der Strahleffekt haengt unter dem Anker.
            if (on && fixWash != null)
            {
                var arr = fixWash.m_nozzleRaycasters;
                int n = arr == null ? 0 : Math.Min(arr.Length, Math.Max(fixWash.m_activeNozzleCount, 1));
                for (int i = 0; i < n; i++)
                {
                    var r = arr![i];
                    var rs = r?.NozzleAnchor?.RaySpawnPoint;
                    if (rs != null && rs.parent != null) ClampNozzleAnchor(rs.parent, r!.NozzleAnchorOffset, r.RaySpawnPointRotation);
                }
            }
            if (anchorLog.Length > 0) { LoggerInstance.Msg("KORREKTUR: " + anchorLog); anchorLog = ""; }
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("KORREKTUR: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // Mit dem Ausschalten der Pistole: alles zurueck, was ein Zustand ist.
    private void ReleaseGunFixes()
    {
        gunPublished = false;
        try
        {
            if (lockWritten && lockId >= 0) { Shader.SetGlobalFloat(lockId, lockRest); lockWritten = false; }
            if (p2fDisabledByUs && fixP2F != null) { fixP2F.enabled = true; p2fDisabledByUs = false; }
        }
        catch { }
    }

    private string FixStatus()
    {
        var s = $"KORREKTUREN {(fixesOn ? "AN" : "AUS")}{(FixActive ? "" : " (inaktiv)")} | _LockFOV vorher {lockReadBefore:F2} ({(lockWritten ? "->0" : "Spiel")}) | " +
            $"PositionToFOV {(fixP2F == null ? "fehlt" : fixP2F.enabled ? "an" : "aus")} | SetWashDirection {swdSkipped}/{swdCalls} uebersprungen | " +
            $"RaycastUpdate {rcCalls}x, {rcWrites} Raycaster geschrieben ({rcNozzles} Duesen){(rcTurbo.Length > 0 ? " " + rcTurbo : "")} | Pitch {pitchWrites}x zuletzt {lastPitchWritten:F1} | {rcLast}";
        swdCalls = swdSkipped = rcCalls = rcWrites = pitchWrites = 0;
        return s;
    }
}
