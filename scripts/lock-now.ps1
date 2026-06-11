<#
.SYNOPSIS
  Lock the machine "with Matrix": show the rain now, lock on return.
.DESCRIPTION
  Launches the screensaver directly in /lock mode. It ignores input for ~1s
  (so the launching hotkey can't dismiss it), shows the rain across all
  monitors, and when you come back and move the mouse / press a key it locks
  the workstation -- Windows then asks for your password.

  We launch it directly rather than via the system screensaver path, because
  Windows' own screensaver monitor would kill it the instant the launching
  keystroke arrives. We also launch the .exe (not the .scr): running a .scr via
  ShellExecute drops custom arguments, so /lock would be lost and it would fall
  back to plain screensaver mode without locking.
#>
[CmdletBinding()]
param()

$exe = Join-Path $env:USERPROFILE 'Matrix\Matrix.exe'
if (-not (Test-Path $exe)) { throw "Matrix.exe not found. Run scripts\install.ps1 first." }
Start-Process -FilePath $exe -ArgumentList '/lock'
