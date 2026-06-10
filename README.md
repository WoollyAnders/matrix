# matrix

Classic green katakana "digital rain" from *The Matrix*, for Windows — as a
**screensaver**, an on-demand **lock-with-rain hotkey**, and a live animated
**desktop wallpaper**.

The native pieces are a tiny C# binary built with the compiler that already
ships inside Windows, so there's **nothing to install** for them (no .NET SDK,
no runtime). The rain is a single continuous field across all monitors — one
unbroken cascade, not a separate copy per screen.

| Piece | What it is | How |
|------|------------|-----|
| **Screensaver** | rain on idle, password on resume | `Matrix.scr` + `scripts/install.ps1` |
| **Lock with Matrix** | hotkey → rain now, password on return | `Ctrl+Alt+L` + `scripts/install-lock-shortcut.ps1` |
| **Live wallpaper** | animated rain behind the desktop | `wallpaper/matrix.html` via [Lively](https://www.rocksdanister.com/lively/) |

> Why a screensaver and a hotkey instead of "draw on the Win+L lock screen"?
> Windows renders the real lock screen on an isolated *secure desktop* that no
> app can draw on. The screensaver (with password-on-resume) and the lock hotkey
> are the supported ways to get the effect.

## Build

From WSL (or any shell that can reach `csc.exe`):

```bash
./build.sh
```

Compiles [src/Matrix.cs](src/Matrix.cs) with the in-box .NET Framework compiler
into `Matrix.scr` (screensaver) and `MatrixWallpaper.exe` (single-monitor native
wallpaper — see note below) in the repo root.

## 1. Screensaver

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

Registers it for the current user (no admin) with a **5-minute idle timeout** and
**password on resume**:

- change the timeout: `-TimeoutSeconds 120`
- disable the password prompt: `-Secure $false`

Preview it now (move mouse / press a key to exit):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test.ps1
```

## 2. Lock with Matrix (Ctrl+Alt+L)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-lock-shortcut.ps1
```

Creates a **"Lock with Matrix"** shortcut on your Desktop with the global hotkey
**`Ctrl+Alt+L`** (change with `-Hotkey "CTRL+SHIFT+L"`). Press it when you step
away: the rain starts immediately and cascades down from the top; when you return
and move the mouse / press a key it **locks the workstation** (password required).

It launches the screensaver directly in `/lock` mode rather than through Windows'
screensaver path — otherwise the launching keystroke would dismiss it before the
first frame.

> `Win+L` itself still shows Windows' own secure screen (no app can override
> that). This hotkey is how you get rain-on-demand.

## 3. Live wallpaper (Lively)

A single spanned canvas reliably across multiple monitors is best handled by
[**Lively Wallpaper**](https://www.rocksdanister.com/lively/) (free, open source),
which solves the Win11 multi-monitor wallpaper hosting that the native trick
can't do cleanly.

```powershell
winget install rocksdanister.LivelyWallpaper
```

Then in Lively:

1. **Settings → multiple-displays / arrangement → "Span single wallpaper across
   all screens."**
2. **Add Wallpaper → Browse** to [wallpaper/matrix.html](wallpaper/matrix.html)
   (copy it somewhere local first), and apply.

With span, the rain is one continuous field across the whole virtual desktop:
drops fall off the bottom of a higher monitor and continue into the one below.

The look is tunable via constants at the top of `matrix.html`:

| Constant | Effect |
|---|---|
| `FRAME_MS` | higher = slower |
| `SPEED_MIN/MAX` | fall speed |
| `DECAY` | higher = longer/denser tails |
| `GLITCH_RATE` / `GLITCH_GLOW` | how often glyphs flip, and how bright they re-glow |

> **Native single-monitor option:** `MatrixWallpaper.exe` (built by `build.sh`)
> draws the rain behind the desktop icons via the WorkerW technique and is
> managed by `scripts/wallpaper.ps1` (`install` / `start` / `stop` / `uninstall`).
> It only renders reliably on a single monitor on Win11, which is why Lively is
> recommended for multi-monitor setups.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
```

Removes the screensaver registration, the desktop lock shortcut, the native
wallpaper + its autostart, and the installed files in `%USERPROFILE%\Matrix`. For
the Lively wallpaper, remove it in Lively (and `winget uninstall` Lively if you
no longer want it).

## How it works

Same binary, several modes (the mode is inferred from the exe name when no flag
is given — `Matrix*Wallpaper*.exe` → `/w`, otherwise `/s`):

| Args | Behavior |
|------|----------|
| `/s` | full-screen on every monitor, exits on input |
| `/lock` | like `/s`, but locks the workstation when dismissed |
| `/p <hwnd>` | live preview inside the Screen Saver settings dialog |
| `/c` | minimal config dialog (nothing to configure) |
| `/t` | windowed render test (ignores input) |
| `/w` | live wallpaper parented to the desktop's WorkerW (single-monitor) |

The rain is a shared **`RainField`**: one grid spanning the whole virtual desktop
where each cell owns its own glyph and brightness. A head lights cells at full
brightness as it falls; the trail behind it is the **same** glyphs fading out;
random cells occasionally flip glyph and re-glow (the authentic churn). One window
per monitor renders its slice of that field, so the rain is continuous across
screens. Glyphs are pre-rendered per brightness level into a small bitmap cache,
so each frame is a fast blit rather than slow text drawing. The same algorithm is
mirrored in [wallpaper/matrix.html](wallpaper/matrix.html) for Lively.

## Notes

- On a managed/domain PC, **Group Policy can override** screensaver settings.
- Built with the in-box `csc.exe` under `C:\Windows\Microsoft.NET\Framework64\`
  — no .NET SDK required.
