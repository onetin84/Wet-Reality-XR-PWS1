================================================================================
  WET REALITY XR MOD  @@VERSION@@  -  BETA
  Room-scale VR for PowerWash Simulator (the first game)
  by Tino
================================================================================

WHAT THIS IS

  A VR mod that puts you inside PowerWash Simulator with a headset and motion
  controllers. The washer follows your hand in six degrees of freedom: where you
  look and where you spray are completely independent.

  Tested on a Meta Quest 3 over Virtual Desktop, with PowerWash Simulator 1.11.0
  (Steam build 1394). Other OpenXR headsets should work but have not been tried.

  This is a beta. It is playable, not finished. See KNOWN LIMITATIONS below.

  Deutsch: siehe LIESMICH.txt


INSTALL

  1. Make sure PowerWash Simulator is CLOSED.

  2. Double-click  Install.cmd

  3. Start your headset software, then start the game through Steam.

  That is the whole thing. Nothing to install beforehand, nothing to choose, and
  no administrator rights.

  The installer needs an internet connection: it fetches the mod loader and, if
  your system has none, the runtime that loader needs - about 55 MB at most.
  Each one is checked against a known fingerprint before it is put in place, and
  anything already correct is skipped. So if a download fails, just run
  Install.cmd again.

  The game already ships the OpenXR support VR needs; the installer only checks
  that it is there.

  Everything lands inside the game's own folder. Nothing is installed on your
  system, nothing goes into the registry, and Uninstall.cmd takes it all back
  out again.

  If Windows says the script cannot run: right-click Install.cmd, Properties,
  and tick "Unblock" at the bottom.

  If the installer cannot write to the game folder - which happens when Steam
  installed the game under Program Files - right-click Install.cmd and choose
  "Run as administrator".


PLAY

  Start the game through Steam as usual. Put the headset on: VR comes up in the
  main menu by itself, and the washer is in your hand as soon as a level has
  loaded.

  Have your headset software running BEFORE you start the game.

  THE FIRST LAUNCH AFTER INSTALLING takes a while with no sign of progress, and
  needs an internet connection. The mod loader prepares its support files once.
  It has not crashed. Let it finish.

  To quit, use the game's own menu.


GRAPHICS SETTINGS FOR VR

  Set these once in the game's own graphics options:

  - VSync OFF, and the frame rate limit ABOVE your headset's refresh rate (or
    unlimited). Otherwise the picture judders.

  - Effects made for a flat screen feel wrong in a headset. Wherever the game
    offers them, switch them off: motion blur, depth of field, chromatic
    aberration, film grain.


CONTROLS AND SETTINGS

  The left controller moves you, the right controller is the washer. Left-handed
  players choose the washer hand in the configurator; every role then swaps
  sides, only the menu button stays on the left.

  WASHER HAND (right)
    Trigger             spray while held; clicks in menus
    Grip                continuous spray on or off
    A                   jump (off with the comfort teleport)
    B tap / hold        crouch / prone
    Stick left / right  turn (smooth, or snap turn)
    Stick up            target teleport, aimed with this hand
    Stick down          next nozzle
    R3 hold             equipment wheels

  FREE HAND (left)
    Stick               walk; pushed fully: sprint
    Trigger             rotate the nozzle
    Grip                highlight dirt; while carrying: rotate the item
    X                   pick up / put down
    Y tap               inventory; in menus: back
    Y hold              next washer
    L3                  next extension
    Menu tap / hold     pause menu / immersion mode (game UI off and on)

  GESTURES
    Washer hand behind your shoulder + grip   next washer
    Washer hand at your hip + grip            soap from the holster
    Free hand at the washer + grip            next extension
    Free hand: grip + X + Y, hold             grip calibration - move your
                                              washer hand to where the washer
                                              should sit and let go

  The target teleport never reaches higher than a jump, and gets you onto
  ledges, walkable stairs and the top of a ladder you have put up.

  COMFORT OPTIONS, all OFF by default: teleport instead of walking, snap turn
  (15, 30, 45 or 60 degrees), vignette while moving. A preset sets them at
  once: All off, Gentle or Maximum.

  Double-click  Configurator.cmd

  It has the full control layout under "Open quick guide", and settings for the
  washer hand, turn speed, pickup reach, vibration, orange gloves, gestures,
  the aiming laser, cartoon outlines (SpongeBob), menu size and distance,
  pointer colour, the headset view on the monitor, the teleport target, the
  comfort options and the grip fine-tuning. The button at the bottom starts
  the game through Steam.

  Everything finer than that sits in <game>\UserData\MelonPreferences.cfg as
  plain text. Close the game before editing it: the loader rewrites that file
  on exit and would overwrite your change.

  The guide is also just a file you can open directly:
  configurator\QuickGuide.html


