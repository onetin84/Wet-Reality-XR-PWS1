# Wet Reality XR Mod - configurator (PWS1 port, XRStart 1.18.0)
#
# Taken from the PWS2 configurator. Changed for PowerWash Simulator 1: AppID
# 1290000, folder 'PowerWash Simulator', cfg section [WetReality_XRStart], mod
# DLL WetReality.XRStart.dll, and only the settings the PWS1 mod really has
# (see MainWindow.xaml head). Everything else - file IO, Steam discovery,
# section-true writing with .bak and appending of missing keys - is PWS2 as is.
# ASCII only in what was added here: PowerShell 5.1 reads this BOM-less file
# as ANSI.
#
# Writes MelonPreferences.cfg, opens the quick guide, and starts the game
# THROUGH STEAM - steam://rungameid/<appid>, never the executable.
#
# The note here used to say a launch was left out deliberately, because "a launch
# path of its own could collide with Steam and its overlay". That objection was
# about starting the .exe directly and it still stands. The URL protocol is the
# opposite: it hands the request to Steam, which starts the game exactly as a
# click in the library does, overlay and all. Nothing is compiled either way, so
# there is still neither a signing problem nor a SmartScreen warning.
#
# Nothing here is compiled. PowerShell 5.1 ships WPF through
# PresentationFramework, the window comes from MainWindow.xaml, and every graphic
# is a file under assets/. A missing file leaves a placeholder in its place and
# changes nothing about the layout.
#
# NOTHING IS WRITTEN without a click on Save, and a backup of the cfg is made
# every time.

