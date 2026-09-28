# Wet Reality XR Mod (PowerWash Simulator 1) - installer
#
# Ported from the PWS2 package. ONE STEP FOR THE USER: run this, then start the
# game. Nothing to install beforehand, no version to choose, no administrator
# rights.
#
# To get there it puts three things in place, each from its own source and each
# verified against a known hash before it is allowed near the game folder:
#
#   MelonLoader v0.7.3     the mod loader           Apache-2.0, from GitHub
#   .NET 6.0.36            what MelonLoader runs on, PORTABLE, into <GAME>\dotnet
#   the mod assembly       from this package
#
# OpenXR is NOT fetched: PWS1 ships the Unity OpenXR plugin itself. It is only
# checked for (Common.ps1).
#
# The runtime is what makes one step possible at all. MelonLoader needs .NET 6
# for Il2Cpp games, and its own GUI installer puts that on the machine - which
# costs the user a download, an unsigned-publisher warning, a version to pick and
# an installer to sit through. Unpacked into the game folder instead it is
# invisible: nothing system-wide, no admin rights, and it leaves with the mod.
#
# Whatever is already present and correct is left alone, so re-running this is
# cheap, and it is also how a failed download gets retried.
#
# It does NOT write MelonPreferences.cfg. The mod creates that on its first run
# with its own defaults, which are the values that were play-tested; a copy of
# them here would only be a second place to go stale.

[CmdletBinding()]
param(
    [string] $GamePath,
    # Re-fetch and re-verify even what is already in place.
    [switch] $Force,
    # Use a .NET 6 already on the machine rather than unpacking the portable one.
    [switch] $SystemDotnet
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$ModSource = Join-Path (Split-Path -Parent $PSScriptRoot) 'mod'

Write-Host ''
Say '  Wet Reality XR Mod - Installer' 'Cyan'
Say '  PowerWash Simulator in room-scale VR'
Write-Host ''

# ------------------------------------------------------------------- 1. checks
#
# Every one of them before anything at all is written.

if (-not (Test-Path -LiteralPath $ModSource)) {
    Fail "The 'mod' folder is missing next to the scripts. Please unpack the whole archive, not just the scripts."
}

foreach ($name in $script:ModFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $ModSource $name))) {
        Fail "$name is missing from the 'mod' folder. Please unpack the archive again."
    }
}

$game = Resolve-GameFolder -Given $GamePath
Say "Game folder:  $game"

# A running game holds its DLLs open, so a copy would fail halfway and leave a
# half-installed state behind.
$running = @(Get-Process -Name $script:GameProcess -ErrorAction SilentlyContinue)

if ($running.Count -gt 0) {
    Fail 'PowerWash Simulator is running. Please close the game and start the installer again.'
}

# The game's own OpenXR plugin (Common.ps1). Without it VR cannot start, and
# no file this package carries could replace it.
$missingXr = @($script:GameOpenXrFiles | Where-Object { -not (Test-Path -LiteralPath (Combine @($game, $_))) })

if ($missingXr.Count -gt 0) {
    Fail ("The game's OpenXR support is missing:" + [Environment]::NewLine +
          (($missingXr | ForEach-Object { "  $_" }) -join [Environment]::NewLine) +
          [Environment]::NewLine + [Environment]::NewLine +
          "Let Steam repair the game (Properties, Installed Files, Verify integrity)" +
          " and run the installer again. Nothing was changed.")
}

# Writable? A library under Program Files needs elevation, and learning that from
# a copy that already failed halfway is far worse than learning it now. Tested by
# actually writing, because permissions on Windows cannot be predicted from a
# path.
try {
    $modsFolder = Combine @($game, 'Mods')

    if (-not (Test-Path -LiteralPath $modsFolder)) {
        New-Item -ItemType Directory -Path $modsFolder -Force | Out-Null
    }

    $probe = Combine @($modsFolder, '.wetreality-write-test')
    Set-Content -LiteralPath $probe -Value 'x' -Encoding ASCII
    Remove-Item -LiteralPath $probe -Force
}
catch {
    Fail ("Cannot write into the game folder:" + [Environment]::NewLine + "  $game" +
          [Environment]::NewLine + [Environment]::NewLine +
          "Close anything that may be using it, or right-click Install.cmd and choose " +
          "'Run as administrator'.")
}

