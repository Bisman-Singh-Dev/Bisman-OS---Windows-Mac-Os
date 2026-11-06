// Bisman OS 2 - the desktop: menu bar, dock, desktop icons, widgets, shortcuts and the main loop.
namespace BismanOS;

public interface IKeyHandler { bool HandleKey(Key k, ModifierKeys m); }

public class ExtWin { public IntPtr H; public string Title, Exe; public uint Pid; public bool Iconic; }

public static class AppIcon
{
    public static FrameworkElement Make(AppEntry a, double size)
    {
        if (a == null) return Designed("", Th.Graphite, Th.Navy, size);
        if (a.Internal) return Designed(a.Glyph, a.C1, a.C2, size);
        return FromShell(a.Parsing, size, true);
    }

    public static FrameworkElement FromShell(string path, double size, bool iconOnly, string fallbackGlyph = "")
    {
        var g = new Grid { Width = size, Height = size };
        var fb = Designed(fallbackGlyph, Color.FromRgb(0x2A, 0x31, 0x40), Color.FromRgb(0x15, 0x19, 0x22), size);
        var img = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        g.Children.Add(fb); g.Children.Add(img);
        ShellImages.Get(path, size > 40 ? 128 : (size > 20 ? 64 : 32), iconOnly, s => { if (s != null) { img.Source = s; fb.Visibility = Visibility.Collapsed; } });
        return g;
    }

    public static FrameworkElement Designed(string glyph, Color c1, Color c2, double size)
    {
        var g = new Grid { Width = size, Height = size };
        var r = new CornerRadius(size * 0.235);
        g.Children.Add(new Border { CornerRadius = r, Background = new LinearGradientBrush(c1, c2, 90) });
        g.Children.Add(new Border
        {
            CornerRadius = r, BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(Math.Max(0.5, size / 64)),
            Background = new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.55))
        });
        g.Children.Add(new TextBlock { Text = glyph, FontFamily = Th.Icons, FontSize = size * 0.44, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return g;
    }
}

// ================================================================ main window
public class Desktop : Window
{
    public static Desktop I;
    public Grid Root;
    public Wallpaper Wall;
    Ambient amb;
    public WinMgr Wm;
    public Canvas WinLayer, LabelLayer;
    public Dock Dock;
    public DockWindow DockWin;
    public MenuBar Bar;
    public DesktopIcons Icons;
    public Widgets Widgets;
    public Grid Overlay;
    public StartMenu Start;
    public Spotlight Spot;
    public ControlCentre CC;
    public NotificationCentre NC;
    public IntPtr Hwnd, LastExternalFg;
    public List<ExtWin> External = new();
    readonly Stopwatch sw = Stopwatch.StartNew();
    double last, liveAcc; int frame; bool paused;
    readonly Dictionary<uint, string> exeCache = new();
    Sys.Power lastPower; bool? lastNetUp; bool batteryWarned;

    public Desktop()
    {
        I = this;
        Title = "Bisman OS";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.Manual;
        // One pixel short of the full screen, so Windows doesn't treat Bisman OS as a full-screen app
        // (full-screen apps make tools like Wispr Flow hide their overlay). The dock window covers that pixel.
        Left = 0; Top = 0; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight - 1;
        Background = new SolidColorBrush(Th.Midnight);
        FontFamily = Th.Text; Foreground = Th.Fg;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;

        Root = new Grid();
        Content = Root;
        Wall = new Wallpaper(); Glass.Wall = Wall; Root.Children.Add(Wall);
        amb = new Ambient(); Root.Children.Add(amb);
        Icons = new DesktopIcons(); Root.Children.Add(Icons);
        Widgets = new Widgets(); Root.Children.Add(Widgets);
        WinLayer = new Canvas(); Root.Children.Add(WinLayer);
        Wm = new WinMgr(WinLayer);
        LabelLayer = new Canvas { IsHitTestVisible = false };
        DockWin = new DockWindow();
        Dock = new Dock(DockWin); DockWin.Host.Children.Insert(0, Dock);
        Bar = new MenuBar(); Root.Children.Add(Bar);
        Overlay = new Grid(); Root.Children.Add(Overlay);
        Start = new StartMenu(); Overlay.Children.Add(Start);
        Spot = new Spotlight(); Overlay.Children.Add(Spot);
        CC = new ControlCentre(); Overlay.Children.Add(CC);
        NC = new NotificationCentre(); Overlay.Children.Add(NC);
        Root.Children.Add(NC.Toasts);
        Root.Children.Add(LabelLayer);
        var dialogs = new Grid(); Root.Children.Add(dialogs); Dialog.Host = dialogs;

        Wm.DockPoint = Dock.PointFor;
        Wm.Changed += () => { Dock.UpdateIndicators(); Bar.UpdateApp(); };

        RegisterApps();
        Pins.Load();
        Catalog.Loaded += () => { Pins.EnsureDefaults(); Dock.Rebuild(); Start.Rebuild(); };
        Pins.Changed += () => { Dock.Rebuild(); Start.Rebuild(); };
        Catalog.LoadAsync(Dispatcher);

        Th.SetAccent(Settings.Get("accent"));
        Settings.Changed += OnSetting;
        Wall.Load(Settings.Get("wallpaper"));
        Wall.Changed += Glass.RefreshAll;

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(Hwnd);
            src.AddHook(WndProc);
            Native.RegisterHotKey(Hwnd, 1, 0x0002 | 0x0001 | 0x4000, 0x42); // Ctrl+Alt+B: bring Bisman OS to the front
            Native.RegisterHotKey(Hwnd, 2, 0x0002 | 0x0001 | 0x4000, 0x20); // Ctrl+Alt+Space: search from anywhere
            double scale = src.CompositionTarget.TransformToDevice.M11;
            try { Th.MacCursor = MacCursorMaker.Create(scale); } catch { }
            Cursor = Th.MacCursor;
            if (RenderCapability.Tier >> 16 < 2 && !Settings.HasList("tier-checked"))
            {
                Settings.SetBool("glassBlur", false); Settings.SetBool("liveWallpaper", false);
                Settings.SaveList("tier-checked", new[] { "1" });
            }
        };
        Closed += (_, _) => { Native.UnregisterHotKey(Hwnd, 1); Native.UnregisterHotKey(Hwnd, 2); DockHotkey.Remove(); DockWin.Close(); Taskbar.Restore(); };

        Root.PreviewMouseDown += (_, e) =>
        {
            var src = e.OriginalSource as DependencyObject;
            foreach (var f in new Flyout[] { Start, Spot, CC, NC })
                if (f.IsOpen && !Ui.IsInside(src, f.Panel) && !f.IsTrigger(src)) f.Hide();
        };
        PreviewKeyDown += OnKey;
        CompositionTarget.Rendering += OnFrame;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => OnSecond();
        timer.Start();
        Sys.RefreshWifiAsync();
        var wifiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        wifiTimer.Tick += (_, _) => Sys.RefreshWifiAsync();
        wifiTimer.Start();