[CmdletBinding()]
param(
    [string] $GamePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Localisation. Loaded before the window so the first paint is already in the
# right language - switching after showing would flash English.
$script:Language = 'en'
$script:Unmapped = @{}

. (Join-Path $script:Root 'Strings.ps1')
# PWS1: AppID 1290000 (CLAUDE.md; appmanifest_1290000.acf). A constant, the
# launch button still depends on Find-GameFolder proving the installation.
$script:AppId = '1290000'

# Ungespeichertes, als echter Merker und nicht als Hinweistext.
#
# Mark-Dirty schrieb bisher nur in SaveHint. Ein Start mit ungespeicherten
# Aenderungen wuerde die alte cfg laden und still das Gegenteil dessen tun, was
# der Spieler gerade eingestellt hat - deshalb ein Flag, das der Startknopf
# lesen kann. Deklariert statt bei Bedarf gesetzt: unter
# Set-StrictMode -Version Latest ist das LESEN einer nicht gesetzten Variablen
# ein Fehler.
$script:Dirty = $false
$script:CfgPath = $null
$script:GameFolder = $null
$script:Loading = $true

# Abschnitt 147. Beide Merker verhindern, dass sich das Preset selbst
# ueberschreibt:
#
#   ApplyingPreset   laeuft, waehrend das Preset Haekchen und Regler setzt.
#                    Slider.ValueChanged unterscheidet Hand und Skript nicht.
#   SuppressPreset   laeuft, waehrend das Skript die Auswahlbox selbst setzt -
#                    sonst wendet deren SelectionChanged das Preset gleich
#                    wieder an.
#
# Deklariert statt bei Bedarf gesetzt: unter Set-StrictMode -Version Latest ist
# das LESEN einer nicht gesetzten Variablen ein Fehler.
$script:ApplyingPreset = $false
$script:SuppressPreset = $false

# ------------------------------------------------------------- text file IO
#
# Explicit UTF-8, because Get-Content without -Encoding reads a file that has no
# BOM in the system ANSI codepage. Writing that back as UTF-8 double-encodes
# every non-ASCII character. Encoding.UTF8 on the way in copes with a BOM or
# none; on the way out the BOM is written, because PowerShell 5.1 and Notepad
# both read a BOM-less file as ANSI.

function Read-Utf8 {
    param([Parameter(Mandatory)] [string] $Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8 {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyString()] $Content
    )

    # DIE FORM DER VORHANDENEN DATEI BLEIBT - Zeilenenden und BOM.
    #
    # GEMESSEN, nicht vermutet: MelonPreferences.cfg liegt als LF OHNE BOM, und
    # diese Funktion schrieb [Environment]::NewLine (auf Windows CRLF) samt
    # BOM. Damit wurde bei jedem Speichern die GANZE Datei umgeschrieben - 478
    # Zeilen statt der drei, die sich wirklich aendern.
    #
    # Der schwerere Grund ist der BOM: ein UTF-8-BOM ueber dem ersten
    # Abschnittskopf steht in den Projektnotizen als schon einmal zerschossene
    # cfg. MelonLoader vertraegt ihn heute und normalisiert beim Beenden
    # zurueck - deshalb war es nie aufgefallen. Auf eine Toleranz, die einmal
    # gefehlt hat, sollte sich nichts verlassen.
    #
    # Die Vorgabe CRLF MIT BOM gilt weiter fuer eine Datei, die es noch nicht
    # gibt: dort stimmt die alte Begruendung, dass PowerShell 5.1 und Notepad
    # eine BOM-lose Datei als ANSI lesen. Sie stimmt nur nicht gegen eine
    # vorhandene Datei, die ihre eigene Form schon mitbringt.
    $newline = [Environment]::NewLine
    $withBom = $true

    if (Test-Path -LiteralPath $Path) {
        $raw = [System.IO.File]::ReadAllBytes($Path)

        $withBom = ($raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB `
            -and $raw[2] -eq 0xBF)

        $existing = [System.Text.Encoding]::UTF8.GetString($raw)

        # Nur wenn die Datei EINDEUTIG LF benutzt. Eine gemischte Datei
        # bekommt CRLF, also die Windows-Vorgabe - raten wird hier nichts.
        if ($existing -notmatch "`r`n" -and $existing -match "`n") {
            $newline = "`n"
        }
    }

    if ($Content -is [array]) { $Content = ($Content -join $newline) }

    [System.IO.File]::WriteAllText($Path, [string] $Content,
        (New-Object System.Text.UTF8Encoding($withBom)))
}

# ---------------------------------------------------------------- Installation

function Get-SteamLibraries {
    # Registry first, then the library folders file. Both are read-only lookups.
    $libraries = New-Object System.Collections.Generic.List[string]

    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        try {
            $entry = Get-ItemProperty -Path $key -ErrorAction Stop
            $install = $null

            if ($entry.PSObject.Properties.Name -contains 'SteamPath') { $install = $entry.SteamPath }
            elseif ($entry.PSObject.Properties.Name -contains 'InstallPath') { $install = $entry.InstallPath }

            if ($install) { $libraries.Add(($install -replace '/', '\')) }
        }
        catch {
            # Absent key is the normal case on one of the two hives.
        }
    }

    $extra = New-Object System.Collections.Generic.List[string]

    foreach ($base in $libraries) {
        # [System.IO.Path]::Combine, NOT Join-Path - and this is not a style
        # preference.
        #
        # Join-Path resolves against the PowerShell provider and THROWS on a
        # drive that does not exist. libraryfolders.vdf keeps stale entries for
        # libraries that have been removed: this machine still lists drives S:
        # and T:. With $ErrorActionPreference = 'Stop' at the top of the script,
        # the first stale entry killed the whole configurator before the window
        # opened - found by running the discovery on its own rather than by
        # opening the window and seeing nothing.
        #
        # Combine is pure string work. Test-Path on a missing drive then simply
        # answers false, which is the behaviour wanted here.
        $vdf = [System.IO.Path]::Combine($base, 'steamapps', 'libraryfolders.vdf')

        if (-not (Test-Path -LiteralPath $vdf)) { continue }

        # Only the "path" entries are of interest; a full VDF parser would be
        # out of proportion for four lines of regex.
        foreach ($line in (Get-Content -LiteralPath $vdf)) {
            if ($line -match '"path"\s+"(.+?)"') {
                $extra.Add(($matches[1] -replace '\\\\', '\'))
            }
        }
    }

    foreach ($path in $extra) { $libraries.Add($path) }

    return ($libraries | Select-Object -Unique)
}

function Find-GameFolder {
    foreach ($library in Get-SteamLibraries) {
        if (-not $library) { continue }

        $candidate = [System.IO.Path]::Combine(
            $library, 'steamapps', 'common', 'PowerWash Simulator')

        if (Test-Path -LiteralPath ([System.IO.Path]::Combine($candidate, 'MelonLoader'))) {
            return $candidate
        }
    }

    return $null
}

function Test-GameFolder {
    param([string] $Path)

    if (-not $Path) { return $false }
    return (Test-Path -LiteralPath ([System.IO.Path]::Combine($Path, 'MelonLoader')))
}

# ------------------------------------------------------------------------- cfg
#
# Targeted per-key replacement inside the file, NOT a parse-and-rewrite.
#
# MelonPreferences.cfg carries a description comment above every entry and is
# owned by MelonLoader, which rewrites it on its own terms. Reformatting it would
# throw those comments away and risk the mod's own defaults on the next launch.
# So each key is replaced in place and everything else is left byte for byte.

function Read-CfgValue {
    param([string] $Key, [string] $Fallback)

    if (-not $script:CfgPath -or -not (Test-Path -LiteralPath $script:CfgPath)) { return $Fallback }

    foreach ($line in ((Read-Utf8 -Path $script:CfgPath) -split "`r?`n")) {
        if ($line -match "^\s*$([regex]::Escape($Key))\s*=\s*(.+?)\s*$") {
            return $matches[1].Trim().Trim('"')
        }
    }

    return $Fallback
}

# Die Reihenfolge der Auswahlliste, EINMAL. Index null ist die Vorgabe der Mod,
# und ein unbekannter Wert in der cfg landet dort - dieselbe Regel, die die Mod
# selbst anwendet, statt eine zweite Wahrheit aufzustellen.
$script:PointerColors = @('pink', 'green', 'blue', 'yellow')
# PWS1 default is blue (index 2), like the mod.
$script:MirrorModes = @('left', 'right', 'both', 'off')

# Jeder Schluessel, den dieses Werkzeug schreibt, gehoert der Mod-Kategorie
# WetRealityPose. Das ist eine Zusicherung und keine Beobachtung: wer hier einen
# XRBoot- oder Discovery-Schluessel eintraegt, muss diese Konstante mitziehen.
$script:PoseSection = 'WetReality_XRStart'

function Write-CfgValues {
    param([hashtable] $Values)

    if (-not (Test-Path -LiteralPath $script:CfgPath)) {
        throw (T 'MelonPreferences.cfg not found. Start the game once with the mod installed so that it gets created.')
    }

    # One backup per save, timestamped. Cheap, and it is the only way back if a
    # replacement ever goes wrong.
    $backup = "$($script:CfgPath).bak"
    Copy-Item -LiteralPath $script:CfgPath -Destination $backup -Force

    $lines = (Read-Utf8 -Path $script:CfgPath) -split "`r?`n"
    $written = @{}

    # ABSCHNITTSTREU, und das ist der zweite behobene Mangel.
    #
    # Die frühere Fassung ersetzte jede passende Zeile in der GANZEN Datei.
    # DevHotkeys und DevMode stehen in [WetRealityXRBoot] UND in
    # [WetRealityPose] - ein solcher Schlüssel wäre doppelt überschrieben
    # worden. Heute schreibt dieses Werkzeug keinen davon, also war es noch
    # kein Defekt; eine Falle war es trotzdem.
    $section = ''
    $sectionStart = -1
    $sectionEnd = -1

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]

        if ($line -match '^\s*\[(.+)\]\s*$') {
            if ($section -eq $script:PoseSection) { $sectionEnd = $index }
            $section = $matches[1].Trim()
            if ($section -eq $script:PoseSection) { $sectionStart = $index }
            continue
        }

        if ($section -ne $script:PoseSection) { continue }

        foreach ($key in $Values.Keys) {
            if ($line -match "^\s*$([regex]::Escape($key))\s*=") {
                $lines[$index] = "$key = $($Values[$key])"
                $written[$key] = $true
            }
        }
    }

    # Der Abschnitt ist der letzte in der Datei, wenn kein weiterer Kopf folgt.
    if ($sectionStart -ge 0 -and $sectionEnd -lt 0) { $sectionEnd = $lines.Count }

    $missing = @($Values.Keys | Where-Object { -not $written.ContainsKey($_) })
    $appended = @()

    # ANGEHAENGT STATT BLOSS GEMELDET.
    #
    # Ein Schlüssel, den die Mod noch nie angelegt hat, ist der Normalfall nach
    # jedem Update - und ein "Speichern", das für ihn stillschweigend nichts tut,
    # kostet einen Testlauf. Genau das ist passiert: drei Komfortschlüssel
    # fehlten, das Häkchen stand, geschrieben wurde nichts.
    #
    # MelonPreferences liest die Datei beim Start und übernimmt einen
    # vorhandenen Wert, statt den Quell-Default zu nehmen - ein vorgeschriebener
    # Schlüssel wirkt also sofort, ohne dass die Mod ihn erst anlegen muss.
    if ($sectionStart -ge 0 -and $missing.Count -gt 0) {
        # Hinter die letzte nicht-leere Zeile des Abschnitts, damit die
        # Leerzeile zum nächsten Kopf erhalten bleibt.
        $at = $sectionEnd

        while ($at -gt ($sectionStart + 1) -and -not $lines[$at - 1].Trim()) {
            $at--
        }

        foreach ($key in ($missing | Sort-Object)) {
            $appended += "$key = $($Values[$key])"
        }

        # Über Array-Teile statt über eine List[string]: Write-Utf8 prüft
        # `-is [array]`, und eine List ist keines - sie wäre als Typname in die
        # Datei geschrieben worden.
        $head = @()
        if ($at -gt 0) { $head = $lines[0..($at - 1)] }

        $tail = @()
        if ($at -lt $lines.Count) { $tail = $lines[$at..($lines.Count - 1)] }

        $lines = @($head) + @($appended) + @($tail)
    }

    # Explicit UTF-8 in and out. A user whose Windows account carries an umlaut
    # has it in the UnityExplorer paths inside this file, and an ANSI round trip
    # would corrupt their config on every save.
    Write-Utf8 -Path $script:CfgPath -Content $lines

    # Zwei Listen, weil sie zwei verschiedene Meldungen verdienen: angelegt ist
    # ein Erfolg, nicht gefunden ein Problem. Beide in @() gewickelt - eine leere
    # PowerShell-Rueckgabe kollabiert sonst zu $null und .Count wirft unter
    # StrictMode.
    return [pscustomobject]@{
        Appended = @($appended)
        Missing  = @(if ($sectionStart -lt 0) { $missing } else { @() })
    }
}

function Format-Bool {
    param([bool] $Value)
    if ($Value) { return 'true' } else { return 'false' }
}

function Format-Float {
    param([double] $Value)
    # Invariant decimal point: the cfg is read by a .NET mod, and a German comma
    # would parse as something else entirely.
    return $Value.ToString('0.####', [System.Globalization.CultureInfo]::InvariantCulture)
}

# ---------------------------------------------------------------------- window

$xamlPath = Join-Path $script:Root 'MainWindow.xaml'

if (-not (Test-Path -LiteralPath $xamlPath)) {
    [System.Windows.MessageBox]::Show((T 'MainWindow.xaml is missing next to this script.'), 'Wet Reality') | Out-Null
    return
}

$reader = New-Object System.Xml.XmlNodeReader ([xml](Get-Content -LiteralPath $xamlPath -Raw))
$window = [Windows.Markup.XamlReader]::Load($reader)

function Ctl { param([string] $Name) return $window.FindName($Name) }

# Applied to the tree BEFORE anything else reads a label, and before the window
# is shown.
Set-Language -Window $window -Value (Resolve-StartLanguage)

function Update-LanguageButton {
    # The button offers the OTHER language, so its caption is the destination.
    $button = Ctl 'LangButton'
    if (-not $button) { return }

    if ($script:Language -eq 'de') { $button.Content = 'English' }
    else { $button.Content = 'Deutsch' }
}

Update-LanguageButton

# --------------------------------------------------------------------- images
#
# Every graphic is optional. Present, it is used and the placeholder goes away;
# absent, the placeholder stays. That lets the layout be finished before the
# assets are, and each image be dropped in on its own.

# MUSS VORHANDEN SEIN, BEVOR ES GELESEN WIRD. Set-StrictMode -Version Latest
# steht oben in dieser Datei, und damit wirft der Zugriff auf eine nie
# zugewiesene Variable - im Dev-Baum gibt es keine Assets.ps1, die sie setzt,
# also waere genau dort jeder Bildaufruf gescheitert.
#
# Tried getrennt von der Hashtable, weil "nicht vorhanden" und "vorhanden und
# leer" zwei verschiedene Zustaende sind und nur der erste einen zweiten
# Ladeversuch verdient.
$script:EmbeddedAssets = $null
$script:EmbeddedTried = $false

function Set-OptionalImage {
    param(
        [string[]] $FileNames,
        [Parameter(Mandatory)] $Image,
        $Placeholder,
        # The name this image has inside Assets.ps1. Defaults to nothing, so a
        # caller that does not pass one simply uses the folder.
        [string] $Key = ''
    )

    # SEVERAL candidate names, first match wins - because the right format
    # differs per asset and the script should not dictate it.
    #
    # The logo and the avatar need a real alpha channel and must be PNG. The
    # background plate has no transparency and is photographic, so PNG was the
    # wrong choice: the finished plate came to 3.7 MB as PNG and 204 KB as JPEG
    # at quality 92, with no visible difference on a blurred gradient. Quality 85
    # would have been 130 KB but soft gradients are where JPEG starts to band.
    # EMBEDDED FIRST, FOLDER SECOND.
    #
    # The packaged build has no assets folder: Make-Package.ps1 writes the images
    # into Assets.ps1 as base64 so they are not sitting in the download as files
    # to be swapped. Obfuscation, not protection - base64 is recognisable, and
    # that is understood.
    #
    # The dev tree has no Assets.ps1, so it drops straight through to the folder
    # below and nothing about working on the images changes.
    if (-not $script:EmbeddedTried) {
        $script:EmbeddedTried = $true
        $embedded = Join-Path $script:Root 'Assets.ps1'
        if (Test-Path -LiteralPath $embedded) { . $embedded }
    }

    if ($script:EmbeddedAssets -and $script:EmbeddedAssets.ContainsKey($Key)) {
        try {
            $bytes = [System.Convert]::FromBase64String($script:EmbeddedAssets[$Key])
            $stream = New-Object System.IO.MemoryStream(, $bytes)

            $bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
            $bitmap.BeginInit()
            $bitmap.StreamSource = $stream
            # OnLoad so the decode happens here and the stream can go.
            $bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
            $bitmap.EndInit()

            $Image.Source = $bitmap
            $Image.Visibility = 'Visible'

            if ($Placeholder) { $Placeholder.Visibility = 'Collapsed' }

            return $true
        }
        catch {
            # Falls through to the folder rather than failing: a window with a
            # gradient instead of a plate is usable, a crashed configurator is not.
        }
    }

    $folder = Join-Path $script:Root 'assets'
    $path = $null

    foreach ($name in $FileNames) {
        $candidate = Join-Path $folder $name

        if (Test-Path -LiteralPath $candidate) {
            $path = $candidate
            break
        }
    }

    if (-not $path) { return $false }

    try {
        $bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
        $bitmap.BeginInit()
        $bitmap.UriSource = New-Object System.Uri((Resolve-Path -LiteralPath $path).Path)
        # Loaded into memory so the file is not locked while the window is open -
        # otherwise a newly exported asset could not overwrite the old one.
        $bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
        $bitmap.EndInit()

        $Image.Source = $bitmap
        $Image.Visibility = 'Visible'

        if ($Placeholder) { $Placeholder.Visibility = 'Collapsed' }

        return $true
    }
    catch {
        return $false
    }
}

Set-OptionalImage -FileNames @('background.jpg', 'background.png') -Key 'background' `
    -Image (Ctl 'BackgroundImage') | Out-Null

Set-OptionalImage -FileNames @('logo.png') -Key 'logo' `
    -Image (Ctl 'LogoImage') -Placeholder (Ctl 'LogoPlaceholder') | Out-Null

Set-OptionalImage -FileNames @('avatar.png') -Key 'avatar' `
    -Image (Ctl 'AvatarImage') -Placeholder (Ctl 'AvatarPlaceholder') | Out-Null

# ---------------------------------------------------------------------- state

function Set-Status {
    param([string] $Path)

    $ok = Test-GameFolder -Path $Path

    # Kept, because the language toggle needs to re-issue the status line and the
    # label it would otherwise read back holds an INSTRUCTION when no
    # installation was found, not a path.
    $script:GameFolder = $Path

    if ($ok) {
        $script:CfgPath = [System.IO.Path]::Combine($Path, 'UserData', 'MelonPreferences.cfg')
        (Ctl 'StatusDot').Fill = (Ctl 'StatusDot').FindResource('Cyan')
        (Ctl 'StatusText').Text = T 'Installation found'
        (Ctl 'StatusPath').Text = $Path
    }
    else {
        $script:CfgPath = $null
        (Ctl 'StatusDot').Fill = (Ctl 'StatusDot').FindResource('Coral')
        (Ctl 'StatusText').Text = T 'No installation found'
        (Ctl 'StatusPath').Text = T 'Choose the "PowerWash Simulator" folder manually.'
    }

    (Ctl 'SaveButton').IsEnabled = $ok

    # Derselbe Nachweis fuer den Start: $ok bedeutet, dass unter dem Pfad ein
    # MelonLoader-Ordner liegt. Ohne Installation bleibt der Knopf grau statt
    # eine Fehlermeldung zu produzieren.
    (Ctl 'LaunchButton').IsEnabled = $ok
    ShowVersions -GamePath $Path
    return $ok
}

# Reads the version out of the INSTALLED DLLs rather than out of this script.
#
# FileVersionInfo reads the PE metadata without loading the assembly, which
# matters here: loading WetReality.Pose.dll outside the game would pull in the
# Il2Cpp interop types and fail.
#
# And reading the installed file rather than a constant means the number shown is
# always the number that will actually run. A hardcoded string would drift the
# moment somebody copies an older DLL in - which is exactly the situation where a
# version display has to be trustworthy.
function ShowVersions {
    param([string] $GamePath)

    $label = Ctl 'VersionText'

    if (-not $label) { return }

    try {
        if (-not (Test-GameFolder -Path $GamePath)) {
            $label.Text = ''
            return
        }

        $parts = @()

        foreach ($mod in @(,@('Mod', 'WetReality.XRStart.dll'))) {
            $dll = [System.IO.Path]::Combine($GamePath, 'Mods', $mod[1])

            if (Test-Path -LiteralPath $dll) {
                $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
                $v = $info.FileVersion

                # Only a FOURTH component gets dropped. A bare '\.0$' also ate the
                # patch digit and turned 0.95.0 into 0.95 - caught by printing the
                # result rather than by reading the pattern.
                if ($v) { $parts += ("{0} {1}" -f $mod[0], ($v -replace '^(\d+\.\d+\.\d+)\.0$', '$1')) }
            }
            else {
                $parts += ("{0} {1}" -f $mod[0], (T 'missing'))
            }
        }

        if ($parts.Count -gt 0) { $label.Text = ($parts -join '   ') }
        else { $label.Text = '' }
    }
    catch {
        $label.Text = ''
    }
}

function Load-Settings {
    $script:Loading = $true

    # Fallbacks = the mod's own source defaults (DevTools.cs, PointerStyle.cs).
    # They only apply where the key is missing from the cfg.
    (Ctl 'TurnSpeedSlider').Value = [double](Read-CfgValue -Key 'TurnSpeed' -Fallback '90')
    (Ctl 'HapticIntensitySlider').Value = [double](Read-CfgValue -Key 'HapticIntensity' -Fallback '1')
    (Ctl 'UiScaleSlider').Value = [double](Read-CfgValue -Key 'UiScale' -Fallback '0.3627')
    (Ctl 'UiDistanceSlider').Value = [double](Read-CfgValue -Key 'UiDistance' -Fallback '2')
    (Ctl 'ReachSlider').Value = [double](Read-CfgValue -Key 'InteractionRange' -Fallback '5')
    (Ctl 'MarkerSizeSlider').Value = [double](Read-CfgValue -Key 'TeleportMarkerSize' -Fallback '0.45')

    (Ctl 'SprayHapticsCheck').IsChecked = (Read-CfgValue -Key 'SprayHaptics' -Fallback 'true') -eq 'true'
    (Ctl 'TeleportCheck').IsChecked = (Read-CfgValue -Key 'ComfortTeleport' -Fallback 'false') -eq 'true'
    # Comfort.cs (XRStart 1.23.0), PWS2 names and defaults.
    (Ctl 'SnapTurnCheck').IsChecked = (Read-CfgValue -Key 'SnapTurn' -Fallback 'false') -eq 'true'
    (Ctl 'VignetteCheck').IsChecked = (Read-CfgValue -Key 'ComfortVignette' -Fallback 'false') -eq 'true'
    (Ctl 'SnapAngleSlider').Value = [double](Read-CfgValue -Key 'SnapAngle' -Fallback '45')
    (Ctl 'VignetteStrengthSlider').Value = [double](Read-CfgValue -Key 'VignetteStrength' -Fallback '0.7')
    # Gestures.cs, HandSpray.cs, MenuCamera.cs, Immersion.cs (XRStart 1.24-1.30).
    (Ctl 'GesturesCheck').IsChecked = (Read-CfgValue -Key 'GestureZones' -Fallback 'true') -eq 'true'
    (Ctl 'HandHitCheck').IsChecked = (Read-CfgValue -Key 'HandHit' -Fallback 'true') -eq 'true'
    (Ctl 'OrangeHandsCheck').IsChecked = (Read-CfgValue -Key 'OrangeHands' -Fallback 'true') -eq 'true'
    # LoadingBlackout and MenuHoldSeconds are not player options any more
    # (user 28.09.): not read and never written - the mod uses its defaults.
    # Grip.cs (XRStart 1.32.0), PWS2 keys; 0 = the washer exactly on the controller.
    (Ctl 'GripXSlider').Value = [double](Read-CfgValue -Key 'GripOffsetX' -Fallback '0')
    (Ctl 'GripYSlider').Value = [double](Read-CfgValue -Key 'GripOffsetY' -Fallback '0')
    (Ctl 'GripZSlider').Value = [double](Read-CfgValue -Key 'GripOffsetZ' -Fallback '0')
    (Ctl 'RotPitchSlider').Value = [double](Read-CfgValue -Key 'RotationOffsetPitch' -Fallback '0')
    (Ctl 'RotYawSlider').Value = [double](Read-CfgValue -Key 'RotationOffsetYaw' -Fallback '0')
    (Ctl 'RotRollSlider').Value = [double](Read-CfgValue -Key 'RotationOffsetRoll' -Fallback '0')
    (Ctl 'LaserCheck').IsChecked = (Read-CfgValue -Key 'ShowWashLaser' -Fallback 'false') -eq 'true'
    $hand = (Read-CfgValue -Key 'Hand' -Fallback 'RightHand').Trim('"')
    if ($hand -eq 'LeftHand') { (Ctl 'HandBox').SelectedIndex = 1 } else { (Ctl 'HandBox').SelectedIndex = 0 }
    # AutoStart, SkipLoadingContinue and DevCheats are not player options any
    # more (user 28.09.): not read and never written - the cfg keeps its values.

    # An unknown name lands on the mod's default, the same rule the mod applies.
    $colour = (Read-CfgValue -Key 'PointerColor' -Fallback 'blue').ToLowerInvariant()
    $colourIndex = [Array]::IndexOf($script:PointerColors, $colour)
    if ($colourIndex -lt 0) { $colourIndex = 2 }
    (Ctl 'PointerColorBox').SelectedIndex = $colourIndex

    # Monitor view is on/off only (user 28.09.). On keeps a right/both from the
    # cfg; anything unknown is the mod's default, left.
    $mirror = (Read-CfgValue -Key 'DesktopMirror' -Fallback 'left').Trim('"').ToLowerInvariant()
    if ([Array]::IndexOf($script:MirrorModes, $mirror) -lt 0) { $mirror = 'left' }
    $script:MirrorOn = if ($mirror -eq 'off') { 'left' } else { $mirror }
    (Ctl 'MirrorCheck').IsChecked = $mirror -ne 'off'

    Sync-ComfortPreset
    $script:Loading = $false
    Update-Labels
    (Ctl 'SaveHint').Text = ''
}

function Update-Labels {
    (Ctl 'TurnSpeedValue').Text = "$([int](Ctl 'TurnSpeedSlider').Value) $(T 'deg/s')"
    (Ctl 'HapticIntensityValue').Text = "$([int]((Ctl 'HapticIntensitySlider').Value * 100)) %"
    (Ctl 'UiScaleValue').Text = "$([int]((Ctl 'UiScaleSlider').Value * 100)) %"
    (Ctl 'UiDistanceValue').Text = "$((Format-Float ([Math]::Round((Ctl 'UiDistanceSlider').Value, 1)))) m"
    (Ctl 'ReachValue').Text = "$((Format-Float ([Math]::Round((Ctl 'ReachSlider').Value, 1)))) m"
    (Ctl 'MarkerSizeValue').Text = "$([int]((Ctl 'MarkerSizeSlider').Value * 100)) cm"

    $deg = [char]0x00B0
    (Ctl 'GripXValue').Text = "$([int][Math]::Round((Ctl 'GripXSlider').Value * 100)) cm"
    (Ctl 'GripYValue').Text = "$([int][Math]::Round((Ctl 'GripYSlider').Value * 100)) cm"
    (Ctl 'GripZValue').Text = "$([int][Math]::Round((Ctl 'GripZSlider').Value * 100)) cm"
    (Ctl 'RotPitchValue').Text = "$([int](Ctl 'RotPitchSlider').Value)$deg"
    (Ctl 'RotYawValue').Text = "$([int](Ctl 'RotYawSlider').Value)$deg"
    (Ctl 'RotRollValue').Text = "$([int](Ctl 'RotRollSlider').Value)$deg"
    (Ctl 'SnapAngleValue').Text = "$([int](Ctl 'SnapAngleSlider').Value)$deg"
    # At 0 the word says the vignette is off - "0 %" reads like a measurement (PWS2).
    $strength = [int]((Ctl 'VignetteStrengthSlider').Value * 100)
    if ($strength -le 0) { (Ctl 'VignetteStrengthValue').Text = T 'none' }
    else { (Ctl 'VignetteStrengthValue').Text = "$strength %" }

    # A slider for a switched-off feature is greyed, not hidden (PWS2).
    (Ctl 'SnapAngleSlider').IsEnabled = [bool](Ctl 'SnapTurnCheck').IsChecked
    (Ctl 'VignetteStrengthSlider').IsEnabled = [bool](Ctl 'VignetteCheck').IsChecked
}

# Comfort presets as in PWS2. Index 3 "Custom" sets nothing - it names a state
# that matches no preset. Sliders only where the option is on: a preset that
# moves a greyed value changes something the player does not see.
function Set-ComfortPreset {
    param([int] $Index)
    $teleport = $false; $snap = $false; $vignette = $false
    switch ($Index) {
        0 { }
        1 { $snap = $true; $vignette = $true }
        2 { $teleport = $true; $snap = $true; $vignette = $true }
        default { return }
    }
    $script:ApplyingPreset = $true
    try {
        (Ctl 'TeleportCheck').IsChecked = $teleport
        (Ctl 'SnapTurnCheck').IsChecked = $snap
        (Ctl 'VignetteCheck').IsChecked = $vignette
        if ($snap) { (Ctl 'SnapAngleSlider').Value = 45 }
        if ($vignette) { (Ctl 'VignetteStrengthSlider').Value = 0.7 }
    }
    finally { $script:ApplyingPreset = $false }
    Update-Labels
    Mark-Dirty
}

# Which preset describes the current state? The SLIDERS COUNT TOO - comparing
# only the ticks would claim "Gentle" while the snap angle stood at 30 (PWS2).
function Sync-ComfortPreset {
    $teleport = [bool](Ctl 'TeleportCheck').IsChecked
    $snap = [bool](Ctl 'SnapTurnCheck').IsChecked
    $vignette = [bool](Ctl 'VignetteCheck').IsChecked
    $stock = ([Math]::Abs((Ctl 'SnapAngleSlider').Value - 45) -lt 0.01) -and ([Math]::Abs((Ctl 'VignetteStrengthSlider').Value - 0.7) -lt 0.01)
    $index = 3
    if (-not $teleport -and -not $snap -and -not $vignette) { $index = 0 }
    elseif (-not $teleport -and $snap -and $vignette -and $stock) { $index = 1 }
    elseif ($teleport -and $snap -and $vignette -and $stock) { $index = 2 }
    $script:SuppressPreset = $true
    try { (Ctl 'ComfortPresetBox').SelectedIndex = $index }
    finally { $script:SuppressPreset = $false }
}

function Mark-Dirty {
    if ($script:Loading) { return }
    $script:Dirty = $true
    (Ctl 'SaveHint').Text = T 'Unsaved changes'
}

# --------------------------------------------------------------------- events

foreach ($name in @('TurnSpeedSlider', 'HapticIntensitySlider', 'UiScaleSlider',
                    'UiDistanceSlider', 'ReachSlider', 'MarkerSizeSlider',
                    'SnapAngleSlider', 'VignetteStrengthSlider',
                    'GripXSlider', 'GripYSlider', 'GripZSlider',
                    'RotPitchSlider', 'RotYawSlider', 'RotRollSlider')) {
    (Ctl $name).Add_ValueChanged({ Update-Labels; Mark-Dirty })
}

# These two also grey their slider, so they refresh the labels too.
foreach ($name in @('SnapTurnCheck', 'VignetteCheck')) {
    (Ctl $name).Add_Click({ Update-Labels; Mark-Dirty })
}

foreach ($name in @('SprayHapticsCheck', 'TeleportCheck',
                    'GesturesCheck', 'HandHitCheck', 'OrangeHandsCheck',
                    'LaserCheck', 'MirrorCheck')) {
    (Ctl $name).Add_Click({ Mark-Dirty })
}

(Ctl 'PointerColorBox').Add_SelectionChanged({ Mark-Dirty })
(Ctl 'HandBox').Add_SelectionChanged({ Mark-Dirty })

# Add_Click, NOT Add_Checked: a scripted IsChecked fires no Click, so a preset
# does not read its own ticks back as a user change (PWS2).
foreach ($name in @('TeleportCheck', 'SnapTurnCheck', 'VignetteCheck')) {
    (Ctl $name).Add_Click({ if (-not $script:ApplyingPreset) { Sync-ComfortPreset } })
}
foreach ($name in @('SnapAngleSlider', 'VignetteStrengthSlider')) {
    (Ctl $name).Add_ValueChanged({ if (-not $script:Loading -and -not $script:ApplyingPreset) { Sync-ComfortPreset } })
}
(Ctl 'ComfortPresetBox').Add_SelectionChanged({
    if ($script:Loading -or $script:SuppressPreset) { return }
    Set-ComfortPreset -Index (Ctl 'ComfortPresetBox').SelectedIndex
})

(Ctl 'GripResetButton').Add_Click({
    foreach ($n in @('GripXSlider', 'GripYSlider', 'GripZSlider', 'RotPitchSlider', 'RotYawSlider', 'RotRollSlider')) { (Ctl $n).Value = 0 }
})

(Ctl 'BrowseButton').Add_Click({
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = T 'Select the "PowerWash Simulator" folder'

    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        if (Set-Status -Path $dialog.SelectedPath) { Load-Settings }
    }
})

(Ctl 'LangButton').Add_Click({
    $next = 'de'
    if ($script:Language -eq 'de') { $next = 'en' }

    Set-Language -Window $window -Value $next
    Save-Language -Value $next
    Update-LanguageButton

    Set-Status -Path $script:GameFolder | Out-Null
    if (-not $script:Loading) { Update-Labels }
})

# KURZANLEITUNG - seit 1.34.0 wieder verdrahtet (Port aus PWS2). Beim Port
# 1.19.1 blieb der Knopf ausgeblendet, weil es noch keine Anleitung gab; seit
# 1.33.0 liegt sie im Paket, und README/LIESMICH verweisen auf diesen Knopf.
# Folgt der Fenstersprache, faellt auf die englische Datei zurueck.
(Ctl 'GuideButton').Add_Click({
    $guide = Join-Path $script:Root 'QuickGuide.html'
    if ($script:Language -eq 'de') {
        $de = Join-Path $script:Root 'QuickGuide-de.html'
        if (Test-Path -LiteralPath $de) { $guide = $de }
    }

    if (Test-Path -LiteralPath $guide) {
        Start-Process -FilePath $guide
    }
    else {
        [System.Windows.MessageBox]::Show(
            "$(T 'The quick guide is not available yet.')`n`n$(T 'Expected at'): $guide",
            'Wet Reality') | Out-Null
    }
})

(Ctl 'LaunchButton').Add_Click({
    # Warn first, then start - PWS2 rule: unsaved changes would not apply.
    if ($script:Dirty) {
        $answer = [System.Windows.MessageBox]::Show(
            (T 'There are unsaved changes. They will not apply to this session. Start anyway?'),
            'Wet Reality',
            [System.Windows.MessageBoxButton]::OKCancel,
            [System.Windows.MessageBoxImage]::Warning)

        if ($answer -ne [System.Windows.MessageBoxResult]::OK) { return }
    }

    try {
        # Through Steam, like a click in the library - never the .exe.
        Start-Process -FilePath "steam://rungameid/$($script:AppId)"
        (Ctl 'SaveHint').Text = T 'Starting through Steam...'
    }
    catch {
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Wet Reality') | Out-Null
    }
})

(Ctl 'SaveButton').Add_Click({
    try {
        $values = @{
            'TurnSpeed'          = Format-Float (Ctl 'TurnSpeedSlider').Value
            'HapticIntensity'    = Format-Float (Ctl 'HapticIntensitySlider').Value
            'SprayHaptics'       = Format-Bool ([bool](Ctl 'SprayHapticsCheck').IsChecked)
            'UiScale'            = Format-Float (Ctl 'UiScaleSlider').Value
            'UiDistance'         = Format-Float ([Math]::Round((Ctl 'UiDistanceSlider').Value, 2))
            'InteractionRange'   = Format-Float ([Math]::Round((Ctl 'ReachSlider').Value, 2))
            # Kein Haken mehr (Nutzer 28.09.): die VR-Haende sind immer an. Fest
            # geschrieben statt weggelassen, damit ein altes "false" repariert wird.
            'ShowVrHands'        = 'true'
            'TeleportMarkerSize' = Format-Float ([Math]::Round((Ctl 'MarkerSizeSlider').Value, 2))
            'ComfortTeleport'    = Format-Bool ([bool](Ctl 'TeleportCheck').IsChecked)
            'SnapTurn'           = Format-Bool ([bool](Ctl 'SnapTurnCheck').IsChecked)
            'SnapAngle'          = Format-Float ([Math]::Round((Ctl 'SnapAngleSlider').Value))
            'ComfortVignette'    = Format-Bool ([bool](Ctl 'VignetteCheck').IsChecked)
            'VignetteStrength'   = Format-Float ([Math]::Round((Ctl 'VignetteStrengthSlider').Value, 2))
            'GestureZones'       = Format-Bool ([bool](Ctl 'GesturesCheck').IsChecked)
            'HandHit'            = Format-Bool ([bool](Ctl 'HandHitCheck').IsChecked)
            'OrangeHands'        = Format-Bool ([bool](Ctl 'OrangeHandsCheck').IsChecked)
            'ShowWashLaser'      = Format-Bool ([bool](Ctl 'LaserCheck').IsChecked)
            # As a name in quotes, like every MelonPreferences string entry.
            'Hand'               = $(if ((Ctl 'HandBox').SelectedIndex -eq 1) { '"LeftHand"' } else { '"RightHand"' })
            'GripOffsetX'        = Format-Float ([Math]::Round((Ctl 'GripXSlider').Value, 3))
            'GripOffsetY'        = Format-Float ([Math]::Round((Ctl 'GripYSlider').Value, 3))
            'GripOffsetZ'        = Format-Float ([Math]::Round((Ctl 'GripZSlider').Value, 3))
            'RotationOffsetPitch' = Format-Float ([Math]::Round((Ctl 'RotPitchSlider').Value))
            'RotationOffsetYaw'  = Format-Float ([Math]::Round((Ctl 'RotYawSlider').Value))
            'RotationOffsetRoll' = Format-Float ([Math]::Round((Ctl 'RotRollSlider').Value))
            # As names in quotes, the way MelonPreferences keeps a string entry.
            'PointerColor'       = "`"$($script:PointerColors[[Math]::Max(0, (Ctl 'PointerColorBox').SelectedIndex)])`""
            'DesktopMirror'      = "`"$(if ([bool](Ctl 'MirrorCheck').IsChecked) { $script:MirrorOn } else { 'off' })`""
        }

        # @() around the result: an empty PowerShell return collapses to $null
        # and .Count throws under StrictMode (PWS2 lesson).
        $result = Write-CfgValues -Values $values
        $script:Dirty = $false

        $missing = @($result.Missing)
        $appended = @($result.Appended)

        if ($missing.Count -gt 0) {
            (Ctl 'SaveHint').Text = (T 'Saved. Keys not found: {0}') -f ($missing -join ', ')
        }
        elseif ($appended.Count -gt 0) {
            (Ctl 'SaveHint').Text = (T 'Saved at {0}. {1} new setting(s) added. Backup written as .bak.') `
                -f (Get-Date -Format 'HH:mm:ss'), $appended.Count
        }
        else {
            (Ctl 'SaveHint').Text = (T 'Saved at {0}. Backup written as .bak.') -f (Get-Date -Format 'HH:mm:ss')
        }
    }
    catch {
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Wet Reality') | Out-Null
    }
})

