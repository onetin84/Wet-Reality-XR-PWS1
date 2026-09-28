// Y = Zurueck im Menue (XRStart 1.31.0). Nach PWS2 "Y im Menue" (Handbuch §6,
// §175): das fehlende ESC-Aequivalent. Nur SCHLIESSEN, nie oeffnen.
//
// PWS1 hat PlayerInput.NavigateBack(InputActionEventData) (0x6F3710) - fast
// ganz eingebettete Logik plus ein virtueller Aufruf, nicht sauber
// nachzubauen. Stattdessen der Weg ueber die Knoepfe, die das Spiel selbst
// zeigt (wie PWS2 Kontextknopf -> Popup-CloseButton), in dieser Reihenfolge:
//   1. Popup schliessen   - Name enthaelt "Close", aber nicht "Tablet"
//   2. Tablet zurueck     - Name enthaelt "Back"
//   3. Tablet schliessen  - "CloseTabletButton" (Rahmenmessung 1.28.0)
// Nur aktiv, interaktiv und sichtbar (CanvasGroup-Alpha > 0,01 ueber die
// Elternkette). Geklickt wie mit dem Zeiger (ClickMenu, MenuPointer.cs). Jeder
// Druck schreibt die Kandidaten ins Log - die Namen sind aus dem Dump und einer
// Messung, nicht aus jedem Menue.

using UnityEngine;
using UnityEngine.UI;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private void PressMenuBack()
    {
        try
        {
            if (uiRoot == null) { btnEvents.Add("Y Zurueck: keine UI"); return; }
            var all = uiRoot.GetComponentsInChildren<Selectable>(false);
            Selectable? popup = null, back = null, tablet = null;
            var names = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                if (s == null || !s.IsActive() || !s.IsInteractable() || !VisibleUp(s.transform)) continue;
                string n = s.name;
                bool close = n.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0;
                bool tab = n.IndexOf("Tablet", StringComparison.OrdinalIgnoreCase) >= 0;
                bool bk = n.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Background", StringComparison.OrdinalIgnoreCase) < 0;
                if (!close && !bk) continue;
                names.Add(n);
                if (close && !tab) { if (popup == null) popup = s; }
                else if (bk) { if (back == null) back = s; }
                else if (close && tab) { if (tablet == null) tablet = s; }
            }
            var pick = popup ?? back ?? tablet;
            string list = names.Count == 0 ? "keine" : string.Join(", ", names);
            if (pick == null) { btnEvents.Add($"Y Zurueck: nichts zu schliessen (Kandidaten: {list})"); return; }
            btnEvents.Add($"Y Zurueck: '{PathOf(pick.transform)}' ({(pick == popup ? "Popup" : pick == back ? "Zurueck" : "Tablet zu")}) - Kandidaten: {list}");
            ClickMenu(pick, pick.transform.position, null);
        }
        catch (Exception e) { btnEvents.Add("Y Zurueck: " + e.GetType().Name + ": " + e.Message); }
    }

    // Sichtbar = keine CanvasGroup mit Alpha ~0 in der Elternkette.
    private static bool VisibleUp(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            var g = p.GetComponent<CanvasGroup>();
            if (g != null && g.alpha < 0.01f) return false;
        }
        return true;
    }
}
