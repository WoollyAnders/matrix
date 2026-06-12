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

**Two engines kept in lock-step (plus a kiosk variant).** `wallpaper/matrix.html` is a JavaScript/canvas re-implementation of the *same* algorithm for Lively. **When you change the rain behavior or its tuning constants, change both `src/Matrix.cs` and `wallpaper/matrix.html`** so the lock screen and wallpaper stay identical. `wallpaper/matrix-kiosk.html` is a third copy of the same engine for the Android EPIC-Clock kiosk — keep its rain behavior in sync too, but it diverges on the GPU-sensitive bits (see gotchas).

**The look — no-fade teal rain.** Color is a 3-anchor ramp `COL_TAIL`→`COL_BODY`→`COL_HEAD` via `LevelColor(brightness)`: deep teal → blue-leaning Matrix green → near-white mint head (never pure white). There is **no comet-tail decay** (the old `DECAY` is gone): a freshly-lit head is full-bright, SETTLES over a few frames (`HEAD_SETTLE`) down to a steady `BODY_BRIGHT`, then HOLDS — a lit cell only goes dark when actively cleared. Clearing, most→least frequent: the **backspace COLLAPSE** (a frozen/standing line deleted from its TOP/trailing end downward, leaving the head end last — the PRIMARY deletion), `SEG_ERASE` gap-punches (constant light "editing" so streams don't pile up solid), and the rare instant `INTERRUPT` wipe. Only **frozen** columns can be collapsed, so `FREEZE_CHANCE` gates how much gets backspaced; `RESTART_GAP` is how long a finished column RESTS (dark, headless) before its next stream — the main lever for head count / field sparsity. Glyph churn is mostly single-cell (`CHANGE_CHANCE`) but a `CHUNK_CHANCE` fraction spreads to a few contiguous neighbors that switch in sync.

**Tuning lives in constants.** The look is driven entirely by labeled constants — `RainField` in `src/Matrix.cs` and the `// tunables` block atop `wallpaper/matrix.html`. Groups: **color** (`COL_TAIL/COL_BODY/COL_HEAD`, `COL_HEAD_GLOW`); **no-fade** (`BODY_BRIGHT`, `HEAD_SETTLE`, `STAY_BRIGHT`); **motion/lifecycle** (`SPEED_MIN/MAX`, `RESTART_GAP`, `FREEZE_CHANCE`, `MID_FREEZE_CHANCE`, `MIN_FREEZE_LEN`); **churn** (`CHANGE_CHANCE`, `MORPH_STEP`, `MORPH_DIP`, `GLITCH_RATE`, `CHUNK_CHANCE/MIN/MAX`, `FLASH_BOOST/DECAY`, `FLIP_RATE/FLIP_CHANCE`); **deletion** (`COLLAPSE_CHANCE/FULL_FRAC/MIN/MAX/SPEED`, `SEG_ERASE_RATE` + weighted `SEG_SMALL/MED/LARGE_FRAC`/`_MIN`/`_MAX` tiers, `INTERRUPT_CHANCE`, `SPAWN_ON_ERASE` sprouts, `TOP_SPROUT_CHANCE`); **glow** (`GLOW_STRENGTH`, `GLOW_RADIUS`, `HEAD_GLOW_RADIUS/STRENGTH`, `LEVELS`). **Keep the rain-behavior values byte-identical between the two files** (quick check: parse `NAME = value` from each and diff — currently 45 shared). The *glow technique* differs per engine, so glow params legitimately diverge (C# has no `GLOW_RADIUS` — it uses a downscale factor; the kiosk's `GLOW_RADIUS` is a `shadowBlur` px).

## Critical gotchas (each cost real debugging)

- **`.scr` files drop command-line args.** Launching `Matrix.scr /lock` via ShellExecute (e.g. `Start-Process`) runs it through the screensaver *file association*, which ignores extra args — it silently falls back to `/s` and never locks. The hotkey therefore launches **`Matrix.exe /lock`** (a plain exe receives args). This is why `Matrix.exe` exists.
- **Group Policy controls the screensaver on managed/domain machines.** A GPO under `HKCU\Software\Policies\...\Control Panel\Desktop` can set `ScreenSaveActive=0`, clear `SCRNSAVE.EXE`, and disable secure-resume — and re-applies on every policy refresh, wiping `install.ps1`'s settings. The **idle screensaver + its password-on-resume cannot be relied on there.** The `Ctrl+Alt+L` hotkey (direct `LockWorkStation()`) is GPO-independent and is the reliable lock.
- **Lock timing:** on dismiss in `/lock`, call `LockWorkStation()` and hold the process ~600 ms (a guarded one-shot timer) before `Application.Exit()` so Winlogon can switch to the secure desktop before teardown.
- **Multi-monitor wallpaper / WorkerW is version-specific.** On this Win11 build there is **no full-screen WorkerW** — the desktop is hosted on `Progman` (which `GetWallpaperHost` falls back to). The native `/w` wallpaper only renders reliably on one monitor, which is why **Lively is the recommended multi-monitor wallpaper** path. `GetWallpaperHost` ignores the leftover tiny WorkerW windows by size.
- **Screensaver launched from a hotkey would instantly dismiss itself** (the launching keystroke counts as input). Mitigated by an `armed` flag that ignores input for the first ~1 second.
- **Glyph set is half-width katakana only** (`U+FF66`–`U+FF9D`) plus digits/symbols — full-width katakana would break the monospaced grid.
- **Glow is two layers, and glyph tiles are opaque black-bg — so glow can't bleed across cells for free.** (1) A **general bloom** gives *every* glyph (the body too) its glow: C# blurs a shrunk copy of the frame and lays it back over at `GLOW_STRENGTH`, then RE-BLITS the crisp glyphs on top; the canvas does the same via a self-`drawImage` with `filter:blur(GLOW_RADIUS)` + `'lighter'`. Keep the blur **tight** (~half a cell) — a wide blur reads as a flat regional haze, not a per-glyph glow. (2) A **dedicated head glow** is **GLYPH-SHAPED, not a circle**: per-glyph sprites (the glyph + a soft halo in `COL_HEAD_GLOW` teal) are **baked once** (canvas: build-time `shadowBlur`; C#: `DrawString` + shrink/grow blur into `glowCache`) and blitted at the head's cell, crisp glyph re-blit on top.
- **Head-glow pitfalls (each forced a redo).** (a) NEVER `shadowBlur` per-head per-frame — it tanks FPS; bake the halo once and blit. (b) Key the head glow to the column's **head POSITION** (`head[c]`)/sprout — ONE per active column. Keying off a per-cell *brightness threshold* STROBES (a head cell only crosses ~1.0 for a single frame). (c) The **kiosk gets NO dedicated head glow**: a texture-blit halo sprite risks its GPU (the documented atlas-crash) and a per-head `shadowBlur` is too slow — it relies solely on its per-glyph `shadowBlur` general glow.

## Conventions

- Commit only when asked; build artifacts (`*.scr`, `*.exe`, `*.pdb`) are git-ignored — never commit them.
- README.md documents the end-user install/build/uninstall flow.
