// Bisman OS 2 - live wallpaper, ambient particles, frosted glass surfaces, window manager, dialogs.
namespace BismanOS;

// ---------------------------------------------------------------- wallpaper with a slow, continuous "camera drift"
public class Wallpaper : Canvas
{
    readonly Image img = new() { Stretch = Stretch.Fill };
    readonly MatrixTransform mt = new();
    readonly MediaElement vid = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
    BitmapSource src;
    public BitmapSource Blurred;
    public Rect Drawn, Static;
    Rect cover;
    bool isVideo, playing = true;
    double vw = 1920, vh = 1080;

    public static bool IsVideo(string p) => p != null && new[] { ".mp4", ".mov", ".wmv", ".avi", ".mkv", ".m4v", ".webm" }.Contains(Path.GetExtension(p).ToLowerInvariant());

    // Lets you pick any video file and makes it the wallpaper.
    public static void ChooseVideo()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Videos|*.mp4;*.mov;*.wmv;*.avi;*.mkv;*.m4v;*.webm|All files|*.*", Title = "Choose a video wallpaper" };
        if (dlg.ShowDialog(Desktop.I) != true) return;
        var list = Settings.LoadList("custom-videos"); if (!list.Contains(dlg.FileName)) list.Add(dlg.FileName);
        Settings.SaveList("custom-videos", list);
        Settings.Set("wallpaper", dlg.FileName);
    }

    public void SetPlaying(bool on)
    {
        if (!isVideo || on == playing) return;
        playing = on;
        if (on) vid.Play(); else vid.Pause();
    }

    void SnapshotVideo()
    {
        try
        {
            int w = 480, h = Math.Max(1, (int)(480 * vh / vw));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(vid) { Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            Blurred = MakeBlur(rtb);
            Changed?.Invoke();
        }
        catch (Exception ex) { Settings.Log("Video snapshot: " + ex.Message); }
    }
    double t;
    public bool Live = true;
    public event Action Changed;

    public Wallpaper()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        img.RenderTransform = mt;
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Children.Add(img);
        vid.RenderTransform = mt;
        Children.Add(vid);
        vid.MediaEnded += (_, _) => { vid.Position = TimeSpan.Zero; vid.Play(); };
        vid.MediaOpened += (_, _) =>
        {
            if (vid.NaturalVideoWidth > 0) { vw = vid.NaturalVideoWidth; vh = vid.NaturalVideoHeight; }
            Relayout();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (_, _) => { t.Stop(); if (isVideo) SnapshotVideo(); };
            t.Start();
        };
        vid.MediaFailed += (_, e) =>
        {
            Settings.Log("Video wallpaper: " + e.ErrorException?.Message);
            Notify.Post("Wallpaper", "", "Couldn't play that video", "Try an .mp4 file. Switched back to a picture wallpaper.");
            Settings.Set("wallpaper", "");
            Load(null);
        };
        SizeChanged += (_, _) => Relayout();
    }

    public static string AssetDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

    public static List<string> BuiltIn()
    {
        try { return Directory.GetFiles(Path.Combine(AssetDir, "wallpapers")).Where(f => f.EndsWith(".jpg") || f.EndsWith(".png")).OrderBy(f => f).ToList(); }
        catch { return new List<string>(); }
    }

    public void Load(string path)
    {
        if (IsVideo(path) && File.Exists(path))
        {
            isVideo = true; playing = true;
            img.Visibility = Visibility.Collapsed; vid.Visibility = Visibility.Visible;
            vid.Source = new Uri(path); vid.Play();
            Relayout();
            return;
        }
        if (isVideo) { isVideo = false; vid.Stop(); vid.Source = null; vid.Visibility = Visibility.Collapsed; img.Visibility = Visibility.Visible; }
        BitmapSource b = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = BuiltIn().FirstOrDefault();
        try
        {
            if (path != null && File.Exists(path))
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                b = bi;
            }
        }
        catch (Exception ex) { Settings.Log("Wallpaper: " + ex.Message); }
        b ??= Procedural();
        src = b; img.Source = b;
        Blurred = MakeBlur(b);
        Relayout();
        Changed?.Invoke();
    }

    // Small pre-blurred copy used by every glass surface (cheap: no per-frame blur work).
    static BitmapSource MakeBlur(BitmapSource b)
    {
        int w = 360, h = Math.Max(1, (int)Math.Round(360.0 * b.PixelHeight / b.PixelWidth));
        var host = new Grid { Width = w, Height = h, ClipToBounds = true };
        host.Children.Add(new Image { Source = b, Stretch = Stretch.Fill });
        host.Children.Add(new Image { Source = b, Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 16, KernelType = KernelType.Gaussian } });
        host.Measure(new Size(w, h)); host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host); rtb.Freeze();
        return rtb;
    }

    static BitmapSource Procedural()
    {
        int w = 1920, h = 1200;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Th.Midnight, Th.Navy, 60), null, new Rect(0, 0, w, h));
            var r = new RadialGradientBrush(Color.FromArgb(120, 0x3D, 0x8B, 0xFF), Color.FromArgb(0, 0x3D, 0x8B, 0xFF));
            dc.DrawEllipse(r, null, new Point(w * 0.75, h * 0.3), 900, 700);
            var v = new RadialGradientBrush(Color.FromArgb(90, 0x8B, 0x7C, 0xFF), Color.FromArgb(0, 0x8B, 0x7C, 0xFF));
            dc.DrawEllipse(v, null, new Point(w * 0.15, h * 0.9), 900, 700);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    void Relayout()
    {
        if ((src == null && !isVideo) || ActualWidth <= 0) return;
        double iw = isVideo ? vw : src.PixelWidth, ih = isVideo ? vh : src.PixelHeight;
        double s = Math.Max(ActualWidth / iw, ActualHeight / ih);
        cover = new Rect((ActualWidth - iw * s) / 2, (ActualHeight - ih * s) / 2, iw * s, ih * s);
        foreach (FrameworkElement e in new FrameworkElement[] { img, vid })
        {
            e.Width = cover.Width; e.Height = cover.Height;
            SetLeft(e, cover.X); SetTop(e, cover.Y);
        }
        Apply();
    }

    public void Tick(double dt) { if (Live && !Anim.Reduced) { t += dt; Apply(); } }

    void Apply()
    {
        bool moving = Live && !Anim.Reduced;
        double k = moving ? 1.06 + 0.03 * Math.Sin(t * 2 * Math.PI / 70) : 1.04;
        double ox = moving ? 22 * Math.Sin(t * 2 * Math.PI / 97) : 0;
        double oy = moving ? 12 * Math.Sin(t * 2 * Math.PI / 83 + 1.3) : 0;
        double cx = cover.Width / 2, cy = cover.Height / 2;
        var m = Matrix.Identity;
        m.Translate(-cx, -cy); m.Scale(k, k); m.Translate(cx + ox, cy + oy);
        mt.Matrix = m;
        Drawn = new Rect(cover.X + cx - cx * k + ox, cover.Y + cy - cy * k + oy, cover.Width * k, cover.Height * k);
        // Glass samples a fixed (average) framing so it only redraws when a window moves, not every frame.
        double k0 = moving ? 1.075 : 1.04;
        Static = new Rect(cover.X + cx - cx * k0, cover.Y + cy - cy * k0, cover.Width * k0, cover.Height * k0);
    }

    public Rect Map(Rect r) => Static.Width <= 0 ? new Rect(0, 0, 1, 1) :
        new Rect((r.X - Static.X) / Static.Width, (r.Y - Static.Y) / Static.Height, r.Width / Static.Width, r.Height / Static.Height);
}

