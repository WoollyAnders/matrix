#!/usr/bin/env bash
# Compile the Matrix screensaver with the C# compiler that ships inside Windows
# (.NET Framework's csc.exe) -- nothing extra to install. Run from WSL.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$HERE/src/Matrix.cs"
OUT="$HERE/Matrix.scr"

CSC="/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$CSC" ] || CSC="/mnt/c/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe"
[ -x "$CSC" ] || { echo "csc.exe not found under C:\\Windows\\Microsoft.NET" >&2; exit 1; }

# csc.exe is a Windows program: hand it Windows-style paths.
SRC_WIN="$(wslpath -w "$SRC")"
OUT_WIN="$(wslpath -w "$OUT")"

"$CSC" /nologo /target:winexe /optimize+ \
  /out:"$OUT_WIN" \
  /reference:System.dll \
  /reference:System.Drawing.dll \
  /reference:System.Windows.Forms.dll \
  "$SRC_WIN"

echo "Built: $OUT"

# Same binary, named so it runs as the live wallpaper by default and shows a
# sensible name in Task Manager.
WALL="$HERE/MatrixWallpaper.exe"
cp -f "$OUT" "$WALL"
echo "Built: $WALL"

# Same binary as a .exe for the lock hotkey: launching the .scr via ShellExecute
# drops custom args (so /lock is lost), but a .exe receives them normally.
EXE="$HERE/Matrix.exe"
cp -f "$OUT" "$EXE"
echo "Built: $EXE"
