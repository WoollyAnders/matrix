<#
.SYNOPSIS
  Create a desktop "Lock with Matrix" shortcut with a global hotkey.
.DESCRIPTION
  Copies lock-now.ps1 into %USERPROFILE%\Matrix and drops a shortcut on the
  Desktop that runs it. A shortcut placed on the Desktop (or Start Menu) can
  carry a system-wide hotkey, so the chosen key combo locks-with-rain anywhere.
.PARAMETER Hotkey
  Hotkey assigned to the shortcut. Default "CTRL+ALT+L" (an L for "lock",
  Win+L being unavailable to apps; CTRL+ALT avoids app-shortcut clashes).
#>
[CmdletBinding()]
param(
    [string]$Hotkey = "CTRL+ALT+L"
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:USERPROFILE 'Matrix'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$lockSrc = Join-Path $PSScriptRoot 'lock-now.ps1'
$lockDst = Join-Path $installDir 'lock-now.ps1'
Copy-Item -Force -Path $lockSrc -Destination $lockDst

$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$desktop    = [Environment]::GetFolderPath('Desktop')
$lnkPath    = Join-Path $desktop 'Lock with Matrix.lnk'

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath       = $powershell
$lnk.Arguments        = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$lockDst`""
$lnk.WorkingDirectory = $installDir
$lnk.WindowStyle      = 7          # run minimized (no console flash)
$lnk.Hotkey           = $Hotkey
$lnk.Description       = "Lock the screen with the Matrix screensaver"
$scr = Join-Path $installDir 'Matrix.scr'
if (Test-Path $scr) { $lnk.IconLocation = "$scr,0" }
$lnk.Save()

Write-Host "Created shortcut: $lnkPath"
Write-Host "Global hotkey:    $Hotkey"
Write-Host "(Re-run with -Hotkey 'CTRL+ALT+M' etc. to change it.)"
