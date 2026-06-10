<#
.SYNOPSIS
  Remove everything Matrix sets up for the current user.
.DESCRIPTION
  Clears the screensaver registration, stops and removes the live wallpaper
  (and its logon autostart), deletes the desktop "Lock with Matrix" shortcut,
  and removes the installed files. All per-user; no admin required.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# --- screensaver registration ---
$desk = 'HKCU:\Control Panel\Desktop'
Set-ItemProperty    -Path $desk -Name 'ScreenSaveActive' -Value '0'
Remove-ItemProperty -Path $desk -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
Write-Host 'Cleared screensaver registration.'

# --- live wallpaper ---
Get-Process -Name 'MatrixWallpaper' -ErrorAction SilentlyContinue | Stop-Process -Force
$startupLnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'MatrixWallpaper.lnk'
if (Test-Path $startupLnk) { Remove-Item -Force $startupLnk; Write-Host 'Removed wallpaper autostart.' }

# --- desktop lock shortcut ---
$lockLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Lock with Matrix.lnk'
if (Test-Path $lockLnk) { Remove-Item -Force $lockLnk; Write-Host 'Removed "Lock with Matrix" shortcut.' }

# --- installed files ---
$installDir = Join-Path $env:USERPROFILE 'Matrix'
if (Test-Path $installDir) { Remove-Item -Recurse -Force $installDir; Write-Host "Removed $installDir" }

# Apply the screensaver change immediately.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SpiU {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool SystemParametersInfo(uint a, uint p, IntPtr v, uint f);
}
"@
[SpiU]::SystemParametersInfo(0x0011, 0, [IntPtr]::Zero, 0x03) | Out-Null

Write-Host 'Matrix fully uninstalled.'
