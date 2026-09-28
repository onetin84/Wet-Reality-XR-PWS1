// Linkshaender-Modus (XRStart 1.33.0). PWS2-Schluessel "Hand" = RightHand |
// LeftHand (Konfigurator: Pistolenhand).
//
// ROLLEN statt Seiten: WasherCtl ist der Controller der Pistolenhand, OffCtl der
// der freien Hand. Alles, was bisher fest rechts/links las, liest die Rolle -
// Pistole, Sprühen, Drehen/Teleport/Duese am Stick, R3-Scheibe, A/B (primary/
// secondary der Pistolenhand), Gehen, Aufnehmen, Y, L3, Menue-Zeiger,
// Griffzeiger, Vibration, Handtreffer, Gesten. Linkshaendig liegt damit z. B.
// Springen auf X und Aufnehmen auf A - dieselben Rollen, gespiegelt.
// AUSNAHME wie PWS2: die MENUE-TASTE bleibt physisch links, weil es sie nur
// dort gibt (rechts liegt die reservierte Systemtaste).
//
// Handmodelle bleiben an ihrem Controller (L-Modell links, R-Modell rechts);
// gespiegelt werden Pose (Griffpose an der Pistolenhand) und Versatz: die
// Werte der Pistolenhand (HandRightPos/Rot) gelten gespiegelt fuer die linke
// Hand - Position (-x, y, z), Drehung (x, -y, -z) - und umgekehrt. Gestenzonen
// spiegeln X. Linkshaendig UNGETESTET in PWS2 wie hier.

using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<string> prefHand = null!;
    internal static bool leftHanded;

    private void InitHandedness(MelonPreferences_Category cat)
    {
        prefHand = cat.CreateEntry("Hand", "RightHand", description: "Pistolenhand: RightHand oder LeftHand (Linkshaender - alle Rollen gespiegelt, die Menue-Taste bleibt links)");
        leftHanded = string.Equals((prefHand.Value ?? "").Trim(), "LeftHand", StringComparison.OrdinalIgnoreCase);
        LoggerInstance.Msg($"HAND: Pistolenhand {(leftHanded ? "LINKS (Linkshaender)" : "rechts")}");
    }

    internal static XRController? WasherCtl => leftHanded ? XRController.leftHand : XRController.rightHand;
    internal static XRController? OffCtl => leftHanded ? XRController.rightHand : XRController.leftHand;

    internal static Vector3 MirrorPos(Vector3 p) => new(-p.x, p.y, p.z);
    internal static Vector3 MirrorRot(Vector3 e) => new(e.x, -e.y, -e.z);
    internal static float HandSide => leftHanded ? -1f : 1f;

    // Modell der freien Hand (Handtreffer-Collider) und der Pistolenhand.
    private GameObject? OffHandModel => leftHanded ? handR : handL;
}