// ---------------------------------------------------------------- floating light particles + slow light sweep
public class Ambient : FrameworkElement
{
    struct P { public double X, Y, V, R, Ph, A; }
    P[] ps;
    readonly Random rnd = new(11);
    double t;
    static readonly Brush Glow, Sweep, TopShade, BottomShade;

    static Ambient()
    {
        var g = new RadialGradientBrush(Color.FromArgb(255, 0xC8, 0xE6, 0xFF), Color.FromArgb(0, 0xA8, 0xD8, 0xFF)); g.Freeze(); Glow = g;
        var s = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        s.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xA8, 0xD8, 0xFF), 0));
        s.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0xA8, 0xD8, 0xFF), 0.5));
        s.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xA8, 0xD8, 0xFF), 1)); s.Freeze(); Sweep = s;
        var ts = new LinearGradientBrush(Color.FromArgb(120, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90); ts.Freeze(); TopShade = ts;
        var bs = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90); bs.Freeze(); BottomShade = bs;
    }

    public Ambient() { IsHitTestVisible = false; }

    public void Tick(double dt)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0) return;
        t += dt;
        if (ps == null)
        {
            ps = new P[70];
            for (int i = 0; i < ps.Length; i++) ps[i] = New(W, H, rnd.NextDouble() * H);
        }
        for (int i = 0; i < ps.Length; i++)
        {
            ps[i].Y -= ps[i].V * dt;
            ps[i].X += Math.Sin(t * 0.25 + ps[i].Ph) * 5 * dt;
            if (ps[i].Y < -10) ps[i] = New(W, H, H + 10);
        }
        InvalidateVisual();
    }

    P New(double W, double H, double y) => new() { X = rnd.NextDouble() * W, Y = y, V = 6 + rnd.NextDouble() * 14, R = 0.8 + rnd.NextDouble() * 1.8, Ph = rnd.NextDouble() * 6.28, A = 0.25 + rnd.NextDouble() * 0.5 };

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0) return;
        if (Settings.Bool("liveWallpaper") && !Anim.Reduced)
        {
            double sx = W * (0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 50));
            dc.PushOpacity(0.045);
            dc.PushTransform(new SkewTransform(-18, 0, sx, H / 2));
            dc.DrawRectangle(Sweep, null, new Rect(sx - W * 0.22, 0, W * 0.44, H));
            dc.Pop(); dc.Pop();
            if (Settings.Bool("particles") && ps != null)
                foreach (var p in ps)
                {
                    double a = p.A * Math.Sin(Math.PI * Th.Clamp(p.Y / H, 0, 1));
                    if (a <= 0.01) continue;
                    dc.PushOpacity(a);
                    dc.DrawEllipse(Glow, null, new Point(p.X, p.Y), p.R * 3, p.R * 3);
                    dc.Pop();
                }
        }
        dc.DrawRectangle(TopShade, null, new Rect(0, 0, W, 140));
        dc.DrawRectangle(BottomShade, null, new Rect(0, H - 220, W, 220));
    }
}