# ----------------------------------------------------------------------- start

$found = $GamePath
if (-not $found) { $found = Find-GameFolder }

# Ohne Installation stehen die Vorgaben aus der XAML (Value, IsChecked,
# SelectedIndex = die Vorgaben der Mod); Update-Labels schreibt ihre Zahlen
# dazu. Vorher standen dort leere Felder und Regler am Minimum - gelesen als
# "20 % und 0,8 m sind eingestellt" (Nutzer 28.09.).
if (Set-Status -Path $found) { Load-Settings } else { $script:Loading = $false; Update-Labels }

# THE WINDOW MUST NOT BE TALLER THAN THE SCREEN IT OPENS ON.
#
# MainWindow.xaml asks for 965 px seit Abschnitt 109 - das Spiegelbild-Haekchen
# ist wieder heraus, weil es nicht behebt, wonach es aussieht. Die Preference
# DesktopMirror bleibt in der cfg: sie stoppt den Augenpuffer-Blit, sie
# beseitigt nur das verschachtelte Titelbild nicht.
#
# Vorher, Abschnitt 108: die Vibrations- und
# Spiegelbild-Steuerelemente sind dazugekommen, und der Knopf zum
# Zuruecksetzen des Griffs lag darunter - gemeldet als "man muss scrollen".
# Der Deckel aus WorkArea unten bleibt die Rueckfallebene fuer kleine Schirme;
# auf 1080p ist bei etwa 1040 px Arbeitsflaeche noch Luft.
#
# MainWindow.xaml asks for 900 px so that all six grip sliders fit without
# scrolling - the three rotation rows used to sit below the edge. On a 1080p
# laptop with a taskbar that is close to the whole screen, so the work area is
# the limit and the XAML's ScrollViewer takes over from there. Shrinking the
# window is the harmless direction; the settings stay reachable either way.
#
# Guarded rather than trusted: WorkArea comes back empty on some remote
# sessions, and a MaxHeight of nearly zero would leave a title bar with
# nothing under it.
$work = [System.Windows.SystemParameters]::WorkArea.Height

