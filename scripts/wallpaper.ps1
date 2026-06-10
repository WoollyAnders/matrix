<#
.SYNOPSIS
  Manage the Matrix live wallpaper (animated rain behind the desktop icons).
.DESCRIPTION
  Actions:
    install    copy MatrixWallpaper.exe to %USERPROFILE%\Matrix, add a logon
               autostart entry, and start it now
    start      start it (deploys first if needed)
    stop       stop the running wallpaper
    uninstall  stop it, remove autostart, delete the installed copy
    status     report running / stopped
.PARAMETER Action
  install | start | stop | uninstall | status   (default: start)
.PARAMETER AutoStart
  When installing, also start it at logon. Default $true.
.NOTES
  A persistent animation uses some CPU/GPU -- expect a few percent and a little
  extra battery draw on a laptop. Stop it any time with: wallpaper.ps1 stop
#>
[CmdletBinding()]
param(
    [ValidateSet('install', 'start', 'stop', 'uninstall', 'status')]
    [string]$Action = 'start',
    [bool]$AutoStart = $true
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:USERPROFILE 'Matrix'
$exeDest    = Join-Path $installDir 'MatrixWallpaper.exe'
$startupLnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'MatrixWallpaper.lnk'

function Deploy {
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    $src = Join-Path $PSScriptRoot '..\MatrixWallpaper.exe'
    if (-not (Test-Path $src)) { throw "MatrixWallpaper.exe not found. Build it first:  ./build.sh" }
    Copy-Item -Force -Path (Resolve-Path $src).Path -Destination $exeDest
}

function Running { [bool](Get-Process -Name 'MatrixWallpaper' -ErrorAction SilentlyContinue) }

function StartWp {
    if (Running) { Write-Host 'Already running.'; return }
    Start-Process -FilePath $exeDest -ArgumentList '/w' -WorkingDirectory $installDir
}

function StopWp {
    Get-Process -Name 'MatrixWallpaper' -ErrorAction SilentlyContinue | Stop-Process -Force
}

function AddAutoStart {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($startupLnk)
    $lnk.TargetPath       = $exeDest
    $lnk.Arguments        = '/w'
    $lnk.WorkingDirectory = $installDir
    $lnk.Description       = 'Matrix live wallpaper'
    $lnk.Save()
}

function RemoveAutoStart { if (Test-Path $startupLnk) { Remove-Item -Force $startupLnk } }

switch ($Action) {
    'install' {
        Deploy
        if ($AutoStart) { AddAutoStart; Write-Host "Autostart enabled (logon)." }
        StartWp
        Start-Sleep -Milliseconds 1200
        if (Running) { Write-Host 'Wallpaper: running' }
        else { Write-Host 'Wallpaper: FAILED to stay running (WorkerW may differ on this Windows build)' }
    }
    'start'     { if (-not (Test-Path $exeDest)) { Deploy }; StartWp; Write-Host 'Started.' }
    'stop'      { StopWp; Write-Host 'Stopped.' }
    'uninstall' { StopWp; RemoveAutoStart; if (Test-Path $exeDest) { Remove-Item -Force $exeDest }; Write-Host 'Uninstalled.' }
    'status'    { if (Running) { 'running' } else { 'stopped' } }
}