// ---------------------------------------------------------------- frosted glass surface
public class Glass : Border
{
    public static Wallpaper Wall;
    static readonly List<Glass> all = new();
    readonly Grid layers = new();
    readonly Rectangle blurRect, tintRect, sheen;
    readonly ImageBrush brush = new() { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
    readonly SolidColorBrush tintBrush;
    readonly double tint;
    UIElement body;

    public Glass(double radius = 16, double tint = -1)
    {
        this.tint = tint;
        CornerRadius = new CornerRadius(radius);
        BorderThickness = new Thickness(1);
        BorderBrush = new LinearGradientBrush(Color.FromArgb(64, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90);
        double r = Math.Max(0, radius - 1);
        blurRect = new Rectangle { Fill = brush, RadiusX = r, RadiusY = r };
        tintBrush = new SolidColorBrush(Color.FromArgb(140, 0x0C, 0x11, 0x1C));
        tintRect = new Rectangle { Fill = tintBrush, RadiusX = r, RadiusY = r };
        sheen = new Rectangle
        {
            RadiusX = r, RadiusY = r, IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromArgb(20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.5))
        };
        layers.Children.Add(blurRect); layers.Children.Add(tintRect); layers.Children.Add(sheen);
        Child = layers;
        Loaded += (_, _) => { if (!all.Contains(this)) all.Add(this); Refresh(); };
        Unloaded += (_, _) => all.Remove(this);
    }

    public UIElement Body
    {
        get => body;
        set { if (body != null) layers.Children.Remove(body); body = value; if (value != null) layers.Children.Add(value); }
    }

    public void Refresh()
    {
        bool blur = Settings.Bool("glassBlur") && Wall?.Blurred != null;
        blurRect.Visibility = blur ? Visibility.Visible : Visibility.Collapsed;
        brush.ImageSource = Wall?.Blurred;
        double a = tint >= 0 ? tint : Settings.Num("glassTint", 0.55);
        if (!blur) a = Math.Min(0.96, a + 0.35);
        tintBrush.Color = Color.FromArgb((byte)(Th.Clamp(a, 0, 1) * 255), 0x0C, 0x11, 0x1C);
    }

    public static void RefreshAll() { foreach (var g in all.ToArray()) g.Refresh(); }

    // Keeps each glass surface showing exactly the part of the (moving) wallpaper behind it.
    public static void UpdateAll(Visual root)
    {
        if (Wall == null) return;
        foreach (var g in all.ToArray())
        {
            if (!g.IsVisible || g.blurRect.Visibility != Visibility.Visible || g.ActualWidth <= 0) continue;
            try
            {
                var ps = PresentationSource.FromVisual(g);
                if (ps == null) continue;
                Rect r;
                if (ps.RootVisual == root) r = g.TransformToAncestor(root).TransformBounds(new Rect(g.RenderSize));
                else
                {
                    // Glass in another window (the dock): line it up through screen coordinates.
                    double s = ps.CompositionTarget.TransformToDevice.M11;
                    var p = g.PointToScreen(new Point(0, 0)); var o = root.PointToScreen(new Point(0, 0));
                    r = new Rect((p.X - o.X) / s, (p.Y - o.Y) / s, g.ActualWidth, g.ActualHeight);
                }
                var vb = Wall.Map(r);
                if (g.brush.Viewbox != vb) g.brush.Viewbox = vb;
            }
            catch { }
        }
    }
}

// ---------------------------------------------------------------- an app window inside Bisman OS
public class ShellWin : Border
{
    public readonly string AppId;
    public readonly StackPanel Toolbar;
    public readonly ScaleTransform St = new(1, 1);
    public readonly TranslateTransform Tt = new();
    public readonly UIElement Body;
    readonly TextBlock titleText;
    readonly List<(Ellipse e, TextBlock g, Color c)> lights = new();
    public bool IsMin, IsMax, IsActive;
    public Rect RestoreBounds;
    public double MinW = 360, MinH = 240;
    public WinMgr Mgr;
    public event Action Closing;
    public Func<bool> OnCloseShortcut;
    bool dragging; Point dragAt;
    readonly DropShadowEffect shadow;

