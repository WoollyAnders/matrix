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
            Application.Run(new RainController(field, windows, 50));
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
        const float DECAY = 0.96f;           // trail fade per step (higher = longer tails)
        const float SPEED_MIN = 0.45f, SPEED_MAX = 1.0f; // rows per step
        const double CHANGE_CHANCE = 0.10;   // per-step chance a lit trailing glyph silently morphs (constant churn)
        const double GLITCH_RATE = 0.0020;   // fraction of cells that change glyph WITH a flash (emphasis pops)
        const float FLASH_BOOST = 0.5f;      // momentary extra glow when a glyph changes/flips
        const float FLASH_DECAY = 0.45f;     // how fast that flash fades (low = brief); base tail fade is unchanged
        const double FLIP_RATE = 0.0015;     // fraction of cells mirrored horizontally per step
        const float FLIP_CHANCE = 0.28f;     // chance a freshly-lit glyph spawns mirrored
        const double INTERRUPT_CHANCE = 0.004; // per-step chance a whole column is wiped & restarted ("being edited")
        const double SEG_ERASE_RATE = 0.06;  // segment-erase attempts per column per step (punches gaps in streams)
        const int SEG_MIN = 5, SEG_MAX = 20; // erased segment length range (cells)
        const int RESTART_GAP = 22;          // how far above the top a finished column restarts
        const int LEVELS = 48;               // brightness quantization for the glyph cache (smooth gradient)

        public readonly Font Font;
        public readonly int CellW, CellH, Cols, Rows;
        public readonly float[] Bright;
        public readonly int[] Chars;         // glyph index per cell
        public readonly bool[] Flip;         // drawn horizontally mirrored?
        public readonly float[] Flash;       // momentary glow on change/flip; fades fast, separate from the tail

        readonly float[] head;
        readonly float[] speed;
        readonly int[] prevRow;
        readonly Random rnd = new Random();
        readonly Bitmap[,] cache;            // [glyph, level]; level LEVELS == bright head
        readonly Bitmap[,] cacheFlipped;     // same, mirrored horizontally

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
            Flash = new float[Cols * Rows];
            head = new float[Cols];
            speed = new float[Cols];
            prevRow = new int[Cols];
            for (int c = 0; c < Cols; c++)
            {
                // Start every column above the top (staggered) so the rain cascades
                // in from the top downward when the saver starts.
                head[c] = -(float)(rnd.NextDouble() * Rows);
                speed[c] = RandSpeed();
                prevRow[c] = (int)Math.Floor(head[c]);
            }

            // Pre-render each glyph at each brightness level onto an opaque black
            // tile, so per-frame drawing is a fast blit instead of slow text layout.
            cache = new Bitmap[Glyphs.Length, LEVELS + 1];
            for (int gi = 0; gi < Glyphs.Length; gi++)
                for (int l = 0; l <= LEVELS; l++)
                {
                    Bitmap bm = new Bitmap(CellW, CellH, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(bm))
                    {
                        g.Clear(Color.Black);
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        // gradient: pure-white head -> matrix green -> black tail
                        float bb = l / (float)LEVELS;
                        int cr, cg, cb;
                        if (bb >= 0.8f) { float t = (bb - 0.8f) / 0.2f; cr = (int)(255 * t); cg = 255; cb = (int)(70 + 185 * t); }
                        else { float t = bb / 0.8f; cr = 0; cg = (int)(255 * t); cb = (int)(70 * t); }
                        Color col = Color.FromArgb(cr, cg, cb);
                        using (SolidBrush br = new SolidBrush(col))
                            g.DrawString(Glyphs[gi].ToString(), Font, br, 0, 0, StringFormat.GenericTypographic);
                    }
                    cache[gi, l] = bm;
                }

            // Mirrored copies for the horizontal-flip effect.
            cacheFlipped = new Bitmap[Glyphs.Length, LEVELS + 1];
            for (int gi = 0; gi < Glyphs.Length; gi++)
                for (int l = 0; l <= LEVELS; l++)
                {
                    Bitmap bm = (Bitmap)cache[gi, l].Clone();
                    bm.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    cacheFlipped[gi, l] = bm;
                }
        }

        public Bitmap Tile(int glyphIdx, float b, bool flipped)
        {
            int l = (int)(b * LEVELS + 0.5f);
            if (l < 0) l = 0; else if (l > LEVELS) l = LEVELS;
            return flipped ? cacheFlipped[glyphIdx, l] : cache[glyphIdx, l];
        }

        public void Step()
        {
            for (int i = 0; i < Bright.Length; i++)
            {
                if (Bright[i] > 0.001f) Bright[i] *= DECAY;
                if (Flash[i] > 0.001f) Flash[i] *= FLASH_DECAY; else Flash[i] = 0f;
                // constantly cycle visible trailing glyphs (no flash) -- the "always changing" look
                if (Bright[i] > 0.12f && rnd.NextDouble() < CHANGE_CHANCE) Chars[i] = GlyphIdx();
            }

            for (int c = 0; c < Cols; c++)
            {
                head[c] += speed[c];
                int nr = (int)Math.Floor(head[c]);
                if (nr != prevRow[c])
                {
                    for (int r = prevRow[c] + 1; r <= nr; r++)
                        if (r >= 0 && r < Rows) { int idx = c * Rows + r; Chars[idx] = GlyphIdx(); Flip[idx] = rnd.NextDouble() < FLIP_CHANCE; Bright[idx] = 1f; }
                    prevRow[c] = nr;
                }
                if (head[c] > Rows + 6)
                {
                    head[c] = -rnd.Next(RESTART_GAP);
                    prevRow[c] = (int)Math.Floor(head[c]);
                    speed[c] = RandSpeed();
                }
                else if (rnd.NextDouble() < INTERRUPT_CHANCE)
                {
                    // "being edited": wipe this stream, then replace it -- half the time a
                    // fresh stream from the top (renewal), half a mid-screen reappearance (edit).
                    for (int r = 0; r < Rows; r++) { int idx = c * Rows + r; Bright[idx] = 0f; Flash[idx] = 0f; }
                    head[c] = (rnd.NextDouble() < 0.5) ? -(float)(rnd.NextDouble() * RESTART_GAP) : (float)(rnd.NextDouble() * Rows);
                    prevRow[c] = (int)Math.Floor(head[c]);
                    speed[c] = RandSpeed();
                }
            }

            int glitches = Math.Max(1, (int)(Cols * Rows * GLITCH_RATE));
            for (int k = 0; k < glitches; k++)
            {
                int idx = rnd.Next(Bright.Length);
                if (Bright[idx] > 0.15f) { Chars[idx] = GlyphIdx(); Flash[idx] = FLASH_BOOST; }
            }

            // ...and separately, mirror some other random lit cells horizontally.
            int flips = Math.Max(1, (int)(Cols * Rows * FLIP_RATE));
            for (int k = 0; k < flips; k++)
            {
                int idx = rnd.Next(Bright.Length);
                if (Bright[idx] > 0.15f) { Flip[idx] = !Flip[idx]; Flash[idx] = FLASH_BOOST; }
            }

            // ...and punch short gaps into random lit streams ("sections taken out").
            int segErases = Math.Max(1, (int)(Cols * SEG_ERASE_RATE));
            for (int k = 0; k < segErases; k++)
            {
                int idx = rnd.Next(Bright.Length);
                if (Bright[idx] > 0.2f)
                {
                    int baseIdx = (idx / Rows) * Rows;
                    int r0 = idx % Rows;
                    int len = SEG_MIN + rnd.Next(SEG_MAX - SEG_MIN + 1);
                    for (int r = r0; r < r0 + len && r < Rows; r++) { Bright[baseIdx + r] = 0f; Flash[baseIdx + r] = 0f; }
                }
            }
        }
    }

    // One timer drives the shared field and repaints every window.
    class RainController : ApplicationContext
    {
        readonly Timer timer;

        public RainController(RainField field, List<MatrixForm> windows, int interval)
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
            gBuf.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            gBuf.Clear(Color.Black);
        }

        // Called by the controller each tick: clear, then blit this window's slice
        // of the shared field (only lit cells).
        public void RenderStep()
        {
            if ((mode == RunMode.Preview || mode == RunMode.Wallpaper) && !IsWindow(parentHandle)) { Close(); return; }
            if (buffer == null || buffer.Width != ClientSize.Width || buffer.Height != ClientSize.Height) BuildBuffer();

            int W = buffer.Width, H = buffer.Height;
            gBuf.Clear(Color.Black);

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
                    float b = field.Bright[baseIdx + r] + field.Flash[baseIdx + r];
                    if (b <= 0.04f) continue;
                    if (b > 1f) b = 1f;
                    gBuf.DrawImageUnscaled(field.Tile(field.Chars[baseIdx + r], b, field.Flip[baseIdx + r]), lx, r * field.CellH - offsetY);
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (buffer != null) e.Graphics.DrawImageUnscaled(buffer, 0, 0);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* buffer covers it */ }

        protected override void OnKeyDown(KeyEventArgs e) { if (mode == RunMode.Screensaver && armed) Dismiss(); }
        protected override void OnMouseDown(MouseEventArgs e) { if (mode == RunMode.Screensaver && armed) Dismiss(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mode != RunMode.Screensaver || !armed) return;
            Point p = Cursor.Position;
            if (Math.Abs(p.X - armCursor.X) > 8 || Math.Abs(p.Y - armCursor.Y) > 8) Dismiss();
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
