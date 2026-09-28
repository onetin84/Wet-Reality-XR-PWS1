// Immersionsmodus (XRStart 1.24.0). Port von PWS2 (Pose.cs ToggleImmersion,
// GameUi.ApplyHidden, PWS2-Handbuch "Menue halten").
//
// GESTE auf der Menue-Taste (links): TIPPEN oeffnet die Pause wie bisher, aber
// beim LOSLASSEN; HALTEN (MenuHoldSeconds, 0,6 s) schaltet die Spiel-UI aus/an.
// Der Puls bestaetigt, WAEHREND die Taste noch unten ist. Ein Doppelklick war in
// PWS2 die erste Fassung - Virtual Desktop belegt ihn selbst. MenuHoldSeconds 0
// = keine Geste, Pause wieder beim Druecken.
//
// AUSBLENDEN wie PWS2: CanvasGroup.alpha auf UIRoot (multipliziert durch jeden
// Unter-Canvas, ohne OnDisable - ScreenManagerViewportBase haelt in OnDisable
// die Screen-Liste; SetActive haette Screens entladen koennen). Hat UIRoot
// keine CanvasGroup, legt die Mod eine an. ABGLEICH je Frame gegen den WUNSCH
// "immersion && kein Menue && keine Scheibe": ein offenes Menue, das Inventar
// und die Waehlscheibe bleiben sichtbar - sonst oeffnete ein Tippen im
// Immersionsmodus eine unsichtbare Pause, und die Bewegung stuende ohne Grund.
// Idempotent: im Normalfall ein bool-Vergleich.

using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<float> prefMenuHold = null!;
    private static float menuHoldStatic = 0.6f;

    internal static bool immersion;
    private static float menuDownAt = -1f;
    private static bool menuHoldFired;

    private CanvasGroup? immersionGroup;
    private bool uiHidden;

    private void InitImmersion(MelonPreferences_Category cat)
    {
        prefMenuHold = cat.CreateEntry("MenuHoldSeconds", 0.6f, description: "Sekunden die Menue-Taste HALTEN fuer den Immersionsmodus (Spiel-UI aus/an). Kurz tippen oeffnet die Pause beim Loslassen. 0 = keine Geste.");
        menuHoldStatic = prefMenuHold.Value;
    }

    // Aus UpdateButtons (PlayerInput.Update-Postfix).
    private static void MenuButton(Il2CppPWS.PlayerInput pi, XRController? l)
    {
        bool down = Held(l, "menu");
        float now = Time.unscaledTime;
        float hold = menuHoldStatic;
        if (hold <= 0.001f)
        {
            // Ohne Geste: wie bis 1.23.0, Pause beim Druecken.
            if (down && menuDownAt < 0f) { menuDownAt = now; pi.m_pauseAction?.Invoke(); btnEvents.Add("Menue: m_pauseAction"); }
            else if (!down) menuDownAt = -1f;
            return;
        }
        if (down)
        {
            if (menuDownAt < 0f) { menuDownAt = now; menuHoldFired = false; return; }
            if (!menuHoldFired && now - menuDownAt >= hold)
            {
                menuHoldFired = true;
                immersion = !immersion;
                Buzz(false, immersion ? "Immersion an" : "Immersion aus");   // freie Hand
                btnEvents.Add($"Menue gehalten ({hold:F2} s): Immersionsmodus {(immersion ? "AN" : "aus")}");
            }
            return;
        }
        if (menuDownAt >= 0f)
        {
            if (!menuHoldFired) { pi.m_pauseAction?.Invoke(); btnEvents.Add("Menue getippt: m_pauseAction"); }
            menuDownAt = -1f;
            menuHoldFired = false;
        }
    }

    // Aus OnUpdate, nach TickVrUi: Wunsch gegen Zustand.
    private void TickImmersion()
    {
        menuHoldStatic = prefMenuHold.Value;
        bool want = started && immersion && !menuActive && !wheelOpen;
        if (want == uiHidden) return;
        if (!ResolveUiRoot()) return;
        try
        {
            if (immersionGroup == null)
            {
                var root = uiRoot!.gameObject;
                immersionGroup = root.GetComponent<CanvasGroup>();
                if (immersionGroup == null)
                {
                    immersionGroup = root.AddComponent<CanvasGroup>();
                    LoggerInstance.Msg("IMMERSION: UIRoot hatte keine CanvasGroup - eine angelegt");
                }
            }
            immersionGroup.alpha = want ? 0f : 1f;
            immersionGroup.blocksRaycasts = !want;
            uiHidden = want;
            Diag($"IMMERSION: Spiel-UI {(want ? "AUS" : "wieder sichtbar")} (Modus {(immersion ? "an" : "aus")}, Menue {(menuActive ? "offen" : "zu")}, Scheibe {(wheelOpen ? "offen" : "zu")})");
        }
        catch (Exception e) { LoggerInstance.Warning("IMMERSION: " + e.GetType().Name + ": " + e.Message); immersionGroup = null; uiHidden = false; }
    }
}
