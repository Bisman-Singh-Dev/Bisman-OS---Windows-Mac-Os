// Bisman OS 2 - core: design tokens, settings, native interop, shell icons, UI helpers, notifications.
global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Controls.Primitives;
global using System.Windows.Input;
global using System.Windows.Interop;
global using System.Windows.Media;
global using System.Windows.Media.Animation;
global using System.Windows.Media.Effects;
global using System.Windows.Media.Imaging;
global using System.Windows.Shapes;
global using System.Windows.Threading;
global using Path = System.IO.Path;
global using WDock = System.Windows.Controls.Dock;

namespace BismanOS;

// ---------------------------------------------------------------- design tokens
public static class Th
{
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Small = new("Segoe UI Variable Small, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Code, Cascadia Mono, Consolas");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly Color Midnight = C("#07090E"), Graphite = C("#141820"), Navy = C("#0B1222");
    public static readonly Color Electric = C("#3D8BFF"), Ice = C("#A8D8FF"), Violet = C("#8B7CFF"), Cyan = C("#3EE0FF"), Silver = C("#C9D1DC");
    public static readonly Color Grass = C("#4CD93A"), Red = C("#FF5F57"), Yellow = C("#FEBC2E"), Green = C("#28C840");

    public static readonly SolidColorBrush Fg = B("#F3F6FB"), Fg2 = B("#B4BDCB"), Fg3 = B("#7D8797");
    public static readonly SolidColorBrush Hairline = new(Color.FromArgb(30, 255, 255, 255));
    public static readonly SolidColorBrush Hover = new(Color.FromArgb(24, 255, 255, 255));
    public static readonly SolidColorBrush Press = new(Color.FromArgb(40, 255, 255, 255));
    public static readonly SolidColorBrush Card = new(Color.FromArgb(20, 255, 255, 255));
    public static readonly SolidColorBrush Inset = new(Color.FromArgb(70, 0, 0, 0));
    public static readonly SolidColorBrush GrassBrush = new(Grass);
    public static readonly SolidColorBrush Danger = B("#FF6B6B");

    // Accent is one shared brush instance so a colour change repaints everything live.
    public static readonly SolidColorBrush Accent = new(Electric);
    public static readonly SolidColorBrush AccentSoft = new(Color.FromArgb(56, 0x3D, 0x8B, 0xFF));
    public static event Action AccentChanged;

    public static Cursor MacCursor = Cursors.Arrow;

    public static readonly Dictionary<string, Color> Accents = new()
    {
        ["blue"] = Electric, ["violet"] = Violet, ["cyan"] = Cyan, ["silver"] = Silver, ["green"] = Grass
    };

    public static void SetAccent(string name)
    {
        if (!Accents.TryGetValue(name ?? "", out var c)) c = Electric;
        Accent.Color = c;
        AccentSoft.Color = Color.FromArgb(56, c.R, c.G, c.B);
        // Templates use their own (frozen) copies through DynamicResource, so replace those.
        if (Application.Current != null)
        {
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(c);
            Application.Current.Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(56, c.R, c.G, c.B));
        }
        AccentChanged?.Invoke();
    }

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static SolidColorBrush B(string hex) => new(C(hex));
    public static SolidColorBrush A(byte a, Color c) => new(Color.FromArgb(a, c.R, c.G, c.B));
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    public static bool Has(this string s, string q) => s != null && q != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
}