        Loaded += (_, _) =>
        {
            Sys.Cpu();
            Taskbar.Hide();
            DockWin.Show();
            DockHotkey.Install();
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            settle.Tick += (_, _) => { settle.Stop(); DockWin.Place(); };
            settle.Start();
            OnSecond();
            if (!Settings.Bool("onboarded")) Onboarding.Show();
            else Notify.Post("Bisman OS", "", "Welcome back, " + Environment.UserName.Split(' ')[0], "Everything is ready. Press Ctrl+Space for Start or Alt+Space to search.");
        };
    }

    void RegisterApps()
    {
        Catalog.AddInternal("bisman:files", "Files", "", Th.C("#5AB0FF"), Th.C("#2563EB"), () => OpenFiles(null));
        Catalog.AddInternal("bisman:terminal", "Terminal", "", Th.C("#26302A"), Th.C("#0A0F0B"), () => OpenTerminal(null, null));
        Catalog.AddInternal("bisman:settings", "System Settings", "", Th.C("#9AA4B5"), Th.C("#4B5363"), () => OpenSettings(null));
        Catalog.AddInternal("bisman:calculator", "Calculator", "", Th.C("#FFAA4D"), Th.C("#F07A1A"), OpenCalculator);
        Catalog.AddInternal("bisman:taskmanager", "Task Manager", "", Th.C("#8B7CFF"), Th.C("#5B3FD9"), OpenTaskManager);
        Shortcuts.LoadIntoCatalog();
    }

    // ---------------- app openers
    public void OpenTerminal(string cwd, string run) { Hide4(); TerminalApp.Open(cwd, run); }

    public void OpenFiles(string path)
    {
        Hide4();
        var existing = Wm.For("bisman:files").LastOrDefault();
        if (existing != null && path == null) { Wm.Restore(existing); return; }
        FilesApp.Open(path);
    }

    public void OpenSettings(string page)
    {
        Hide4();
        var w = Wm.For("bisman:settings").FirstOrDefault();
        if (w != null) { Wm.Restore(w); if (page != null) ((SettingsView)w.Body).Go(page); return; }
        SettingsApp.Open(page);
    }

    public void OpenCalculator()
    {
        Hide4();
        var w = Wm.For("bisman:calculator").FirstOrDefault();
        if (w != null) { Wm.Restore(w); return; }
        Wm.Open(new ShellWin("bisman:calculator", "Calculator", 340, 540, new CalcView()) { MinW = 300, MinH = 460 });
    }

    public void OpenTaskManager()
    {
        Hide4();
        var w = Wm.For("bisman:taskmanager").FirstOrDefault();
        if (w != null) { Wm.Restore(w); return; }
        Wm.Open(new ShellWin("bisman:taskmanager", "Task Manager", 980, 660, new TaskView()) { MinW = 640, MinH = 420 });
    }

    public void OpenAbout() { Hide4(); AboutApp.Open(); }

    // Dock / Start click on an internal app: open it, bring it forward, or minimise it.
    public void ActivateApp(string id)
    {
        var wins = Wm.For(id);
        if (wins.Count == 0) { Catalog.Launch(Catalog.ById[id]); return; }
        if (Wm.Active != null && Wm.Active.AppId == id && !Wm.Active.IsMin) { Wm.Minimize(Wm.Active); return; }
        var top = wins.OrderByDescending(Panel.GetZIndex).First();
        Wm.Restore(top);
    }

    public void Hide4() { Start.Hide(); Spot.Hide(); CC.Hide(); NC.Hide(); }

    // Brings the Bisman OS desktop in front of other apps (used by the dock, which lives in its own window).
    public void BringFront()
    {
        if (IsActive) return;
        Activate(); Topmost = true; Topmost = false; Focus();
    }

    public void ExitNow() => Application.Current.Shutdown();

    public void ConfirmExit() => Dialog.Confirm("Exit Bisman OS?", "You'll go back to the normal Windows desktop. Your apps keep running.", "Exit", false, () => Application.Current.Shutdown(), "");

    public void PowerMenu(UIElement target, PlacementMode mode)
    {
        Ui.ShowMenu(target, Ui.Menu(
            ("", "Lock", () => { Hide4(); Sys.Lock(); }),
            ("", "Sleep", () => { Hide4(); Sys.Sleep(); }),
            ("", "Sign out…", () => Dialog.Confirm("Sign out of Windows?", "Apps will close. Save your work first.", "Sign out", true, Sys.SignOut, "")),
            Ui.Sep,
            ("", "Restart…", () => Dialog.Confirm("Restart your PC?", "Apps will close. Save your work first.", "Restart", true, Sys.Restart, "")),
            ("", "Shut down…", () => Dialog.Confirm("Shut down your PC?", "Apps will close. Save your work first.", "Shut down", true, Sys.ShutDown, "")),
            Ui.Sep,
            ("", "Exit to Windows", ConfirmExit)), mode);
    }

    // ---------------- external (real Windows) app windows
    public void FocusExternal(IntPtr h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
        Native.SetForegroundWindow(h);
    }

    void ScanExternal()
    {
        var list = new List<ExtWin>();
        uint me = (uint)Process.GetCurrentProcess().Id;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, 4) != IntPtr.Zero) return true;
            if ((Native.GetWindowLong(h, -20) & 0x80) != 0) return true;
            int len = Native.GetWindowTextLength(h); if (len == 0) return true;
            if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == me) return true;
            var sb = new StringBuilder(len + 1); Native.GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title == "Program Manager") return true;
            if (!exeCache.TryGetValue(pid, out var exe)) { exe = Native.ProcessPath(pid); exeCache[pid] = exe; }
            list.Add(new ExtWin { H = h, Title = title, Pid = pid, Exe = exe, Iconic = Native.IsIconic(h) });
            return true;
        }, IntPtr.Zero);
        External = list;
        if (exeCache.Count > 400) exeCache.Clear();
        Dock.UpdateRunning(list);
    }

    // ---------------- loops
    void OnFrame(object s, EventArgs e)
    {
        double now = sw.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - last); last = now; frame++;
        bool live = Settings.Bool("liveWallpaper") && !paused;
        Wall.Live = live;
        liveAcc += dt;
        // The drift is slow, so 30 frames a second looks the same and halves the graphics work.
        if (frame % 2 == 0)
        {
            if (live) { Wall.Tick(liveAcc); amb.Tick(liveAcc); }
            liveAcc = 0;
        }
        if (!paused || Dock.MouseNear) Glass.UpdateAll(Root);
        Dock.Tick();
    }

    void OnSecond()
    {
        Bar.Update();
        Widgets.Update();
        ScanExternal();
        var fg = Native.GetForegroundWindow();
        bool ours = fg == Hwnd;
        if (!ours && fg != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(fg, out uint pid);
            ours = pid == (uint)Process.GetCurrentProcess().Id;
        }
        bool covered = false, fullscreen = false;
        if (!ours && fg != IntPtr.Zero)
        {
            LastExternalFg = fg;
            Native.GetWindowRect(fg, out var r);
            fullscreen = r.Right - r.Left >= Native.GetSystemMetrics(0) && r.Bottom - r.Top >= Native.GetSystemMetrics(1);
            if (fullscreen)
            {
                var cls = new StringBuilder(64); GetClassName(fg, cls, 64);
                if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW") fullscreen = false;
            }
            covered = Native.IsZoomed(fg) || fullscreen;
        }
        paused = covered || WindowState == WindowState.Minimized;
        Wall.SetPlaying(!paused);
        Taskbar.KeepHidden();
        // Step aside for real full-screen apps (videos, games); otherwise the dock stays on top of everything.
        if (fullscreen && DockWin.IsVisible) DockWin.Hide();
        else if (!fullscreen && !DockWin.IsVisible && IsLoaded) DockWin.Show();

        // Real system events become notifications.
        var p = Sys.Battery();
        if (p.HasBattery && lastPower.HasBattery)
        {
            if (p.Plugged != lastPower.Plugged && Settings.Bool("notifyBattery"))
                Notify.Post("System", Sys.BatteryGlyph(p), p.Plugged ? "Charger connected" : "Running on battery", p.Percent + "% battery");
            if (!p.Plugged && p.Percent <= 20 && !batteryWarned && Settings.Bool("notifyBattery"))
            {
                batteryWarned = true;
                Notify.Post("System", Sys.BatteryGlyph(p), "Battery low", p.Percent + "% left. Plug in your charger soon.", () => Shell.Run("ms-settings:batterysaver"));
            }
            if (p.Plugged) batteryWarned = false;
        }
        lastPower = p;
        var net = Sys.Network();
        if (lastNetUp.HasValue && lastNetUp.Value != net.Up && Settings.Bool("notifyNetwork"))
            Notify.Post("System", net.Up ? "" : "", net.Up ? "Connected" : "Disconnected", net.Up ? net.Kind + " · " + net.Name : "Your PC is offline.");
        lastNetUp = net.Up;
    }

    void OnSetting(string k)
    {
        switch (k)
        {
            case "accent": Th.SetAccent(Settings.Get("accent")); break;
            case "glassBlur": case "glassTint": Glass.RefreshAll(); break;
            case "wallpaper": Wall.Load(Settings.Get("wallpaper")); break;
            case "dockSize": case "dockMagnify": Dock.Rebuild(); DockWin.Place(); break;
            case "hideTaskbar": if (Settings.Bool("hideTaskbar")) Taskbar.Hide(); else Taskbar.Restore(); DockWin.Place(); break;
            case "fastGpu": Gpu.Apply(); break;
            case "showDesktopIcons": Icons.Reload(); break;
            case "showWidgets": Widgets.Visibility = Settings.Bool("showWidgets") ? Visibility.Visible : Visibility.Collapsed; break;
            case "clock24": case "showSeconds": Bar.Update(); Widgets.Update(); break;
        }
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate(); Topmost = true; Topmost = false; Focus();
            if (w.ToInt32() == 2) Spot.Show();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---------------- keyboard shortcuts
    void OnKey(object s, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var m = Keyboard.Modifiers;
        if (Dialog.IsOpen)
        {
            if (k == Key.Escape) { Dialog.Close(); e.Handled = true; }
            else if (k == Key.Enter) { Dialog.Accept(); e.Handled = true; }
            return;
        }
        bool ctrl = m == ModifierKeys.Control, ctrlShift = m == (ModifierKeys.Control | ModifierKeys.Shift), ctrlAlt = m == (ModifierKeys.Control | ModifierKeys.Alt), alt = m == ModifierKeys.Alt;
        var a = Wm.Active;
        bool done = true;
        if (ctrlShift && k == Key.Q) ConfirmExit();
        else if (ctrl && k == Key.Space) Start.Toggle();
        else if (alt && k == Key.Space) Spot.Toggle();
        else if (ctrlAlt && k == Key.T) OpenTerminal(null, null);
        else if (ctrlAlt && k == Key.E) FilesApp.Open(null);
        else if (ctrlAlt && k == Key.M) OpenTaskManager();
        else if (ctrlAlt && k == Key.N) NC.Toggle();
        else if (ctrlAlt && k == Key.C) CC.Toggle();
        else if (ctrl && k == Key.OemComma) OpenSettings(null);
        else if (ctrl && k == Key.W && a != null) { if (a.OnCloseShortcut == null || !a.OnCloseShortcut()) Wm.Close(a); }
        else if (ctrl && k == Key.M && a != null) Wm.Minimize(a);
        else if (ctrlAlt && k == Key.Up && a != null) { if (!a.IsMax) Wm.ToggleMax(a); }
        else if (ctrlAlt && k == Key.Down && a != null) { if (a.IsMax) Wm.ToggleMax(a); else Wm.Minimize(a); }
        else if (ctrl && k == Key.Oem3) Wm.Cycle();
        else if (k == Key.Escape && (Start.IsOpen || Spot.IsOpen || CC.IsOpen || NC.IsOpen)) Hide4();
        else if (k == Key.F5 && a == null) Icons.Reload();
        else done = false;
        if (done) { e.Handled = true; return; }
        if (Spot.IsOpen && Spot.HandleKey(k)) { e.Handled = true; return; }
        if (Start.IsOpen && Start.HandleKey(k)) { e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return;
        if (a != null && a.Body is IKeyHandler kh) { if (kh.HandleKey(k, m)) e.Handled = true; return; }
        if (a == null && Icons.HandleKey(k, m)) e.Handled = true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "BismanOS.Shell.2", out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Settings.Log(e.Exception.ToString());
            Notify.Post("Bisman OS", "", "Something went wrong", e.Exception.Message);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Settings.Log(e.ExceptionObject.ToString()); Taskbar.Restore(); };
        // Always give the Windows taskbar back, however Bisman OS closes.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Taskbar.Restore();
        app.Exit += (_, _) => Taskbar.Restore();
        app.SessionEnding += (_, _) => Taskbar.Restore();
        Gpu.Apply();
        Styles.Install(app);
        ShellImages.Init(app.Dispatcher);
        app.Run(new Desktop());
    }
}

