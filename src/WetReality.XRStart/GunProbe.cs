// Pistolenmessung (XRStart 0.7.0) - schreibt NICHTS an der Pistole.
//
// Frage vor dem 6DOF-Schreiber (Handbuch 3): Wie sieht die Kette unter
// PlayerCamera aus, und WER schreibt sie pro Frame? Die Lehre aus 2.7: ein
// Schreiber, der vom Transform zurueckliest, ist ohne fremden Schreibzugriff
// unsichtbar - hier wird deshalb erst nur gezaehlt, wer was bewegt.
//
// Zu Beginn des F9-Fensters:
//   - Baum unter Camera.main, Tiefe <= 6, je Knoten lokale Pose und Komponenten
//   - der EquipmentManager, dessen m_anchorPoint unter Camera.main haengt
//     (Zeigervergleich - PWS2 hatte Anker und Assembler doppelt, 1. und 3. Person)
//   - der PowerWasherAssembler unter diesem Anker, GunLocator, NozzleAnchor
// Verfolgt werden vier Knoten: Anker, Assembly, GunLocator, NozzleAnchor.
// Je Messframe (einmal pro Sekunde, wie der Kopf): ihre lokale Pose in U, L, R.
// Je Sekunde und Kandidat (EquipmentManager.Update, WashEquipment.LateUpdate,
// PositionToFOV.LateUpdate): Aufrufe, und wie oft und wie weit jeder Knoten
// zwischen Prefix und Postfix bewegt wurde. Was sich zwischen U und R bewegt,
// ohne dass ein Kandidat es war, hat einen anderen Schreiber (Animator, ...).

