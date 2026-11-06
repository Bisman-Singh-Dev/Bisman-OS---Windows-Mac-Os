// Bisman OS - a custom desktop shell that runs on top of Windows.
// Build with build.bat (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BismanOS
{
    public static class Theme
    {
        public static readonly FontFamily Brand = new FontFamily("Gabriola");
        public static readonly FontFamily Display = new FontFamily("Bahnschrift Light, Bahnschrift, Segoe UI Light");
        public static readonly FontFamily DisplayBold = new FontFamily("Bahnschrift SemiBold, Bahnschrift, Segoe UI Semibold");
        public static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Code, Cascadia Mono, Consolas");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly Color GrassColor = Color.FromRgb(0x4C, 0xD9, 0x3A);
        public static readonly SolidColorBrush Grass = new SolidColorBrush(GrassColor);
        public static Cursor MacCursor = Cursors.Arrow;

        public static SolidColorBrush Argb(byte a, byte r, byte g, byte b) { return new SolidColorBrush(Color.FromArgb(a, r, g, b)); }

        // macOS-style hover: element grows slightly with a smooth ease and its background brightens.
        public static void MacHover(FrameworkElement el, Border bg, Brush normal, Brush hover)
        {
            var st = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            if (bg != null) bg.Background = normal;
            el.MouseEnter += (s, e) => { Scale(st, 1.06); if (bg != null) bg.Background = hover; };
            el.MouseLeave += (s, e) => { Scale(st, 1.0); if (bg != null) bg.Background = normal; };
        }

        public static void Scale(ScaleTransform st, double to)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public static void Launch(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file);
                if (args != null) psi.Arguments = args;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open \"" + file + "\"\n\n" + ex.Message, "Bisman OS");
            }
        }
    }

    // Draws a macOS-style arrow (black with white outline and soft shadow) and turns it into a real Windows cursor.
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
                if (a > 0 && a < 255)
                    for (int c = 0; c < 3; c++) pix[i + c] = (byte)Math.Min(255, pix[i + c] * 255 / a);
            }

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int maskRow = ((px + 31) / 32) * 4;
            int imageBytes = 40 + pix.Length + maskRow * px;
            // .cur header
            w.Write((short)0); w.Write((short)2); w.Write((short)1);
            w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)Math.Round(3 * scale)); w.Write((short)Math.Round(2 * scale)); // hotspot = arrow tip
            w.Write(imageBytes); w.Write(22);
            // bitmap header
            w.Write(40); w.Write(px); w.Write(px * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(pix.Length); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = px - 1; y >= 0; y--) w.Write(pix, y * px * 4, px * 4);
            w.Write(new byte[maskRow * px]);
            w.Flush();
            ms.Position = 0;
            return new Cursor(ms);
        }
    }

    static class Native
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
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
    }

    class AppDef
    {
        public string Name, Glyph; public Color C1, C2; public Action Run; public FontFamily Font;
        public AppDef(string name, string glyph, Color c1, Color c2, Action run) { Name = name; Glyph = glyph; C1 = c1; C2 = c2; Run = run; }
    }

    class DockItem
    {
        public AppDef App; public Border Icon; public double Size, Target;
        public TranslateTransform Bounce = new TranslateTransform();
    }

    public class Desktop : Window
    {
        const double BaseSize = 44, MaxSize = 74, Range = 170, Gap = 10;
        Grid root;
        Canvas overlay;
        TextBlock bigClock, bigDate, trayTime, trayDate, hoverText;
        Border hoverLabel;
        StackPanel dock, winPanel, appList;
        readonly List<DockItem> items = new List<DockItem>();
        readonly List<AppDef> apps = new List<AppDef>();
        readonly List<KeyValuePair<string, string>> allApps = new List<KeyValuePair<string, string>>();
        readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>();
        Border startMenu;
        TranslateTransform startShift = new TranslateTransform();
        TextBox searchBox;
        bool mouseInDock; double mouseX; double labelOpacity;
        IntPtr myHwnd; string winSignature = "";

        public Desktop()
        {
            Title = "Bisman OS";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            FontFamily = Theme.Ui;
            Foreground = Brushes.White;
            root = new Grid();
            Content = root;

            DefineApps();
            BuildWallpaper();
            BuildGreeting();
            BuildTaskbar();
            BuildStartMenu();
            overlay = new Canvas { IsHitTestVisible = false };
            hoverText = new TextBlock { FontSize = 12.5, Foreground = Brushes.White, FontFamily = Theme.Ui };
            hoverLabel = new Border { Background = Theme.Argb(235, 0x20, 0x20, 0x2C), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 4, 10, 5), Child = hoverText, Opacity = 0, BorderBrush = Theme.Argb(50, 255, 255, 255), BorderThickness = new Thickness(1) };
            overlay.Children.Add(hoverLabel);
            root.Children.Add(overlay);

            SourceInitialized += (s, e) =>
            {
                myHwnd = new WindowInteropHelper(this).Handle;
                double scale = 1;
                var src = PresentationSource.FromVisual(this);
                if (src != null) scale = src.CompositionTarget.TransformToDevice.M11;
                try { Theme.MacCursor = MacCursorMaker.Create(scale); } catch { }
                Cursor = Theme.MacCursor;
            };

            root.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(root);
                double half = items.Count * (BaseSize + Gap) / 2 + 60;
                mouseInDock = p.Y > root.ActualHeight - 150 && Math.Abs(p.X - root.ActualWidth / 2) < half;
                mouseX = p.X;
            };
            root.MouseLeave += (s, e) => { mouseInDock = false; };
            root.PreviewMouseDown += (s, e) =>
            {
                if (startMenu.Visibility == Visibility.Visible && !startMenu.IsMouseOver && !items[0].Icon.IsMouseOver) HideStart();
            };
            PreviewKeyDown += OnKey;
            CompositionTarget.Rendering += Tick;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => { UpdateClock(); RefreshWindows(); };
            timer.Start();
            UpdateClock();
            new Thread(LoadApps) { IsBackground = true }.Start();
        }

        void DefineApps()
        {
            var start = new AppDef("Start", "B", Color.FromRgb(0x4C, 0xD9, 0x3A), Color.FromRgb(0x0E, 0x8C, 0x7A), ToggleStart) { Font = Theme.Brand };
            apps.Add(start);
            apps.Add(new AppDef("Terminal", "", Color.FromRgb(0x1C, 0x2B, 0x1C), Color.FromRgb(0x06, 0x0A, 0x06), OpenTerminal));
            apps.Add(new AppDef("Files", "", Color.FromRgb(0xFF, 0xC8, 0x3D), Color.FromRgb(0xF0, 0x8A, 0x1C), () => Theme.Launch("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
            apps.Add(new AppDef("Browser", "", Color.FromRgb(0x4F, 0xA8, 0xFF), Color.FromRgb(0x1E, 0x4F, 0xD8), () => Theme.Launch("https://www.google.com", null)));
            apps.Add(new AppDef("Notes", "", Color.FromRgb(0xFF, 0x8F, 0xB1), Color.FromRgb(0xD9, 0x3A, 0x7A), () => Theme.Launch("notepad.exe", null)));
            apps.Add(new AppDef("Calculator", "", Color.FromRgb(0x8E, 0x8E, 0x9E), Color.FromRgb(0x3A, 0x3A, 0x48), () => Theme.Launch("calc.exe", null)));
            apps.Add(new AppDef("Settings", "", Color.FromRgb(0xA0, 0x7C, 0xFF), Color.FromRgb(0x5B, 0x2E, 0xD9), () => Theme.Launch("ms-settings:", null)));
        }

        void BuildWallpaper()
        {
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0D, 0x0B, 0x24), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x25, 0x5C), 0.55));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x36, 0x28), 1));
            root.Background = bg;
            root.Children.Add(Blob(Color.FromArgb(150, 0x4C, 0xD9, 0x3A), HorizontalAlignment.Right, VerticalAlignment.Top, 950, new Thickness(0, -300, -280, 0)));
            root.Children.Add(Blob(Color.FromArgb(140, 0x8E, 0x2D, 0xE2), HorizontalAlignment.Left, VerticalAlignment.Bottom, 1050, new Thickness(-320, 0, 0, -380)));
        }

        UIElement Blob(Color c, HorizontalAlignment h, VerticalAlignment v, double size, Thickness m)
        {
            var r = new RadialGradientBrush();
            r.GradientStops.Add(new GradientStop(c, 0));
            r.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            return new Ellipse { Width = size, Height = size, Fill = r, HorizontalAlignment = h, VerticalAlignment = v, Margin = m, IsHitTestVisible = false };
        }

        void BuildGreeting()
        {
            var glow = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Color = Theme.GrassColor, Opacity = 0.55 };
            var brand = new StackPanel { Margin = new Thickness(64, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            brand.Children.Add(new TextBlock { Text = "Bisman OS", FontFamily = Theme.Brand, FontSize = 104, Foreground = Brushes.White, Effect = glow });
            brand.Children.Add(new TextBlock { Text = "Y O U R   D E S K T O P ,   Y O U R   R U L E S", FontFamily = Theme.Display, FontSize = 15, Foreground = Theme.Grass, Margin = new Thickness(6, -18, 0, 0) });
            root.Children.Add(brand);

            var hint = new TextBlock { Text = "Ctrl+Space  Start     Ctrl+Alt+T  Terminal     Ctrl+Shift+Q  Exit", FontFamily = Theme.Mono, FontSize = 12, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 22, 28, 0) };
            root.Children.Add(hint);

            var clockPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 60) };
            bigClock = new TextBlock { FontFamily = Theme.Display, FontWeight = FontWeights.Light, FontSize = 168, HorizontalAlignment = HorizontalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.45 } };
            bigDate = new TextBlock { FontFamily = Theme.Ui, FontSize = 26, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -14, 0, 0) };
            clockPanel.Children.Add(bigClock);
            clockPanel.Children.Add(bigDate);
            root.Children.Add(clockPanel);
        }

        void BuildTaskbar()
        {
            var bar = new Border { Height = 56, VerticalAlignment = VerticalAlignment.Bottom, Background = Theme.Argb(215, 0x10, 0x10, 0x1A), BorderBrush = Theme.Argb(40, 255, 255, 255), BorderThickness = new Thickness(0, 1, 0, 0) };
            var barGrid = new Grid();
            winPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var tray = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
            trayTime = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
            trayDate = new TextBlock { FontSize = 11.5, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right };
            tray.Children.Add(trayTime); tray.Children.Add(trayDate);
            barGrid.Children.Add(winPanel); barGrid.Children.Add(tray);
            bar.Child = barGrid;
            root.Children.Add(bar);

            dock = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var a in apps)
            {
                var it = new DockItem { App = a, Size = BaseSize, Target = BaseSize };
                it.Icon = MakeIcon(a, BaseSize);
                it.Icon.Margin = new Thickness(Gap / 2, 0, Gap / 2, 0);
                it.Icon.VerticalAlignment = VerticalAlignment.Bottom;
                it.Icon.RenderTransform = it.Bounce;
                var captured = it;
                it.Icon.MouseLeftButtonUp += (s, e) => { BounceIcon(captured); captured.App.Run(); };
                items.Add(it);
                dock.Children.Add(it.Icon);
            }
            root.Children.Add(dock);
        }

        Border MakeIcon(AppDef a, double size)
        {
            var g = new LinearGradientBrush(a.C1, a.C2, 90);
            var tb = new TextBlock { Text = a.Glyph, FontFamily = a.Font ?? Theme.Icons, Foreground = Brushes.White, FontSize = 20 };
            if (a.Font == Theme.Brand)
            {
                // Gabriola has very tall line spacing; trim it so the letter fills the icon.
                tb.FontSize = 40; tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; tb.LineHeight = 30;
                tb.Margin = new Thickness(0, 6, 0, -2);
            }
            return new Border
            {
                Width = size, Height = size, Background = g, CornerRadius = new CornerRadius(size * 0.25), Padding = new Thickness(size * 0.24),
                Child = new Viewbox { Child = tb },
                BorderBrush = Theme.Argb(70, 255, 255, 255), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45, Direction = 270 }
            };
        }

        void BounceIcon(DockItem it)
        {
            var a = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            it.Bounce.BeginAnimation(TranslateTransform.YProperty, a);
        }

        // macOS dock magnification: icons near the mouse swell smoothly and push their neighbours aside.
        void Tick(object sender, EventArgs e)
        {
            int n = items.Count;
            double slot = BaseSize + Gap;
            double left = root.ActualWidth / 2 - n * slot / 2;
            DockItem hovered = null;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                double c = left + i * slot + slot / 2;
                double d = Math.Abs(mouseX - c);
                double f = (mouseInDock && d < Range) ? (Math.Cos(Math.PI * d / Range) + 1) / 2 : 0;
                it.Target = BaseSize + (MaxSize - BaseSize) * f;
                if (mouseInDock && d < slot / 2) hovered = it;
                double next = it.Size + (it.Target - it.Size) * 0.28;
                if (Math.Abs(next - it.Target) < 0.05) next = it.Target;
                if (next != it.Size)
                {
                    it.Size = next;
                    it.Icon.Width = it.Icon.Height = next;
                    it.Icon.CornerRadius = new CornerRadius(next * 0.25);
                    it.Icon.Padding = new Thickness(next * 0.24);
                }
            }
            double targetOp = hovered != null ? 1 : 0;
            labelOpacity += (targetOp - labelOpacity) * 0.25;
            hoverLabel.Opacity = labelOpacity;
            if (hovered != null)
            {
                hoverText.Text = hovered.App.Name;
                hoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var p = hovered.Icon.TranslatePoint(new Point(hovered.Icon.ActualWidth / 2, 0), root);
                Canvas.SetLeft(hoverLabel, p.X - hoverLabel.DesiredSize.Width / 2);
                Canvas.SetTop(hoverLabel, p.Y - hoverLabel.DesiredSize.Height - 12);
            }
        }

        void UpdateClock()
        {
            var now = DateTime.Now;
            bigClock.Text = now.ToString("HH:mm");
            bigDate.Text = now.ToString("dddd, d MMMM");
            trayTime.Text = now.ToString("h:mm tt");
            trayDate.Text = now.ToString("dd-MM-yyyy");
        }

        // ---------- open windows on the taskbar ----------
        void RefreshWindows()
        {
            var list = new List<IntPtr>();
            var fg = Native.GetForegroundWindow();
            Native.EnumWindows((h, l) =>
            {
                if (h == myHwnd || !Native.IsWindowVisible(h)) return true;
                if (Native.GetWindow(h, 4) != IntPtr.Zero) return true;            // owned popups
                if ((Native.GetWindowLong(h, -20) & 0x80) != 0) return true;       // tool windows
                if (Native.GetWindowTextLength(h) == 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                list.Add(h);
                return true;
            }, IntPtr.Zero);

            var titles = new List<string>();
            var sig = new StringBuilder();
            foreach (var h in list)
            {
                var sb = new StringBuilder(256);
                Native.GetWindowText(h, sb, 256);
                string t = sb.ToString();
                if (t == "Program Manager") { titles.Add(null); continue; }
                titles.Add(t);
                sig.Append(h.ToInt64()).Append('|').Append(t).Append(h == fg ? "*" : "").Append(';');
            }
            if (sig.ToString() == winSignature) return;
            winSignature = sig.ToString();

            winPanel.Children.Clear();
            // keep the window buttons clear of the dock in the middle
            double room = root.ActualWidth / 2 - items.Count * (BaseSize + Gap) / 2 - 40;
            double used = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (titles[i] == null) continue;
                if (used + 160 > room) break;
                used += 160;
                IntPtr h = list[i];
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var img = IconFor(pid);
                if (img != null) row.Children.Add(new Image { Source = img, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(new TextBlock { Text = titles[i], FontSize = 12, Width = 100, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var btn = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 6, 11, 6), Margin = new Thickness(3, 0, 3, 0), Child = row };
                bool active = h == fg;
                Theme.MacHover(btn, btn, active ? Theme.Argb(60, 255, 255, 255) : Theme.Argb(18, 255, 255, 255), Theme.Argb(80, 255, 255, 255));
                if (active) { btn.BorderBrush = Theme.Grass; btn.BorderThickness = new Thickness(0, 0, 0, 2); }
                btn.MouseLeftButtonUp += (s, e) =>
                {
                    if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
                    Native.SetForegroundWindow(h);
                };
                winPanel.Children.Add(btn);
            }
        }

        ImageSource IconFor(uint pid)
        {
            string path = null;
            try { path = Process.GetProcessById((int)pid).MainModule.FileName; } catch { }
            if (path == null) return null;
            ImageSource img;
            if (iconCache.TryGetValue(path, out img)) return img;
            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    img = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                }
            }
            catch { img = null; }
            iconCache[path] = img;
            return img;
        }

        // ---------- start menu ----------
        void BuildStartMenu()
        {
            startMenu = new Border
            {
                Width = 640, Height = 610, CornerRadius = new CornerRadius(18), Padding = new Thickness(26),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 72),
                Visibility = Visibility.Collapsed, Background = Theme.Argb(244, 0x16, 0x16, 0x22), BorderBrush = Theme.Argb(50, 255, 255, 255), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.6, Direction = 270 }, RenderTransform = startShift
            };
            var g = new Grid();
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                g.RowDefinitions.Add(new RowDefinition { Height = h });

            // search box
            var searchWrap = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Argb(28, 255, 255, 255), Padding = new Thickness(14, 9, 14, 9), BorderBrush = Theme.Argb(60, 0x4C, 0xD9, 0x3A), BorderThickness = new Thickness(1) };
            var sd = new DockPanel();
            var sicon = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Grass };
            DockPanel.SetDock(sicon, Dock.Left);
            sd.Children.Add(sicon);
            var sg = new Grid { Margin = new Thickness(12, 0, 0, 0) };
            var placeholder = new TextBlock { Text = "Search apps, or type a command and press Enter", Opacity = 0.45, FontSize = 15, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
            searchBox = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Theme.Grass, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
            searchBox.TextChanged += (s, e) => { placeholder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; FilterApps(searchBox.Text); };
            searchBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) RunSearch(); };
            sg.Children.Add(searchBox); sg.Children.Add(placeholder);
            sd.Children.Add(sg);
            searchWrap.Child = sd;
            Grid.SetRow(searchWrap, 0); g.Children.Add(searchWrap);

            var h1 = Header("Pinned"); Grid.SetRow(h1, 1); g.Children.Add(h1);
            var tiles = new WrapPanel { Margin = new Thickness(-4, 0, 0, 0) };
            for (int i = 1; i < apps.Count; i++) tiles.Children.Add(Tile(apps[i]));
            Grid.SetRow(tiles, 2); g.Children.Add(tiles);

            var h2 = Header("All apps"); Grid.SetRow(h2, 3); g.Children.Add(h2);
            appList = new StackPanel();
            var scroll = new ScrollViewer { Content = appList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            Grid.SetRow(scroll, 4); g.Children.Add(scroll);

            // bottom: user + power
            var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var user = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var avatar = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new LinearGradientBrush(Theme.GrassColor, Color.FromRgb(0x0E, 0x8C, 0x7A), 45), Child = new TextBlock { Text = "B", FontFamily = Theme.Brand, FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) } };
            user.Children.Add(avatar);
            user.Children.Add(new TextBlock { Text = Environment.UserName, FontFamily = Theme.DisplayBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            DockPanel.SetDock(user, Dock.Left);
            bottom.Children.Add(user);
            var power = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            power.Children.Add(Pill("", "Exit to Windows", ConfirmExit));
            power.Children.Add(Pill("", "Restart", () => { if (Ask("Restart the PC now?")) Theme.Launch("shutdown.exe", "/r /t 0"); }));
            power.Children.Add(Pill("", "Shut down", () => { if (Ask("Shut down the PC now?")) Theme.Launch("shutdown.exe", "/s /t 0"); }));
            bottom.Children.Add(power);
            Grid.SetRow(bottom, 5); g.Children.Add(bottom);

            startMenu.Child = g;
            root.Children.Add(startMenu);
        }

        TextBlock Header(string text)
        {
            return new TextBlock { Text = text, FontFamily = Theme.DisplayBold, FontSize = 16, Margin = new Thickness(2, 20, 0, 10), Foreground = Theme.Grass };
        }

        UIElement Tile(AppDef a)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = MakeIcon(a, 38); icon.Effect = null;
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = a.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) });
            var b = new Border { Width = 92, Height = 84, Margin = new Thickness(4), CornerRadius = new CornerRadius(12), Child = sp };
            Theme.MacHover(b, b, Brushes.Transparent, Theme.Argb(30, 255, 255, 255));
            b.MouseLeftButtonUp += (s, e) => { HideStart(); a.Run(); };
            return b;
        }

        UIElement Pill(string glyph, string text, Action act)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12 });
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(6, 0, 0, 0), Child = sp };
            Theme.MacHover(b, b, Theme.Argb(22, 255, 255, 255), Theme.Argb(60, 0x4C, 0xD9, 0x3A));
            b.MouseLeftButtonUp += (s, e) => act();
            return b;
        }

        void LoadApps()
        {
            var found = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
                Collect(dir, found, 0);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var kv in found) allApps.Add(kv);
                FilterApps(searchBox.Text);
            }));
        }

        void Collect(string dir, SortedDictionary<string, string> found, int depth)
        {
            if (depth > 4 || string.IsNullOrEmpty(dir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.lnk"))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!found.ContainsKey(name)) found[name] = f;
                }
                foreach (var d in Directory.GetDirectories(dir)) Collect(d, found, depth + 1);
            }
            catch { }
        }

        void FilterApps(string q)
        {
            if (appList == null) return;
            appList.Children.Clear();
            int count = 0;
            foreach (var kv in allApps)
            {
                if (q.Length > 0 && kv.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++count > 150) break;
                string path = kv.Value;
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 1, 8, 1), Child = new TextBlock { Text = kv.Key, FontSize = 14 }, Tag = path };
                Theme.MacHover(row, row, Brushes.Transparent, Theme.Argb(30, 255, 255, 255));
                row.RenderTransformOrigin = new Point(0.1, 0.5);
                row.MouseLeftButtonUp += (s, e) => { HideStart(); Theme.Launch(path, null); };
                appList.Children.Add(row);
            }
            if (count == 0 && q.Length > 0)
                appList.Children.Add(new TextBlock { Text = "No app found. Press Enter to run \"" + q + "\" as a command.", Opacity = 0.6, Margin = new Thickness(12, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        }

        void RunSearch()
        {
            string q = searchBox.Text.Trim();
            if (q.Length == 0) return;
            foreach (UIElement el in appList.Children)
            {
                var b = el as Border;
                if (b != null && b.Tag is string) { HideStart(); Theme.Launch((string)b.Tag, null); return; }
            }
            HideStart();
            Theme.Launch("cmd.exe", "/c start \"\" " + q);
        }

        void ToggleStart()
        {
            if (startMenu.Visibility == Visibility.Visible) { HideStart(); return; }
            searchBox.Text = "";
            startMenu.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            startMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            startShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            Dispatcher.BeginInvoke(new Action(() => { searchBox.Focus(); Keyboard.Focus(searchBox); }), DispatcherPriority.Input);
        }

        void HideStart() { startMenu.Visibility = Visibility.Collapsed; }

        void OpenTerminal() { HideStart(); new Terminal().Show(); }

        bool Ask(string q) { return MessageBox.Show(this, q, "Bisman OS", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }

        void ConfirmExit() { if (Ask("Exit Bisman OS and go back to Windows?")) Application.Current.Shutdown(); }

        void OnKey(object s, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && k == Key.Q) { e.Handled = true; ConfirmExit(); }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && k == Key.T) { e.Handled = true; OpenTerminal(); }
            else if (mods == ModifierKeys.Control && k == Key.Space) { e.Handled = true; ToggleStart(); }
            else if (k == Key.Escape) HideStart();
        }
    }

    // ---------- green terminal ----------
    public class Terminal : Window
    {
        TextBox output, input;
        TextBlock prompt;
        string cwd;
        Process proc;
        readonly List<string> history = new List<string>();
        int hIdx;
        static readonly Encoding Oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

        const string Banner =
@" ____  _                               ___  ____
| __ )(_)___ _ __ ___   __ _ _ __     / _ \/ ___|
|  _ \| / __| '_ ` _ \ / _` | '_ \   | | | \___ \
| |_) | \__ \ | | | | | (_| | | | |  | |_| |___) |
|____/|_|___/_| |_| |_|\__,_|_| |_|   \___/|____/

 Welcome to the Bisman OS terminal. Type 'help' for tips.

