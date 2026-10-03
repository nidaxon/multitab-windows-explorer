using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Controls;
using Microsoft.WindowsAPICodePack.Controls.WindowsForms;
using Microsoft.WindowsAPICodePack.Shell;

namespace TabbedExplorer
{
    // =====================================================================
    //  Theme colors
    // =====================================================================
    public class AppTheme
    {
        public bool IsDark;
        public Color Back, Surface, SurfaceHover, SurfacePressed, Border;
        public Color Text, Muted, TabSelected, TabUnselected, Accent, Link;

        public static readonly AppTheme LightTheme = new AppTheme
        {
            IsDark = false,
            Back = Color.FromArgb(243, 243, 243),
            Surface = Color.FromArgb(251, 251, 251),
            SurfaceHover = Color.FromArgb(229, 229, 229),
            SurfacePressed = Color.FromArgb(204, 204, 204),
            Border = Color.FromArgb(204, 204, 204),
            Text = Color.FromArgb(27, 27, 27),
            Muted = Color.FromArgb(96, 96, 96),
            TabSelected = Color.FromArgb(255, 255, 255),
            TabUnselected = Color.FromArgb(243, 243, 243),
            Accent = Color.FromArgb(0, 120, 212),
            Link = Color.FromArgb(0, 102, 204)
        };

        public static readonly AppTheme DarkTheme = new AppTheme
        {
            IsDark = true,
            Back = Color.FromArgb(32, 32, 32),
            Surface = Color.FromArgb(51, 51, 51),
            SurfaceHover = Color.FromArgb(64, 64, 64),
            SurfacePressed = Color.FromArgb(80, 80, 80),
            Border = Color.FromArgb(72, 72, 72),
            Text = Color.FromArgb(240, 240, 240),
            Muted = Color.FromArgb(160, 160, 160),
            TabSelected = Color.FromArgb(51, 51, 51),
            TabUnselected = Color.FromArgb(32, 32, 32),
            Accent = Color.FromArgb(76, 194, 255),
            Link = Color.FromArgb(102, 178, 255)
        };