// ================================================================ top menu bar
public class MenuBar : Grid
{
    readonly TextBlock appName, clock, battPct, battGlyph, netGlyph, volGlyph;
    readonly Border netBtn, battBtn, volBtn, searchBtn, ccBtn, bellBtn, clockBtn, logoBtn;
    readonly Ellipse bellDot;

    public MenuBar()
    {
        Height = 32; VerticalAlignment = VerticalAlignment.Top;
        var glass = new Glass(0, 0.42) { BorderThickness = new Thickness(0, 0, 0, 1) };
        Children.Add(glass);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var logo = Ui.Logo(18);
        logoBtn = Item(logo, () => LogoMenu());
        left.Children.Add(logoBtn);
        appName = Ui.Txt("Desktop", 13, FontWeights.SemiBold);
        appName.Margin = new Thickness(6, 0, 0, 0);
        left.Children.Add(appName);
        Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        searchBtn = Item(Ui.Glyph("", 13), () => Desktop.I.Spot.Toggle(), "Search (Alt+Space)");
        netGlyph = Ui.Glyph("", 14); netBtn = Item(netGlyph, () => Desktop.I.CC.Toggle());
        battGlyph = Ui.Glyph("", 15);
        battPct = Ui.Tab(Ui.Txt("", 12, FontWeights.Medium)); battPct.Margin = new Thickness(5, 0, 0, 0);
        var bsp = new StackPanel { Orientation = Orientation.Horizontal }; bsp.Children.Add(battGlyph); bsp.Children.Add(battPct);
        battBtn = Item(bsp, () => Desktop.I.CC.Toggle());
        volGlyph = Ui.Glyph("", 14); volBtn = Item(volGlyph, () => Desktop.I.CC.Toggle());
        ccBtn = Item(Ui.Glyph("", 13), () => Desktop.I.CC.Toggle(), "Control Centre (Ctrl+Alt+C)");
        var bell = new Grid(); bell.Children.Add(Ui.Glyph("", 13));
        bellDot = new Ellipse { Width = 6, Height = 6, Fill = Th.Accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, -3, 0), Visibility = Visibility.Collapsed };
        bell.Children.Add(bellDot);
        bellBtn = Item(bell, () => Desktop.I.NC.Toggle(), "Notifications (Ctrl+Alt+N)");
        clock = Ui.Tab(Ui.Txt("", 13, FontWeights.Medium));
        clockBtn = Item(clock, () => Desktop.I.NC.Toggle());
        var addBtn = Item(Ui.Glyph("", 13), Shortcuts.Show, "New shortcut: add any app, file, folder or website");
        var back = new StackPanel { Orientation = Orientation.Horizontal };
        back.Children.Add(Ui.Glyph("", 11));
        var bt = Ui.Txt("Windows", 12.5, FontWeights.Medium); bt.Margin = new Thickness(6, 0, 0, 0); back.Children.Add(bt);
        var backBtn = Item(back, () => Desktop.I.ExitNow(), "Back to the normal Windows desktop (Ctrl+Shift+Q)");
        backBtn.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        backBtn.Margin = new Thickness(1, 0, 8, 0);
        foreach (var b in new[] { backBtn, addBtn, searchBtn, netBtn, battBtn, volBtn, ccBtn, bellBtn, clockBtn }) right.Children.Add(b);
        Children.Add(right);
        Notify.Changed += () => bellDot.Visibility = Notify.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public IEnumerable<UIElement> CcTriggers => new UIElement[] { netBtn, battBtn, volBtn, ccBtn };
    public IEnumerable<UIElement> NcTriggers => new UIElement[] { bellBtn, clockBtn };
    public UIElement SearchTrigger => searchBtn;