";
        const string Help =
@" Bisman OS terminal
   Any Windows command works:  dir, ipconfig, ping google.com, python file.py
   cd <folder>   change folder        cls / clear   clear the screen
   Up / Down     command history      Ctrl+C        stop a running command
   exit          close terminal

";

        public Terminal()
        {
            Title = "Bisman Terminal";
            Width = 920; Height = 560;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Cursor = Theme.MacCursor;
            cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var frame = new Border
            {
                CornerRadius = new CornerRadius(12), Margin = new Thickness(18), Background = Theme.Argb(245, 0x08, 0x0D, 0x08),
                BorderBrush = Theme.Argb(110, 0x4C, 0xD9, 0x3A), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.6, Direction = 270 }
            };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // title bar with mac traffic lights
            var titleBar = new Border { Background = Theme.Argb(255, 0x12, 0x1A, 0x12), CornerRadius = new CornerRadius(12, 12, 0, 0) };
            var tg = new Grid();
            var lights = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            lights.Children.Add(Light(Color.FromRgb(0xFF, 0x5F, 0x57), Close));
            lights.Children.Add(Light(Color.FromRgb(0xFE, 0xBC, 0x2E), () => WindowState = WindowState.Minimized));
            lights.Children.Add(Light(Color.FromRgb(0x28, 0xC8, 0x40), ToggleMax));
            tg.Children.Add(lights);
            tg.Children.Add(new TextBlock { Text = "bisman@os  —  terminal", FontFamily = Theme.Mono, FontSize = 13, Foreground = Theme.Grass, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
            titleBar.Child = tg;
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) ToggleMax(); else DragMove(); };
            g.Children.Add(titleBar);

            output = new TextBox
            {
                IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Theme.Grass,
                FontFamily = Theme.Mono, FontSize = 14.5, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Padding = new Thickness(14, 10, 14, 0), SelectionBrush = Theme.Grass
            };
            TextBlock.SetLineStackingStrategy(output, LineStackingStrategy.BlockLineHeight);
            TextBlock.SetLineHeight(output, 21);
            Grid.SetRow(output, 1); g.Children.Add(output);

            var inputRow = new DockPanel { Margin = new Thickness(18, 4, 18, 14) };
            prompt = new TextBlock { FontFamily = Theme.Mono, FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = Theme.Grass, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(prompt, Dock.Left);
            input = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Theme.Grass, FontFamily = Theme.Mono, FontSize = 14.5, Margin = new Thickness(8, 0, 0, 0) };
            input.PreviewKeyDown += OnInputKey;
            inputRow.Children.Add(prompt); inputRow.Children.Add(input);
            Grid.SetRow(inputRow, 2); g.Children.Add(inputRow);

            frame.Child = g;
            Content = frame;
            output.Text = Banner;
            UpdatePrompt();
            Loaded += (s, e) => input.Focus();
            frame.MouseLeftButtonUp += (s, e) => { if (output.SelectionLength == 0) input.Focus(); };
            Closed += (s, e) => KillProc();
        }

        UIElement Light(Color c, Action act)
        {
            var el = new Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 8, 0), Stroke = Theme.Argb(60, 0, 0, 0), StrokeThickness = 1 };
            Theme.MacHover(el, null, null, null);
            el.MouseLeftButtonDown += (s, e) => { e.Handled = true; act(); };
            return el;
        }

        void ToggleMax() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

        string PromptText() { return "bisman ❯ " + cwd + " $"; }
        void UpdatePrompt() { prompt.Text = proc == null ? PromptText() : "…"; }

        void Append(string s)
        {
            if (output.Text.Length > 300000) output.Text = output.Text.Substring(150000);
            output.AppendText(s);
            output.ScrollToEnd();
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string line = input.Text; input.Text = "";
                if (proc != null)
                {
                    Append(line + "\n");
                    try { proc.StandardInput.WriteLine(line); } catch { }
                }
                else Run(line);
            }
            else if (e.Key == Key.Up && history.Count > 0 && proc == null)
            {
                e.Handled = true; hIdx = Math.Max(0, hIdx - 1); input.Text = history[hIdx]; input.CaretIndex = input.Text.Length;
            }
            else if (e.Key == Key.Down && history.Count > 0 && proc == null)
            {
                e.Handled = true; hIdx = Math.Min(history.Count, hIdx + 1);
                input.Text = hIdx < history.Count ? history[hIdx] : ""; input.CaretIndex = input.Text.Length;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && proc != null && input.SelectionLength == 0)
            {
                e.Handled = true; KillProc(); Append("^C\n");
            }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; output.Clear(); }
        }

        void Run(string line)
        {
            Append(PromptText() + " " + line + "\n");
            string t = line.Trim();
            if (t.Length == 0) return;
            history.Add(t); hIdx = history.Count;
            string lower = t.ToLowerInvariant();

            if (lower == "cls" || lower == "clear") { output.Clear(); return; }
            if (lower == "exit") { Close(); return; }
            if (lower == "help" || lower == "bisman") { Append(Help); return; }
            if (t.Length == 2 && t[1] == ':') { ChangeDir(t + "\\"); return; }
            if (lower == "cd") { Append(cwd + "\n\n"); return; }
            if (lower.StartsWith("cd ") || lower.StartsWith("cd\\") || lower.StartsWith("cd.") || lower.StartsWith("cd/"))
            {
                string arg = t.Substring(2).Trim();
                if (arg.StartsWith("/d ", StringComparison.OrdinalIgnoreCase)) arg = arg.Substring(3).Trim();
                ChangeDir(arg.Trim('"'));
                return;
            }

            var psi = new ProcessStartInfo("cmd.exe", "/d /c " + t)
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Oem, StandardErrorEncoding = Oem
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => Append(e.Data + "\n"))); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => Append(e.Data + "\n"))); };
            p.Exited += (s, e) =>
            {
                try { p.WaitForExit(); } catch { }
                Dispatcher.BeginInvoke(new Action(() => { if (proc == p) { proc = null; Append("\n"); UpdatePrompt(); } }));
            };
            try
            {
                p.Start();
                proc = p;
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                UpdatePrompt();
            }
            catch (Exception ex) { Append("error: " + ex.Message + "\n\n"); }
        }

        void ChangeDir(string arg)
        {
            try
            {
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cwd, Environment.ExpandEnvironmentVariables(arg)));
                if (Directory.Exists(path)) { cwd = path; UpdatePrompt(); }
                else Append("The system cannot find the path specified.\n\n");
            }
            catch (Exception ex) { Append(ex.Message + "\n\n"); }
        }

        void KillProc()
        {
            var p = proc;
            if (p == null) return;
            proc = null;
            try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            UpdatePrompt();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) => { MessageBox.Show(e.Exception.Message, "Bisman OS error"); e.Handled = true; };
            app.Run(new Desktop());
        }
    }
}