// ---------------------------------------------------------------- animation helpers
public static class Anim
{
    public static bool Reduced => Settings.Bool("reduceMotion");
    public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction InOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
    public static readonly IEasingFunction Spring = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };

    public static void Run(IAnimatable target, DependencyProperty dp, double from, double to, double ms, IEasingFunction ease = null, Action done = null, double delay = 0)
    {
        if (Reduced) { ms = 1; delay = 0; }
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay) };
        Hook(target, dp, a, to, done);
    }

    public static void To(IAnimatable target, DependencyProperty dp, double to, double ms, IEasingFunction ease = null, Action done = null)
    {
        if (Reduced) ms = 1;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out };
        Hook(target, dp, a, to, done);
    }

    static void Hook(IAnimatable target, DependencyProperty dp, DoubleAnimation a, double to, Action done)
    {
        a.Completed += (_, _) =>
        {
            ((DependencyObject)target).SetValue(dp, to);
            target.BeginAnimation(dp, null);
            done?.Invoke();
        };
        target.BeginAnimation(dp, a);
    }

    public static void Scale(ScaleTransform st, double to, double ms = 170)
    {
        To(st, ScaleTransform.ScaleXProperty, to, ms);
        To(st, ScaleTransform.ScaleYProperty, to, ms);
    }
}

// ---------------------------------------------------------------- settings (key=value file in %APPDATA%\BismanOS)
public static class Settings
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BismanOS");
    static readonly string FilePath = Path.Combine(Dir, "settings.ini");
    static readonly Dictionary<string, string> d = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["liveWallpaper"] = "true", ["particles"] = "true", ["glassBlur"] = "true", ["glassTint"] = "0.55",
        ["reduceMotion"] = "false", ["accent"] = "blue", ["dockMagnify"] = "true", ["dockSize"] = "50",
        ["showDesktopIcons"] = "true", ["showWidgets"] = "true", ["clock24"] = "true", ["showSeconds"] = "false",
        ["dnd"] = "false", ["notifyBattery"] = "true", ["notifyNetwork"] = "true", ["notifyTerminal"] = "true",
        ["wallpaper"] = "", ["onboarded"] = "false", ["hideTaskbar"] = "true", ["fastGpu"] = "true"
    };
    public static event Action<string> Changed;

    static Settings()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
        }
        catch { }
    }

    public static string Get(string k) => d.TryGetValue(k, out var v) ? v : (defaults.TryGetValue(k, out var dv) ? dv : "");
    public static bool Bool(string k) => Get(k) == "true";
    public static double Num(string k, double fallback = 0) => double.TryParse(Get(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    public static void SetBool(string k, bool v) => Set(k, v ? "true" : "false");
    public static void SetNum(string k, double v) => Set(k, v.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static void Set(string k, string v)
    {
        if (Get(k) == v) return;
        d[k] = v;
        Save();
        Changed?.Invoke(k);
    }

    static void Save()
    {
        try { File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value)); } catch { }
    }

    public static List<string> LoadList(string name)
    {
        try
        {
            var p = Path.Combine(Dir, name + ".txt");
            if (File.Exists(p)) return File.ReadAllLines(p).Where(l => l.Length > 0).ToList();
        }
        catch { }
        return new List<string>();
    }

    public static bool HasList(string name) => File.Exists(Path.Combine(Dir, name + ".txt"));

    public static void SaveList(string name, IEnumerable<string> items)
    {
        try { File.WriteAllLines(Path.Combine(Dir, name + ".txt"), items); } catch { }
    }

    public static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(Dir, "error.log"), DateTime.Now.ToString("s") + "  " + text + Environment.NewLine); } catch { }
    }
}

// ---------------------------------------------------------------- native interop
public static class Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO sei);
    [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory; public int nShow;
        public IntPtr hInstApp, lpIDList; public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
    }

    public static string ProcessPath(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024); int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) == 0)
            {
                var s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); return s;
            }
        }
        catch { }
        return null;
    }

    public static string Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory { [PreserveSig] int GetImage(Native.SIZE size, int flags, out IntPtr phbm); }

// ---------------------------------------------------------------- real icons & thumbnails from the Windows shell
public static class ShellImages
{
    static readonly System.Collections.Concurrent.BlockingCollection<(string key, string path, int size, bool iconOnly)> queue = new();
    static readonly Dictionary<string, ImageSource> cache = new();
    static readonly Dictionary<string, List<Action<ImageSource>>> waiting = new();
    static Dispatcher ui;

