// Ziellaser (XRStart 1.32.0). Nach PWS2 WashLaser: eine rote Linie aus der Duese
// entlang der TATSAECHLICHEN Strahlrichtung (pubNozzleOrigin/-Dir aus GunFixes,
// dieselbe, die die Haptik benutzt), bis zur ersten Oberflaeche der Strahlmaske
// (Bisektion ueber Bool-Raycasts - kein RaycastHit ueber die Interop-Grenze),
// sonst LaserLength. Zeichnet nur, schreibt nichts ins Spiel.
//
// AUS in der Vorgabe (PWS2: ein Messwerkzeug; eine Linie aus der Duese ist das
// Erste, was ein neuer Spieler als Fehler meldet) - im Konfigurator als
// "Ziellaser anzeigen" einschaltbar. EIN Abbauweg: jede Luecke (kein Strahl,
// Menue, Scheibe, XR aus) blendet die Linie im selben Frame aus - PWS2s erste
// Fassung liess eine eingefrorene Linie stehen.

using MelonLoader;
using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private MelonPreferences_Entry<bool> prefShowLaser = null!;
    private MelonPreferences_Entry<float> prefLaserLength = null!, prefLaserWidth = null!;
    private LineRenderer? laserLine;
    private GameObject? laserGo;
    private static readonly Color LaserColor = new(1f, 0.15f, 0.1f, 0.85f);

    private void InitWashLaser(MelonPreferences_Category cat)
    {
        prefShowLaser = cat.CreateEntry("ShowWashLaser", false, description: "Rote Linie aus der Duese entlang der Strahlrichtung bis zur ersten Oberflaeche (Zielhilfe, PWS2)");
        prefLaserLength = cat.CreateEntry("LaserLength", 4f, description: "Meter, wenn nichts getroffen wird");
        prefLaserWidth = cat.CreateEntry("LaserWidth", 0.006f, description: "Meter Linienbreite");
    }

    // In onBeforeRender nach der Pistole (pubNozzle ist dann gesetzt).
    private void DriveWashLaser()
    {
        bool want = prefShowLaser.Value && started && writeGun && pubNozzleReady && !menuActive && !wheelOpen && !tpAiming;
        if (!want) { HideLaser(); return; }
        try
        {
            if (laserLine == null) laserLine = MakeLine("WetReality_WashLaser", out laserGo);
            var dir = pubNozzleDir.normalized;
            var start = pubNozzleOrigin;
            float len = Math.Max(0.1f, prefLaserLength.Value);
            int mask = sprayMask;
            if (mask != 0 && Physics.Raycast(start, dir, len, mask, QueryTriggerInteraction.Ignore))
            {
                float lo = 0f, hi = len;
                for (int i = 0; i < 12; i++) { float mid = (lo + hi) * 0.5f; if (Physics.Raycast(start, dir, mid, mask, QueryTriggerInteraction.Ignore)) hi = mid; else lo = mid; }
                len = hi;
            }
            laserLine.startColor = laserLine.endColor = LaserColor;
            laserLine.startWidth = laserLine.endWidth = Math.Max(0.001f, prefLaserWidth.Value);
            laserLine.positionCount = 2;
            laserLine.SetPosition(0, start);
            laserLine.SetPosition(1, start + dir * len);
            if (!laserLine.enabled) laserLine.enabled = true;
        }
        catch (Exception e) { LoggerInstance.Warning("ZIELLASER: " + e.GetType().Name + ": " + e.Message); DropLaser(); }
    }

    private void HideLaser()
    {
        try { if (laserLine != null && laserLine.enabled) laserLine.enabled = false; } catch { DropLaser(); }
    }

    private void DropLaser()
    {
        try { if (laserGo != null) UnityEngine.Object.Destroy(laserGo); } catch { }
        laserGo = null; laserLine = null;
    }
}