REMOVE

  Double-click  Uninstall.cmd

  It removes exactly what it installed, and only while those files are still the
  ones it wrote - anything you replaced yourself is left alone and reported.
  Your settings are kept unless you ask for them to go as well.

  If you already had the mod loader installed before this package, it is left
  where it is: it may be carrying other mods.


WHAT GETS INSTALLED, AND FROM WHERE

  All of it inside the game folder, nowhere else.

  MelonLoader v0.7.3      the mod loader that lets any of this run. Open
                          source, Apache-2.0, from its own GitHub releases.
                          Left alone if it is already installed.

  .NET 6.0.36 runtime     what MelonLoader runs on, unpacked into
                          <game>\dotnet - only if your system has no .NET 6.
                          From Microsoft.

  WetReality.XRStart.dll  the mod itself, into <game>\Mods.

  One setting in <game>\UserData\Loader.cfg: the MelonLoader console window is
  hidden (see FOR DEVELOPERS).

  On the game's first launch afterwards MelonLoader fetches its own support
  files too. That is the wait mentioned above.


KNOWN LIMITATIONS

  - No physical VR interactions: no belt, no picking bottles up by hand. The
    game's existing menus are used as they are, shown in the headset.

  - Left-handed play works but is less tested than right-handed. The hand
    positions may need the grip calibration or the fine-tuning. Anything that
    feels mirrored the wrong way round is a defect - please report it.

  - The equipment wheel looks distorted when you look at it from the side.

  - The inventory can flash up briefly.

  - The game menus float in front of you; they are not anchored in the room.

  - If VR does not come up, restart the game - there is no key to start it by
    hand in this build.


IF SOMETHING GOES WRONG

  The log is the first place to look:
    <game folder>\MelonLoader\Latest.log

  VR did not start:             start the headset software first, then the game.
  The washer sits wrong:        calibrate in game (free hand: grip + X + Y) or
                                use the grip fine-tuning in the configurator.
  Menus too big or too close:   menu size and distance in the configurator.
  A red line from the nozzle:   the aiming laser - off in the configurator.

  The game will not start at all - no window, nothing in the log:
    run Install.cmd again. It reports whether it can find the runtime, and
    puts it back if it cannot.

  When reporting a problem, Latest.log plus what you were doing is usually
  enough to find it.


FOR DEVELOPERS

  This build has the development machinery switched off: the diagnostic log
  lines, the measuring paths, the F-key hotkeys and the cheats. It is one
  preference, in <game folder>\UserData\MelonPreferences.cfg, category
  [WetReality_XRStart]:

    DevMode = false

  Set it to true and the individual switches apply again - DevHotkeys (F1-F11)
  and DevCheats. DevMode only caps them; their own stored values are left
  untouched.

  The MelonLoader console window is separate. The installer hides it by setting

    [console]
    hide_console = true

  in UserData\Loader.cfg. Set it back to false to get the console while
  developing; the uninstaller reverts it either way.

  Source code: https://github.com/onetin84/Wet-Reality-XR-PWS1


================================================================================
  Unofficial. Not affiliated with FuturLab or Square Enix.

  MelonLoader is a separate open-source project (Apache-2.0). The .NET runtime
  is Microsoft's (MIT). Neither is affiliated with this mod.
================================================================================
