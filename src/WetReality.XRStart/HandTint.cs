// Orange Handschuhe (XRStart 1.30.0). Port von PWS2 VrHands.ApplyTint.
//
// Flach traegt der Spieler orange Handschuhe; die VR-Haende (Rig_VRHand_L/R)
// haben ein eigenes Lit-Material. Getoent wird _BaseColor auf der EIGENEN
// Materialinstanz (renderer.materials - dieselben Instanzen, die FixMirrored
// schon anlegt), nie auf dem geteilten Material des Spiels. _BaseColor
// MULTIPLIZIERT die Grundtextur; ob es eine gibt, sagt die Logzeile - mit einer
// hautfarbenen Textur wird das Orange dunkler, dann ist HandTintColor
// nachzustellen, nicht der Weg. cfg OrangeHands (an), HandTintColor #F5912A
// (PWS2-Vorgabe). Gilt beim Laden der Haende (Konfigurator: Spiel zu).

using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefOrangeHands = null!;
    private MelonPreferences_Entry<string> prefHandTint = null!;

    private void InitHandTint(MelonPreferences_Category cat)
    {
        prefOrangeHands = cat.CreateEntry("OrangeHands", true, description: "VR-Haende wie die orangen Handschuhe des Spiels einfaerben (Farbe HandTintColor)");
        prefHandTint = cat.CreateEntry("HandTintColor", "#F5912A", description: "Handschuhfarbe als #RRGGBB; multipliziert die Grundtextur der Hand");
    }

    private void TintHand(GameObject go, string label)
    {
        if (!prefOrangeHands.Value) return;
        if (!ColorUtility.TryParseHtmlString((prefHandTint.Value ?? "").Trim(), out var tint))
        {
            LoggerInstance.Warning($"HAENDE: HandTintColor \"{prefHandTint.Value}\" ist kein #RRGGBB - ungetoent");
            return;
        }
        try
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (var m in r.materials)   // eigene Instanzen
                {
                    if (m == null) continue;
                    Color before = Color.white;
                    try { before = m.GetColor("_BaseColor"); } catch { }
                    try { m.SetColor("_BaseColor", tint); } catch { }
                    try { m.SetColor("_Color", tint); } catch { }   // falls kein URP-Lit
                    Texture? map = null;
                    try { map = m.GetTexture("_BaseMap"); } catch { }
                    LoggerInstance.Msg($"HAENDE: {label} '{r.name}' Farbe {ColorUtility.ToHtmlStringRGB(before)} -> {ColorUtility.ToHtmlStringRGB(tint)}, Shader '{(m.shader == null ? "?" : m.shader.name)}', Grundtextur {(map == null ? "keine" : "'" + map.name + "'")}");
                }
            }
        }
        catch (Exception e) { LoggerInstance.Warning($"HAENDE: Einfaerben {label} " + e.GetType().Name + ": " + e.Message); }
    }
}
