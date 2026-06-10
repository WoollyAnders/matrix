<#
.SYNOPSIS
  Lock the machine "with Matrix": show the rain now, lock on return.
.DESCRIPTION
  Launches the screensaver directly in /lock mode. It ignores input for ~1s
  (so the launching hotkey can't dismiss it), shows the rain across all
  monitors, and when you come back and move the mouse / press a key it locks
  the workstation -- Windows then asks for your password.

  (We launch it directly rather than via the system screensaver path, because
  Windows' own screensaver monitor would kill it the instant the launching
  keystroke arrives.)
#>
[CmdletBinding()]
param()

$scr = Join-Path $env:USERPROFILE 'Matrix\Matrix.scr'
if (-not (Test-Path $scr)) { throw "Matrix.scr not found. Run scripts\install.ps1 first." }
Start-Process -FilePath $scr -ArgumentList '/lock'
