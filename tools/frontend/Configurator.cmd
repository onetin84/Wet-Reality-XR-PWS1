@echo off
rem Wet Reality XR Mod (PWS1) - Konfigurator. Wie PWS2 tools/package/Configurator.cmd,
rem nur liegt das Skript hier im selben Ordner.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0WetReality-Config.ps1"
