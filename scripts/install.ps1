<#
.SYNOPSIS
  Install the Matrix screensaver for the current user (no admin required).
.DESCRIPTION
  Copies Matrix.scr into %USERPROFILE%\Matrix and registers it as the active
  Windows screensaver via HKCU, with an idle timeout and password-on-resume.
.PARAMETER TimeoutSeconds
  Idle time before the screensaver starts. Default 300 (5 minutes).
.PARAMETER Secure
  Require the logon password when the screensaver is dismissed. Default $true.
#>
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 300,
    [bool]$Secure = $true
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot '..\Matrix.scr'
if (-not (Test-Path $source)) {
    throw "Matrix.scr not found next to the repo. Build it first:  ./build.sh"
}
$source = (Resolve-Path $source).Path

# Run from the Windows filesystem, not the \\wsl$ share, so the saver host is happy.
$destDir = Join-Path $env:USERPROFILE 'Matrix'
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$dest = Join-Path $destDir 'Matrix.scr'
Copy-Item -Force -Path $source -Destination $dest
Write-Host "Copied screensaver -> $dest"

# Also deploy the .exe build (used by the lock hotkey, which needs /lock to pass).
$exeSrc = Join-Path $PSScriptRoot '..\Matrix.exe'
if (Test-Path $exeSrc) { Copy-Item -Force -Path (Resolve-Path $exeSrc).Path -Destination (Join-Path $destDir 'Matrix.exe') }

$desk = 'HKCU:\Control Panel\Desktop'
Set-ItemProperty -Path $desk -Name 'SCRNSAVE.EXE'        -Value $dest
Set-ItemProperty -Path $desk -Name 'ScreenSaveActive'    -Value '1'
Set-ItemProperty -Path $desk -Name 'ScreenSaveTimeOut'   -Value "$TimeoutSeconds"
Set-ItemProperty -Path $desk -Name 'ScreenSaverIsSecure' -Value $(if ($Secure) { '1' } else { '0' })

# Apply immediately so no sign-out is needed.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Spi {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool SystemParametersInfo(uint a, uint p, IntPtr v, uint f);
}
"@
$SPI_SETSCREENSAVEACTIVE  = 0x0011
$SPI_SETSCREENSAVETIMEOUT = 0x000F
$SPIF_APPLY = 0x03  # SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
[Spi]::SystemParametersInfo($SPI_SETSCREENSAVEACTIVE, 1, [IntPtr]::Zero, $SPIF_APPLY)  | Out-Null
[Spi]::SystemParametersInfo($SPI_SETSCREENSAVETIMEOUT, [uint32]$TimeoutSeconds, [IntPtr]::Zero, $SPIF_APPLY) | Out-Null

Write-Host ""
Write-Host ("Active: starts after {0}s idle; password on resume = {1}." -f $TimeoutSeconds, $Secure)
Write-Host "Preview it now:  powershell -ExecutionPolicy Bypass -File scripts\test.ps1"
Write-Host "Note: on a managed/domain PC, Group Policy can override these settings."
