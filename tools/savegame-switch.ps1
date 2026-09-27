# Spielstand zwischen "original" und "dev" (Cheats) umschalten.
#
#   powershell -ExecutionPolicy Bypass -File tools\savegame-switch.ps1 -To original
#   powershell -ExecutionPolicy Bypass -File tools\savegame-switch.ps1 -To dev
#   powershell -ExecutionPolicy Bypass -File tools\savegame-switch.ps1          (zeigt nur den Stand)
#
# Getauscht wird nur der Slot-Ordner SaveData\0 (Fortschritt, Guthaben,
# Zeitraffer). SaveData\Preferences (Grafik, Steuerung) bleibt, wie er ist.
# Der gerade aktive Stand wird zuerst in seinen eigenen Platz unter
# savegame-backup\ zurueckgeschrieben, dann der andere eingespielt - beide
# Staende leben also weiter. Zusaetzlich legt jeder Wechsel eine Kopie mit
# Zeitstempel an (savegame-backup\safety\).
#
# DevCheats in UserData\MelonPreferences.cfg wird passend gesetzt (dev = true,
# original = false). Das geht nur bei BEENDETEM Spiel: MelonLoader schreibt
# die cfg beim Beenden neu.
#
# Steam Cloud synchronisiert SaveData: nach einem Wechsel kann Steam beim
# naechsten Start einen Konflikt melden - dann "lokale Dateien" waehlen.
#
# Nur ASCII in dieser Datei: Windows PowerShell 5.1 liest Skripte ohne BOM als ANSI.

param(
    [ValidateSet('original', 'dev', '')]
    [string]$To = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root    = Split-Path -Parent $PSScriptRoot
$store   = Join-Path $root 'savegame-backup'
$marker  = Join-Path $store 'current.txt'
$live    = Join-Path $env:USERPROFILE 'AppData\LocalLow\FuturLab\PowerWash Simulator\SaveData\0'
# Spielordner aus Directory.Build.props (nicht versioniert, wie beim Bauen) -
# kein Laufwerkspfad im Repository.
$props   = Join-Path $root 'Directory.Build.props'
if (-not (Test-Path $props)) { throw "Keine $props - Spielordner unbekannt (Vorlage: Directory.Build.props.example)." }
$m = [regex]::Match([IO.File]::ReadAllText($props), '<GamePath>([^<]+)</GamePath>')
if (-not $m.Success) { throw "Kein <GamePath> in $props" }
$cfg     = Join-Path $m.Groups[1].Value.Trim() 'UserData\MelonPreferences.cfg'

function Slot([string]$name) { Join-Path $store (Join-Path $name 'SaveData\0') }

if (-not (Test-Path $marker)) { throw "Kein $marker - unbekannt, welcher Stand aktiv ist. Nicht geraten; bitte pruefen." }
$current = ([IO.File]::ReadAllText($marker)).Trim()
Write-Host "Aktiv: $current   (live: $live)"
foreach ($n in 'original', 'dev') {
    $p = Slot $n
    $state = if (Test-Path $p) { "vorhanden, $(@(Get-ChildItem -Recurse -File $p).Count) Dateien" } else { 'leer' }
    Write-Host ("  {0,-9} {1}" -f $n, $state)
}
if ($To -eq '') { return }
if ($To -eq $current) { Write-Host "Schon aktiv: $To - nichts zu tun."; return }

if (Get-Process PowerWashSimulator -ErrorAction SilentlyContinue) { throw 'Das Spiel laeuft - erst beenden.' }
if (-not (Test-Path $live)) { throw "Live-Slot fehlt: $live" }

# 1. Sicherheitskopie des Live-Stands
$stamp  = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$safety = Join-Path $store "safety\$stamp`_$current\SaveData\0"
New-Item -ItemType Directory -Force (Split-Path -Parent $safety) | Out-Null
Copy-Item -Recurse $live $safety
Write-Host "Sicherheitskopie: $safety"

# 2. Live-Stand in seinen Platz zurueckschreiben (ersetzt den alten Stand dieses Platzes)
$own = Slot $current
if (Test-Path $own) { Remove-Item -Recurse -Force $own }
New-Item -ItemType Directory -Force (Split-Path -Parent $own) | Out-Null
Copy-Item -Recurse $live $own
Write-Host "Live -> $current"

# 3. Zielstand einspielen. Ist "dev" noch leer, beginnt er als Kopie von original.
$src = Slot $To
if (-not (Test-Path $src)) {
    if ($To -ne 'dev') { throw "Kein gesicherter Stand fuer $To unter $src" }
    Write-Host 'dev ist noch leer - startet als Kopie des aktuellen Stands.'
    $src = $own
}
Remove-Item -Recurse -Force $live
Copy-Item -Recurse $src $live
[IO.File]::WriteAllText($marker, "$To`n")
Write-Host "$To -> Live"

# 4. DevCheats in der cfg setzen (UTF-8 ohne BOM, kein Get-/Set-Content)
$want = if ($To -eq 'dev') { 'true' } else { 'false' }
if (Test-Path $cfg) {
    $text = [IO.File]::ReadAllText($cfg)
    $rx = '(?m)^(DevCheats\s*=\s*)(true|false)'
    if ($text -match $rx) { $text = [regex]::Replace($text, $rx, "`${1}$want") }
    elseif ($text -match '(?m)^\[WetReality_XRStart\]') { $text = [regex]::Replace($text, '(?m)^\[WetReality_XRStart\]\r?\n', "[WetReality_XRStart]`nDevCheats = $want`n") }
    else { $text = $text.TrimEnd() + "`n`n[WetReality_XRStart]`nDevCheats = $want`n" }
    [IO.File]::WriteAllText($cfg, $text, (New-Object Text.UTF8Encoding($false)))
    Write-Host "DevCheats = $want in $cfg"
} else {
    Write-Host "Keine cfg unter $cfg - DevCheats beim naechsten Start von Hand pruefen."
}