    public static void Init(Dispatcher d)
    {
        ui = d;
        for (int i = 0; i < 2; i++)
        {
            var t = new Thread(Worker) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    // Gets the real shell icon (or a thumbnail when iconOnly is false) for a file path, folder or shell:AppsFolder item.
    public static void Get(string path, int size, bool iconOnly, Action<ImageSource> cb)
    {
        if (string.IsNullOrEmpty(path)) { cb(null); return; }
        string key = path + "|" + size + "|" + iconOnly;
        lock (cache)
        {
            if (cache.TryGetValue(key, out var img)) { cb(img); return; }
            if (waiting.TryGetValue(key, out var list)) { list.Add(cb); return; }
            waiting[key] = new List<Action<ImageSource>> { cb };
        }
        queue.Add((key, path, size, iconOnly));
    }

    static void Worker()
    {
        foreach (var job in queue.GetConsumingEnumerable())
        {
            ImageSource img = null;
            try { img = Load(job.path, job.size, job.iconOnly); } catch { }
            List<Action<ImageSource>> cbs;
            lock (cache) { cache[job.key] = img; waiting.TryGetValue(job.key, out cbs); waiting.Remove(job.key); }
            if (cbs != null) ui.BeginInvoke(new Action(() => { foreach (var cb in cbs) cb(img); }), DispatcherPriority.Background);
        }
    }

    static ImageSource Load(string path, int size, bool iconOnly)
    {
        Guid iid = typeof(IShellItemImageFactory).GUID;
        if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f) != 0 || f == null) return null;
        try
        {
            if (f.GetImage(new Native.SIZE { cx = size, cy = size }, iconOnly ? 0x4 : 0x0, out var hbm) != 0) return null;
            return FromHBitmap(hbm);
        }
        finally { Marshal.ReleaseComObject(f); }
    }

    static BitmapSource FromHBitmap(IntPtr hbm)
    {
        try
        {
            Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.BITMAP)), out var bm);
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var px = new byte[w * h * 4];
            var dc = Native.GetDC(IntPtr.Zero);
            Native.GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0);
            Native.ReleaseDC(IntPtr.Zero, dc);
            bool anyAlpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            src.Freeze();
            return src;
        }
        finally { Native.DeleteObject(hbm); }
    }
}

// ---------------------------------------------------------------- macOS-style cursor (kept from Bisman OS 1)
public static class MacCursorMaker
{
    public static Cursor Create(double scale)
    {
        int px = (int)Math.Round(32 * scale);
        var geo = Geometry.Parse("M 3,2 L 3,24 L 8.5,19 L 12,27 L 15.5,25.5 L 12,17.8 L 19.5,17.8 Z");
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(0.7, 1.3));
            var shadow = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            dc.DrawGeometry(shadow, new Pen(shadow, 3) { LineJoin = PenLineJoin.Round }, geo);
            dc.Pop();
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.8) { LineJoin = PenLineJoin.Round }, geo);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var pix = new byte[px * px * 4];
        rtb.CopyPixels(pix, px * 4, 0);
        for (int i = 0; i < pix.Length; i += 4)
        {
            int a = pix[i + 3];
            if (a > 0 && a < 255) for (int c = 0; c < 3; c++) pix[i + c] = (byte)Math.Min(255, pix[i + c] * 255 / a);
        }
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int maskRow = ((px + 31) / 32) * 4;
        w.Write((short)0); w.Write((short)2); w.Write((short)1);
        w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)0); w.Write((byte)0);
        w.Write((short)Math.Round(3 * scale)); w.Write((short)Math.Round(2 * scale));
        w.Write(40 + pix.Length + maskRow * px); w.Write(22);
        w.Write(40); w.Write(px); w.Write(px * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(pix.Length); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = px - 1; y >= 0; y--) w.Write(pix, y * px * 4, px * 4);
        w.Write(new byte[maskRow * px]);
        w.Flush(); ms.Position = 0;
        return new Cursor(ms);
    }
}