    Border Item(UIElement content, Action click, string tip = null)
    {
        var b = new Border { Child = content, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(6), Margin = new Thickness(1, 0, 1, 0), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        Ui.Hoverable(b);
        Ui.OnClick(b, click, false);
        return b;
    }

    void LogoMenu()
    {
        var cm = Ui.Menu(
            ("", "About Bisman OS", Desktop.I.OpenAbout),
            Ui.Sep,
            ("", "System Settings…", () => Desktop.I.OpenSettings(null)),
            ("", "Task Manager", Desktop.I.OpenTaskManager),
            ("", "Start", Desktop.I.Start.Show),
            Ui.Sep,
            ("", "Lock", Sys.Lock),
            ("", "Sleep", Sys.Sleep),
            ("", "Restart…", () => Dialog.Confirm("Restart your PC?", "Apps will close. Save your work first.", "Restart", true, Sys.Restart, "")),
            ("", "Shut down…", () => Dialog.Confirm("Shut down your PC?", "Apps will close. Save your work first.", "Shut down", true, Sys.ShutDown, "")),
            ("", "Sign out…", () => Dialog.Confirm("Sign out of Windows?", "Apps will close. Save your work first.", "Sign out", true, Sys.SignOut, "")),
            Ui.Sep,
            ("", "Exit to Windows", Desktop.I.ConfirmExit));
        ((MenuItem)cm.Items[cm.Items.Count - 1]).InputGestureText = "Ctrl+Shift+Q";
        Ui.ShowMenu(logoBtn, cm, PlacementMode.Bottom);
    }

    public void UpdateApp()
    {
        var a = Desktop.I.Wm.Active;
        appName.Text = a != null && Catalog.ById.TryGetValue(a.AppId, out var e) ? e.Name : "Desktop";
    }

    public void Update()
    {
        var now = DateTime.Now;
        string fmt = Settings.Bool("clock24") ? (Settings.Bool("showSeconds") ? "HH:mm:ss" : "HH:mm") : (Settings.Bool("showSeconds") ? "h:mm:ss tt" : "h:mm tt");
        clock.Text = now.ToString("ddd d MMM") + "   " + now.ToString(fmt);
        var p = Sys.Battery();
        battBtn.Visibility = p.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        battGlyph.Text = Sys.BatteryGlyph(p);
        battGlyph.Foreground = !p.Plugged && p.Percent <= 20 ? Th.Danger : Th.Fg;
        battPct.Text = p.Percent + "%";
        battBtn.ToolTip = p.Percent + "%" + (p.Charging ? " · charging" : p.Plugged ? " · plugged in" : "");
        var n = Sys.Network();
        netGlyph.Text = n.Kind == "Ethernet" ? "" : "";
        netGlyph.Opacity = n.Up ? 1 : 0.35;
        netBtn.ToolTip = n.Up ? n.Kind + " · " + n.Name : "Not connected";
        if (Sys.Audio.Available)
        {
            float v = Sys.Audio.Volume; bool mute = Sys.Audio.Muted;
            volGlyph.Text = mute || v < 0.01 ? "" : v < 0.34 ? "" : v < 0.67 ? "" : "";
            volBtn.ToolTip = mute ? "Muted" : "Volume " + (int)Math.Round(v * 100) + "%";
        }
        else volBtn.Visibility = Visibility.Collapsed;
    }
}

// ================================================================ the dock
public class Dock : Grid
{
    class Item
    {
        public string Id, Kind, Label, Exe; public AppEntry App;
        public FrameworkElement Vis; public StackPanel Host; public Ellipse Dot;
        public double Size, Target; public bool Divider;
        public List<IntPtr> Hwnds = new();
        public TranslateTransform Bounce = new();
    }

    readonly Glass bg;
    readonly StackPanel row;
    readonly List<Item> items = new();
    readonly Border label; readonly TextBlock labelText;
    double labelOp;
    const double Gap = 9;
    public bool MouseNear; public double MouseX;
    List<(string key, string exe, string title, AppEntry app, List<IntPtr> hwnds)> extra = new();
    Dictionary<string, List<IntPtr>> matched = new();
    string runSig = "";

    public double Base => Th.Clamp(Settings.Num("dockSize", 50), 36, 72);
    public double MaxSize => Settings.Bool("dockMagnify") ? Base * 1.55 : Base;
    public double TotalBase => items.Sum(i => i.Divider ? 13 : Base + Gap);
    readonly DockWindow win;