    public string Title { get => titleText.Text; set => titleText.Text = value; }

    public ShellWin(string appId, string title, double w, double h, UIElement body, double tint = 0.74)
    {
        AppId = appId; Width = w; Height = h; Body = body;
        CornerRadius = new CornerRadius(14);
        RenderTransformOrigin = new Point(0.5, 0.5);
        var tg = new TransformGroup(); tg.Children.Add(St); tg.Children.Add(Tt);
        RenderTransform = tg;
        // The shadow sits inside a cached layer, so it's drawn once instead of every frame.
        CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        var glass = new Glass(14, tint);
        shadow = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.55, RenderingBias = RenderingBias.Performance };
        glass.Effect = shadow;
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        g.RowDefinitions.Add(new RowDefinition());

        var bar = new Grid { Background = Brushes.Transparent };
        var lp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
        AddLight(lp, Th.Red, "", () => Mgr.Close(this));
        AddLight(lp, Th.Yellow, "", () => Mgr.Minimize(this));
        AddLight(lp, Th.Green, "", () => Mgr.ToggleMax(this));
        lp.MouseEnter += (_, _) => { foreach (var l in lights) l.g.Opacity = 1; };
        lp.MouseLeave += (_, _) => { foreach (var l in lights) l.g.Opacity = 0; };
        titleText = Ui.Txt(title, 13, FontWeights.SemiBold, Th.Fg2);
        titleText.HorizontalAlignment = HorizontalAlignment.Center; titleText.IsHitTestVisible = false;
        Toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        bar.Children.Add(titleText); bar.Children.Add(lp); bar.Children.Add(Toolbar);
        bar.MouseLeftButtonDown += BarDown; bar.MouseMove += BarMove; bar.MouseLeftButtonUp += BarUp;
        g.Children.Add(bar);
        Grid.SetRow(body, 1); g.Children.Add(body);
        glass.Body = g;

