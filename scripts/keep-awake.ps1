<#
.SYNOPSIS
  Keeps this workstation awake with a tiny periodic mouse "jiggle", so the Windows
  idle timer never reaches the inactivity auto-lock / display-sleep threshold.

.DESCRIPTION
  Every -IntervalSeconds it injects a 1px relative mouse move and immediately moves
  back (net-zero cursor position). The injected input updates the system "last input
  time" (GetLastInputInfo) -- which is what the "Interactive logon: Machine inactivity
  limit" policy and the power/screen-saver idle timers read -- so the machine stays
  unlocked while this runs. Stop it with Ctrl+C or by closing the window.

  AUTHORIZED USE ONLY. This deliberately keeps a managed workstation from auto-locking;
  run it only with IT/security approval for this machine.

  DO NOT run this alongside the Matrix /lock curtain (Matrix.exe /lock) or the /s
  screensaver -- both DISMISS on any input, so the jiggle would instantly dismiss them
  (and /lock would then lock the PC). Use it with the Matrix *wallpaper* (Lively) or
  on its own.

.PARAMETER IntervalSeconds
  Seconds between jiggles. Default 60 -- comfortably under the 900s (15 min) inactivity
  limit measured on this machine.

.PARAMETER SelfTest
  Prove the mechanism: measure system idle time before and after one jiggle, then exit.
  Keep hands off the mouse/keyboard for ~4s during the test for a clean reading.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\keep-awake.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\keep-awake.ps1 -IntervalSeconds 120
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\keep-awake.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [int]$IntervalSeconds = 60,
    [switch]$SelfTest
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class KeepAwake {
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
    public const uint MOUSEEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("kernel32.dll")] public static extern uint GetTickCount();

    // Net-zero relative move: 1px out and back. Two real input events fire (resetting the
    // idle clock) but the cursor ends where it started, so it never drifts.
    public static void Nudge() {
        mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, IntPtr.Zero);
        mouse_event(MOUSEEVENTF_MOVE, -1, 0, 0, IntPtr.Zero);
    }

    public static uint IdleMs() {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        GetLastInputInfo(ref lii);
        return unchecked(GetTickCount() - lii.dwTime);
    }
}
"@

if ($SelfTest) {
    Write-Host "Self-test: confirming a jiggle resets the system idle timer." -ForegroundColor Cyan
    Write-Host "  (keep hands off the mouse/keyboard for ~4 seconds)" -ForegroundColor DarkGray
    Start-Sleep -Seconds 4
    $before = [KeepAwake]::IdleMs()
    [KeepAwake]::Nudge()
    Start-Sleep -Milliseconds 150
    $after = [KeepAwake]::IdleMs()
    Write-Host ("  idle before jiggle : {0,6} ms" -f $before)
    Write-Host ("  idle after  jiggle : {0,6} ms" -f $after)
    if ($before -lt 2000) {
        Write-Host "NOTE: only $before ms idle before the test -- real input happened during the wait. Keep hands off and rerun for a clean reading." -ForegroundColor Yellow
    } elseif ($after -lt $before) {
        Write-Host "PASS - the jiggle reset the idle clock; the inactivity lock will not fire while keep-awake runs." -ForegroundColor Green
    } else {
        Write-Host "WARN - idle did not drop; this build/EDR may be filtering synthetic input. Tell me and we'll switch methods (e.g. SendInput / F15)." -ForegroundColor Red
    }
    return
}

Write-Host "Keep-awake running - jiggle every $IntervalSeconds s. Ctrl+C or close the window to stop." -ForegroundColor Green
Write-Host "Authorized keep-awake: this machine will not auto-lock while this is running." -ForegroundColor DarkGray
while ($true) {
    [KeepAwake]::Nudge()
    Write-Host ("{0:HH:mm:ss}  jiggle sent (idle now ~{1} ms)" -f (Get-Date), [KeepAwake]::IdleMs())
    Start-Sleep -Seconds $IntervalSeconds
}
