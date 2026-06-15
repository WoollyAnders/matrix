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

> **`./build.sh` is NOT a deploy.** It only emits the binaries to the **repo root** (git-ignored). The *live* lock screen is the separate **copy** at `%USERPROFILE%\Matrix\` — the registered `SCRNSAVE.EXE` and the `Ctrl+Alt+L` shortcut's `lock-now.ps1` both run **that** copy, not the repo build. So "I rebuilt" ≠ "it's live"; you must copy the two files over. From WSL the install dir is `/mnt/c/Users/<user>/Matrix/` — **verify it via that mount directly** (`ls`). A `powershell.exe -Command "...$env:USERPROFILE..."` check from bash can mis-expand the env var through the quoting and **false-negative** (report "not installed" when it is). The Lively wallpaper and the Android kiosk are **separate** deploys — neither updates from the lock-screen copy.

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

**Shared field, per-monitor windows.** The rain is one `RainField` spanning the **entire virtual desktop** (a grid of cells, each owning its own glyph index, brightness, and mirror flag). One `MatrixForm` window is created **per monitor** (`Screen.AllScreens`), each rendering only its slice of that shared field — so the rain is one continuous field across screens, and it works on irregular/mixed-DPI multi-monitor layouts where a single spanning window does not. `RainController` runs **one timer** that steps the field once and repaints every window. Glyphs are pre-rendered into **OPAQUE two-color tiles with the glow baked in** (uniform body + bright head, ± mirrored); each frame blits only the lit cells (`DrawImageUnscaled`, a fast memcpy) plus a transparent halo sprite on the few heads — not slow text layout or a full-frame bloom. **Render cost scales with lit-cell count × monitor count**, so the field must stay sparse (see the look/gotchas) or even opaque blits across 4 monitors blow the per-frame budget.

**Two engines kept in lock-step (plus a kiosk variant).** `wallpaper/matrix.html` is a JavaScript/canvas re-implementation of the *same* algorithm for Lively. **When you change the rain behavior or its tuning constants, change both `src/Matrix.cs` and `wallpaper/matrix.html`** so the lock screen and wallpaper stay identical. `wallpaper/matrix-kiosk.html` is a third copy of the same engine for the Android EPIC-Clock kiosk — keep its rain behavior in sync too, but it diverges on the GPU-sensitive bits (see gotchas).

**The look — no-fade, uniform-body teal rain.** Color is a 3-anchor ramp `COL_TAIL`→`COL_BODY`→`COL_HEAD` via `LevelColor(brightness)`: deep teal → blue-leaning Matrix green → near-white mint head (never pure white). There is **no comet-tail decay and no settle gradient**: the **body is uniform** — every lit cell holds at exactly `BODY_BRIGHT` — and only the **single head cell** of each falling stream is full-bright (re-asserted each step). A lit cell goes dark only when actively cleared; glyph changes are **instant** (no fade/dim) and there is **no flash**. Clearing: the **animated backspace** is the PRIMARY deletion — it begins at a *random* row of a lit line and eats characters one at a time **downward** through the rest of the line, at a per-event fraction of that column's head speed (`ERASE_SPEED_MIN/MAX_FRAC`, "at the head speed or slower"); alongside it a light constant `SEG_ERASE` punches small/medium *instant* gaps so streams don't pile up solid. There is **no whole-line or instant-column wipe** any more (the old `COLLAPSE` and `INTERRUPT` are gone). Every finished stream FREEZES and is then backspaced clear (`FREEZE_CHANCE = 1.0`), so no stream wraps leaving a permanent un-erased trail. **Density must stay bounded or it never plateaus** — the field fills, and (C# side) the render slows past budget. The levers: `RESTART_GAP` (dark rest before a column's next stream — bigger = sparser) and `ERASE_CHANCE` (how soon a standing line starts clearing); and critically `TOP_SPROUT_CHANCE`/`SPAWN_ON_ERASE` must stay **LOW** — at high rates they re-light frozen columns faster than the slow head-speed backspace can clear them, so the field saturates (this was a real bug). Glyph churn is mostly single-cell (`CHANGE_CHANCE`) but a `CHUNK_CHANCE` fraction spreads to a few contiguous neighbors that switch in sync; some cells also flip horizontally (`FLIP_RATE`).

**Tuning lives in constants.** The look is driven entirely by labeled constants — `RainField` in `src/Matrix.cs` and the `// tunables` block atop `wallpaper/matrix.html`. Groups: **color** (`COL_TAIL/COL_BODY/COL_HEAD`, `COL_HEAD_GLOW`); **uniform body** (`BODY_BRIGHT` — only the head sits above it, at 1.0); **motion/lifecycle** (`SPEED_MIN/MAX`, `RESTART_GAP`, `FREEZE_CHANCE`, `MID_FREEZE_CHANCE`, `MIN_FREEZE_LEN`); **churn** (`CHANGE_CHANCE`, `CHUNK_CHANCE/MIN/MAX`, `FLIP_RATE/FLIP_CHANCE`); **deletion** (`ERASE_CHANCE`, `ERASE_SPEED_MIN/MAX_FRAC` for the animated backspace; `SEG_ERASE_RATE` + the weighted small/medium `SEG_SMALL/MED_*` tiers + `SEG_SMALL_FRAC`; `SPAWN_ON_ERASE` sprouts, `TOP_SPROUT_CHANCE`); **glow** (`GLOW_STRENGTH`, `GLOW_RADIUS`, `HEAD_GLOW_RADIUS/STRENGTH`, `LEVELS`). **Keep the rain-behavior values byte-identical across the engines** (quick check: parse `NAME = value` from each and diff — currently **32 shared** between `src/Matrix.cs` and `wallpaper/matrix.html`, and **30** between the two HTML engines). The *glow technique* differs per engine, so glow params legitimately diverge: C# bakes a within-cell glow into the OPAQUE body tile and gives only heads an alpha halo (so `GLOW_STRENGTH`/`GLOW_RADIUS` are **canvas-only**, kept in C# just for parity); the canvas uses `filter:blur(GLOW_RADIUS)` + `'lighter'`; the kiosk's glow is a `shadowBlur` (px) and it has **no** dedicated head glow.

