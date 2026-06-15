// Matrix - classic green katakana "digital rain".
//
// One binary, built with the in-box .NET Framework compiler (csc.exe, C# 5),
// so nothing needs to be installed. Modes:
//
//   Screensaver (Matrix.scr)
//     /s            one window per monitor, exits on input
//     /p <hwnd>     preview inside the Screen Saver settings dialog
//     /c            minimal config dialog (nothing to configure)
//     /t            windowed render test (on-top, ignores input)
//
//   Wallpaper (MatrixWallpaper.exe)
//     /w            one window per monitor parented to the desktop's WorkerW
//
// All monitors share ONE RainField, so the rain is a single continuous field
// across the whole virtual desktop -- each window just paints its own slice.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MatrixScreensaver
{
    enum RunMode { Screensaver, Preview, Wallpaper, Windowed }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        struct RECT { public int Left, Top, Right, Bottom; }

        static System.Threading.Mutex wallpaperMutex;

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string mode = null;
            IntPtr previewHandle = IntPtr.Zero;

            if (args.Length > 0)
            {
                string raw = args[0].Trim().ToLowerInvariant();
                string flag = raw.Length >= 2 ? raw.Substring(0, 2) : raw;
                mode = flag;
                if (flag == "/p")
                {
                    string h = null;
                    if (raw.Length > 3 && raw[2] == ':') h = raw.Substring(3);
                    else if (args.Length > 1) h = args[1].Trim();
                    long parsed;
                    if (h != null && long.TryParse(h, out parsed)) previewHandle = new IntPtr(parsed);
                }
            }

            if (mode == null)
            {
                string exe = "";
                try { exe = Path.GetFileNameWithoutExtension(Application.ExecutablePath); }
                catch { }
                mode = (exe.IndexOf("Wallpaper", StringComparison.OrdinalIgnoreCase) >= 0) ? "/w" : "/s";
            }

            switch (mode)
            {
                case "/c":
                    MessageBox.Show("Matrix - classic green code rain.\n\nThere is nothing to configure.",
                        "Matrix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                case "/p": if (previewHandle != IntPtr.Zero) RunPreview(previewHandle); return;
                case "/w": RunWallpaper(); return;
                case "/t": RunWindowedTest(); return;
                case "/l": RunScreensaver(true); return;   // lock-on-dismiss (used by the hotkey)
                default: RunScreensaver(false); return;    // "/s"
            }
        }

        static int FontPxFor(int heightPx) { return Math.Max(14, heightPx / 55); }

        static void RunScreensaver(bool lockOnExit)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            RainField field = new RainField(vs.Width, vs.Height, FontPxFor(SystemInformation.PrimaryMonitorSize.Height));
            List<MatrixForm> windows = new List<MatrixForm>();
            foreach (Screen s in Screen.AllScreens)
                windows.Add(MatrixForm.Screensaver(field, s.Bounds, vs.Location, lockOnExit));
            Application.Run(new RainController(field, windows, 50, lockOnExit));
        }

        static void RunWallpaper()
        {
            bool createdNew;
            wallpaperMutex = new System.Threading.Mutex(true, "MatrixWallpaperSingleton", out createdNew);
            if (!createdNew) return;

            IntPtr host = GetWallpaperHost();
            if (host == IntPtr.Zero)
            {
                MessageBox.Show("Could not find the desktop wallpaper window (is Explorer running?).",
                    "Matrix Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Rectangle vs = SystemInformation.VirtualScreen;
            RainField field = new RainField(vs.Width, vs.Height, FontPxFor(SystemInformation.PrimaryMonitorSize.Height));
            List<MatrixForm> windows = new List<MatrixForm>();
            foreach (Screen s in Screen.AllScreens)
            {
                // Position relative to the host window (its origin is the virtual-screen top-left).
                Rectangle child = new Rectangle(s.Bounds.X - vs.X, s.Bounds.Y - vs.Y, s.Bounds.Width, s.Bounds.Height);
                windows.Add(MatrixForm.Wallpaper(field, host, child));
            }
            Application.Run(new RainController(field, windows, 60));
        }

        static void RunPreview(IntPtr parent)
        {
            RECT r;
            int w = 240, h = 180;
            if (GetClientRect(parent, out r)) { w = Math.Max(1, r.Right - r.Left); h = Math.Max(1, r.Bottom - r.Top); }
            RainField field = new RainField(w, h, Math.Max(8, h / 12));
            List<MatrixForm> windows = new List<MatrixForm>();
            windows.Add(MatrixForm.Preview(field, parent, new Size(w, h)));
            Application.Run(new RainController(field, windows, 70));
        }

        static void RunWindowedTest()
        {
            Size sz = new Size(900, 600);
            RainField field = new RainField(sz.Width, sz.Height, FontPxFor(sz.Height));
            List<MatrixForm> windows = new List<MatrixForm>();
            windows.Add(MatrixForm.Windowed(field, sz));
            Application.Run(new RainController(field, windows, 50));
        }

        // Find the window that hosts the desktop wallpaper, so we can parent our
        // rain windows behind the icons. Windows versions differ a lot here, so
        // we try several strategies and fall back to Progman.
        static IntPtr GetWallpaperHost()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr dummy;
            // Nudge the shell into creating the WorkerW that sits behind the icons.
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out dummy);
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out dummy);

            // A usable host must actually span (most of) the desktop -- on some
            // builds there are leftover tiny WorkerW windows we must ignore.
            int minWidth = SystemInformation.PrimaryMonitorSize.Width;

            // Strategy 1 (Win10 classic): the desktop-sized WorkerW sibling that
            // sits *after* the window hosting SHELLDLL_DefView.
            IntPtr workerw = IntPtr.Zero;
            EnumWindows(delegate (IntPtr top, IntPtr lp)
            {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    IntPtr w = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    if (w != IntPtr.Zero && IsBigEnough(w, minWidth)) workerw = w;
                }
                return true;
            }, IntPtr.Zero);

            // Strategy 2: a desktop-sized WorkerW that is a child of Progman.
            if (workerw == IntPtr.Zero)
            {
                IntPtr w = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                if (w != IntPtr.Zero && IsBigEnough(w, minWidth)) workerw = w;
            }

            // Strategy 3 (this Win11 build): no real WorkerW -- the desktop lives
            // on Progman itself, which spans the whole virtual screen.
            return workerw != IntPtr.Zero ? workerw : progman;
        }

        static bool IsBigEnough(IntPtr hWnd, int minWidth)
        {
            RECT r;
            return GetWindowRect(hWnd, out r) && (r.Right - r.Left) >= minWidth;
        }
    }

    // Shared simulation across the whole virtual desktop. Each CELL owns its own
    // glyph + brightness: the head is bright, the trail is the SAME glyphs fading
    // (no two-character overlap), and random cells flip + glow (authentic churn).
    // Windows render their own slice, so every monitor shows one continuous field.
    // Mirrors wallpaper/matrix.html.
    class RainField
    {
        // NO FADE, UNIFORM BODY: a lit cell is not a comet-tail. The single head cell is
        // full-bright; every other lit cell holds at the SAME steady body brightness (no
        // settle gradient, no flash, no dim) -- so only the head stands out. Cells go dark
        // only when erased/backspaced, never by fading.
        const float BODY_BRIGHT = 0.62f;     // steady green EVERY lit body cell holds at (== the color-ramp split); the head cell alone sits brighter at 1.0
        // Green-blue (teal-leaning) ramp with a near-white -- not pure white -- head.
        static readonly int[] COL_TAIL = { 0, 25, 18 };    // deep teal, only seen briefly as a cell clears
        static readonly int[] COL_BODY = { 0, 235, 140 };  // the steady Matrix green (blue-leaning vs the old 0,255,70)
        static readonly int[] COL_HEAD = { 235, 255, 250 };// near-white mint head (bright, but not pure RGB 255,255,255)
        public static readonly int[] COL_HEAD_GLOW = { 160, 250, 240 }; // head-GLOW color -- near-white TEAL (not pure white); used by MatrixForm's head halo
        const float SPEED_MIN = 0.30f, SPEED_MAX = 0.70f; // rows per step
        const double CHANGE_CHANCE = 0.04;   // per-step chance a lit glyph INSTANTLY swaps to another (constant churn; no fade/dim now)
        // Group change: most swaps are a single glyph, but a triggered change sometimes takes a few
        // CONTIGUOUS neighbors with it, so a small block switches at the same instant (not chunk-ONLY).
        const double CHUNK_CHANCE = 0.15;    // share of triggered changes that become a group instead of a single
        const int CHUNK_MIN = 2, CHUNK_MAX = 6; // group length (rows) when a change spreads
        const double FLIP_RATE = 0.0015;     // fraction of cells mirrored horizontally per step
        const float FLIP_CHANCE = 0.28f;     // chance a freshly-lit glyph spawns mirrored
        // ANIMATED BACKSPACE (primary deletion): an erase can start ANYWHERE on a lit line and
        // eats characters one at a time DOWNWARD, at a fraction of the column's head speed
        // ("at the head speed or slower"). Replaces the old whole-line collapse and the instant
        // whole-column INTERRUPT. The small/medium instant SEG_ERASE gaps below still run too.
        const double ERASE_CHANCE = 0.05;    // per-step chance a column with lit cells begins a new animated backspace (the main lever for how long standing code lingers)
        const float ERASE_SPEED_MIN_FRAC = 0.7f; // slowest backspace = this fraction of the column's head speed
        const float ERASE_SPEED_MAX_FRAC = 1.0f; // fastest backspace = head speed (never faster)
        // Instant gap "editing": each erase punches a small/medium gap into a lit stream --
        // a few stray characters or a mid-size segment (the old long-chunk tier is gone).
        const double SEG_ERASE_RATE = 0.12;  // instant-erase attempts per step -- constant small "editing" of every line so lines don't pile up solid
        const double SEG_SMALL_FRAC = 0.72;  // share of instant erases that nibble just a few chars (rest = a mid-size segment)
        const int SEG_SMALL_MIN = 1, SEG_SMALL_MAX = 3;
        const int SEG_MED_MIN = 5, SEG_MED_MAX = 18;
        const double SPAWN_ON_ERASE = 0.08;  // chance an erase also seeds a NEW falling head at the gap (a little regrowth variety)
        const double TOP_SPROUT_CHANCE = 0.02; // per-step chance to drop a fresh head on a bare-topped frozen column -- kept RARE: at higher rates it re-lights columns faster than the backspace can clear them, so the field fills up
        const int RESTART_GAP = 110;         // how far above the top a finished column restarts -- larger = columns REST (dark, headless) longer between streams, so fewer heads fall at once and the field is less crowded
        const double FREEZE_CHANCE = 1.0;    // a head reaching the bottom with a long-enough line ALWAYS freezes (then gets backspaced) -- so no stream wraps leaving a permanent un-erased trail behind
        const double MID_FREEZE_CHANCE = 0.002; // per-step chance a still-FALLING line stops mid-screen and freezes in place
        const int MIN_FREEZE_LEN = 10;       // a line must have drawn at least this many chars before it may freeze (no 1-char freezes)
        const int LEVELS = 48;               // brightness quantization shared with wallpaper/matrix.html's atlas (the C# tile cache no longer uses it -- only two colors render now)
        // Eerie glow -- engine-divergent technique (GDI+ alpha-blending is slow, so the body can't
        // afford a per-cell alpha halo). C# bakes a soft within-cell glow INTO the OPAQUE body tile
        // (blits as a fast memcpy), and gives only the few HEADS a real cross-cell alpha halo.
        // GLOW_STRENGTH is kept only for parity with wallpaper/matrix.html (the canvas bloom opacity);
        // it is NOT used by the C# render (the body glow is baked structurally, not at this opacity).
        public const float GLOW_STRENGTH = 0.95f; // canvas-only (kept for shared-constant parity)
        // Dedicated HEAD glow: the body teal is nearly as luminous as the near-white head, so the
        // baked body glow can't make the head stand out. A brighter near-white halo is baked per
        // glyph and blitted on each actively-falling head/sprout, positioned by its FRACTIONAL row
        // so it glides (never strobes).
        public const float HEAD_GLOW_RADIUS = 0.95f; // head-halo radius in CELLS -- tight, wraps the character
        public const float HEAD_GLOW_STRENGTH = 1.0f;// head-halo intensity (0 disables)

        public readonly Font Font;
        public readonly int CellW, CellH, Cols, Rows;
        public readonly float[] Bright;
        public readonly int[] Chars;         // glyph index per cell
        public readonly bool[] Flip;         // drawn horizontally mirrored?

        readonly float[] head;
        readonly float[] speed;
        readonly int[] prevRow;
        readonly bool[] frozen;              // true = column has frozen into static code (set on reaching bottom)
        readonly int[] lit;                  // length of each column's current falling line (gates freezing)
        // Animated backspace: per-column eraser that eats characters downward one at a time.
        readonly bool[] eraseActive;         // true = a backspace is in progress in this column
        readonly float[] erasePos;           // fractional row the backspace is eating at (advances downward)
        readonly int[] eraseRemaining;       // characters still to remove in the current backspace
        readonly float[] eraseSpeed;         // rows/step for this backspace (a fraction of the column's head speed)
        // Secondary "regrowth" heads, spawned where code was just erased: they fall and
        // re-write the gap. A third way the rain is built, besides wrap-around restart
        // (head past the bottom) and the top-sprout refill.
        readonly List<Sprout> sprouts = new List<Sprout>();
        sealed class Sprout { public int Col; public float Pos; public float Speed; public int Prev; }
        readonly Random rnd = new Random();
        // OPAQUE cell tiles with a soft within-cell glow baked in under the crisp glyph, so each
        // frame is a fast memcpy blit (no per-pixel alpha) -- the speed win. Only two colors ever
        // render now (uniform body, bright head). Opaque + black bg means tiles meet black-to-black,
        // so the baked glow can't show a box edge; heads get an extra cross-cell halo on top.
        readonly Bitmap[] glyphBody, glyphBodyF;   // body color (COL_BODY) + within-cell glow; normal + mirrored
        readonly Bitmap[] glyphHead, glyphHeadF;   // head color (COL_HEAD) + within-cell glow; normal + mirrored
        // Baked GLYPH-SHAPED head halo (transparent, soft, oversized) -- blitted only on the few
        // heads/sprouts (alpha is slow, so NOT on every body cell).
        readonly Bitmap[] headGlow, headGlowF;
        readonly int glowPad;                // halo padding around the glyph in the head-glow sprite

        static readonly char[] Glyphs = BuildGlyphs();
        static char[] BuildGlyphs()
        {
            List<char> g = new List<char>();
            for (int c = 0xFF66; c <= 0xFF9D; c++) g.Add((char)c); // half-width katakana
            foreach (char ch in "0123456789*+-/<>=:;.?!^|()[]{}~") g.Add(ch);
            return g.ToArray();
        }

        int GlyphIdx() { return rnd.Next(Glyphs.Length); }
        float RandSpeed() { return SPEED_MIN + (float)rnd.NextDouble() * (SPEED_MAX - SPEED_MIN); }

        // Color ramp from a brightness in [0,1]: TAIL -> BODY below the body floor, BODY ->
        // near-white HEAD above it. Split at BODY_BRIGHT so a held body cell renders as COL_BODY.
        static Color LevelColor(float bb)
        {
            int[] a, b; float t;
            if (bb >= BODY_BRIGHT) { a = COL_BODY; b = COL_HEAD; t = (bb - BODY_BRIGHT) / (1f - BODY_BRIGHT); }
            else { a = COL_TAIL; b = COL_BODY; t = bb / BODY_BRIGHT; }
            return Color.FromArgb(
                (int)(a[0] + (b[0] - a[0]) * t),
                (int)(a[1] + (b[1] - a[1]) * t),
                (int)(a[2] + (b[2] - a[2]) * t));
        }

        // Weighted erase length: usually just a few characters, sometimes a mid-size segment.
        // (The old long-chunk tier is gone -- big removals are no longer instant.)
        int SegLen()
        {
            if (rnd.NextDouble() < SEG_SMALL_FRAC) return SEG_SMALL_MIN + rnd.Next(SEG_SMALL_MAX - SEG_SMALL_MIN + 1);
            return SEG_MED_MIN + rnd.Next(SEG_MED_MAX - SEG_MED_MIN + 1);
        }

        public RainField(int widthPx, int heightPx, int fontPx)
        {
            Font = new Font("MS Gothic", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b))
            {
                SizeF s = g.MeasureString(((char)0xFF74).ToString(), Font, PointF.Empty, StringFormat.GenericTypographic);
                CellW = Math.Max(6, (int)Math.Round(s.Width));
                CellH = Math.Max(8, (int)Math.Round(Font.GetHeight(g)));
            }
            Cols = Math.Max(1, widthPx / CellW);
            Rows = Math.Max(1, heightPx / CellH + 1);

            Bright = new float[Cols * Rows];
            Chars = new int[Cols * Rows];
            Flip = new bool[Cols * Rows];
            head = new float[Cols];
            speed = new float[Cols];
            prevRow = new int[Cols];
            frozen = new bool[Cols];
            lit = new int[Cols];
            eraseActive = new bool[Cols];
            erasePos = new float[Cols];
            eraseRemaining = new int[Cols];
            eraseSpeed = new float[Cols];
            for (int c = 0; c < Cols; c++)
            {
                // Start every column above the top (staggered) so the rain cascades
                // in from the top downward when the saver starts.
                head[c] = -(float)(rnd.NextDouble() * Rows);
                speed[c] = RandSpeed();
                prevRow[c] = (int)Math.Floor(head[c]);
            }

            // OPAQUE cell tiles (glow baked in, below) -- blitted as a fast memcpy. Body cells are
            // uniform and the head is full-bright, so only two colors ever render.
            glyphBody = BuildGlowTiles(COL_BODY, false);
            glyphBodyF = BuildGlowTiles(COL_BODY, true);
            glyphHead = BuildGlowTiles(COL_HEAD, false);
            glyphHeadF = BuildGlowTiles(COL_HEAD, true);

            // The dedicated HEAD halo (transparent, oversized, blurred) -- blitted only on heads.
            glowPad = Math.Max(3, (int)(CellH * HEAD_GLOW_RADIUS));
            BuildGlowSprites(COL_HEAD_GLOW, HEAD_GLOW_STRENGTH, out headGlow, out headGlowF);
        }

        // One OPAQUE cell tile per glyph: a soft within-cell glow (a blurred copy of the glyph laid
        // dim over black) under the crisp glyph. Opaque so the per-frame blit is a memcpy, not a
        // per-pixel alpha blend; black bg so neighboring tiles meet black-to-black (no box edge).
        Bitmap[] BuildGlowTiles(int[] rgb, bool flipped)
        {
            Bitmap[] tiles = new Bitmap[Glyphs.Length];
            Color col = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
            int sw = Math.Max(1, CellW / 2), sh = Math.Max(1, CellH / 2);   // shrink target -> the blur amount
            for (int gi = 0; gi < Glyphs.Length; gi++)
            {
                Bitmap bm = new Bitmap(CellW, CellH, PixelFormat.Format24bppRgb);   // OPAQUE
                using (Graphics g = Graphics.FromImage(bm))
                {
                    g.Clear(Color.Black);
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    // 1) soft glow: blur the glyph (shrink+grow) and lay it -- its partial alpha over
                    //    black reads as a dim halo, kept inside the cell so tiles stay seamless.
                    using (Bitmap tmp = new Bitmap(CellW, CellH, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics gt = Graphics.FromImage(tmp))
                        {
                            gt.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (SolidBrush br = new SolidBrush(col))
                                gt.DrawString(Glyphs[gi].ToString(), Font, br, 0, 0, StringFormat.GenericTypographic);
                        }
                        using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
                        {
                            using (Graphics gs = Graphics.FromImage(small))
                            {
                                gs.CompositingQuality = CompositingQuality.HighQuality;
                                gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
                                gs.PixelOffsetMode = PixelOffsetMode.Half;
                                gs.DrawImage(tmp, 0, 0, sw, sh);
                            }
                            g.DrawImage(small, new Rectangle(0, 0, CellW, CellH), 0, 0, sw, sh, GraphicsUnit.Pixel);
                        }
                    }
                    // 2) crisp glyph on top
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (SolidBrush br = new SolidBrush(col))
                        g.DrawString(Glyphs[gi].ToString(), Font, br, 0, 0, StringFormat.GenericTypographic);
                }
                if (flipped) bm.RotateFlip(RotateFlipType.RotateNoneFlipX);
                tiles[gi] = bm;
            }
            return tiles;
        }

        // One baked halo sprite per glyph: the glyph drawn in the glow color, then softened by a
        // shrink+grow blur and layered for strength. Premultiplied ARGB + high-quality bilinear
        // keeps the transparent black from bleeding a dark fringe into the (dim) halo edges.
        void BuildGlowSprites(int[] rgb, float strength, out Bitmap[] normal, out Bitmap[] flipped)
        {
            int gw = CellW + glowPad * 2, gh = CellH + glowPad * 2;
            int gd = Math.Max(2, glowPad / 2);                       // blur amount (shrink factor)
            int gsw = Math.Max(1, gw / gd), gsh = Math.Max(1, gh / gd);
            int ga = (int)(255 * strength); if (ga > 255) ga = 255; if (ga < 0) ga = 0;
            normal = new Bitmap[Glyphs.Length];
            flipped = new Bitmap[Glyphs.Length];
            for (int gi = 0; gi < Glyphs.Length; gi++)
            {
                Bitmap spr = new Bitmap(gw, gh, PixelFormat.Format32bppPArgb);
                using (Bitmap tmp = new Bitmap(gw, gh, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics gt = Graphics.FromImage(tmp))
                    {
                        gt.TextRenderingHint = TextRenderingHint.AntiAlias;   // grayscale AA -> clean alpha on transparent
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(ga, rgb[0], rgb[1], rgb[2])))
                            gt.DrawString(Glyphs[gi].ToString(), Font, br, glowPad, glowPad, StringFormat.GenericTypographic);
                    }
                    using (Bitmap small = new Bitmap(gsw, gsh, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics gs = Graphics.FromImage(small))
                        {
                            gs.CompositingQuality = CompositingQuality.HighQuality;
                            gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            gs.PixelOffsetMode = PixelOffsetMode.Half;
                            gs.DrawImage(tmp, 0, 0, gsw, gsh);            // shrink (averaging = blur)
                        }
                        using (Graphics gp = Graphics.FromImage(spr))
                        {
                            gp.CompositingQuality = CompositingQuality.HighQuality;
                            gp.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            gp.PixelOffsetMode = PixelOffsetMode.Half;
                            Rectangle dst = new Rectangle(0, 0, gw, gh);
                            gp.DrawImage(small, dst, 0, 0, gsw, gsh, GraphicsUnit.Pixel);   // soft glyph-shaped halo...
                            gp.DrawImage(small, dst, 0, 0, gsw, gsh, GraphicsUnit.Pixel);   // ...layered for strength
                            gp.DrawImage(small, dst, 0, 0, gsw, gsh, GraphicsUnit.Pixel);
                        }
                    }
                }
                normal[gi] = spr;
                flipped[gi] = (Bitmap)spr.Clone(); flipped[gi].RotateFlip(RotateFlipType.RotateNoneFlipX);
            }
        }

        // Crisp tile for a cell: head color when the cell is the bright head, else the uniform body.
        public bool IsHeadBright(float b) { return b > (BODY_BRIGHT + 1f) * 0.5f; }
        public Bitmap CrispTile(int glyphIdx, float b, bool flipped)
        {
            if (IsHeadBright(b)) return flipped ? glyphHeadF[glyphIdx] : glyphHead[glyphIdx];
            return flipped ? glyphBodyF[glyphIdx] : glyphBody[glyphIdx];
        }

        public int GlowPad { get { return glowPad; } }
        public Bitmap HeadGlowTile(int glyphIdx, bool flipped) { return flipped ? headGlowF[glyphIdx] : headGlow[glyphIdx]; }

        // Head-glow accessors: the leading falling-head row per column (fractional), and the
        // regrowth sprouts -- so the renderer can lay a smooth halo at each active head.
        public float HeadRow(int c) { return head[c]; }
        public bool IsFrozen(int c) { return frozen[c]; }
        public int SproutCount { get { return sprouts.Count; } }
        public void SproutAt(int i, out int col, out float pos) { Sprout s = sprouts[i]; col = s.Col; pos = s.Pos; }

        public void Step()
        {
            // UNIFORM BODY, NO FADE: every lit cell snaps to BODY_BRIGHT (the head re-asserts itself
            // to 1.0 in the lifecycle pass below). Glyph changes are INSTANT -- no morph fade, no dim,
            // no flash. A cell goes dark only when erased/backspaced, never by fading.
            for (int c = 0; c < Cols; c++)
            {
                int baseIdx = c * Rows;
                for (int r = 0; r < Rows; r++)
                {
                    int i = baseIdx + r;
                    if (Bright[i] > BODY_BRIGHT) Bright[i] = BODY_BRIGHT;   // instant settle -> uniform body
                    if (Bright[i] > 0.12f && rnd.NextDouble() < CHANGE_CHANCE)
                    {
                        // usually a single glyph; sometimes a contiguous group switches together (upward,
                        // over cells already visited this step) -- an INSTANT swap, no fade.
                        int len = (rnd.NextDouble() < CHUNK_CHANCE) ? CHUNK_MIN + rnd.Next(CHUNK_MAX - CHUNK_MIN + 1) : 1;
                        for (int k = 0; k < len && r - k >= 0; k++)
                        {
                            int j = baseIdx + r - k;
                            if (Bright[j] > 0.12f) Chars[j] = GlyphIdx();
                        }
                    }
                }
            }

            // Falling columns: advance the head, write the trail at BODY_BRIGHT, and hold the single
            // head cell full-bright. At the bottom a long-enough line may FREEZE (stand) to be
            // backspaced; otherwise it restarts. Frozen columns don't advance.
            for (int c = 0; c < Cols; c++)
            {
                if (frozen[c]) continue;
                int baseIdx = c * Rows;
                head[c] += speed[c];
                int nr = (int)Math.Floor(head[c]);
                if (nr != prevRow[c])
                {
                    for (int r = prevRow[c] + 1; r <= nr; r++)
                        if (r >= 0 && r < Rows) { int idx = baseIdx + r; Chars[idx] = GlyphIdx(); Flip[idx] = rnd.NextDouble() < FLIP_CHANCE; Bright[idx] = BODY_BRIGHT; lit[c]++; }
                    prevRow[c] = nr;
                }
                if (nr >= 0 && nr < Rows) Bright[baseIdx + nr] = 1f;   // the head cell alone holds full-bright

                if (head[c] > Rows + 6)
                {
                    if (lit[c] >= MIN_FREEZE_LEN && rnd.NextDouble() < FREEZE_CHANCE) frozen[c] = true; // stand, to be backspaced
                    else RestartColumn(c);
                }
                else if (lit[c] >= MIN_FREEZE_LEN && rnd.NextDouble() < MID_FREEZE_CHANCE)
                {
                    frozen[c] = true; // a still-falling line can stop mid-screen and stand
                }
            }

            // ANIMATED BACKSPACE (primary deletion): a backspace can begin ANYWHERE on a lit line and
            // eats characters one at a time DOWNWARD, at a fraction of the column's head speed ("at the
            // head speed or slower"). It runs on falling and frozen columns alike; a frozen column that
            // ends up fully cleared recycles into a fresh head from the top.
            for (int c = 0; c < Cols; c++)
            {
                int baseIdx = c * Rows;
                if (!eraseActive[c] && rnd.NextDouble() < ERASE_CHANCE)
                {
                    int start = PickLitRow(c);
                    if (start >= 0)
                    {
                        eraseActive[c] = true;
                        erasePos[c] = start;
                        // the backspace eats from the start point DOWN through the rest of the line
                        // (it ends when it runs off the bottom) -- this is what removes standing lines
                        eraseRemaining[c] = Rows;
                        eraseSpeed[c] = speed[c] * (ERASE_SPEED_MIN_FRAC + (float)rnd.NextDouble() * (ERASE_SPEED_MAX_FRAC - ERASE_SPEED_MIN_FRAC));
                    }
                }
                if (eraseActive[c])
                {
                    int from = (int)Math.Floor(erasePos[c]);
                    erasePos[c] += eraseSpeed[c];
                    int to = (int)Math.Floor(erasePos[c]);
                    for (int r = from; r <= to && eraseRemaining[c] > 0; r++)
                        if (r >= 0 && r < Rows && Bright[baseIdx + r] > 0.001f) { Bright[baseIdx + r] = 0f; eraseRemaining[c]--; }
                    if (eraseRemaining[c] <= 0 || erasePos[c] >= Rows) eraseActive[c] = false;
                }
                if (frozen[c] && !eraseActive[c] && ColumnEmpty(c)) RestartColumn(c);
            }

            // Mirror some random lit cells horizontally (kept churn; no flash now).
            int flips = Math.Max(1, (int)(Cols * Rows * FLIP_RATE));
            for (int k = 0; k < flips; k++)
            {
                int idx = rnd.Next(Bright.Length);
                if (Bright[idx] > 0.15f) Flip[idx] = !Flip[idx];
            }

            // Instant small/medium gap "editing" so lines don't pile up solid (no long-chunk tier now).
            int segErases = Math.Max(1, (int)(Cols * SEG_ERASE_RATE));
            for (int k = 0; k < segErases; k++)
            {
                int idx = rnd.Next(Bright.Length);
                if (Bright[idx] > 0.2f)
                {
                    int b = (idx / Rows) * Rows;
                    int r0 = idx % Rows;
                    int len = SegLen();
                    for (int r = r0; r < r0 + len && r < Rows; r++) Bright[b + r] = 0f;
                    // ...and sometimes regrow: drop a fresh head at the top of the gap so a NEW stream
                    // re-writes where code was just removed.
                    if (sprouts.Count < Cols && rnd.NextDouble() < SPAWN_ON_ERASE)
                    {
                        Chars[b + r0] = GlyphIdx(); Flip[b + r0] = rnd.NextDouble() < FLIP_CHANCE; Bright[b + r0] = 1f;
                        sprouts.Add(new Sprout { Col = idx / Rows, Pos = r0, Speed = RandSpeed(), Prev = r0 });
                    }
                }
            }

            // Keep the TOP from going bare: occasionally drop a fresh head at the top of a frozen
            // column whose top has cleared. It falls and re-lights the column.
            if (sprouts.Count < Cols && rnd.NextDouble() < TOP_SPROUT_CHANCE)
            {
                int col = rnd.Next(Cols);
                if (frozen[col] && Bright[col * Rows] < 0.3f)
                {
                    int b = col * Rows;
                    Chars[b] = GlyphIdx(); Flip[b] = rnd.NextDouble() < FLIP_CHANCE; Bright[b] = 1f;
                    sprouts.Add(new Sprout { Col = col, Pos = 0, Speed = RandSpeed(), Prev = 0 });
                }
            }

            // Advance those regrowth heads like the main heads: trail at BODY_BRIGHT, head full-bright.
            for (int s = sprouts.Count - 1; s >= 0; s--)
            {
                Sprout sp = sprouts[s];
                sp.Pos += sp.Speed;
                int nr = (int)Math.Floor(sp.Pos);
                if (nr != sp.Prev)
                {
                    int b = sp.Col * Rows;
                    for (int r = sp.Prev + 1; r <= nr; r++)
                        if (r >= 0 && r < Rows) { Chars[b + r] = GlyphIdx(); Flip[b + r] = rnd.NextDouble() < FLIP_CHANCE; Bright[b + r] = BODY_BRIGHT; }
                    sp.Prev = nr;
                }
                if (nr >= 0 && nr < Rows) Bright[sp.Col * Rows + nr] = 1f;   // sprout head full-bright
                if (sp.Pos > Rows + 6) sprouts.RemoveAt(s);
            }
        }

        void RestartColumn(int c)
        {
            frozen[c] = false;
            eraseActive[c] = false;
            head[c] = -rnd.Next(RESTART_GAP);
            prevRow[c] = (int)Math.Floor(head[c]);
            speed[c] = RandSpeed();
            lit[c] = 0;
        }

        // A random LIT row in column c (scanning from a random offset), or -1 if none -- so a
        // backspace can begin "anywhere on the line".
        int PickLitRow(int c)
        {
            int baseIdx = c * Rows;
            int r0 = rnd.Next(Rows);
            for (int k = 0; k < Rows; k++)
            {
                int r = r0 + k; if (r >= Rows) r -= Rows;
                if (Bright[baseIdx + r] > 0.12f) return r;
            }
            return -1;
        }

        bool ColumnEmpty(int c)
        {
            int baseIdx = c * Rows;
            for (int r = 0; r < Rows; r++) if (Bright[baseIdx + r] > 0.001f) return false;
            return true;
        }
    }

    // One timer drives the shared field and repaints every window.
    class RainController : ApplicationContext
    {
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
        const uint MOUSEEVENTF_MOVE = 0x0001;

        readonly Timer timer;
        readonly Timer jiggle;

        public RainController(RainField field, List<MatrixForm> windows, int interval, bool keepAwake = false)
        {
            foreach (MatrixForm w in windows)
            {
                w.FormClosed += delegate { ExitThreadCore(); };
                w.Show();
            }
            timer = new Timer();
            timer.Interval = interval;
            timer.Tick += delegate
            {
                field.Step();
                for (int i = 0; i < windows.Count; i++) windows[i].RenderStep();
            };
            timer.Start();

            if (keepAwake)
            {
                // Keep the machine awake while the lock curtain is up: a net-zero mouse "jiggle"
                // every 60s resets the input-idle timer so the inactivity auto-lock can't fire and
                // cut off the rain. Authorized keep-awake; stops automatically when the curtain exits
                // (Esc -> LockWorkStation). The curtain ignores this injected move -- only Esc dismisses it.
                jiggle = new Timer();
                jiggle.Interval = 60000;
                jiggle.Tick += delegate
                {
                    try { mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, IntPtr.Zero); mouse_event(MOUSEEVENTF_MOVE, -1, 0, 0, IntPtr.Zero); } catch { }
                };
                jiggle.Start();
            }
        }
    }

    class MatrixForm : Form
    {
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const int WS_CHILD = 0x40000000;
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

        RainField field;
        RunMode mode;
        IntPtr parentHandle;
        int offsetX, offsetY;      // this window's top-left within the field (px)
        Rectangle initialBounds;
        Bitmap buffer;
        Graphics gBuf;
        bool armed;
        bool lockOnExit;           // /lock mode: lock the workstation when dismissed
        Point armCursor;

        MatrixForm() { }

        public static MatrixForm Screensaver(RainField f, Rectangle screen, Point fieldOrigin, bool lockOnExit)
        {
            MatrixForm m = new MatrixForm();
            m.field = f; m.mode = RunMode.Screensaver; m.lockOnExit = lockOnExit;
            m.offsetX = screen.X - fieldOrigin.X; m.offsetY = screen.Y - fieldOrigin.Y;
            m.initialBounds = screen;
            m.FormBorderStyle = FormBorderStyle.None; m.StartPosition = FormStartPosition.Manual;
            m.Bounds = screen; m.TopMost = true; m.ShowInTaskbar = false;
            m.BackColor = Color.Black; m.DoubleBuffered = true; m.KeyPreview = true;
            return m;
        }

        public static MatrixForm Wallpaper(RainField f, IntPtr host, Rectangle childRect)
        {
            MatrixForm m = new MatrixForm();
            m.field = f; m.mode = RunMode.Wallpaper; m.parentHandle = host;
            m.offsetX = childRect.X; m.offsetY = childRect.Y; m.initialBounds = childRect;
            m.FormBorderStyle = FormBorderStyle.None; m.StartPosition = FormStartPosition.Manual;
            m.BackColor = Color.Black; m.DoubleBuffered = true; m.Bounds = childRect;
            return m;
        }

        public static MatrixForm Preview(RainField f, IntPtr parent, Size sz)
        {
            MatrixForm m = new MatrixForm();
            m.field = f; m.mode = RunMode.Preview; m.parentHandle = parent;
            m.FormBorderStyle = FormBorderStyle.None; m.BackColor = Color.Black;
            m.DoubleBuffered = true; m.Size = sz; m.Location = Point.Empty;
            return m;
        }

        public static MatrixForm Windowed(RainField f, Size sz)
        {
            MatrixForm m = new MatrixForm();
            m.field = f; m.mode = RunMode.Windowed;
            m.FormBorderStyle = FormBorderStyle.None; m.StartPosition = FormStartPosition.Manual;
            m.Location = new Point(100, 100); m.ClientSize = sz; m.TopMost = true;
            m.BackColor = Color.Black; m.DoubleBuffered = true;
            return m;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (mode == RunMode.Preview || mode == RunMode.Wallpaper)
                {
                    cp.Style |= WS_CHILD;
                    cp.Parent = parentHandle;
                }
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (mode == RunMode.Wallpaper)
            {
                Bounds = initialBounds;
                // Sit behind the desktop icons (bottom of the host's child z-order).
                SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            BuildBuffer();

            if (mode == RunMode.Screensaver)
            {
                // Ignore input for ~1s so the hotkey/mouse that launched the saver
                // (e.g. Ctrl+Alt+L) doesn't immediately dismiss it.
                Timer arm = new Timer();
                arm.Interval = 1000;
                arm.Tick += delegate { armed = true; armCursor = Cursor.Position; arm.Stop(); arm.Dispose(); };
                arm.Start();
            }
        }

        void BuildBuffer()
        {
            int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
            if (buffer != null) buffer.Dispose();
            if (gBuf != null) gBuf.Dispose();
            buffer = new Bitmap(w, h);
            gBuf = Graphics.FromImage(buffer);
            gBuf.Clear(Color.Black);
            // Glyph tiles (opaque, glow baked in) and the head-halo sprites are baked once in
            // RainField (shared across windows); each frame is just blits -- no per-frame bloom.
        }

        // Called by the controller each tick. Two sparse passes over only the LIT cells (classic
        // rain is mostly dark): the OPAQUE glow-baked glyph tiles (a fast memcpy blit), then a
        // brighter cross-cell halo on each falling head/sprout (alpha, but only a handful).
        public void RenderStep()
        {
            if ((mode == RunMode.Preview || mode == RunMode.Wallpaper) && !IsWindow(parentHandle)) { Close(); return; }
            if (buffer == null || buffer.Width != ClientSize.Width || buffer.Height != ClientSize.Height) BuildBuffer();

            gBuf.Clear(Color.Black);
            BlitCrisp();       // opaque glyph+glow tiles for every lit cell (fast)
            DrawHeadGlows();   // brighter halo on each head/sprout, on top (position-keyed, glides)
            Invalidate();
        }

        // (1) Opaque glyph tiles (glow baked in): head color for the bright head cell, body else.
        void BlitCrisp()
        {
            int W = buffer.Width, H = buffer.Height;
            int cStart = Math.Max(0, offsetX / field.CellW - 1);
            int cEnd = Math.Min(field.Cols - 1, (offsetX + W) / field.CellW + 1);
            int rStart = Math.Max(0, offsetY / field.CellH - 1);
            int rEnd = Math.Min(field.Rows - 1, (offsetY + H) / field.CellH + 1);
            for (int c = cStart; c <= cEnd; c++)
            {
                int lx = c * field.CellW - offsetX;
                int baseIdx = c * field.Rows;
                for (int r = rStart; r <= rEnd; r++)
                {
                    int idx = baseIdx + r;
                    float b = field.Bright[idx];
                    if (b <= 0.04f) continue;
                    gBuf.DrawImageUnscaled(field.CrispTile(field.Chars[idx], b, field.Flip[idx]), lx, r * field.CellH - offsetY);
                }
            }
        }

        // (A2) A brighter GLYPH-SHAPED halo on each actively-falling head (+ regrowth sprouts),
        // positioned by the head's FRACTIONAL row so it glides (never strobes). Frozen lines have
        // no head -> none.
        void DrawHeadGlows()
        {
            int pad = field.GlowPad, W = buffer.Width, H = buffer.Height;
            for (int c = 0; c < field.Cols; c++)
            {
                if (field.IsFrozen(c)) continue;
                float hp = field.HeadRow(c);
                if (hp < 0f || hp >= field.Rows) continue;
                BlitHeadGlow(c, (int)hp, pad, W, H);
            }
            for (int i = 0; i < field.SproutCount; i++)
            {
                int col; float pos; field.SproutAt(i, out col, out pos);
                if (pos < 0f || pos >= field.Rows) continue;
                BlitHeadGlow(col, (int)pos, pad, W, H);
            }
        }

        void BlitHeadGlow(int c, int row, int pad, int W, int H)
        {
            int idx = c * field.Rows + row;
            Bitmap spr = field.HeadGlowTile(field.Chars[idx], field.Flip[idx]);
            int x = c * field.CellW - offsetX - pad, y = row * field.CellH - offsetY - pad;
            if (x > W || y > H || x + spr.Width < 0 || y + spr.Height < 0) return;   // offscreen for this window's slice
            gBuf.DrawImageUnscaled(spr, x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (buffer != null) e.Graphics.DrawImageUnscaled(buffer, 0, 0);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* buffer covers it */ }

        // The /lock "keep-awake curtain" stays up through the mouse jiggle and any other input --
        // ONLY Esc dismisses it (which then locks the workstation). The plain idle screensaver
        // keeps the classic behavior: any key or mouse movement dismisses it.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (mode != RunMode.Screensaver || !armed) return;
            if (lockOnExit) { if (e.KeyCode == Keys.Escape) Dismiss(); }
            else Dismiss();
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (mode == RunMode.Screensaver && armed && !lockOnExit) Dismiss(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mode != RunMode.Screensaver || !armed || lockOnExit) return;
            Point p = Cursor.Position;
            if (Math.Abs(p.X - armCursor.X) > 8 || Math.Abs(p.Y - armCursor.Y) > 8) Dismiss();
        }

        // The keep-awake lock curtain has exactly one user-facing way out: lock. Route any
        // user-initiated close (Alt+F4, etc.) through Dismiss() so it locks the workstation
        // instead of dropping to the unlocked desktop. (The Application.Exit during lock
        // teardown reports ApplicationExitCall, not UserClosing, so it isn't re-routed.)
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode == RunMode.Screensaver && lockOnExit && !dismissing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Dismiss();
                return;
            }
            base.OnFormClosing(e);
        }

        static bool dismissing;
        void Dismiss()
        {
            if (dismissing) return;
            dismissing = true;
            if (lockOnExit)
            {
                // Lock the workstation as we go away -> password on return.
                try { LockWorkStation(); } catch { }
                // Give Winlogon a moment to switch to the secure desktop before we tear down.
                Timer t = new Timer();
                t.Interval = 600;
                t.Tick += delegate { t.Stop(); t.Dispose(); Application.Exit(); };
                t.Start();
            }
            else Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (gBuf != null) gBuf.Dispose();
                if (buffer != null) buffer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