    public Dock(DockWindow w)
    {
        win = w;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 8);
        bg = new Glass(20, 0.4) { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        bg.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance };
        var bgHost = new Grid { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, CacheMode = new BitmapCache { SnapsToDevicePixels = true } };
        bgHost.Children.Add(bg);
        row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };
        Children.Add(bgHost); Children.Add(row);
        labelText = Ui.Txt("", 12.5, FontWeights.Medium);
        label = new Border { Background = new SolidColorBrush(Color.FromArgb(236, 0x1B, 0x20, 0x2B)), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6), Child = labelText, Opacity = 0, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1) };
        win.Labels.Children.Add(label);
        Rebuild();
    }

    public void Rebuild()
    {
        row.Children.Clear(); items.Clear();
        Add(new Item { Id = "start", Kind = "start", Label = "Start" }, Ui.Logo(64));
        foreach (var id in Pins.Dock)
            if (Catalog.ById.TryGetValue(id, out var a))
                Add(new Item { Id = id, Kind = a.Internal ? "internal" : "app", App = a, Label = a.Name, Exe = a.ExePath }, AppIcon.Make(a, 64));
        if (extra.Count > 0)
        {
            AddDivider();
            foreach (var x in extra)
            {
                var it = new Item { Id = "run:" + x.key, Kind = "running", App = x.app, Label = x.app?.Name ?? x.title, Exe = x.exe };
                it.Hwnds.AddRange(x.hwnds);
                Add(it, x.app != null ? AppIcon.Make(x.app, 64) : AppIcon.FromShell(x.exe, 64, true));
            }
        }
        AddDivider();
        Add(new Item { Id = "trash", Kind = "trash", Label = "Recycle Bin" }, AppIcon.FromShell("::{645FF040-5081-101B-9F08-00AA002F954E}", 64, true, ""));
        ApplyMatches();
        UpdateIndicators();
    }

    void Add(Item it, FrameworkElement content)
    {
        var vb = new Viewbox { Width = Base, Height = Base, Child = content, RenderTransform = it.Bounce };
        it.Vis = vb; it.Size = it.Target = Base;
        it.Dot = new Ellipse { Width = 4, Height = 4, Fill = Th.Fg, Opacity = 0, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var host = new StackPanel { Margin = new Thickness(Gap / 2, 0, Gap / 2, 0), VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent };
        host.Children.Add(vb); host.Children.Add(it.Dot);
        it.Host = host;
        host.MouseLeftButtonDown += (_, e) => e.Handled = true;
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; Click(it); };
        host.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(it); };
        row.Children.Add(host); items.Add(it);
    }

    void AddDivider()
    {
        var d = new Border { Width = 1, Height = Base * 0.7, Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), Margin = new Thickness(6, 0, 6, Base * 0.15 + 7), VerticalAlignment = VerticalAlignment.Bottom };
        row.Children.Add(d);
        items.Add(new Item { Divider = true, Host = null });
    }

    public FrameworkElement StartVisual => items.Count > 0 ? items[0].Host : null;

    void Click(Item it)
    {
        if (it.Kind != "start") Desktop.I.Hide4();
        switch (it.Kind)
        {
            case "start": Desktop.I.BringFront(); Desktop.I.Start.Toggle(); break;
            case "trash": Shell.Run("explorer.exe", "shell:RecycleBinFolder"); break;
            case "internal": Desktop.I.BringFront(); if (Desktop.I.Wm.For(it.Id).Count == 0) BounceOnce(it); Desktop.I.ActivateApp(it.Id); break;
            default:
                if (it.Hwnds.Count > 0)
                {
                    int i = it.Hwnds.IndexOf(Desktop.I.LastExternalFg);
                    Desktop.I.FocusExternal(it.Hwnds[(i + 1) % it.Hwnds.Count]);
                }
                else { BounceOnce(it); Catalog.Launch(it.App); }
                break;
        }
    }

    void BounceOnce(Item it)
    {
        Anim.Run(it.Bounce, TranslateTransform.YProperty, 0, -Base * 0.32, 200, Anim.Out, () =>
            Anim.Run(it.Bounce, TranslateTransform.YProperty, -Base * 0.32, 0, 300, new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
    }

    void ShowMenu(Item it)
    {
        var list = new List<(string, string, Action)>();
        var a = it.App;
        switch (it.Kind)
        {
            case "start":
                list.Add(("", "Files", () => Desktop.I.OpenFiles(null)));
                list.Add(("", "Terminal", () => Desktop.I.OpenTerminal(null, null)));
                list.Add(("", "Task Manager", Desktop.I.OpenTaskManager));
                list.Add(("", "System Settings", () => Desktop.I.OpenSettings(null)));
                list.Add(("", "New shortcut…", Shortcuts.Show));
                list.Add(Ui.Sep);
                list.Add(("", "Back to Windows", Desktop.I.ExitNow));
                break;
            case "trash":
                list.Add(("", "Open Recycle Bin", () => Shell.Run("explorer.exe", "shell:RecycleBinFolder")));
                break;
            default:
                if (a != null)
                {
                    list.Add(("", it.Kind == "internal" || it.Hwnds.Count == 0 ? "Open" : "New window", () => Catalog.Launch(a)));
                    list.Add(Ui.Sep);
                    list.Add((Pins.InDock(a.Id) ? "" : "", Pins.InDock(a.Id) ? "Remove from Dock" : "Keep in Dock", () => Pins.ToggleDock(a.Id)));
                    list.Add((Pins.InStart(a.Id) ? "" : "", Pins.InStart(a.Id) ? "Unpin from Start" : "Pin to Start", () => Pins.ToggleStart(a.Id)));
                    if (a.Path != null) list.Add(("", "Open file location", () => Shell.Reveal(a.Path)));
                }
                else if (it.Exe != null) list.Add(("", "Open file location", () => Shell.Reveal(it.Exe)));
                if (it.Kind == "internal" && Desktop.I.Wm.For(it.Id).Count > 0)
                {
                    list.Add(Ui.Sep);
                    list.Add(("", "Quit", () => { foreach (var w in Desktop.I.Wm.For(it.Id)) Desktop.I.Wm.Close(w); }));
                }
                else if (it.Hwnds.Count > 0)
                {
                    list.Add(Ui.Sep);
                    var hs = it.Hwnds.ToList();
                    list.Add(("", hs.Count > 1 ? "Close all windows" : "Close window", () => { foreach (var h in hs) Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }));
                }
                break;
        }
        Ui.ShowMenu(it.Host, Ui.Menu(list.ToArray()), PlacementMode.Top);
    }

    // Groups real Windows windows by app; pinned apps get a running dot, others appear after the divider.
    public void UpdateRunning(List<ExtWin> wins)
    {
        matched = new Dictionary<string, List<IntPtr>>();
        var groups = new Dictionary<string, (string key, string exe, string title, AppEntry app, List<IntPtr> hwnds)>(StringComparer.OrdinalIgnoreCase);
        var pinned = Pins.Dock.Where(id => Catalog.ById.ContainsKey(id)).Select(id => Catalog.ById[id]).Where(a => !a.Internal).ToList();
        foreach (var w in wins)
        {
            AppEntry hit = null;
            string exeName = w.Exe != null ? Path.GetFileName(w.Exe) : null;
            bool frameHost = exeName != null && exeName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            foreach (var a in pinned)
            {
                if (frameHost) { if (w.Title == a.Name || w.Title.EndsWith("- " + a.Name)) { hit = a; break; } }
                else if (a.ExePath != null && exeName != null && Path.GetFileName(a.ExePath).Equals(exeName, StringComparison.OrdinalIgnoreCase)) { hit = a; break; }
            }
            if (hit != null)
            {
                if (!matched.TryGetValue(hit.Id, out var l)) matched[hit.Id] = l = new List<IntPtr>();
                l.Add(w.H);
                continue;
            }
            string key = frameHost ? "uwp:" + w.Title : (w.Exe ?? "pid:" + w.Pid);
            if (!groups.TryGetValue(key, out var g))
            {
                AppEntry app = frameHost ? Catalog.Apps.FirstOrDefault(a => !a.Internal && a.Name == w.Title)
                    : Catalog.Apps.FirstOrDefault(a => a.ExePath != null && exeName != null && Path.GetFileName(a.ExePath).Equals(exeName, StringComparison.OrdinalIgnoreCase));
                g = (key, w.Exe, w.Title, app, new List<IntPtr>());
                groups[key] = g;
            }
            g.hwnds.Add(w.H);
        }
        var newExtra = groups.Values.Take(8).ToList();
        string sig = string.Join("|", newExtra.Select(x => x.key));
        extra = newExtra;
        if (sig != runSig) { runSig = sig; Rebuild(); }
        else
        {
            foreach (var x in extra) { var it = items.FirstOrDefault(i => i.Id == "run:" + x.key); if (it != null) { it.Hwnds = x.hwnds; } }
            ApplyMatches(); UpdateIndicators();
        }
    }

    void ApplyMatches()
    {
        foreach (var it in items)
            if (it.Kind == "app") it.Hwnds = matched.TryGetValue(it.Id, out var l) ? l : new List<IntPtr>();
    }

    public void UpdateIndicators()
    {
        foreach (var it in items)
        {
            if (it.Divider || it.Dot == null) continue;
            bool on = it.Kind == "internal" ? Desktop.I.Wm.For(it.Id).Count > 0 : it.Hwnds.Count > 0;
            Anim.To(it.Dot, UIElement.OpacityProperty, on ? 0.9 : 0, 200);
        }
    }

    public Point? PointFor(string appId)
    {
        var it = items.FirstOrDefault(i => i.Id == appId);
        if (it?.Host == null || !it.Host.IsVisible) return null;
        try { return Desktop.I.Root.PointFromScreen(it.Host.PointToScreen(new Point(it.Host.ActualWidth / 2, it.Size / 2))); }
        catch { return null; }
    }

    // macOS magnification: icons near the pointer swell on a cosine curve and push neighbours aside.
    public void Tick()
    {
        if (items.Count == 0) return;
        double b = Base, mx = MaxSize, range = b * 3.3;
        double x = win.Host.ActualWidth / 2 - TotalBase / 2;
        Item hovered = null;
        foreach (var it in items)
        {
            double w = it.Divider ? 13 : b + Gap, c = x + w / 2; x += w;
            if (it.Divider) continue;
            double d = Math.Abs(MouseX - c);
            double f = MouseNear && d < range && !Anim.Reduced ? (Math.Cos(Math.PI * d / range) + 1) / 2 : 0;
            it.Target = b + (mx - b) * f;
            if (MouseNear && d < w / 2) hovered = it;
            double next = it.Size + (it.Target - it.Size) * 0.3;
            if (Math.Abs(next - it.Target) < 0.05) next = it.Target;
            if (next != it.Size) { it.Size = next; it.Vis.Width = it.Vis.Height = next; }
        }
        bg.Width = row.ActualWidth + 14;
        bg.Height = b + 18;
        labelOp += ((hovered != null ? 1 : 0) - labelOp) * 0.25;
        label.Opacity = labelOp;
        if (hovered != null)
        {
            labelText.Text = hovered.Label;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var p = hovered.Host.TranslatePoint(new Point(hovered.Host.ActualWidth / 2, 0), win.Host);
            double top = p.Y + (hovered.Host.ActualHeight - hovered.Size - 7) - label.DesiredSize.Height - 10;
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, top);
        }
    }
}