        var outer = new Grid();
        outer.Children.Add(glass);
        outer.Children.Add(Grip(Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Stretch, 6, double.NaN, true, false));
        outer.Children.Add(Grip(Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 6, false, true));
        outer.Children.Add(Grip(Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, 16, 16, true, true));
        Child = outer;
        PreviewMouseDown += (_, _) => { if (Mgr != null && Mgr.Active != this) Mgr.Activate(this); };
    }

    void AddLight(StackPanel p, Color c, string glyph, Action act)
    {
        var e = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(c), Stroke = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), StrokeThickness = 0.5 };
        var t = new TextBlock { Text = glyph, FontFamily = Th.Icons, FontSize = 6.5, Foreground = new SolidColorBrush(Color.FromArgb(200, 40, 20, 10)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
        var host = new Grid { Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent };
        host.Children.Add(e); host.Children.Add(t);
        Ui.OnClick(host, act);
        lights.Add((e, t, c));
        p.Children.Add(host);
    }

    Thumb Grip(Cursor cur, HorizontalAlignment h, VerticalAlignment v, double w, double ht, bool horiz, bool vert)
    {
        var th = new Thumb { Style = Styles.Get("Grip"), Cursor = cur, HorizontalAlignment = h, VerticalAlignment = v, Width = w, Height = ht };
        th.DragDelta += (_, e) =>
        {
            if (IsMax) return;
            if (horiz) Width = Math.Max(MinW, Width + e.HorizontalChange);
            if (vert) Height = Math.Max(MinH, Height + e.VerticalChange);
        };
        return th;
    }

    void BarDown(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Mgr.ToggleMax(this); return; }
        dragging = true; dragAt = e.GetPosition(this);
        ((UIElement)s).CaptureMouse();
    }

    void BarMove(object s, MouseEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(Mgr.Layer);
        if (IsMax)
        {
            double rx = dragAt.X / Math.Max(1, ActualWidth);
            IsMax = false; Width = RestoreBounds.Width; Height = RestoreBounds.Height;
            dragAt = new Point(RestoreBounds.Width * rx, dragAt.Y);
        }
        Canvas.SetLeft(this, p.X - dragAt.X);
        Canvas.SetTop(this, Math.Max(Mgr.Top - 6, Math.Min(Mgr.Layer.ActualHeight - 60, p.Y - dragAt.Y)));
    }

    void BarUp(object s, MouseButtonEventArgs e) { dragging = false; ((UIElement)s).ReleaseMouseCapture(); }

    public void SetActive(bool on)
    {
        IsActive = on;
        foreach (var l in lights) ((SolidColorBrush)l.e.Fill).Color = on ? l.c : Color.FromRgb(0x4A, 0x4F, 0x5A);
        titleText.Foreground = on ? Th.Fg : Th.Fg3;
        shadow.Opacity = on ? 0.6 : 0.35;
    }

    public void RaiseClosing() => Closing?.Invoke();
}

public class WinMgr
{
    public readonly Canvas Layer;
    public readonly List<ShellWin> Wins = new();
    public ShellWin Active;
    public event Action Changed;
    public Func<string, Point?> DockPoint;
    public double Top = 40, Bottom = 100;
    int z = 10;

    public WinMgr(Canvas layer) { Layer = layer; }

    public Rect Work => new(10, Top, Math.Max(300, Layer.ActualWidth - 20), Math.Max(240, Layer.ActualHeight - Top - Bottom));

    public void Open(ShellWin w)
    {
        w.Mgr = this;
        var wa = Work;
        w.Width = Math.Min(w.Width, wa.Width); w.Height = Math.Min(w.Height, wa.Height);
        int n = Wins.Count(x => !x.IsMin);
        double x = wa.X + (wa.Width - w.Width) / 2 + (n % 6) * 30 - 45;
        double y = wa.Y + (wa.Height - w.Height) / 2 + (n % 6) * 26 - 35;
        Canvas.SetLeft(w, Math.Max(wa.X, x)); Canvas.SetTop(w, Math.Max(wa.Y, y));
        Wins.Add(w); Layer.Children.Add(w);
        Activate(w);
        Anim.Run(w, UIElement.OpacityProperty, 0, 1, 180);
        Anim.Run(w.St, ScaleTransform.ScaleXProperty, 0.93, 1, 300, Anim.Spring);
        Anim.Run(w.St, ScaleTransform.ScaleYProperty, 0.93, 1, 300, Anim.Spring);
        Anim.Run(w.Tt, TranslateTransform.YProperty, 16, 0, 300, Anim.Spring);
        Changed?.Invoke();
    }