## Critical gotchas (each cost real debugging)

- **`.scr` files drop command-line args.** Launching `Matrix.scr /lock` via ShellExecute (e.g. `Start-Process`) runs it through the screensaver *file association*, which ignores extra args — it silently falls back to `/s` and never locks. The hotkey therefore launches **`Matrix.exe /lock`** (a plain exe receives args). This is why `Matrix.exe` exists.
- **Group Policy controls the screensaver on managed/domain machines.** A GPO under `HKCU\Software\Policies\...\Control Panel\Desktop` can set `ScreenSaveActive=0`, clear `SCRNSAVE.EXE`, and disable secure-resume — and re-applies on every policy refresh, wiping `install.ps1`'s settings. The **idle screensaver + its password-on-resume cannot be relied on there.** The `Ctrl+Alt+L` hotkey (direct `LockWorkStation()`) is GPO-independent and is the reliable lock.
- **Lock timing:** on dismiss in `/lock`, call `LockWorkStation()` and hold the process ~600 ms (a guarded one-shot timer) before `Application.Exit()` so Winlogon can switch to the secure desktop before teardown.
- **Multi-monitor wallpaper / WorkerW is version-specific.** On this Win11 build there is **no full-screen WorkerW** — the desktop is hosted on `Progman` (which `GetWallpaperHost` falls back to). The native `/w` wallpaper only renders reliably on one monitor, which is why **Lively is the recommended multi-monitor wallpaper** path. `GetWallpaperHost` ignores the leftover tiny WorkerW windows by size.
- **Screensaver launched from a hotkey would instantly dismiss itself** (the launching keystroke counts as input). Mitigated by an `armed` flag that ignores input for the first ~1 second.
- **Glyph set is half-width katakana only** (`U+FF66`–`U+FF9D`) plus digits/symbols — full-width katakana would break the monospaced grid.
- **C# glow = opaque baked tiles + a head-only alpha halo (GDI+ alpha-blending is slow).** Two earlier C# glow designs were both too slow on 4 monitors: (1) the original per-frame **bloom** did two full-frame resamples per window AND re-blit *opaque* glyph tiles over the blur, which punched a black `CellW×CellH` box into the surrounding haze; (2) a per-cell **alpha halo sprite** on every lit cell — alpha-blending ~14×-cell-area sprites × thousands of cells × 4 monitors ran 100ms+/frame. The fix: bake a soft **within-cell** glow INTO the OPAQUE body/head tile (the blurred glyph laid dim under the crisp glyph) so the per-frame blit is a fast **memcpy**, not an alpha blend; opaque black-bg tiles meet black-to-black so there's no haze to box; and only the few **heads/sprouts** get a real cross-cell **alpha** halo (`COL_HEAD_GLOW`, drawn last so nothing overwrites it). Bake all sprites **premultiplied** (`Format32bppPArgb` + `HighQualityBilinear` + `PixelOffsetMode.Half`) or the shrink/grow blur leaves a dark fringe. Measured: this holds ~20 ms/frame on 4×1080p (vs 100ms+ before) **as long as density stays bounded**. The canvas keeps its own technique (self-`drawImage` `filter:blur(GLOW_RADIUS)` + `'lighter'` bloom + build-time `shadowBlur` head sprites) — its GPU compositing makes the per-cell cost a non-issue there.
- **Head-glow pitfalls (each forced a redo).** (a) NEVER `shadowBlur` per-head per-frame — it tanks FPS; bake the halo once and blit. (b) Key the head halo to the column's **head POSITION** (`head[c]`)/sprout — ONE per active head — so it glides smoothly by the fractional row. (Historically, keying off a per-cell brightness *threshold* strobed because a head cell only crossed ~1.0 for a single frame; the head is now HELD at 1.0 every step, but position-keying is still what gives the smooth glide and exactly one halo per head.) (c) The **kiosk gets NO dedicated head glow**: a texture-blit halo sprite risks its GPU (the documented atlas-crash) and a per-head `shadowBlur` is too slow — it relies solely on its per-glyph `shadowBlur` general glow.

## Conventions

- Commit only when asked; build artifacts (`*.scr`, `*.exe`, `*.pdb`) are git-ignored — never commit them.
- README.md documents the end-user install/build/uninstall flow.