if ($work -gt 400 -and $window.Height -gt $work - 40) {
    $window.Height = $work - 40
}

# DIE HOEHE FOLGT DEM INHALT - seit 1.34.0, gemeldet als "grosse Luecken".
#
# Die 900 px aus der XAML sind nur noch der erste Wurf. Sobald das Fenster
# gezeichnet ist, sagt die ScrollViewer, wie weit Inhalt und Sichtflaeche
# auseinanderliegen: positiv = es muesste gescrollt werden, negativ = unter
# den Feldern steht leere Flaeche. Um genau diesen Betrag wird das Fenster
# korrigiert und als MaxHeight festgehalten, damit Aufziehen keine Luecke mehr
# erzeugt. Der WorkArea-Deckel oben gilt weiter; darunter scrollt es wie bisher.
#
# NACHGEFUEHRT, wenn sich der INHALT aendert (Sprachwechsel, andere Breite und
# damit anderer Umbruch) - nicht bei ScrollChanged, sonst liesse sich das
# Fenster von Hand nicht mehr verkleinern. SizeToContent ist keine Alternative,
# siehe MainWindow.xaml: das Hintergrundbild wuerde die Hoehe bestimmen.
function Fit-WindowHeight {
    param([switch] $Center)

    $scroll = Ctl 'SettingsScroll'
    if (-not $scroll -or $scroll.ViewportHeight -le 0) { return }

    $fit = $window.ActualHeight + ($scroll.ExtentHeight - $scroll.ViewportHeight)
    if ($work -gt 400) { $fit = [math]::Min($fit, $work - 40) }
    $fit = [math]::Max($fit, $window.MinHeight)

    if ([math]::Abs($fit - $window.ActualHeight) -lt 1 -and $window.MaxHeight -eq $fit) { return }

    $window.MaxHeight = $fit
    $window.Height = $fit

    if ($Center) {
        $area = [System.Windows.SystemParameters]::WorkArea
        if ($area.Height -gt 400) { $window.Top = $area.Top + ($area.Height - $fit) / 2 }
    }
}