        public static AppTheme Detect()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key?.GetValue("AppsUseLightTheme");
                    if (value is int light)
                        return light == 0 ? DarkTheme : LightTheme;
                }
            }
            catch
            {
            }
            return LightTheme;
        }
    }

    // =====================================================================
    //  Icon helper — loads the app icon from the embedded Win32 resource
    //  (the same icon set via <ApplicationIcon> in the .csproj).
    // =====================================================================
    public static class AppIcon
    {
        private static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null) return _cached;

            // 1) Try extracting from the running executable (most reliable —
            //    the icon is already baked into the .exe by the csproj).
            try
            {
                _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (_cached != null) return _cached;
            }
            catch { }

            // 2) Fallback: look next to the executable
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string path = Path.Combine(dir, "mtwe.ico");
                if (File.Exists(path))
                    _cached = new Icon(path);
            }
            catch { }

            // 3) Fallback: look in the current working directory
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "mtwe.ico");
                if (File.Exists(path))
                    _cached = new Icon(path);
            }
            catch { }

            return _cached;
        }
    }

    // =====================================================================
    //  Native theming helpers (dark title bar, dark Explorer view, etc.)
    // =====================================================================
    public static class ThemeHelper
    {
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr updateRect, IntPtr updateRgn, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const int WM_THEMECHANGED = 0x031A;
        private const int TVM_SETBKCOLOR = 0x111D;
        private const int TVM_SETTEXTCOLOR = 0x111E;

        public static void SetAppMode(bool dark)
        {
            try
            {
                SetPreferredAppMode(dark ? 2 : 3);
                FlushMenuThemes();
            }
            catch { }
        }

        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch { }
        }

        public static void ApplyToShellWindows(IntPtr root, bool dark, bool force)
        {
            if (root == IntPtr.Zero) return;
            if (!dark && !force) return;
            try
            {
                string style = dark ? "DarkMode_Explorer" : "Explorer";
                int treeBack = ColorTranslator.ToWin32(dark ? Color.FromArgb(25, 25, 25) : SystemColors.Window);
                int treeText = ColorTranslator.ToWin32(dark ? Color.FromArgb(240, 240, 240) : SystemColors.WindowText);
                var className = new StringBuilder(64);
                EnumChildProc callback = (hWnd, lParam) =>
                {
                    try
                    {
                        SetWindowTheme(hWnd, style, null);
                        SendMessage(hWnd, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
                        className.Clear();
                        GetClassName(hWnd, className, className.Capacity);
                        string cls = className.ToString();
                        if (cls == "SysTreeView32")
                        {
                            SendMessage(hWnd, TVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)treeBack);
                            SendMessage(hWnd, TVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)treeText);
                        }
                    }
                    catch { }
                    return true;
                };
                EnumChildWindows(root, callback, IntPtr.Zero);
                RedrawWindow(root, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);
            }
            catch { }
        }

        public static bool TryGetSplitterGap(IntPtr root, out Rectangle gap)
        {
            gap = Rectangle.Empty;
            if (root == IntPtr.Zero) return false;
            RECT tree = new RECT(), view = new RECT(), alt = new RECT();
            bool haveTree = false, haveView = false, haveAlt = false;
            var name = new StringBuilder(64);
            EnumChildProc callback = (hWnd, lParam) =>
            {
                name.Clear();
                GetClassName(hWnd, name, name.Capacity);
                string cls = name.ToString();
                if (cls == "SysTreeView32" && IsWindowVisible(hWnd))
                {
                    GetWindowRect(hWnd, out tree);
                    haveTree = true;
                }
                else if (cls == "SHELLDLL_DefView" && IsWindowVisible(hWnd))
                {
                    GetWindowRect(hWnd, out view);
                    haveView = true;
                }
                else if (cls == "NamespaceTreeControl" && IsWindowVisible(hWnd))
                {
                    GetWindowRect(hWnd, out alt);
                    haveAlt = true;
                }
                return true;
            };
            EnumChildWindows(root, callback, IntPtr.Zero);
            if (!haveView) return false;
            if ((!haveTree || tree.right >= view.left) && haveAlt && alt.right < view.left)
            {
                tree = alt;
                haveTree = true;
            }
            if (!haveTree || tree.right >= view.left) return false;
            int top = Math.Min(tree.top, view.top);
            int bottom = Math.Max(tree.bottom, view.bottom);
            gap = new Rectangle(tree.right, top, view.left - tree.right, bottom - top);
            return true;
        }

        public static void RaiseToTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        public static void Apply(Control c, AppTheme t)
        {
            if (c is ThemedTabControl) return;
            if (c is Form || c is Panel)
            {
                c.BackColor = t.Back;
                c.ForeColor = t.Text;
            }
            else if (c is Button button) { StyleButton(button, t); }
            else if (c is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = t.Surface;
                textBox.ForeColor = t.Text;
            }
            else if (c is LinkLabel link)
            {
                link.LinkColor = t.Link;
                link.ActiveLinkColor = t.Link;
                link.VisitedLinkColor = t.Link;
                link.BackColor = t.Back;
                link.ForeColor = t.Text;
            }
            else if (c is Label label)
            {
                label.BackColor = t.Back;
                label.ForeColor = (label.Tag as string) == "muted" ? t.Muted : t.Text;
            }
            foreach (Control child in c.Controls)
                Apply(child, t);
        }

        private static void StyleButton(Button b, AppTheme t)
        {
            b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = t.Surface;
            b.ForeColor = t.Text;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = t.Border;
            b.FlatAppearance.MouseOverBackColor = t.SurfaceHover;
            b.FlatAppearance.MouseDownBackColor = t.SurfacePressed;
        }
    }

    // =====================================================================
    //  Frameless right-click menus in dark mode
    // =====================================================================
    public static class MenuBorderHook
    {
        private delegate bool EnumThreadProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadProc callback, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);

        private const int WM_SIZE = 0x0005;
        private const int WM_WINDOWPOSCHANGED = 0x0047;
        private const int VerticalInset = 3;

        private static System.Windows.Forms.Timer timer;
        private static EnumThreadProc scanProc;
        private static readonly StringBuilder className = new StringBuilder(32);
        private static readonly HashSet<IntPtr> attached = new HashSet<IntPtr>();

        public static bool Dark;

        public static void Install()
        {
            if (timer != null) return;
            scanProc = OnWindow;
            timer = new System.Windows.Forms.Timer { Interval = 30 };
            timer.Tick += (s, e) => Scan();
            timer.Start();
        }

        public static void Uninstall()
        {
            if (timer == null) return;
            timer.Stop();
            timer.Dispose();
            timer = null;
        }

        private static void Scan()
        {
            if (!Dark) return;
            try { EnumThreadWindows(GetCurrentThreadId(), scanProc, IntPtr.Zero); } catch { }
        }

        private static bool OnWindow(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                if (attached.Contains(hwnd) || !IsWindowVisible(hwnd)) return true;
                className.Clear();
                GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() != "#32768") return true;
                attached.Add(hwnd);
                new MenuWindow(hwnd);
            }
            catch { }
            return true;
        }

        private class MenuWindow : NativeWindow
        {
            private readonly IntPtr menuHandle;
            private bool applying;
            private int lastWidth, lastHeight;

            public MenuWindow(IntPtr hwnd)
            {
                menuHandle = hwnd;
                AssignHandle(hwnd);
                ApplyRegion();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (Handle == IntPtr.Zero) { attached.Remove(menuHandle); return; }
                if (m.Msg == WM_WINDOWPOSCHANGED || m.Msg == WM_SIZE) ApplyRegion();
            }

            private void ApplyRegion()
            {
                if (applying || Handle == IntPtr.Zero) return;
                applying = true;
                try
                {
                    RECT window, client;
                    GetWindowRect(Handle, out window);
                    GetClientRect(Handle, out client);
                    int width = window.right - window.left;
                    int height = window.bottom - window.top;
                    if (width == lastWidth && height == lastHeight) return;
                    var origin = new POINT();
                    ClientToScreen(Handle, ref origin);
                    int left = origin.x - window.left;
                    int top = origin.y - window.top;
                    int clientWidth = client.right - client.left;
                    int clientHeight = client.bottom - client.top;
                    if (clientWidth <= 0 || clientHeight <= 0) return;
                    int inset = clientHeight > 2 * VerticalInset + 20 ? VerticalInset : 0;
                    IntPtr region = CreateRectRgn(left, top + inset, left + clientWidth, top + clientHeight - inset);
                    SetWindowRgn(Handle, region, true);
                    lastWidth = width;
                    lastHeight = height;
                }
                catch { }
                finally { applying = false; }
            }
        }
    }

    // =====================================================================
    //  Icons from the built-in Windows icon font
    // =====================================================================
    public static class GlyphIcons
    {
        public static readonly string FontName = ResolveFont();

        private static string ResolveFont()
        {
            using (var fonts = new InstalledFontCollection())
            {
                foreach (var family in fonts.Families)
                {
                    if (family.Name == "Segoe Fluent Icons")
                        return family.Name;
                }
            }
            return "Segoe MDL2 Assets";
        }

        public static Bitmap Create(string glyph, Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontName, size - 3, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip))
            using (var brush = new SolidBrush(color))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            }
            return bmp;
        }
    }

    // =====================================================================
    //  Per-tab state + pinned tab storage
    // =====================================================================
    public class TabState
    {
        public bool Pinned;
        public string PinnedPath;
        public string Title = "Loading...";
    }

    public static class PinnedStore
    {
        private static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TabbedExplorer", "pinned-tabs.txt");
            }
        }

        public static List<string> Load()
        {
            var list = new List<string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            list.Add(line.Trim());
                    }
                }
            }
            catch { }
            return list;
        }

        public static void Save(IEnumerable<string> paths)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, paths);
            }
            catch { }
        }
    }

    // =====================================================================
    //  Thin dark strip drawn over Explorer's white splitter
    // =====================================================================
    public class SplitterOverlay : Control
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        public SplitterOverlay()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }
    }

    // =====================================================================
    //  Tab control that paints itself with the current theme
    // =====================================================================
    public class ThemedTabControl : TabControl
    {
        private const string PinGlyph = "\uE718";
        private AppTheme palette = AppTheme.LightTheme;
        private readonly Font pinFont;
        private int hoverIndex = -1;

        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Padding = new Point(12, 5);
            pinFont = new Font(GlyphIcons.FontName, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public AppTheme Palette { get { return palette; } set { palette = value; Invalidate(); } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pinFont.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(palette.Back);
            if (TabCount == 0) return;
            Rectangle first = GetTabRect(0);
            using (var pen = new Pen(palette.Border))
                g.DrawLine(pen, 0, first.Bottom, Width, first.Bottom);
            for (int i = 0; i < TabCount; i++)
                DrawTab(g, i);
        }

        private void DrawTab(Graphics g, int i)
        {
            Rectangle r = GetTabRect(i);
            bool selected = i == SelectedIndex;
            Color fill = selected ? palette.TabSelected
                       : (i == hoverIndex ? palette.SurfaceHover : palette.TabUnselected);
            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, r);
            if (selected)
            {
                using (var accent = new SolidBrush(palette.Accent))
                    g.FillRectangle(accent, r.X, r.Y, r.Width, 2);
            }
            TabPage page = TabPages[i];
            var state = page.Tag as TabState;
            string title = state != null ? state.Title : page.Text.Trim();
            bool pinned = state != null && state.Pinned;
            Color textColor = selected ? palette.Text : palette.Muted;
            Rectangle textRect = new Rectangle(r.X + Padding.X, r.Y, r.Width - Padding.X * 2, r.Height);
            if (pinned)
            {
                int slot = (int)(18 * DeviceDpi / 96f);
                Rectangle iconRect = new Rectangle(textRect.X, r.Y, slot, r.Height);
                TextRenderer.DrawText(g, PinGlyph, pinFont, iconRect, palette.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                textRect.X += slot;
                textRect.Width -= slot;
            }
            TextRenderer.DrawText(g, title, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = -1;
            for (int i = 0; i < TabCount; i++)
            {
                if (GetTabRect(i).Contains(e.Location)) { index = i; break; }
            }
            if (index != hoverIndex) { hoverIndex = index; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex != -1) { hoverIndex = -1; Invalidate(); }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }
    }

    // =====================================================================
    //  Main window
    // =====================================================================
    public class MainForm : Form, IMessageFilter
    {
        private const string ThisPC = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        private const string GlyphAdd = "\uE710";
        private const string GlyphClose = "\uE711";
        private const string GlyphBack = "\uE72B";
        private const string GlyphForward = "\uE72A";
        private const string GlyphPin = "\uE718";
        private const string GlyphUnpin = "\uE77A";
        private const string GlyphSettings = "\uE713";
        private const string GlyphInfo = "\uE946";
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_XBUTTONDBLCLK = 0x020D;
        private const int WM_APPCOMMAND = 0x0319;
        private const int WM_SETTINGCHANGE = 0x001A;

        private AppTheme theme = AppTheme.Detect();
        private readonly Dictionary<Button, string> buttonGlyphs = new Dictionary<Button, string>();
        private ThemedTabControl tabControl;
        private Panel topPanel;
        private Button addTabButton;
        private Button closeTabButton;
        private Button backButton;
        private Button forwardButton;
        private TextBox addressBar;
        private Button pinButton;
        private Button optionsButton;
        private Button aboutButton;
        private ToolTip toolTip;
        private System.Windows.Forms.Timer splitterTimer;

        public MainForm()
        {
            InitializeComponent();
            ApplyTheme();

            Application.AddMessageFilter(this);
            MenuBorderHook.Install();
            MenuBorderHook.Dark = theme.IsDark;

            splitterTimer = new System.Windows.Forms.Timer { Interval = 200 };
            splitterTimer.Tick += (s, e) => UpdateSplitter(GetActiveBrowser());
            splitterTimer.Start();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            this.Load += (s, e) =>
            {
                ResizeAddressBar();
                List<string> pinned = PinnedStore.Load();
                if (pinned.Count == 0)
                {
                    AddNewTab(ThisPC);
                }
                else
                {
                    foreach (string path in pinned)
                        AddNewTab(path, -1, true, true);
                    tabControl.SelectedIndex = 0;
                }
            };
        }

        private void InitializeComponent()
        {
            this.tabControl = new ThemedTabControl();
            this.topPanel = new Panel();
            this.addTabButton = new Button();
            this.closeTabButton = new Button();
            this.backButton = new Button();
            this.forwardButton = new Button();
            this.addressBar = new TextBox();
            this.pinButton = new Button();
            this.optionsButton = new Button();
            this.aboutButton = new Button();
            this.toolTip = new ToolTip();

            this.topPanel.Dock = DockStyle.Top;
            this.topPanel.Height = 35;

            StyleButton(closeTabButton, "", GlyphClose, new Rectangle(5, 5, 28, 25), "Close current tab (Ctrl+F4)");
            this.closeTabButton.Click += CloseCurrentTab;

            StyleButton(backButton, "", GlyphBack, new Rectangle(38, 5, 28, 25), "Go back");
            this.backButton.Click += (s, e) => GoBack();

            StyleButton(forwardButton, "", GlyphForward, new Rectangle(71, 5, 28, 25), "Go forward");
            this.forwardButton.Click += (s, e) => GoForward();

            this.addressBar.Location = new Point(104, 6);
            this.addressBar.Height = 25;
            this.addressBar.KeyDown += AddressBar_KeyDown;
            this.topPanel.Resize += (s, e) => ResizeAddressBar();

            StyleButton(pinButton, "", GlyphPin, new Rectangle(0, 5, 28, 25), "Pin this tab (reopens on startup)");
            this.pinButton.Click += (s, e) => TogglePin();

            StyleButton(optionsButton, "Folder Options", GlyphSettings, new Rectangle(0, 5, 125, 25), "Open Windows Folder Options");
            this.optionsButton.Click += (s, e) => ShowFolderOptions();

            StyleButton(aboutButton, "About", GlyphInfo, new Rectangle(0, 5, 80, 25), "About this program");
            this.aboutButton.Click += (s, e) => ShowAbout();

            this.topPanel.Controls.AddRange(new Control[] {
                closeTabButton, backButton, forwardButton, addressBar, pinButton, optionsButton, aboutButton
            });

            this.tabControl.Dock = DockStyle.Fill;
            this.tabControl.SelectedIndexChanged += (s, e) =>
            {
                UpdateUIState();
                UpdateNewTabButtonPosition();
            };
            this.tabControl.Resize += (s, e) => UpdateNewTabButtonPosition();
            this.tabControl.MouseDown += TabControl_MouseDown;
            this.tabControl.MouseUp += TabControl_MouseUp;

            StyleButton(addTabButton, "", GlyphAdd, new Rectangle(0, 0, 28, 22), "New tab (Ctrl+T)");
            this.addTabButton.TabStop = false;
            this.addTabButton.Click += (s, e) => AddNewTab(ThisPC);

            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.topPanel);
            this.Controls.Add(this.addTabButton);
            this.addTabButton.BringToFront();

            this.Text = "MultiTab Windows Explorer";
            this.Size = new Size(920, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // --- Set the window title bar icon from the embedded exe resource ---
            Icon appIcon = AppIcon.Get();
            if (appIcon != null)
                this.Icon = appIcon;
        }

        private void ApplyTheme()
        {
            SuspendLayout();
            ThemeHelper.Apply(this, theme);
            MenuBorderHook.Dark = theme.IsDark;
            addTabButton.BackColor = theme.Back;
            addTabButton.FlatAppearance.BorderSize = 0;
            tabControl.Palette = theme;
            foreach (TabPage page in tabControl.TabPages)
            {
                page.UseVisualStyleBackColor = false;
                page.BackColor = theme.Back;
                var browser = page.Controls.Count > 0 ? page.Controls[0] as ExplorerBrowser : null;
                if (browser != null)
                {
                    ApplyPaneVisibility(browser);
                    ThemeHelper.ApplyToShellWindows(browser.Handle, theme.IsDark, true);
                    UpdateSplitter(browser);
                }
            }
            foreach (Button b in new List<Button>(buttonGlyphs.Keys))
                RefreshIcon(b);
            if (IsHandleCreated)
                ThemeHelper.SetDarkTitleBar(Handle, theme.IsDark);
            ResumeLayout(true);
            Invalidate(true);
        }

        private void ApplyPaneVisibility(ExplorerBrowser browser)
        {
            try
            {
                browser.NavigationOptions.PaneVisibility.Commands = PaneVisibilityState.Hide;
                browser.NavigationOptions.PaneVisibility.CommandsOrganize = PaneVisibilityState.Hide;
                browser.NavigationOptions.PaneVisibility.CommandsView = PaneVisibilityState.Hide;
            }
            catch { }
        }

        private void ApplyShellThemeSoon(ExplorerBrowser browser)
        {
            if (browser.IsDisposed || !browser.IsHandleCreated) return;
            ThemeHelper.ApplyToShellWindows(browser.Handle, theme.IsDark, false);
            UpdateSplitter(browser);
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!browser.IsDisposed && browser.IsHandleCreated)
                {
                    ThemeHelper.ApplyToShellWindows(browser.Handle, theme.IsDark, false);
                    UpdateSplitter(browser);
                }
            };
            timer.Start();
        }

        private void UpdateSplitter(ExplorerBrowser browser)
        {
            if (browser == null || browser.IsDisposed || !browser.IsHandleCreated) return;
            var overlay = browser.Tag as SplitterOverlay;
            if (overlay == null) return;
            Rectangle screenGap;
            if (!theme.IsDark || !ThemeHelper.TryGetSplitterGap(browser.Handle, out screenGap))
            {
                if (overlay.Visible) overlay.Visible = false;
                return;
            }
            Rectangle local = browser.RectangleToClient(screenGap);
            if (local.Width <= 0 || local.Width > 24 || local.Height <= 0)
            {
                if (overlay.Visible) overlay.Visible = false;
                return;
            }
            overlay.BackColor = Color.FromArgb(45, 45, 45);
            if (overlay.Bounds != local) overlay.Bounds = local;
            if (!overlay.Visible) overlay.Visible = true;
            ThemeHelper.RaiseToTop(overlay.Handle);
        }

        private void OnThemeMaybeChanged()
        {
            AppTheme detected = AppTheme.Detect();
            if (detected.IsDark == theme.IsDark) return;
            theme = detected;
            ThemeHelper.SetAppMode(theme.IsDark);
            ApplyTheme();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)OnThemeMaybeChanged);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeHelper.SetDarkTitleBar(Handle, theme.IsDark);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_SETTINGCHANGE && m.LParam != IntPtr.Zero)
            {
                try
                {
                    string area = Marshal.PtrToStringUni(m.LParam);
                    if (area == "ImmersiveColorSet" && IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)OnThemeMaybeChanged);
                }
                catch { }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            MenuBorderHook.Uninstall();
            if (splitterTimer != null) { splitterTimer.Stop(); splitterTimer.Dispose(); }
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnFormClosed(e);
        }

        public bool PreFilterMessage(ref Message m)
        {
            bool isMouseNav = m.Msg == WM_XBUTTONDOWN || m.Msg == WM_XBUTTONUP ||
                              m.Msg == WM_XBUTTONDBLCLK || m.Msg == WM_APPCOMMAND;
            if (m.Msg != WM_KEYDOWN && !isMouseNav) return false;
            if (Form.ActiveForm != this) return false;
            if (isMouseNav)
            {
                if (m.Msg == WM_APPCOMMAND)
                {
                    int appCommand = (unchecked((int)m.LParam.ToInt64()) >> 16) & 0x0FFF;
                    if (appCommand == 1) { GoBack(); return true; }
                    if (appCommand == 2) { GoForward(); return true; }
                    return false;
                }
                if (m.Msg == WM_XBUTTONUP)
                {
                    int xButton = (unchecked((int)m.WParam.ToInt64()) >> 16) & 0xFFFF;
                    if (xButton == 1) GoBack();
                    else if (xButton == 2) GoForward();
                }
                return true;
            }
            Keys mods = Control.ModifierKeys;
            if ((mods & Keys.Control) == 0 || (mods & Keys.Alt) != 0) return false;
            Keys key = (Keys)(m.WParam.ToInt32() & 0xFFFF);
            bool shift = (mods & Keys.Shift) != 0;
            bool repeat = (m.LParam.ToInt64() & 0x40000000L) != 0;
            switch (key)
            {
                case Keys.T:
                    if (shift) return false;
                    if (!repeat) AddNewTab(ThisPC);
                    return true;
                case Keys.Tab:
                    SwitchTab(shift ? -1 : 1);
                    return true;
                case Keys.F4:
                    if (shift) return false;
                    if (!repeat) CloseTab(tabControl.SelectedTab);
                    return true;
            }
            return false;
        }

        private void GoBack()
        {
            var browser = GetActiveBrowser();
            if (browser != null && browser.NavigationLog.CanNavigateBackward)
                browser.NavigateLogLocation(NavigationLogDirection.Backward);
        }

        private void GoForward()
        {
            var browser = GetActiveBrowser();
            if (browser != null && browser.NavigationLog.CanNavigateForward)
                browser.NavigateLogLocation(NavigationLogDirection.Forward);
        }

        private void SwitchTab(int direction)
        {
            int count = tabControl.TabCount;
            if (count < 2) return;
            tabControl.SelectedIndex = (tabControl.SelectedIndex + direction + count) % count;
        }

        private void StyleButton(Button button, string text, string glyph, Rectangle bounds, string tip)
        {
            button.Text = text;
            button.Bounds = bounds;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            toolTip.SetToolTip(button, tip);
            buttonGlyphs[button] = glyph;
            RefreshIcon(button);
        }

        private void RefreshIcon(Button button)
        {
            Image old = button.Image;
            button.Image = GlyphIcons.Create(buttonGlyphs[button], theme.Text);
            if (old != null) old.Dispose();
        }

        private void ResizeAddressBar()
        {
            aboutButton.Left = Math.Max(
                addressBar.Left + 105 + pinButton.Width + 5 + optionsButton.Width + 5,
                topPanel.ClientSize.Width - aboutButton.Width - 5);
            optionsButton.Left = aboutButton.Left - optionsButton.Width - 5;
            pinButton.Left = optionsButton.Left - pinButton.Width - 5;
            int width = pinButton.Left - 5 - addressBar.Left;
            addressBar.Width = Math.Max(100, width);
        }

        private void UpdateNewTabButtonPosition()
        {
            if (!tabControl.IsHandleCreated) return;
            int x = 4, y = 2, h = 22;
            if (tabControl.TabCount > 0)
            {
                Rectangle last = tabControl.GetTabRect(tabControl.TabCount - 1);
                x = last.Right + 4;
                y = last.Top;
                h = last.Height;
            }
            int maxX = tabControl.ClientSize.Width - addTabButton.Width - 45;
            x = Math.Min(x, Math.Max(4, maxX));
            addTabButton.Height = Math.Max(18, h - 2);
            addTabButton.Location = this.PointToClient(tabControl.PointToScreen(new Point(x, y + 1)));
            addTabButton.BringToFront();
        }

        private void AddNewTab(string path, int insertIndex = -1, bool pinned = false, bool silent = false)
        {
            var state = new TabState
            {
                Pinned = pinned,
                PinnedPath = pinned ? path : null
            };
            TabPage tabPage = new TabPage
            {
                Tag = state,
                UseVisualStyleBackColor = false,
                BackColor = theme.Back
            };
            RefreshTabTitle(tabPage);
            ExplorerBrowser explorerBrowser = new ExplorerBrowser();
            explorerBrowser.Dock = DockStyle.Fill;
            ApplyPaneVisibility(explorerBrowser);
            var splitterOverlay = new SplitterOverlay();
            explorerBrowser.Controls.Add(splitterOverlay);
            explorerBrowser.Tag = splitterOverlay;
            explorerBrowser.NavigationComplete += (s, e) =>
            {
                var location = explorerBrowser.NavigationLog.CurrentLocation;
                if (location != null)
                {
                    state.Title = location.Name;
                    RefreshTabTitle(tabPage);
                    if (tabControl.SelectedTab == tabPage)
                        addressBar.Text = location.ParsingName;
                }
                UpdateUIState();
                ApplyShellThemeSoon(explorerBrowser);
            };
            tabPage.Controls.Add(explorerBrowser);
            if (insertIndex >= 0 && insertIndex <= tabControl.TabCount)
                tabControl.TabPages.Insert(insertIndex, tabPage);
            else
                tabControl.TabPages.Add(tabPage);
            tabControl.SelectedTab = tabPage;
            UpdateNewTabButtonPosition();
            NavigateToPath(explorerBrowser, path, !silent);
        }

        private void RefreshTabTitle(TabPage tab)
        {
            var state = tab.Tag as TabState;
            if (state == null) return;
            tab.Text = (state.Pinned ? new string(' ', 6) : "") + state.Title;
            tabControl.Invalidate();
            UpdateNewTabButtonPosition();
        }

        private void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var browser = GetActiveBrowser();
                if (browser != null)
                    NavigateToPath(browser, addressBar.Text, true);
            }
        }

        private void NavigateToPath(ExplorerBrowser browser, string path, bool showErrors)
        {
            if (string.IsNullOrWhiteSpace(path)) path = ThisPC;
            try
            {
                browser.Navigate(ShellObject.FromParsingName(path));
            }
            catch (Exception ex)
            {
                if (showErrors)
                    MessageBox.Show($"Unable to open target folder:\n{ex.Message}", "Navigation Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                {
                    try { browser.Navigate(ShellObject.FromParsingName(ThisPC)); } catch { }
                }
            }
        }

        private void CloseCurrentTab(object sender, EventArgs e) { CloseTab(tabControl.SelectedTab); }

        private void CloseTab(TabPage tab)
        {
            if (tab == null) return;
            if (tabControl.TabCount > 1)
            {
                bool wasPinned = tab.Tag is TabState state && state.Pinned;
                var browser = tab.Controls.Count > 0 ? tab.Controls[0] as ExplorerBrowser : null;
                browser?.Dispose();
                tabControl.TabPages.Remove(tab);
                if (wasPinned) SavePinnedTabs();
                UpdateNewTabButtonPosition();
            }
            else Application.Exit();
        }

        private void TogglePin()
        {
            TabPage tab = tabControl.SelectedTab;
            var state = tab == null ? null : tab.Tag as TabState;
            if (state == null) return;
            if (state.Pinned)
            {
                state.Pinned = false;
                state.PinnedPath = null;
            }
            else
            {
                var browser = GetActiveBrowser();
                string path = browser == null || browser.NavigationLog.CurrentLocation == null
                    ? null : browser.NavigationLog.CurrentLocation.ParsingName;
                state.PinnedPath = string.IsNullOrWhiteSpace(path) ? ThisPC : path;
                state.Pinned = true;
            }
            RefreshTabTitle(tab);
            SavePinnedTabs();
            UpdatePinButton();
        }

        private void SavePinnedTabs()
        {
            var paths = new List<string>();
            foreach (TabPage page in tabControl.TabPages)
            {
                var state = page.Tag as TabState;
                if (state != null && state.Pinned && !string.IsNullOrWhiteSpace(state.PinnedPath))
                    paths.Add(state.PinnedPath);
            }
            PinnedStore.Save(paths);
        }

        private void UpdatePinButton()
        {
            TabPage tab = tabControl.SelectedTab;
            var state = tab == null ? null : tab.Tag as TabState;
            bool pinned = state != null && state.Pinned;
            pinButton.Enabled = state != null;
            toolTip.SetToolTip(pinButton, pinned ? "Unpin this tab" : "Pin this tab (reopens on startup)");
            string glyph = pinned ? GlyphUnpin : GlyphPin;
            if (buttonGlyphs[pinButton] != glyph)
            {
                buttonGlyphs[pinButton] = glyph;
                RefreshIcon(pinButton);
            }
        }

        private bool hasLastClick;
        private int lastClickTick;
        private Point lastClickPoint;
        private int lastClickTabIndex = -1;

        private int GetTabIndexAt(Point p)
        {
            for (int i = 0; i < tabControl.TabCount; i++)
            {
                if (tabControl.GetTabRect(i).Contains(p)) return i;
            }
            return -1;
        }

        private void TabControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int index = GetTabIndexAt(e.Location);
            if (index < 0) { hasLastClick = false; return; }
            int now = Environment.TickCount;
            Size dragSize = SystemInformation.DoubleClickSize;
            bool isDouble = hasLastClick
                && unchecked(now - lastClickTick) <= SystemInformation.DoubleClickTime
                && Math.Abs(e.X - lastClickPoint.X) <= dragSize.Width
                && Math.Abs(e.Y - lastClickPoint.Y) <= dragSize.Height
                && index == lastClickTabIndex;
            if (isDouble) { hasLastClick = false; DuplicateTab(index); return; }
            hasLastClick = true;
            lastClickTick = now;
            lastClickPoint = e.Location;
            lastClickTabIndex = index;
        }

        private void TabControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Middle) return;
            int index = GetTabIndexAt(e.Location);
            if (index >= 0) CloseTab(tabControl.TabPages[index]);
        }

        private void DuplicateTab(int index)
        {
            var tab = tabControl.TabPages[index];
            var browser = tab.Controls.Count > 0 ? tab.Controls[0] as ExplorerBrowser : null;
            string path = browser == null || browser.NavigationLog.CurrentLocation == null
                ? null : browser.NavigationLog.CurrentLocation.ParsingName;
            AddNewTab(string.IsNullOrWhiteSpace(path) ? ThisPC : path, index + 1);
        }

        private void ShowFolderOptions()
        {
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,Options_RunDLL 0")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open Folder Options:\n{ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowAbout()
        {
            using (var about = new AboutForm(theme))
                about.ShowDialog(this);
        }

        private ExplorerBrowser GetActiveBrowser()
        {
            if (tabControl.SelectedTab != null && tabControl.SelectedTab.Controls.Count > 0)
                return tabControl.SelectedTab.Controls[0] as ExplorerBrowser;
            return null;
        }

        private void UpdateUIState()
        {
            var browser = GetActiveBrowser();
            if (browser != null)
            {
                backButton.Enabled = browser.NavigationLog.CanNavigateBackward;
                forwardButton.Enabled = browser.NavigationLog.CanNavigateForward;
                if (browser.NavigationLog.CurrentLocation != null)
                    addressBar.Text = browser.NavigationLog.CurrentLocation.ParsingName;
            }
            UpdatePinButton();
        }
    }

    // =====================================================================
    //  About dialog
    // =====================================================================
    public class AboutForm : Form
    {
        private const string AuthorUrl = "https://github.com/nidaxon";
        private const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";
        private readonly AppTheme theme;

        public AboutForm(AppTheme theme)
        {
            this.theme = theme;
            this.Text = "About";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding(20, 15, 20, 15);

            // --- Set the window title bar icon from the embedded exe resource ---
            Icon appIcon = AppIcon.Get();
            if (appIcon != null)
                this.Icon = appIcon;

            var italicFont = new Font(this.Font, FontStyle.Italic);

            var titleLabel = new Label
            {
                Text = "MultiTab Windows Explorer",
                Font = new Font(this.Font.FontFamily, 12f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            var createdLabel = new Label { Text = "Created by:", AutoSize = true, Margin = new Padding(0) };
            var authorLink = new LinkLabel { Text = "Ixnando Ondang", AutoSize = true, Margin = new Padding(0) };
            authorLink.LinkClicked += (s, e) => OpenUrl(AuthorUrl);

            var createdRow = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Margin = new Padding(0, 0, 0, 10)
            };
            createdRow.Controls.Add(createdLabel);
            createdRow.Controls.Add(authorLink);

            var licensePrefix = new Label { Text = "This program is licensed under", Font = italicFont, AutoSize = true, Margin = new Padding(0) };
            var licenseLink = new LinkLabel { Text = "GPLv3", Font = italicFont, AutoSize = true, Margin = new Padding(0) };
            licenseLink.LinkClicked += (s, e) => OpenUrl(LicenseUrl);
            var licenseSuffix = new Label { Text = "license", Font = italicFont, AutoSize = true, Margin = new Padding(0) };

            var licenseRow = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Margin = new Padding(0)
            };
            licenseRow.Controls.Add(licensePrefix);
            licenseRow.Controls.Add(licenseLink);
            licenseRow.Controls.Add(licenseSuffix);

            var disclaimerLabel = new Label
            {
                Text = "This program is an independent project and is not affiliated with, " +
                       "endorsed by, or sponsored by Microsoft Corporation. " +
                       "Windows is a registered trademark of Microsoft Corporation.",
                Font = new Font(this.Font.FontFamily, 8.25f, FontStyle.Regular),
                Tag = "muted", AutoSize = true, MaximumSize = new Size(330, 0),
                Margin = new Padding(0, 15, 0, 0)
            };

            var layout = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Dock = DockStyle.Fill
            };
            layout.Controls.Add(titleLabel);
            layout.Controls.Add(createdRow);
            layout.Controls.Add(licenseRow);
            layout.Controls.Add(disclaimerLabel);

            this.Controls.Add(layout);
            ThemeHelper.Apply(this, theme);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeHelper.SetDarkTitleBar(Handle, theme.IsDark);
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open link:\n{ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ThemeHelper.SetAppMode(AppTheme.Detect().IsDark);
            Application.Run(new MainForm());
        }
    }
}