// ---------------------------------------------------------------- UI helpers
public static class Ui
{
    public static readonly (string glyph, string text, Action act) Sep = (null, null, null);

    public static TextBlock Txt(string s, double size = 14, FontWeight? weight = null, Brush fg = null, FontFamily font = null)
    {
        return new TextBlock
        {
            Text = s, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = fg ?? Th.Fg,
            FontFamily = font ?? (size >= 20 ? Th.Display : size <= 11.5 ? Th.Small : Th.Text),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static TextBlock Tab(TextBlock t) { System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }

    public static TextBlock Glyph(string g, double size = 16, Brush fg = null)
    {
        return new TextBlock { Text = g, FontFamily = Th.Icons, FontSize = size, Foreground = fg ?? Th.Fg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    // Click with a soft press-in, like macOS controls. Handles the mouse-down so window dragging never steals it.
    public static void OnClick(FrameworkElement el, Action act, bool press = true)
    {
        ScaleTransform st = null;
        if (press)
        {
            st = el.RenderTransform as ScaleTransform;
            if (st == null && el.RenderTransform == Transform.Identity) { st = new ScaleTransform(1, 1); el.RenderTransform = st; el.RenderTransformOrigin = new Point(0.5, 0.5); }
        }
        bool down = false;
        el.MouseLeftButtonDown += (_, e) => { down = true; e.Handled = true; if (st != null) Anim.Scale(st, 0.95, 90); };
        el.MouseLeftButtonUp += (_, e) =>
        {
            if (st != null) Anim.Scale(st, 1, 140);
            if (down) { down = false; e.Handled = true; act(); }
        };
        el.MouseLeave += (_, _) => { if (down && st != null) Anim.Scale(st, 1, 140); down = false; };
    }

    public static void Hoverable(Border b, Brush normal = null, Brush hover = null)
    {
        b.Background = normal ?? Brushes.Transparent;
        b.MouseEnter += (_, _) => b.Background = hover ?? Th.Hover;
        b.MouseLeave += (_, _) => b.Background = normal ?? Brushes.Transparent;
    }

    public static Border IconButton(string glyph, Action act, string tip = null, double size = 30, double glyphSize = 14)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 3.2), Child = Glyph(glyph, glyphSize, Th.Fg2), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
        Hoverable(b);
        if (act != null) OnClick(b, act);
        return b;
    }

    public static Border Pill(string text, Action act, string glyph = null, bool accent = false, bool danger = false, double size = 13)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Th.Icons, FontSize = size - 1, Foreground = Th.Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        sp.Children.Add(Txt(text, size, FontWeights.SemiBold));
        var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 7, 14, 8), Child = sp };
        if (accent) { b.Background = Th.Accent; b.MouseEnter += (_, _) => b.Opacity = 0.88; b.MouseLeave += (_, _) => b.Opacity = 1; }
        else if (danger) { b.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x3F, 0x3F)); b.MouseEnter += (_, _) => b.Opacity = 0.88; b.MouseLeave += (_, _) => b.Opacity = 1; }
        else Hoverable(b, Th.Card, Th.Press);
        OnClick(b, act);
        return b;
    }

    public static ContextMenu Menu(params (string glyph, string text, Action act)[] items)
    {
        var cm = new ContextMenu { Cursor = Th.MacCursor };
        foreach (var it in items)
        {
            if (it.text == null) { cm.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = it.text, Icon = it.glyph == null ? null : Glyph(it.glyph, 13, Th.Fg2) };
            var act = it.act;
            mi.Click += (_, _) => act?.Invoke();
            cm.Items.Add(mi);
        }
        return cm;
    }

    public static void ShowMenu(UIElement target, ContextMenu cm, PlacementMode mode = PlacementMode.MousePoint)
    {
        cm.PlacementTarget = target; cm.Placement = mode; cm.IsOpen = true;
    }

    public static Border Hairline(bool vertical = false) =>
        vertical ? new Border { Width = 1, Background = Th.Hairline } : new Border { Height = 1, Background = Th.Hairline };

    public static FrameworkElement Logo(double size)
    {
        var g = new Grid { Width = size, Height = size };
        var lg = new LinearGradientBrush(Th.Electric, Th.Violet, 45);
        g.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.28), Background = lg });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.28), Background = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.6)), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), BorderThickness = new Thickness(1) });
        g.Children.Add(new TextBlock { Text = "B", FontFamily = Th.Display, FontWeight = FontWeights.SemiBold, FontSize = size * 0.58, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, size * 0.04) });
        return g;
    }

    public static string Ago(DateTime t)
    {
        var s = DateTime.Now - t;
        if (s.TotalSeconds < 60) return "now";
        if (s.TotalMinutes < 60) return (int)s.TotalMinutes + "m ago";
        if (s.TotalHours < 24) return (int)s.TotalHours + "h ago";
        if (s.TotalDays < 7) return (int)s.TotalDays + "d ago";
        return t.ToString("d MMM");
    }

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return (i == 0 ? b.ToString("0") : b.ToString(b < 10 ? "0.0" : "0")) + " " + u[i];
    }

    public static bool IsInside(DependencyObject child, DependencyObject parent)
    {
        while (child != null) { if (child == parent) return true; child = child is Visual || child is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child); }
        return false;
    }
}