$entries = New-Object System.Collections.Generic.List[object]
$temp = Join-Path $env:TEMP ('wetreality-' + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null

try {

    # ---------------------------------------------------------- 2. MelonLoader

    Write-Host ''
    Say 'Mod loader' 'Cyan'

    if (Test-Path -LiteralPath (Combine @($game, 'MelonLoader'))) {
        # Left completely alone: it may be carrying other mods, and it may be a
        # version the user picked deliberately.
        $installed = '(unknown version)'
        $dll = Combine @($game, 'MelonLoader', 'net6', 'MelonLoader.dll')

        if (Test-Path -LiteralPath $dll) {
            $installed = 'v' + [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
        }

        Say "  MelonLoader already installed, $installed - left untouched"

        if ($installed -notlike 'v0.7.*') {
            Say "  Tested against $($script:MelonLoader.Version). Yours differs; if the game" 'Yellow'
            Say '  misbehaves, suspect that first.' 'Yellow'
        }
    }
    else {
        Say "  installing MelonLoader $($script:MelonLoader.Version)  (about 20 MB)"

        $archive = Get-Verified -Url $script:MelonLoader.Url `
                                -Expected $script:MelonLoader.Sha256 `
                                -Algorithm SHA256 -Into $temp -What 'MelonLoader'

        # The archive's root is MelonLoader\ plus version.dll, which is exactly
        # the layout the game folder wants.
        Expand-Zip -Archive $archive -Into $game

        foreach ($proof in $script:MelonLoader.Proof) {
            if (-not (Test-Path -LiteralPath (Combine @($game, $proof)))) {
                Fail "MelonLoader did not unpack correctly - $proof is missing."
            }
        }

        # Recorded as a TREE, not as hundreds of separate files. The uninstaller
        # removes a tree only when this installer is what created it.
        $entries.Add([ordered] @{ path = 'MelonLoader'; kind = 'tree'; source = 'melonloader' })
        $entries.Add([ordered] @{ path = 'version.dll'; kind = 'file'
                                  sha256 = (Get-Sha256 -Path (Combine @($game, 'version.dll')))
                                  source = 'melonloader' })

        Say '  installed  version.dll + MelonLoader\'
    }

    # -------------------------------------------------------------- 3. runtime

    Write-Host ''
    Say '.NET runtime' 'Cyan'

    $dotnet = Test-DotnetSix -GamePath $game

    if ($dotnet.Ok -and -not $Force) {
        Say "  already satisfied  ($($dotnet.Detail))"
    }
    elseif ($SystemDotnet) {
        Say '  -SystemDotnet given, so nothing is unpacked.' 'Yellow'
        Say "  $($dotnet.Detail)" 'DarkGray'
        Say '  Install the .NET 6 Desktop Runtime (x64) from' 'Yellow'
        Say '      https://dotnet.microsoft.com/download/dotnet/6.0' 'Cyan'
    }
    else {
        Say "  unpacking .NET $($script:DotnetRuntime.Version) into the game folder  (about 32 MB)"
        Say '  Nothing is installed on your system and no admin rights are needed.' 'DarkGray'

        $archive = Get-Verified -Url $script:DotnetRuntime.Url `
                                -Expected $script:DotnetRuntime.Sha512 `
                                -Algorithm SHA512 -Into $temp -What 'the .NET 6 runtime'

        $into = Combine @($game, $script:DotnetRuntime.Folder)

        if (Test-Path -LiteralPath $into) { Remove-Item -LiteralPath $into -Recurse -Force }

        Expand-Zip -Archive $archive -Into $into

        foreach ($proof in $script:DotnetRuntime.Proof) {
            if (-not (Test-Path -LiteralPath (Combine @($into, $proof)))) {
                Fail "The .NET runtime did not unpack correctly - $proof is missing."
            }
        }

        $entries.Add([ordered] @{ path = $script:DotnetRuntime.Folder
                                  kind = 'tree'; source = 'dotnet' })

        # Point MelonLoader at it explicitly. Its own discovery of <GAME>\dotnet
        # ought to find this too, but that could not be verified here, and a beta
        # should not rest on "ought to".
        $result = Set-HostFxrOverride -GamePath $game `
                                      -Value (Combine @($game, $script:DotnetRuntime.HostFxr))

        if ($result -eq 'no-loader-section') {
            Say '  No [loader] section in UserData\Loader.cfg, so no override was set.' 'Yellow'
            Say '  MelonLoader should still find the runtime by itself; if the game does' 'Yellow'
            Say '  not start, delete Loader.cfg and run this installer again.' 'Yellow'
        }
        else {
            $entries.Add([ordered] @{ path = 'UserData\Loader.cfg'; kind = 'cfg-key'
                                      key = 'hostfxr_path_override'; source = 'dotnet' })
            Say "  unpacked  dotnet\  and pointed MelonLoader at it  ($result)"
        }

        if (-not (Test-DotnetSix -GamePath $game).Ok) {
            Fail 'The runtime unpacked but cannot be found afterwards. Please report this.'
        }
    }

    # -------------------------------------------------- 3b. the console window

    # OUT OF THE WAY FOR A BETA. MelonLoader opens a console window at the top
    # left of the screen, which is the first thing a tester sees and has nothing
    # in it they can act on. The mod's own logs still go to Latest.log.
    #
    # Written into the user's Loader.cfg rather than shipped as a file, because
    # shipping one would overwrite every other setting in it. Recorded as a
    # cfg-key entry so the uninstaller puts it back - with 'false', not the empty
    # string the hostfxr path is reset to; this key is a boolean and MelonLoader
    # would not parse hide_console = "".
    $consoleResult = Set-LoaderCfgKey -GamePath $game -Section 'console' `
                                      -Key 'hide_console' -Value 'true'

    if ($consoleResult -eq 'no-section') {
        Say '  No [console] section in UserData\Loader.cfg, so the console stays visible.' 'Yellow'
    }
    else {
        $entries.Add([ordered] @{ path = 'UserData\Loader.cfg'; kind = 'cfg-key'
                                 key = 'hide_console'; restore = 'false'; source = 'console' })
        Say "  hid the MelonLoader console window  ($consoleResult)"
    }

    # ------------------------------------------------------------ 4. mod files

    Write-Host ''
    Say 'The mod' 'Cyan'

    foreach ($name in $script:ModFiles) {
        $from = Join-Path $ModSource $name
        $to = Combine @($game, 'Mods', $name)
        $existed = Test-Path -LiteralPath $to

        Copy-Item -LiteralPath $from -Destination $to -Force

        $hash = Get-Sha256 -Path $to

        if ($hash -ne (Get-Sha256 -Path $from)) {
            Fail "$name did not copy correctly - the file in Mods does not match the package."
        }

        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($to).FileVersion

        $entries.Add([ordered] @{ path = "Mods\$name"; kind = 'file'
                                  sha256 = $hash; replaced = $existed })

        $verb = 'installed'
        if ($existed) { $verb = 'updated  ' }
        Say ("  $verb  $name  v$version")
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 5. manifest

$modVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Combine @($game, 'Mods', 'WetReality.XRStart.dll'))).FileVersion

$manifest = Write-Manifest -GamePath $game -Entries $entries.ToArray() -Version $modVersion

# ------------------------------------------------------------------ 6. report

$final = Test-DotnetSix -GamePath $game

Write-Host ''

if ($final.Ok) {
    Say '  Done. Nothing else to install.' 'Green'
}
else {
    Say '  Done, but the .NET 6 runtime is still missing - the game will not start.' 'Yellow'
    Say "  $($final.Detail)" 'DarkGray'
}

Write-Host ''
Say '  Start the game through Steam. Put the headset on: VR comes up on its own a'
Say '  few seconds later, with the washer already in your hand.'
Write-Host ''
Say '  Have your headset software running BEFORE you start the game.' 'DarkGray'
Write-Host ''
Say '  The FIRST launch takes about half a minute with no sign of progress and' 'Yellow'
Say '  needs an internet connection: MelonLoader prepares its support files once.' 'Yellow'
Say '  It has not crashed.' 'Yellow'
Write-Host ''
Say '  Controls and settings:  Configurator.cmd  in this folder'
Say '  To remove everything:   Uninstall.cmd'
Write-Host ''
Say "  Record of what was installed: $manifest" 'DarkGray'
Write-Host ''
