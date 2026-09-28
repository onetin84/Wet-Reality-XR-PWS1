# Wet Reality XR Mod — PowerWash Simulator

Room-scale VR for **PowerWash Simulator** (the first game) with a headset and motion
controllers. The washer follows your hand in six degrees of freedom: where you look and
where you spray are completely independent.

Deutsch: siehe [LIESMICH.md](LIESMICH.md).

> Unofficial fan project. Not affiliated with FuturLab or Square Enix.
> Tested on a Meta Quest 3 over Virtual Desktop. Other OpenXR headsets should work but
> have not been tried.

## Features

- **6DOF washer** in your right hand (left-handed mode available), VR hands with the
  game's own models, orange gloves, grip fine-tuning — also live in game
- **Game menus in VR** with a pointer beam — main menu, pause menu, shop, popups;
  `Y` goes back
- **Equipment wheels** (hold `R3`) operated with the beam or the left stick
- **Target teleport** on the washer stick, with the jump envelope of the game: ledges,
  walkable stairs, the top of a ladder you have put up
- **Comfort**: teleport instead of walking, snap turn, vignette, presets
- **Gestures**: behind your shoulder = next washer, at your hip = soap from the holster,
  free hand at the washer = extension
- **Haptics**: spray vibration by nozzle and contact, spraying your own free hand
- **Immersion mode** (hold the menu button): game UI off and on
- Sky with clouds, black loading screens, startup logo, desktop mirror
- **Configurator** (Windows) for all player settings

Controls in detail: [Quick Guide](tools/frontend/QuickGuide.html) ·
[Kurzanleitung](tools/frontend/QuickGuide-de.html)

## Requirements

- PowerWash Simulator on Steam (tested: version 1.11.0, build 1394)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) **0.7.3**, x64
- An OpenXR runtime for your headset (for example Virtual Desktop, Meta Quest Link or
  SteamVR). The game already ships the Unity OpenXR plugin; nothing else is needed.

## Install

1. Close the game.
2. Install MelonLoader 0.7.3 into the game folder with the official MelonLoader
   installer.
3. Put `WetReality.XRStart.dll` into the game's `Mods` folder.
4. Optional: open the configurator (`Configurator.cmd`) to choose your settings.
5. Start your headset software, then start the game through Steam.

The first start after installing MelonLoader takes a while with no sign of progress —
it generates its support files once.

**Graphics settings for VR:** in the game's options switch VSync off and set the frame
rate limit above your headset's refresh rate (or unlimited) — otherwise the picture
judders. Flat-screen effects such as motion blur, depth of field, chromatic aberration
and film grain are best off wherever the game offers them.

To uninstall, delete `Mods\WetReality.XRStart.dll` (and MelonLoader, if you no longer
need it).

## Build from source

- .NET SDK (see `global.json`), a game installation with MelonLoader 0.7.3 that has been
  started once (for the generated `Il2CppAssemblies`).
- Copy `Directory.Build.props.example` to `Directory.Build.props` and set `GamePath`.
- `dotnet build src/WetReality.XRStart/WetReality.XRStart.csproj -c Release`
- The build does not install; copy `bin/Release/net6.0/WetReality.XRStart.dll` into
  `Mods` yourself.

The startup logo (`src/WetReality.XRStart/Resources/startup-logo.png`) and the
configurator images are not part of the repository; the build works without them.

## License

See [LICENSE](LICENSE).