// ================================================================ real desktop icons (your Desktop folder)
public class DesktopIcons : Canvas
{
    class Icon { public string Path, Name; public Border Card; public TextBlock Label; public bool IsDir; }

    readonly List<Icon> icons = new();
    readonly HashSet<Icon> sel = new();
    readonly Dictionary<string, Point> pos = new(StringComparer.OrdinalIgnoreCase);
    const double CW = 92, CH = 106, X0 = 16, Y0 = 46;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly List<FileSystemWatcher> watchers = new();

    // dragging icons
    Icon anchor, dropTarget; Point dragStart; bool pressed, moved, ctrlDown;
    Dictionary<Icon, Point> startPos = new();
    // selection box
    readonly Border band;
    bool banding; Point bandStart; HashSet<Icon> bandBase = new();

    static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public DesktopIcons()
    {
        Background = Brushes.Transparent;
        AllowDrop = true;
        band = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        foreach (var line in Settings.LoadList("desktop-positions"))
        {
            var p = line.Split('|');
            if (p.Length == 3 && double.TryParse(p[1], out var x) && double.TryParse(p[2], out var y)) pos[p[0]] = new Point(x, y);
        }
        debounce.Tick += (_, _) => { debounce.Stop(); Reload(); };
        foreach (var d in new[] { UserDesktop, PublicDesktop })
        {
            try
            {
                var w = new FileSystemWatcher(d) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); }));
                w.Created += h; w.Deleted += h; w.Renamed += (_, _) => h(null, null);
                watchers.Add(w);
            }
            catch { }
        }

        // Click and drag on empty desktop space to draw a selection box.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != this) return;
            Desktop.I.Wm.Deactivate();
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) sel.Clear();
            Paint();
            banding = true; bandStart = e.GetPosition(this); bandBase = new HashSet<Icon>(sel);
            band.Background = Th.A(46, Th.Accent.Color); band.BorderBrush = Th.A(200, Th.Accent.Color);
            SetLeft(band, bandStart.X); SetTop(band, bandStart.Y); band.Width = 0; band.Height = 0;
            band.Visibility = Visibility.Visible;
            CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!banding) return;
            var p = e.GetPosition(this);
            var r = new Rect(bandStart, p);
            SetLeft(band, r.X); SetTop(band, r.Y); band.Width = r.Width; band.Height = r.Height;
            sel.Clear();
            foreach (var i in bandBase) sel.Add(i);
            foreach (var ic in icons) if (r.IntersectsWith(CardRect(ic))) sel.Add(ic);
            Paint();
        };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!banding) return;
            banding = false; band.Visibility = Visibility.Collapsed; ReleaseMouseCapture();
        };
        MouseRightButtonUp += (_, e) => { if (e.OriginalSource == this) BackgroundMenu(); };

        // Files dragged in from other apps are copied onto the desktop.
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            var outside = files.Where(f => !string.Equals(Path.GetDirectoryName(f), UserDesktop, StringComparison.OrdinalIgnoreCase)).ToList();
            Transfer(outside, UserDesktop, false);
        };
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        var keep = new HashSet<string>(sel.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        Children.Clear(); icons.Clear(); sel.Clear();
        Children.Add(band); SetZIndex(band, 500);
        if (!Settings.Bool("showDesktopIcons")) return;
        var entries = new List<string>();
        foreach (var d in new[] { UserDesktop, PublicDesktop })
        {
            try
            {
                foreach (var e in Directory.GetFileSystemEntries(d))
                {
                    var attr = File.GetAttributes(e);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (Path.GetFileName(e).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    entries.Add(e);
                }
            }
            catch { }
        }
        entries = entries.OrderBy(e => Directory.Exists(e) ? 0 : 1).ThenBy(e => Path.GetFileName(e), StringComparer.CurrentCultureIgnoreCase).ToList();
        var used = new HashSet<(int, int)>();
        int rows = Math.Max(1, (int)((ActualHeight - Y0 - 110) / CH));
        var pending = new List<Icon>();
        foreach (var e in entries)
        {
            var ic = Make(e);
            if (keep.Contains(e)) sel.Add(ic);
            if (pos.TryGetValue(ic.Name, out var p)) { Place(ic, p); used.Add(Cell(p)); }
            else pending.Add(ic);
        }
        int n = 0;
        foreach (var ic in pending)
        {
            (int c, int r) cell;
            do { cell = (n / rows, n % rows); n++; } while (used.Contains(cell));
            used.Add(cell);
            Place(ic, new Point(X0 + cell.c * CW, Y0 + cell.r * CH));
        }
        Paint();
    }

    (int, int) Cell(Point p) => ((int)Math.Round((p.X - X0) / CW), (int)Math.Round((p.Y - Y0) / CH));
    Rect CardRect(Icon i) => new(GetLeft(i.Card), GetTop(i.Card), i.Card.ActualWidth > 0 ? i.Card.ActualWidth : CW - 6, i.Card.ActualHeight > 0 ? i.Card.ActualHeight : CH - 10);

    Icon Make(string path)
    {
        bool dir = Directory.Exists(path);
        string file = Path.GetFileName(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = (ext == ".lnk" || ext == ".url") ? Path.GetFileNameWithoutExtension(path) : file;
        var ic = new Icon { Path = path, Name = file, IsDir = dir };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var img = AppIcon.FromShell(path, 52, false, dir ? "" : "");
        img.HorizontalAlignment = HorizontalAlignment.Center;
        img.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.4, Direction = 270 };
        sp.Children.Add(img);
        ic.Label = new TextBlock
        {
            Text = name, FontSize = 12, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 6, 0, 0), FontFamily = Th.Text,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.85, Direction = 270 }
        };
        sp.Children.Add(ic.Label);
        ic.Card = new Border { Width = CW - 6, Padding = new Thickness(4, 6, 4, 6), CornerRadius = new CornerRadius(10), Child = sp, Background = Brushes.Transparent, BorderThickness = new Thickness(1) };
        // Caching keeps the icon and label shadows from being redrawn every frame.
        ic.Card.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        ic.Card.MouseEnter += (_, _) => { if (!sel.Contains(ic) && dropTarget != ic) ic.Card.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)); };
        ic.Card.MouseLeave += (_, _) => { if (!sel.Contains(ic) && dropTarget != ic) ic.Card.Background = Brushes.Transparent; };
        ic.Card.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Desktop.I.Wm.Deactivate();
            if (e.ClickCount == 2) { Shell.Open(ic.Path); return; }
            ctrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (ctrlDown) { if (!sel.Remove(ic)) sel.Add(ic); }
            else if (!sel.Contains(ic)) { sel.Clear(); sel.Add(ic); }
            Paint();
            if (!sel.Contains(ic)) return;
            anchor = ic; pressed = true; moved = false; dragStart = e.GetPosition(this);
            startPos = sel.ToDictionary(i => i, i => new Point(GetLeft(i.Card), GetTop(i.Card)));
            ic.Card.CaptureMouse();
        };
        ic.Card.MouseMove += (_, e) =>
        {
            if (!pressed || anchor != ic) return;
            var p = e.GetPosition(this);
            if (!moved && (Math.Abs(p.X - dragStart.X) > 4 || Math.Abs(p.Y - dragStart.Y) > 4))
            {
                moved = true;
                foreach (var s in sel) { s.Card.Opacity = 0.75; SetZIndex(s.Card, 100); }
            }
            if (!moved) return;
            double dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
            foreach (var kv in startPos) { SetLeft(kv.Key.Card, kv.Value.X + dx); SetTop(kv.Key.Card, kv.Value.Y + dy); }
            // Hovering over a folder while dragging highlights it as a drop target.
            var t = icons.FirstOrDefault(i => i.IsDir && !sel.Contains(i) && CardRect(i).Contains(p));
            if (t != dropTarget)
            {
                var old = dropTarget; dropTarget = t;
                if (old != null) { old.Card.Background = Brushes.Transparent; old.Card.BorderBrush = null; }
                if (t != null) { t.Card.Background = Th.A(70, Th.Accent.Color); t.Card.BorderBrush = Th.Accent; }
            }
        };
        ic.Card.MouseLeftButtonUp += (_, _) =>
        {
            if (!pressed || anchor != ic) return;
            pressed = false; ic.Card.ReleaseMouseCapture();
            foreach (var s in sel) { s.Card.Opacity = 1; SetZIndex(s.Card, 0); }
            if (moved)
            {
                if (dropTarget != null)
                {
                    var target = dropTarget; dropTarget = null;
                    foreach (var kv in startPos) Place(kv.Key, kv.Value);
                    Transfer(sel.Select(i => i.Path).ToList(), target.Path, true);
                    Paint();
                }
                else foreach (var s in sel.OrderBy(i => GetLeft(i.Card)).ThenBy(i => GetTop(i.Card)).ToList()) Snap(s);
            }
            else if (!ctrlDown && sel.Count > 1) { sel.Clear(); sel.Add(ic); Paint(); }
        };
        ic.Card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!sel.Contains(ic)) { sel.Clear(); sel.Add(ic); Paint(); }
            if (sel.Count > 1) MultiMenu(ic); else IconMenu(ic);
        };
        icons.Add(ic);
        Children.Add(ic.Card);
        return ic;
    }

    void Place(Icon ic, Point p) { SetLeft(ic.Card, p.X); SetTop(ic.Card, p.Y); }

    void Snap(Icon ic)
    {
        var (c, r) = Cell(new Point(GetLeft(ic.Card), GetTop(ic.Card)));
        c = Math.Max(0, c); r = Math.Max(0, r);
        var taken = new HashSet<(int, int)>(icons.Where(i => i != ic).Select(i => Cell(new Point(GetLeft(i.Card), GetTop(i.Card)))));
        while (taken.Contains((c, r))) r++;
        var p = new Point(X0 + c * CW, Y0 + r * CH);
        SetLeft(ic.Card, p.X); SetTop(ic.Card, p.Y);
        pos[ic.Name] = p;
        SavePositions();
    }

    void SavePositions()
    {
        foreach (var i in icons) pos[i.Name] = new Point(GetLeft(i.Card), GetTop(i.Card));
        Settings.SaveList("desktop-positions", pos.Select(kv => kv.Key + "|" + Math.Round(kv.Value.X) + "|" + Math.Round(kv.Value.Y)));
    }

    void Paint()
    {
        foreach (var ic in icons)
        {
            if (ic == dropTarget) continue;
            bool on = sel.Contains(ic);
            ic.Card.Background = on ? Th.AccentSoft : Brushes.Transparent;
            ic.Card.BorderBrush = on ? Th.A(90, Th.Accent.Color) : null;
        }
    }

    // Moves (or copies) files on a background thread, with the normal Windows progress and conflict dialogs.
    void Transfer(List<string> files, string dest, bool move)
    {
        if (files.Count == 0) return;
        var th = new Thread(() =>
        {
            foreach (var src in files)
            {
                try
                {
                    if (string.Equals(src.TrimEnd('\\'), dest.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    string t = Path.Combine(dest, Path.GetFileName(src.TrimEnd('\\')));
                    if (Directory.Exists(src))
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(src, t, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, t, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    }
                    else
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(src, t, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(src, t, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { var name = Path.GetFileName(src); Dispatcher.BeginInvoke(new Action(() => Notify.Post("Desktop", "", "Couldn't move " + name, ex.Message))); }
            }
            Dispatcher.BeginInvoke(new Action(Reload));
        }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
    }

    void IconMenu(Icon ic)
    {
        Ui.ShowMenu(ic.Card, Ui.Menu(
            ("", "Open", () => Shell.Open(ic.Path)),
            ic.IsDir ? ("", "Open in Files", () => Desktop.I.OpenFiles(ic.Path)) : ("", "Open with…", () => Shell.OpenWith(ic.Path)),
            ("", "Open file location", () => Shell.Reveal(ic.Path)),
            Ui.Sep,
            ("", "Rename", () => BeginRename(ic)),
            ("", "Copy path", () => Clipboard.SetText(ic.Path)),
            ("", "Move to Recycle Bin", ConfirmDelete),
            Ui.Sep,
            ("", "Properties", () => Shell.Properties(ic.Path))));
    }

    void MultiMenu(Icon ic)
    {
        int n = sel.Count;
        Ui.ShowMenu(ic.Card, Ui.Menu(
            ("", "Open " + n + " items", () => { foreach (var s in sel.Take(15).ToList()) Shell.Open(s.Path); }),
            ("", "Copy paths", () => Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(s => s.Path)))),
            Ui.Sep,
            ("", "Move " + n + " items to Recycle Bin", ConfirmDelete)));
    }

    void BackgroundMenu()
    {
        Ui.ShowMenu(this, Ui.Menu(
            ("", "New folder", () => { var p = Shell.NewFolder(UserDesktop); if (p != null) { Reload(); var ic = icons.FirstOrDefault(i => i.Path == p); if (ic != null) BeginRename(ic); } }),
            ("", "New shortcut…", Shortcuts.Show),
            ("", "Select all", () => { foreach (var i in icons) sel.Add(i); Paint(); }),
            ("", "Refresh", Reload),
            ("", "Sort by name", () => { pos.Clear(); Settings.SaveList("desktop-positions", new string[0]); Reload(); }),
            Ui.Sep,
            ("", "Open Desktop in Files", () => Desktop.I.OpenFiles(UserDesktop)),
            ("", "Open in Terminal", () => Desktop.I.OpenTerminal(UserDesktop, null)),
            Ui.Sep,
            ("", "Change wallpaper…", () => Desktop.I.OpenSettings("Wallpaper")),
            ("", "Set a video as wallpaper…", Wallpaper.ChooseVideo),
            ("", "Desktop & Dock settings…", () => Desktop.I.OpenSettings("Desktop & Dock"))));
    }

    void ConfirmDelete()
    {
        var list = sel.ToList();
        if (list.Count == 0) return;
        string title = list.Count == 1 ? "Move “" + Path.GetFileName(list[0].Path) + "” to the Recycle Bin?" : "Move " + list.Count + " items to the Recycle Bin?";
        Dialog.Confirm(title, "You can restore them from the Recycle Bin later.", "Move to Recycle Bin", true, () => { foreach (var ic in list) Shell.Recycle(ic.Path); Reload(); }, "");
    }

    void BeginRename(Icon ic)
    {
        var sp = (StackPanel)ic.Card.Child;
        var tb = new TextBox { Text = ic.Name, Style = Styles.Get("Field"), FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Background = new SolidColorBrush(Color.FromArgb(200, 0x10, 0x14, 0x1E)), Padding = new Thickness(2) };
        int idx = sp.Children.IndexOf(ic.Label);
        sp.Children.RemoveAt(idx); sp.Children.Insert(idx, tb);
        bool finished = false;
        void Finish(bool commit)
        {
            if (finished) return; finished = true;
            if (commit && tb.Text.Trim().Length > 0 && tb.Text != ic.Name)
            {
                var dest = Shell.Rename(ic.Path, tb.Text.Trim());
                if (dest != null && pos.TryGetValue(ic.Name, out var p)) { pos.Remove(ic.Name); pos[Path.GetFileName(dest)] = p; SavePositions(); }
            }
            Reload();
        }
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Finish(true); else if (e.Key == Key.Escape) Finish(false); };
        tb.LostKeyboardFocus += (_, _) => Finish(true);
        tb.Focus();
        string n = ic.Name; int dot = ic.IsDir ? -1 : n.LastIndexOf('.');
        tb.Select(0, dot > 0 ? dot : n.Length);
    }

    public bool HandleKey(Key k, ModifierKeys m)
    {
        if (m == ModifierKeys.Control && k == Key.A) { foreach (var i in icons) sel.Add(i); Paint(); return true; }
        if (sel.Count == 0) return false;
        if (k == Key.Escape) { sel.Clear(); Paint(); return true; }
        if (k == Key.Enter) { foreach (var s in sel.Take(15).ToList()) Shell.Open(s.Path); return true; }
        if (k == Key.F2 && sel.Count == 1) { BeginRename(sel.First()); return true; }
        if (k == Key.Delete) { ConfirmDelete(); return true; }
        return false;
    }
}

// ================================================================ widgets
public class Widgets : StackPanel
{
    readonly TextBlock greet, time, date, cpuT, memT, batT, netT;
    readonly Border cpuBar, memBar, batBar;

    public Widgets()
    {
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top; Margin = new Thickness(0, 50, 22, 0); Width = 300;
        Visibility = Settings.Bool("showWidgets") ? Visibility.Visible : Visibility.Collapsed;

        var clockCard = new Glass(22);
        var cs = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        greet = Ui.Txt("", 13, FontWeights.Medium, Th.Fg2);
        time = Ui.Tab(Ui.Txt("", 60, FontWeights.Light, null, Th.Display)); time.Margin = new Thickness(-2, 0, 0, -4);
        date = Ui.Txt("", 15, FontWeights.Medium, Th.Fg2);
        cs.Children.Add(greet); cs.Children.Add(time); cs.Children.Add(date);
        clockCard.Body = cs;
        Children.Add(clockCard);

        var sys = new Glass(22) { Margin = new Thickness(0, 14, 0, 0) };
        var ss = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var head = Ui.Txt("System", 13, FontWeights.SemiBold, Th.Fg2); head.Margin = new Thickness(0, 0, 0, 10);
        ss.Children.Add(head);
        ss.Children.Add(Meter("CPU", out cpuT, out cpuBar));
        ss.Children.Add(Meter("Memory", out memT, out memBar));
        ss.Children.Add(Meter("Battery", out batT, out batBar));
        netT = Ui.Txt("", 12.5, null, Th.Fg2); netT.Margin = new Thickness(0, 6, 0, 0);
        ss.Children.Add(netT);
        sys.Body = ss;
        Ui.OnClick(sys, () => Desktop.I.OpenTaskManager(), false);
        Children.Add(sys);
    }

    static FrameworkElement Meter(string name, out TextBlock value, out Border fill)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(Ui.Txt(name, 12.5, FontWeights.Medium));
        value = Ui.Tab(Ui.Txt("", 12.5, null, Th.Fg2)); value.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(value);
        var track = new Border { Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 0) };
        fill = new Border { Height = 5, CornerRadius = new CornerRadius(3), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(track, 1); Grid.SetRow(fill, 1);
        g.Children.Add(track); g.Children.Add(fill);
        return g;
    }

    static void SetBar(Border fill, double pct) => Anim.To(fill, WidthProperty, Th.Clamp(pct, 0, 100) / 100 * 256, 500);

    public void Update()
    {
        if (Visibility != Visibility.Visible) return;
        var now = DateTime.Now;
        int h = now.Hour;
        greet.Text = (h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening") + ", " + Environment.UserName.Split(' ')[0];
        time.Text = now.ToString(Settings.Bool("clock24") ? "HH:mm" : "h:mm");
        date.Text = now.ToString("dddd, d MMMM");
        double cpu = Sys.Cpu();
        cpuT.Text = Math.Round(cpu) + "%"; SetBar(cpuBar, cpu);
        var m = Sys.Memory();
        memT.Text = m.usedGb.ToString("0.0") + " / " + m.totalGb.ToString("0") + " GB"; SetBar(memBar, m.pct);
        var p = Sys.Battery();
        batT.Text = p.HasBattery ? p.Percent + "%" + (p.Charging ? " · charging" : "") : "No battery";
        SetBar(batBar, p.HasBattery ? p.Percent : 0);
        batBar.Background = p.HasBattery && !p.Plugged && p.Percent <= 20 ? Th.Danger : (Brush)Th.Accent;
        var n = Sys.Network();
        netT.Text = n.Up ? n.Kind + " · " + n.Name + (n.Ip != null ? "  ·  " + n.Ip : "") : "Offline";
    }
}