$window.Add_ContentRendered({
    Fit-WindowHeight -Center
    (Ctl 'SettingsScroll').Content.Add_SizeChanged({ Fit-WindowHeight })
})

# DIE KONSOLE WEG - Abschnitt 130, und die Stelle ist der Entwurf.
#
# Configurator.cmd startet die Sitzung schon mit -WindowStyle Hidden; ein
# Doppelklick auf diese .ps1 tut das nicht. ShowWindow deckt beide Wege ab.
#
# HIER und nicht oben: alles darueber kann fehlschlagen und schreibt dann in
# die Konsole - fehlende WPF-Assemblies, ein kaputtes XAML, eine unlesbare
# cfg. Eine in Zeile 1 versteckte Konsole macht daraus ein Werkzeug, das beim
# Start still nichts tut, und das ist schlimmer als ein sichtbares Fenster.
#
# GetConsoleWindow gibt 0 zurueck, wenn es gar keine Konsole gibt - dann ist
# nichts zu verstecken. Ein Fehlschlag bleibt folgenlos: das Fenster oeffnet
# sich trotzdem, die Konsole bleibt eben stehen. Das ist die harmlose Richtung.
try {
    if (-not ('WetReality.Native' -as [type])) {
        Add-Type -Namespace WetReality -Name Native -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
'@
    }

    $console = [WetReality.Native]::GetConsoleWindow()

    if ($console -ne [System.IntPtr]::Zero) {
        # 0 ist SW_HIDE.
        [WetReality.Native]::ShowWindow($console, 0) | Out-Null
    }
}
catch {
    # Absichtlich stumm: eine Meldung ueber ein nicht verstecktes Fenster in
    # ein Fenster zu schreiben, das gleich aufgeht, hilft niemandem.
}

$window.ShowDialog() | Out-Null