// ---------------------------------------------------------------- launching things in Windows
public static class Shell
{
    public static bool Run(string file, string args = null, string dir = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = true };
            if (args != null) psi.Arguments = args;
            if (dir != null) psi.WorkingDirectory = dir;
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Notify.Post("Bisman OS", "", "Couldn't open " + Path.GetFileName(file), ex.Message);
            return false;
        }
    }

    public static void Open(string path) => Run(path);
    public static void Reveal(string path) => Run("explorer.exe", "/select,\"" + path + "\"");
    public static void OpenWith(string path) => Run("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path);
    public static void Web(string url) => Run(url);

    public static void Properties(string path)
    {
        var sei = new Native.SHELLEXECUTEINFO { lpVerb = "properties", lpFile = path, nShow = 5, fMask = 0xC };
        sei.cbSize = Marshal.SizeOf(sei);
        Native.ShellExecuteEx(ref sei);
    }

    public static bool Recycle(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Notify.Post("Files", "", "Couldn't delete " + Path.GetFileName(path), ex.Message); return false; }
    }

    public static string Rename(string path, string newName)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            var dest = Path.Combine(dir, newName);
            if (string.Equals(dest, path, StringComparison.Ordinal)) return path;
            if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest);
            return dest;
        }
        catch (Exception ex) { Notify.Post("Files", "", "Couldn't rename", ex.Message); return null; }
    }

    public static string NewFolder(string parent)
    {
        string name = "New folder"; int n = 2;
        while (Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name))) name = "New folder (" + n++ + ")";
        try { var p = Path.Combine(parent, name); Directory.CreateDirectory(p); return p; }
        catch (Exception ex) { Notify.Post("Files", "", "Couldn't create folder", ex.Message); return null; }
    }
}

// ---------------------------------------------------------------- Bisman OS notification centre data
public class Note
{
    public string Source, Glyph, Title, Body;
    public DateTime Time = DateTime.Now;
    public Action Click;
}

public static class Notify
{
    public static readonly List<Note> Items = new();
    public static event Action<Note> Posted;
    public static event Action Changed;

    public static void Post(string source, string glyph, string title, string body, Action click = null)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            var n = new Note { Source = source, Glyph = glyph, Title = title, Body = body, Click = click };
            Items.Insert(0, n);
            if (Items.Count > 60) Items.RemoveAt(Items.Count - 1);
            Posted?.Invoke(n);
            Changed?.Invoke();
        }));
    }

    public static void Dismiss(Note n) { Items.Remove(n); Changed?.Invoke(); }
    public static void Clear() { Items.Clear(); Changed?.Invoke(); }
}