    public void Activate(ShellWin w)
    {
        if (w == null) return;
        Panel.SetZIndex(w, ++z);
        foreach (var x in Wins) x.SetActive(x == w);
        Active = w;
        Changed?.Invoke();
    }

    public void Deactivate()
    {
        foreach (var x in Wins) x.SetActive(false);
        Active = null;
        Changed?.Invoke();
    }

    void ActivateTop()
    {
        var top = Wins.Where(x => !x.IsMin).OrderByDescending(Panel.GetZIndex).FirstOrDefault();
        if (top != null) Activate(top); else { Active = null; Changed?.Invoke(); }
    }

    public void Close(ShellWin w)
    {
        if (!Wins.Contains(w)) return;
        w.RaiseClosing();
        Wins.Remove(w);
        w.IsHitTestVisible = false;
        Anim.To(w, UIElement.OpacityProperty, 0, 160);
        Anim.To(w.St, ScaleTransform.ScaleXProperty, 0.94, 160);
        Anim.To(w.St, ScaleTransform.ScaleYProperty, 0.94, 160, null, () => Layer.Children.Remove(w));
        if (Active == w) ActivateTop(); else Changed?.Invoke();
    }

    (double dx, double dy) ToDock(ShellWin w)
    {
        var p = DockPoint?.Invoke(w.AppId) ?? new Point(Layer.ActualWidth / 2, Layer.ActualHeight - 30);
        double l = Canvas.GetLeft(w), t = Canvas.GetTop(w);
        return (p.X - (l + w.ActualWidth / 2), p.Y - (t + w.ActualHeight / 2));
    }

    // Minimise: the window shrinks into its dock icon.
    public void Minimize(ShellWin w)
    {
        if (w.IsMin) return;
        var (dx, dy) = ToDock(w);
        w.IsMin = true; w.IsHitTestVisible = false;
        Anim.Run(w.St, ScaleTransform.ScaleXProperty, 1, 0.05, 360, Anim.InOut);
        Anim.Run(w.St, ScaleTransform.ScaleYProperty, 1, 0.05, 360, Anim.InOut);
        Anim.Run(w.Tt, TranslateTransform.XProperty, 0, dx, 360, Anim.InOut);
        Anim.Run(w, UIElement.OpacityProperty, 1, 0, 360, Anim.InOut);
        Anim.Run(w.Tt, TranslateTransform.YProperty, 0, dy, 360, Anim.InOut, () =>
        {
            if (!w.IsMin) return;
            w.Visibility = Visibility.Collapsed;
            w.St.ScaleX = w.St.ScaleY = 1; w.Tt.X = w.Tt.Y = 0;
        });
        if (Active == w) ActivateTop(); else Changed?.Invoke();
    }

    public void Restore(ShellWin w)
    {
        if (!w.IsMin) { Activate(w); return; }
        w.IsMin = false; w.Visibility = Visibility.Visible; w.IsHitTestVisible = true;
        var (dx, dy) = ToDock(w);
        Anim.Run(w.St, ScaleTransform.ScaleXProperty, 0.05, 1, 320, Anim.Out);
        Anim.Run(w.St, ScaleTransform.ScaleYProperty, 0.05, 1, 320, Anim.Out);
        Anim.Run(w.Tt, TranslateTransform.XProperty, dx, 0, 320, Anim.Out);
        Anim.Run(w.Tt, TranslateTransform.YProperty, dy, 0, 320, Anim.Out);
        Anim.Run(w, UIElement.OpacityProperty, 0, 1, 260);
        Activate(w);
    }