using System.Text;
using Il2CppPWS;
using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private static readonly string[] GunNames = { "Anker", "Assembly", "GunLocator", "NozzleAnchor" };
    private static readonly Transform?[] gunWatch = new Transform?[4];
    private static bool gunProbeOn;

    // Kandidaten: 0 EquipmentManager.Update, 1 WashEquipment.LateUpdate, 2 PositionToFOV.LateUpdate
    private static readonly string[] GunWriters = { "EquipmentManager.Update", "WashEquipment.LateUpdate", "PositionToFOV.LateUpdate" };
    private static readonly int[] gwCalls = new int[3];
    private static readonly int[,] gwMoves = new int[3, 4];
    private static readonly float[,] gwMaxPos = new float[3, 4];   // Meter
    private static readonly float[,] gwMaxRot = new float[3, 4];   // Grad
    // Schnappschuss je Kandidat; ein Aufruf ist nie in sich selbst verschachtelt.
    private static readonly Vector3[,] gwSnapP = new Vector3[3, 4];
    private static readonly Quaternion[,] gwSnapR = new Quaternion[3, 4];
    private static readonly bool[,] gwSnapOk = new bool[3, 4];

    private static string sGunU = "", sGunL = "-", sGunR = "-";

    private void PatchGunWriters()
    {
        PatchGun(typeof(EquipmentManager), "Update", nameof(GunPre0), nameof(GunPost0));
        PatchGun(typeof(WashEquipment), "LateUpdate", nameof(GunPre1), nameof(GunPost1));
        PatchGun(typeof(PositionToFOV), "LateUpdate", nameof(GunPre2), nameof(GunPost2));
    }

    private void PatchGun(Type type, string method, string prefix, string postfix)
    {
        try
        {
            var target = HarmonyLib.AccessTools.Method(type, method);
            if (target == null) { LoggerInstance.Error($"Patch {type.Name}.{method}: Methode nicht gefunden"); return; }
            HarmonyInstance.Patch(target,
                prefix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(XRStart), prefix)),
                postfix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(XRStart), postfix)));
            LoggerInstance.Msg($"Patch {type.Name}.{method}: installiert (ob er feuert, zeigt der Zaehler)");
        }
        catch (Exception e)
        {
            LoggerInstance.Error($"Patch {type.Name}.{method}: " + e);
        }
    }

    private static void GunPre0() => GunSnap(0);
    private static void GunPost0() => GunDiff(0);
    private static void GunPre1() => GunSnap(1);
    private static void GunPost1() => GunDiff(1);
    private static void GunPre2() => GunSnap(2);
    private static void GunPost2() => GunDiff(2);

    private static void GunSnap(int w)
    {
        if (!gunProbeOn) return;
        gwCalls[w]++;
        for (int i = 0; i < 4; i++)
        {
            var t = gunWatch[i];
            gwSnapOk[w, i] = false;
            if (t == null) continue;
            try
            {
                gwSnapP[w, i] = t.localPosition;
                gwSnapR[w, i] = t.localRotation;
                gwSnapOk[w, i] = true;
            }
            catch { }
        }
    }

    private static void GunDiff(int w)
    {
        if (!gunProbeOn) return;
        for (int i = 0; i < 4; i++)
        {
            var t = gunWatch[i];
            if (!gwSnapOk[w, i] || t == null) continue;
            try
            {
                float dp = (t.localPosition - gwSnapP[w, i]).magnitude;
                float dr = AngleDeg(gwSnapR[w, i], t.localRotation);
                if (dp > 1e-6f || dr > 1e-4f)
                {
                    gwMoves[w, i]++;
                    gwMaxPos[w, i] = Math.Max(gwMaxPos[w, i], dp);
                    gwMaxRot[w, i] = Math.Max(gwMaxRot[w, i], dr);
                }
            }
            catch { }
        }
    }

    // Zu Beginn des F9-Fensters. Loest die vier Knoten auf und schreibt den Baum.
    private void ReportGun()
    {
        for (int i = 0; i < 4; i++) gunWatch[i] = null;
        ResetGunStats();
        try
        {
            var cam = Camera.main;
            if (cam == null) { LoggerInstance.Msg("PISTOLE: Camera.main=null"); gunProbeOn = false; return; }
            var camT = cam.transform;

            var mgrs = UnityEngine.Object.FindObjectsOfType<EquipmentManager>();
            LoggerInstance.Msg($"PISTOLE: {mgrs.Length} EquipmentManager");
            Transform? anchor = null;
            for (int i = 0; i < mgrs.Length; i++)
            {
                var a = mgrs[i].m_anchorPoint;
                var a3 = mgrs[i].m_anchorPointThirdPerson;
                bool under = a != null && IsUnder(a, camT);
                LoggerInstance.Msg($"  [{i}] '{mgrs[i].gameObject.name}' m_anchorPoint='{Name(a)}' unter Camera.main: {under} ({PathOf(a)}) | " +
                    $"m_anchorPointThirdPerson='{Name(a3)}' ({PathOf(a3)})");
                if (under && anchor == null) anchor = a;
            }

            var asms = UnityEngine.Object.FindObjectsOfType<PowerWasherAssembler>();
            LoggerInstance.Msg($"PISTOLE: {asms.Length} PowerWasherAssembler");
            PowerWasherAssembler? asm = null;
            for (int i = 0; i < asms.Length; i++)
            {
                var t = asms[i].transform;
                bool under = anchor != null && IsUnder(t, anchor);
                LoggerInstance.Msg($"  [{i}] {PathOf(t)} unter Anker: {under} | GunLocator={PathOf(asms[i].GunLocator)} NozzleAnchor={PathOf(asms[i].NozzleAnchor)}");
                if (under && asm == null) asm = asms[i];
            }

            var ctl = UnityEngine.Object.FindObjectOfType<PlayerCameraController>();
            gunRig = ctl == null ? null : ctl.m_rigTransform;
            var ht = ctl == null ? null : ctl.m_horizontalLook;
            gunPlayer = ht == null ? null : ht.parent;
            LoggerInstance.Msg($"PISTOLE: Spieler={PathOf(gunPlayer)} Rig={PathOf(gunRig)}");

            gunWatch[0] = anchor;
            gunWatch[1] = asm == null ? null : asm.transform;
            gunWatch[2] = asm == null ? null : asm.GunLocator;
            gunWatch[3] = asm == null ? null : asm.NozzleAnchor;
            var sb = new StringBuilder("PISTOLE: verfolgt");
            for (int i = 0; i < 4; i++) sb.Append($" | {GunNames[i]}={PathOf(gunWatch[i])}");
            LoggerInstance.Msg(sb.ToString());

            LoggerInstance.Msg("PISTOLE: Baum unter Camera.main:");
            DumpTree(camT, 0, 6);
            gunProbeOn = true;
        }
        catch (Exception e)
        {
            LoggerInstance.Error("PISTOLE: Lesefehler " + e);
            gunProbeOn = false;
        }
    }

    private int treeLines;

    private void DumpTree(Transform t, int depth, int maxDepth)
    {
        if (depth == 0) treeLines = 0;
        if (treeLines++ > 120) { if (treeLines == 122) LoggerInstance.Msg("    ... (abgeschnitten)"); return; }
        var comps = new StringBuilder();
        try
        {
            var cs = t.GetComponents<Component>();
            for (int i = 0; i < cs.Length; i++)
            {
                var c = cs[i];
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                if (n == "Transform") continue;
                comps.Append(comps.Length == 0 ? "" : ",").Append(n);
            }
        }
        catch (Exception e) { comps.Append("Lesefehler " + e.GetType().Name); }
        LoggerInstance.Msg($"    {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} lp={t.localPosition.ToString("F3")} le={t.localEulerAngles.ToString("F1")} [{comps}]");
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++)
            DumpTree(t.GetChild(i), depth + 1, maxDepth);
    }

    private static bool IsUnder(Transform t, Transform root)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.Pointer == root.Pointer) return true;
        return false;
    }

    private static string PathOf(Transform? t)
    {
        if (t == null) return "null";
        var sb = new StringBuilder(t.name);
        int n = 0;
        for (var p = t.parent; p != null && n < 8; p = p.parent, n++) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    // Zusatz seit 0.7.1: ohne ihn war nicht zu sehen, ob waehrend der Messung
    // ueberhaupt gelaufen oder gezielt wurde (0.7.0: zwei Kopfwerte in 20 s).
    // Spieler = Wurzel ueber HeadTurn; Rig = PlayerCameraController.m_rigTransform
    // und sein Elternknoten (RigOffset); Anker welt = was die Armanimation bewegt.
    private static Transform? gunPlayer, gunRig;

    private static string ReadGun()
    {
        var sb = new StringBuilder();
        try
        {
            if (gunPlayer != null) sb.Append($" Spieler wp={gunPlayer.position.ToString("F2")}");
            if (gunRig != null)
            {
                var ro = gunRig.parent;
                sb.Append($" Rig lp={gunRig.localPosition.ToString("F3")} le={gunRig.localEulerAngles.ToString("F1")}");
                if (ro != null) sb.Append($" {ro.name} lp={ro.localPosition.ToString("F3")} le={ro.localEulerAngles.ToString("F1")}");
            }
            var a = gunWatch[0];
            var cam = Camera.main;
            if (a != null && cam != null)
            {
                // Anker relativ zur Kamera: frei von Kopf- und Koerperdrehung.
                var ct = cam.transform;
                var rel = ct.InverseTransformPoint(a.position);
                var relRot = Quaternion.Inverse(ct.rotation) * a.rotation;
                sb.Append($" Anker-zur-Kamera p={rel.ToString("F3")} e={relRot.eulerAngles.ToString("F1")}");
            }
        }
        catch (Exception e) { sb.Append(" Zusatz-Lesefehler " + e.GetType().Name); }
        for (int i = 0; i < 4; i++)
        {
            var t = gunWatch[i];
            if (t == null) { sb.Append($" {GunNames[i]}=null"); continue; }
            try { sb.Append($" {GunNames[i]} lp={t.localPosition.ToString("F3")} le={t.localEulerAngles.ToString("F1")}"); }
            catch (Exception e) { sb.Append($" {GunNames[i]} Lesefehler {e.GetType().Name}"); }
        }
        return sb.ToString();
    }

    private void FlushGun(int frame, int frames)
    {
        if (!gunProbeOn) return;
        LoggerInstance.Msg($"PISTOLE f={frame} | U{sGunU} | L{sGunL} | R{sGunR}");
        var sb = new StringBuilder($"PISTOLE-SCHREIBER in {frames} Frames:");
        for (int w = 0; w < 3; w++)
        {
            sb.Append($" | {GunWriters[w]} {gwCalls[w]}x");
            for (int i = 0; i < 4; i++)
                if (gwMoves[w, i] > 0)
                    sb.Append($" {GunNames[i]} {gwMoves[w, i]}x max {gwMaxPos[w, i] * 1000f:F1}mm/{gwMaxRot[w, i]:F2}°");
        }
        LoggerInstance.Msg(sb.ToString());
        ResetGunStats();
    }

    private static void ResetGunStats()
    {
        Array.Clear(gwCalls);
        Array.Clear(gwMoves);
        Array.Clear(gwMaxPos);
        Array.Clear(gwMaxRot);
    }
}
