# Wet Reality XR Mod — PowerWash Simulator

Room-Scale-VR für **PowerWash Simulator** (das erste Spiel) mit Headset und
Motion-Controllern. Die Pistole folgt deiner Hand in sechs Freiheitsgraden: Wohin du
schaust und wohin du sprühst, ist völlig unabhängig.

English: see [README.md](README.md).

> Inoffizielles Fan-Projekt, nicht verbunden mit FuturLab oder Square Enix.
> Getestet mit Meta Quest 3 über Virtual Desktop. Andere OpenXR-Headsets sollten
> funktionieren, sind aber nicht ausprobiert.

## Funktionen

- **Pistole in 6DOF** in der rechten Hand (Linkshänder-Modus wählbar), VR-Hände mit
  den Modellen des Spiels, orange Handschuhe, Griff-Feintuning — auch live im Spiel
- **Spielmenüs in VR** mit Zeigestrahl — Hauptmenü, Pause, Shop, Popups; `Y` geht zurück
- **Ausrüstungsscheiben** (`R3` halten), bedient mit Strahl oder linkem Stick
- **Ziel-Teleport** auf dem Stick der Pistolenhand, mit der Sprunghülle des Spiels:
  Vorsprünge, begehbare Treppen, oben auf eine aufgestellte Leiter
- **Komfort**: Teleport statt Gehen, Sprungdrehung, Vignette, Vorlagen
- **Gesten**: hinter die Schulter = nächster Washer, an die Hüfte = Seife aus dem
  Holster, freie Hand an die Pistole = Verlängerung
- **Vibration**: beim Sprühen nach Düse und Kontakt, Strahl auf die eigene freie Hand
- **Immersionsmodus** (Menütaste halten): Spiel-UI aus und an
- Himmel mit Wolken, schwarze Ladebildschirme, Startlogo, Monitorbild
- **Konfigurator** (Windows) für alle Spieler-Einstellungen

Steuerung im Detail: [Kurzanleitung](tools/frontend/QuickGuide-de.html) ·
[Quick Guide](tools/frontend/QuickGuide.html)

## Voraussetzungen

- PowerWash Simulator auf Steam (getestet: Version 1.11.0, Build 1394)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) **0.7.3**, x64
- Eine OpenXR-Laufzeit für dein Headset (zum Beispiel Virtual Desktop, Meta Quest Link
  oder SteamVR). Das Spiel bringt das Unity-OpenXR-Plugin selbst mit.

## Installation

1. Spiel schließen.
2. MelonLoader 0.7.3 mit dem offiziellen Installer in den Spielordner installieren.
3. `WetReality.XRStart.dll` in den Ordner `Mods` des Spiels legen.
4. Optional: Konfigurator (`Configurator.cmd`) öffnen und Einstellungen wählen.
5. Erst die Headset-Software, dann das Spiel über Steam starten.

Der erste Start nach der Installation von MelonLoader dauert eine Weile ohne sichtbaren
Fortschritt — er erzeugt einmalig seine Hilfsdateien.

Deinstallieren: `Mods\WetReality.XRStart.dll` löschen (und MelonLoader, wenn du ihn
nicht mehr brauchst).

## Selbst bauen

- .NET SDK (siehe `global.json`), eine Spielinstallation mit MelonLoader 0.7.3, die
  einmal gestartet wurde (für die erzeugten `Il2CppAssemblies`).
- `Directory.Build.props.example` nach `Directory.Build.props` kopieren und `GamePath`
  setzen.
- `dotnet build src/WetReality.XRStart/WetReality.XRStart.csproj -c Release`
- Der Build installiert nicht; `bin/Release/net6.0/WetReality.XRStart.dll` selbst nach
  `Mods` kopieren.

Das Startlogo (`src/WetReality.XRStart/Resources/startup-logo.png`) und die Bilder des
Konfigurators liegen nicht im Repository; der Build funktioniert auch ohne sie.

## Lizenz

Siehe [LICENSE](LICENSE).