    public void ToggleMax(ShellWin w)
    {
        if (!w.IsMax)
        {
            w.RestoreBounds = new Rect(Canvas.GetLeft(w), Canvas.GetTop(w), w.Width, w.Height);
            w.IsMax = true;
            Bounds(w, Work);
        }
        else { w.IsMax = false; Bounds(w, w.RestoreBounds); }
    }

    void Bounds(ShellWin w, Rect r)
    {
        Anim.Run(w, Canvas.LeftProperty, Canvas.GetLeft(w), r.X, 240);
        Anim.Run(w, Canvas.TopProperty, Canvas.GetTop(w), r.Y, 240);
        Anim.Run(w, FrameworkElement.WidthProperty, w.Width, r.Width, 240);
        Anim.Run(w, FrameworkElement.HeightProperty, w.Height, r.Height, 240);
    }

    public List<ShellWin> For(string appId) => Wins.Where(w => w.AppId == appId).ToList();

    public void Cycle()
    {
        var vis = Wins.Where(w => !w.IsMin).OrderBy(Panel.GetZIndex).ToList();
        if (vis.Count > 1) Activate(vis[0]);
    }
}

// ---------------------------------------------------------------- glass confirmation dialog
public static class Dialog
{
    public static Grid Host;
    static Grid open;
    static Action openYes;
    public static bool IsOpen => open != null;

    public static void Confirm(string title, string message, string ok, bool danger, Action yes, string glyph = "")
    {
        Close();
        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) };
        var card = new Glass(20, 0.82) { Width = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.6 };
        var sp = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(16), Background = danger ? new SolidColorBrush(Color.FromArgb(60, 0xFF, 0x5F, 0x57)) : Th.AccentSoft, Child = Ui.Glyph(glyph, 24, danger ? Th.Danger : Th.Accent), HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(icon);
        var t = Ui.Txt(title, 18, FontWeights.SemiBold, null, Th.Display); t.Margin = new Thickness(0, 16, 0, 6); t.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(t);
        var m = Ui.Txt(message, 13.5, null, Th.Fg2); m.TextWrapping = TextWrapping.Wrap; m.TextTrimming = TextTrimming.None;
        sp.Children.Add(m);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(Ui.Pill("Cancel", Close));
        var okb = Ui.Pill(ok, () => { Close(); yes(); }, null, !danger, danger); okb.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(okb);
        sp.Children.Add(buttons);
        card.Body = sp;
        layer.Children.Add(card);
        layer.MouseLeftButtonDown += (_, e) => { if (!card.IsMouseOver) Close(); };
        Host.Children.Add(layer);
        open = layer; openYes = yes;
        var st = new ScaleTransform(1, 1); card.RenderTransform = st; card.RenderTransformOrigin = new Point(0.5, 0.5);
        Anim.Run(layer, UIElement.OpacityProperty, 0, 1, 160);
        Anim.Run(st, ScaleTransform.ScaleXProperty, 0.92, 1, 280, Anim.Spring);
        Anim.Run(st, ScaleTransform.ScaleYProperty, 0.92, 1, 280, Anim.Spring);
    }

    // Shows any card as a modal dialog. onEnter runs when Enter is pressed.
    public static void Show(FrameworkElement card, Action onEnter = null)
    {
        Close();
        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) };
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
        layer.Children.Add(card);
        layer.MouseLeftButtonDown += (_, e) => { if (!card.IsMouseOver) Close(); };
        Host.Children.Add(layer);
        open = layer; openYes = onEnter;
        var st = new ScaleTransform(1, 1); card.RenderTransform = st; card.RenderTransformOrigin = new Point(0.5, 0.5);
        Anim.Run(layer, UIElement.OpacityProperty, 0, 1, 160);
        Anim.Run(st, ScaleTransform.ScaleXProperty, 0.92, 1, 280, Anim.Spring);
        Anim.Run(st, ScaleTransform.ScaleYProperty, 0.92, 1, 280, Anim.Spring);
    }

    public static void Accept() { var y = openYes; if (y == null) return; Close(); y.Invoke(); }

    public static void Close()
    {
        if (open == null) return;
        var o = open; open = null; openYes = null;
        o.IsHitTestVisible = false;
        Anim.To(o, UIElement.OpacityProperty, 0, 120, null, () => Host.Children.Remove(o));
    }
}
