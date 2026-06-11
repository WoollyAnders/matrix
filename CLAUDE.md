# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Matrix "digital rain" effect for Windows, delivered three ways from **one C# engine**: a screensaver (`Matrix.scr`), an on-demand lock-with-rain hotkey (`Ctrl+Alt+L`), and a live desktop wallpaper (via Lively). Development happens in **WSL**, but every artifact runs on **Windows** — so commands constantly cross the WSL↔Windows boundary.

## Build / run / verify

There is **no test suite, linter, or package manager**. Verification is visual.

- **Build:** `./build.sh` — compiles `src/Matrix.cs` with the **in-box .NET Framework compiler** (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), no .NET SDK required. Emits three identical binaries at the repo root (all git-ignored): `Matrix.scr` (screensaver), `Matrix.exe` (lock hotkey), `MatrixWallpaper.exe` (native wallpaper).
  - `csc.exe` is a **Windows** program; `build.sh` converts WSL paths with `wslpath -w` before passing them.
  - The in-box compiler is **C# 5** — no string interpolation (`$"..."`), null-conditional (`?.`), expression-bodied members, etc.
- **Run a Windows command from WSL:** `powershell.exe -NoProfile -Command "..."` or `-File "$(wslpath -w scripts/foo.ps1)"`.
- **Quick visual test (no install):** `Matrix.exe /t` — a 900×600 on-top window that renders the rain and ignores input (kill it to close). Or `scripts\test.ps1` runs `Matrix.scr /s` full-screen.
- **Headless screenshot check** (how to "see" the effect from WSL): launch a mode, then `System.Drawing.Graphics.CopyFromScreen` over `SystemInformation.VirtualScreen` to a PNG in `%TEMP%`, and read the PNG. For the wallpaper, `(New-Object -ComObject Shell.Application).MinimizeAll()` first, then `UndoMinimizeALL`.

## Install / deploy (PowerShell, per-user, no admin)

Everything installs to `%USERPROFILE%\Matrix\`. Scripts in `scripts/` are run as `powershell -ExecutionPolicy Bypass -File scripts\<name>.ps1`:
- `install.ps1` — copies `Matrix.scr` + `Matrix.exe`, registers the screensaver (HKCU, timeout, password-on-resume).
- `install-lock-shortcut.ps1` — deploys `lock-now.ps1` + `Matrix.exe`, creates the desktop `Lock with Matrix.lnk` with a global hotkey (default `CTRL+ALT+L`).
- `wallpaper.ps1 install|start|stop|uninstall` — the **native** single-monitor wallpaper (`MatrixWallpaper.exe`).
- `uninstall.ps1` — removes all of the above.

**Iterating on the look (redeploy loop):** edit a constant → `./build.sh` → copy `Matrix.scr`/`Matrix.exe` into `%USERPROFILE%\Matrix\`. The lock screen re-reads on the next `Ctrl+Alt+L`. To **hot-reload the Lively wallpaper** without re-importing: copy `wallpaper/matrix.html` over the file Lively is actually serving (the matching `matrix.html` under `%LOCALAPPDATA%\Lively Wallpaper\Library\SaveData\wptmp\...`), then re-apply with `"C:\Program Files\Lively Wallpaper\Lively.exe" setwp --file <staged matrix.html>`.

## Architecture

**One binary, mode-dispatched.** `src/Matrix.cs` (`Program.Main`) parses the first arg into a mode; with no arg it infers from the exe name (`*Wallpaper*` → `/w`, else `/s`):
| Arg | Mode |
|-----|------|
| `/s` | screensaver, exits on input |
| `/lock` | like `/s`, but calls `LockWorkStation()` on dismiss |
| `/p <hwnd>` | preview in the Screen Saver settings dialog |
| `/c` | config dialog (no-op) |
| `/t` | windowed render test, ignores input |
| `/w` | native wallpaper, child of the desktop WorkerW |

**Shared field, per-monitor windows.** The rain is one `RainField` spanning the **entire virtual desktop** (a grid of cells, each owning its own glyph index, brightness, mirror flag, and a short-lived flash). One `MatrixForm` window is created **per monitor** (`Screen.AllScreens`), each rendering only its slice of that shared field — so the rain is one continuous field across screens, and it works on irregular/mixed-DPI multi-monitor layouts where a single spanning window does not. `RainController` runs **one timer** that steps the field once and repaints every window. Glyphs are pre-rendered into a `[glyph, brightness-level]` (+ mirrored) **bitmap cache** so each frame is a fast `DrawImageUnscaled` blit, not slow text layout — this is what keeps 4 monitors smooth.

**Two engines kept in lock-step.** `wallpaper/matrix.html` is a JavaScript/canvas re-implementation of the *same* algorithm for Lively. **When you change the rain behavior or its tuning constants, change both `src/Matrix.cs` and `wallpaper/matrix.html`** so the lock screen and wallpaper stay identical.

**Tuning lives in constants.** The look is driven entirely by labeled constants — `RainField` in `src/Matrix.cs` and the `// tunables` block at the top of `wallpaper/matrix.html`: `DECAY` (tail length), `SPEED_MIN/MAX`, `CHANGE_CHANCE` (constant glyph churn), `FLASH_BOOST/DECAY` (momentary glow on change/flip), `FLIP_RATE/FLIP_CHANCE` (horizontal mirroring), `INTERRUPT_CHANCE` (wipe + re-seed a whole column), `SEG_ERASE_RATE`/`SEG_MIN/MAX` (punch gaps in streams). Keep the values identical between the two files.

## Critical gotchas (each cost real debugging)

- **`.scr` files drop command-line args.** Launching `Matrix.scr /lock` via ShellExecute (e.g. `Start-Process`) runs it through the screensaver *file association*, which ignores extra args — it silently falls back to `/s` and never locks. The hotkey therefore launches **`Matrix.exe /lock`** (a plain exe receives args). This is why `Matrix.exe` exists.
- **Group Policy controls the screensaver on managed/domain machines.** A GPO under `HKCU\Software\Policies\...\Control Panel\Desktop` can set `ScreenSaveActive=0`, clear `SCRNSAVE.EXE`, and disable secure-resume — and re-applies on every policy refresh, wiping `install.ps1`'s settings. The **idle screensaver + its password-on-resume cannot be relied on there.** The `Ctrl+Alt+L` hotkey (direct `LockWorkStation()`) is GPO-independent and is the reliable lock.
- **Lock timing:** on dismiss in `/lock`, call `LockWorkStation()` and hold the process ~600 ms (a guarded one-shot timer) before `Application.Exit()` so Winlogon can switch to the secure desktop before teardown.
- **Multi-monitor wallpaper / WorkerW is version-specific.** On this Win11 build there is **no full-screen WorkerW** — the desktop is hosted on `Progman` (which `GetWallpaperHost` falls back to). The native `/w` wallpaper only renders reliably on one monitor, which is why **Lively is the recommended multi-monitor wallpaper** path. `GetWallpaperHost` ignores the leftover tiny WorkerW windows by size.
- **Screensaver launched from a hotkey would instantly dismiss itself** (the launching keystroke counts as input). Mitigated by an `armed` flag that ignores input for the first ~1 second.
- **Glyph set is half-width katakana only** (`U+FF66`–`U+FF9D`) plus digits/symbols — full-width katakana would break the monospaced grid.

## Conventions

- Commit only when asked; build artifacts (`*.scr`, `*.exe`, `*.pdb`) are git-ignored — never commit them.
- README.md documents the end-user install/build/uninstall flow.
