<#
.SYNOPSIS
  Launch the Matrix screensaver full-screen right now (move the mouse / press a key to exit).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Prefer the installed copy; fall back to the freshly built one in the repo.
$scr = Join-Path $env:USERPROFILE 'Matrix\Matrix.scr'
if (-not (Test-Path $scr)) {
    $scr = Join-Path $PSScriptRoot '..\Matrix.scr'
}
if (-not (Test-Path $scr)) {
    throw "Matrix.scr not found. Build it first:  ./build.sh"
}
$scr = (Resolve-Path $scr).Path

Start-Process -FilePath $scr -ArgumentList '/s'
