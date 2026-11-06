// Bisman OS 2 - System Settings, Calculator, Task Manager, About.
namespace BismanOS;

// ================================================================ System Settings
public static class SettingsApp
{
    public static void Open(string page)
    {
        var v = new SettingsView();
        var w = new ShellWin("bisman:settings", "System Settings", 1000, 680, v) { MinW = 720, MinH = 440 };
        v.Win = w;
        Desktop.I.Wm.Open(w);
        v.Go(page ?? "Appearance");
    }
}

public class SettingsView : Grid
{
    public ShellWin Win;
    readonly StackPanel nav = new();
    readonly ScrollViewer content = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox search;
    readonly Dictionary<string, Border> navRows = new();
    string current;
    readonly (string name, string glyph, Color c1, Color c2)[] pages =
    {
        ("Appearance", "", Th.C("#5AB0FF"), Th.C("#2563EB")),
        ("Wallpaper", "", Th.C("#3EE0FF"), Th.C("#1A8FB8")),
        ("Desktop & Dock", "", Th.C("#8B7CFF"), Th.C("#5B3FD9")),
        ("Notifications", "", Th.C("#FF6B6B"), Th.C("#D93F3F")),
        ("Startup", "", Th.C("#4CD93A"), Th.C("#249A1A")),
        ("Windows Settings", "", Th.C("#9AA4B5"), Th.C("#4B5363")),
        ("Keyboard Shortcuts", "", Th.C("#FFAA4D"), Th.C("#F07A1A")),
        ("About", "", Th.C("#C9D1DC"), Th.C("#7D8797")),
    };

    public SettingsView()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        ColumnDefinitions.Add(new ColumnDefinition());
        var side = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), CornerRadius = new CornerRadius(0, 0, 0, 13), BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 0, 1, 0) };
        var ss = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
        var sbox = new Border { Height = 32, CornerRadius = new CornerRadius(9), Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 14) };
        var sd = new DockPanel();
        var sg = Ui.Glyph("", 12, Th.Fg3); DockPanel.SetDock(sg, WDock.Left); sd.Children.Add(sg);
        var sgr = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        search = new TextBox { Style = Styles.Get("Field"), FontSize = 13 };
        var hint = Ui.Txt("Search settings", 12.5, null, Th.Fg3); hint.IsHitTestVisible = false;
        sgr.Children.Add(search); sgr.Children.Add(hint); sd.Children.Add(sgr); sbox.Child = sd;
        search.TextChanged += (_, _) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (search.Text.Trim().Length == 0) Go(current ?? "Appearance"); else ShowSearch(search.Text.Trim()); };
        ss.Children.Add(sbox);

        var prof = new Grid { Margin = new Thickness(6, 0, 0, 16) };
        prof.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        prof.ColumnDefinitions.Add(new ColumnDefinition());
        prof.Children.Add(StartMenu.Avatar(44));
        var pt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pt.Children.Add(Ui.Txt(Environment.UserName, 14, FontWeights.SemiBold));
        pt.Children.Add(Ui.Txt("Windows account", 11.5, null, Th.Fg3));
        Grid.SetColumn(pt, 1); prof.Children.Add(pt);
        var pb = new Border { Child = prof, CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 6, 0, 6) };
        Ui.Hoverable(pb); Ui.OnClick(pb, () => Shell.Run("ms-settings:yourinfo"), false);
        ss.Children.Add(pb);
        foreach (var p in pages) ss.Children.Add(NavRow(p.name, p.glyph, p.c1, p.c2));
        side.Child = new ScrollViewer { Content = ss, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(side);
        SetColumn(content, 1); Children.Add(content);
    }

    Border NavRow(string name, string glyph, Color c1, Color c2)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(AppIcon.Designed(glyph, c1, c2, 24));
        var t = Ui.Txt(name, 13.5, FontWeights.Medium); Grid.SetColumn(t, 1); g.Children.Add(t);
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1, 0, 1), Child = g, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => { if (current != name) b.Background = Th.Hover; };
        b.MouseLeave += (_, _) => { if (current != name) b.Background = Brushes.Transparent; };
        Ui.OnClick(b, () => { search.Text = ""; Go(name); }, false);
        navRows[name] = b;
        return b;
    }

    public void Go(string page)
    {
        if (!pages.Any(p => p.name == page)) page = "Appearance";
        current = page;
        foreach (var kv in navRows) kv.Value.Background = kv.Key == page ? Th.AccentSoft : Brushes.Transparent;
        var sp = new StackPanel { Margin = new Thickness(40, 10, 40, 40), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var title = Ui.Txt(page, 28, FontWeights.SemiBold, null, Th.Display); title.Margin = new Thickness(0, 0, 0, 6);
        sp.Children.Add(title);
        switch (page)
        {
            case "Appearance": Appearance(sp); break;
            case "Wallpaper": WallpaperPage(sp); break;
            case "Desktop & Dock": DockPage(sp); break;
            case "Notifications": NotificationsPage(sp); break;
            case "Startup": StartupPage(sp); break;
            case "Windows Settings": WindowsPage(sp); break;
            case "Keyboard Shortcuts": ShortcutsPage(sp); break;
            case "About": AboutPage(sp); break;
        }
        Show(sp);
        if (Win != null) Win.Title = "System Settings";
    }

    void Show(FrameworkElement sp)
    {
        content.Content = sp; content.ScrollToTop();
        var tt = new TranslateTransform(); sp.RenderTransform = tt;
        Anim.Run(sp, OpacityProperty, 0, 1, 200);
        Anim.Run(tt, TranslateTransform.YProperty, 12, 0, 260);
    }

    void ShowSearch(string q)
    {
        foreach (var kv in navRows) kv.Value.Background = Brushes.Transparent;
        var sp = new StackPanel { Margin = new Thickness(40, 10, 40, 40), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.Txt("Results for “" + q + "”", 22, FontWeights.SemiBold, null, Th.Display));
        var rows = new List<FrameworkElement>();
        foreach (var s in Search.BismanSettings.Where(s => s.title.Has(q) || s.page.Has(q)))
        { var page = s.page; rows.Add(LinkRow(s.glyph, s.title, s.page, () => { search.Text = ""; Go(page); })); }
        foreach (var s in Search.WindowsSettings.Where(s => s.title.Has(q)))
        { var uri = s.uri; rows.Add(LinkRow(s.glyph, s.title, "Windows settings", () => Shell.Run(uri))); }
        if (rows.Count == 0) { var t = Ui.Txt("No settings match.", 14, null, Th.Fg3); t.Margin = new Thickness(0, 16, 0, 0); sp.Children.Add(t); }
        else { sp.Children.Add(Section("")); sp.Children.Add(Group(rows.ToArray())); }
        content.Content = sp;
    }

    // ---------------- building blocks
    static TextBlock Section(string t) { var s = Ui.Txt(t, 13, FontWeights.SemiBold, Th.Fg2); s.Margin = new Thickness(4, 24, 0, 8); return s; }

    static Border Group(params FrameworkElement[] rows)
    {
        var sp = new StackPanel();
        for (int i = 0; i < rows.Length; i++) { if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Th.Hairline, Margin = new Thickness(16, 0, 16, 0) }); sp.Children.Add(rows[i]); }
        return new Border { CornerRadius = new CornerRadius(14), Background = Th.Card, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Child = sp };
    }

    static FrameworkElement Row(string title, string sub, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(18, 13, 16, 13) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        t.Children.Add(Ui.Txt(title, 13.5, FontWeights.Medium));
        if (sub != null) { var s = Ui.Txt(sub, 12, null, Th.Fg3); s.TextWrapping = TextWrapping.Wrap; s.TextTrimming = TextTrimming.None; s.Margin = new Thickness(0, 2, 0, 0); t.Children.Add(s); }
        g.Children.Add(t);
        if (control != null) { control.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(control, 1); g.Children.Add(control); }
        return g;
    }

    static FrameworkElement LinkRow(string glyph, string title, string sub, Action act)
    {
        var g = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Th.AccentSoft, Child = Ui.Glyph(glyph, 13), HorizontalAlignment = HorizontalAlignment.Left });
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Ui.Txt(title, 13.5, FontWeights.Medium));
        if (sub != null) t.Children.Add(Ui.Txt(sub, 12, null, Th.Fg3));
        Grid.SetColumn(t, 1); g.Children.Add(t);
        var ch = Ui.Glyph("", 10, Th.Fg3); Grid.SetColumn(ch, 2); g.Children.Add(ch);
        var b = new Border { Child = g, Background = Brushes.Transparent, CornerRadius = new CornerRadius(12) };
        Ui.Hoverable(b);
        Ui.OnClick(b, act, false);
        return b;
    }

    static ToggleButton Toggle(string key, Action after = null)
    {
        var t = new ToggleButton { Style = Styles.Get("Switch"), IsChecked = Settings.Bool(key) };
        t.Checked += (_, _) => { Settings.SetBool(key, true); after?.Invoke(); };
        t.Unchecked += (_, _) => { Settings.SetBool(key, false); after?.Invoke(); };
        return t;
    }

    static FrameworkElement SliderFor(string key, double min, double max, double fallback, Func<double, string> fmt)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var val = Ui.Tab(Ui.Txt("", 12.5, null, Th.Fg2)); val.Width = 48; val.TextAlignment = TextAlignment.Right; val.Margin = new Thickness(0, 0, 12, 0);
        var s = new Slider { Style = Styles.Get("Glider"), Minimum = min, Maximum = max, Width = 200, Value = Settings.Num(key, fallback) };
        val.Text = fmt(s.Value);
        s.ValueChanged += (_, _) => { val.Text = fmt(s.Value); Settings.SetNum(key, Math.Round(s.Value, 2)); };
        sp.Children.Add(val); sp.Children.Add(s);
        return sp;
    }

    // ---------------- pages
    void Appearance(StackPanel sp)
    {
        sp.Children.Add(Section("Accent colour"));
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var kv in Th.Accents)
        {
            bool on = Settings.Get("accent") == kv.Key;
            var g = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) };
            g.Children.Add(new Ellipse { Fill = new SolidColorBrush(kv.Value), Stroke = on ? Brushes.White : Th.Hairline, StrokeThickness = on ? 2.5 : 1 });
            if (on) g.Children.Add(Ui.Glyph("", 12, Brushes.White));
            var name = kv.Key;
            var b = new Border { Child = g, ToolTip = char.ToUpper(name[0]) + name.Substring(1) };
            Ui.OnClick(b, () => { Settings.Set("accent", name); Go("Appearance"); });
            chips.Children.Add(b);
        }
        sp.Children.Add(Group(Row("Accent", "Used for highlights, toggles, selections and the cursor caret.", chips)));
        sp.Children.Add(Section("Glass"));
        sp.Children.Add(Group(
            Row("Frosted glass", "Windows, menus and the dock blur what's behind them. Turn off on slower PCs.", Toggle("glassBlur")),
            Row("Glass tint", "Higher is darker and easier to read; lower is more see-through.", SliderFor("glassTint", 0.3, 0.85, 0.55, v => Math.Round(v * 100) + "%"))));
        sp.Children.Add(Section("Speed"));
        sp.Children.Add(Group(Row("Use the fast graphics chip", "Runs Bisman OS on your dedicated GPU (like NVIDIA) instead of the built-in one. Much smoother, uses a bit more battery. Takes effect the next time Bisman OS starts.", Toggle("fastGpu"))));
        sp.Children.Add(Section("Motion"));
        sp.Children.Add(Group(Row("Reduce motion", "Turns off window animations, dock magnification and the moving wallpaper.", Toggle("reduceMotion"))));
    }

    void WallpaperPage(StackPanel sp)
    {
        sp.Children.Add(Section("Choose a wallpaper"));
        var wrap = new WrapPanel();
        var all = Wallpaper.BuiltIn().Concat(Settings.LoadList("custom-wallpapers").Where(File.Exists)).Distinct().ToList();
        string currentWp = Settings.Get("wallpaper");
        if (string.IsNullOrEmpty(currentWp) || !File.Exists(currentWp)) currentWp = all.FirstOrDefault();
        foreach (var f in all)
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(f); bi.DecodePixelWidth = 360; bi.EndInit();
            bool on = string.Equals(f, currentWp, StringComparison.OrdinalIgnoreCase);
            var img = new Border { Width = 210, Height = 118, CornerRadius = new CornerRadius(12), Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }, BorderBrush = on ? Th.Accent : Th.Hairline, BorderThickness = new Thickness(on ? 3 : 1) };
            var cap = Ui.Txt(Path.GetFileNameWithoutExtension(f).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-').Replace('-', ' '), 12, null, on ? Th.Fg : Th.Fg2);
            cap.Margin = new Thickness(2, 6, 0, 0);
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 14) };
            cell.Children.Add(img); cell.Children.Add(cap);
            var path = f;
            Ui.OnClick(cell, () => { Settings.Set("wallpaper", path); Go("Wallpaper"); });
            wrap.Children.Add(cell);
        }
        sp.Children.Add(wrap);
        var add = Ui.Pill("Add a photo…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp", Title = "Choose a wallpaper" };
            if (dlg.ShowDialog() == true)
            {
                var list = Settings.LoadList("custom-wallpapers"); if (!list.Contains(dlg.FileName)) list.Add(dlg.FileName);
                Settings.SaveList("custom-wallpapers", list);
                Settings.Set("wallpaper", dlg.FileName);
                Go("Wallpaper");
            }
        }, "");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(add);
        sp.Children.Add(Section("Video wallpaper"));
        var vwrap = new WrapPanel();
        foreach (var f in Settings.LoadList("custom-videos").Where(File.Exists))
        {
            bool on = string.Equals(f, Settings.Get("wallpaper"), StringComparison.OrdinalIgnoreCase);
            var img = new Image { Stretch = Stretch.UniformToFill };
            ShellImages.Get(f, 256, false, s => img.Source = s);
            var frame = new Border { Width = 210, Height = 118, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Th.Inset, BorderBrush = on ? Th.Accent : Th.Hairline, BorderThickness = new Thickness(on ? 3 : 1), Child = img };
            var cap = Ui.Txt("▶  " + Path.GetFileNameWithoutExtension(f), 12, null, on ? Th.Fg : Th.Fg2); cap.Margin = new Thickness(2, 6, 0, 0); cap.MaxWidth = 210;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 14) };
            cell.Children.Add(frame); cell.Children.Add(cap);
            var path = f;
            Ui.OnClick(cell, () => { Settings.Set("wallpaper", path); Go("Wallpaper"); });
            cell.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                Ui.ShowMenu(cell, Ui.Menu(("", "Remove from list", () =>
                {
                    var list = Settings.LoadList("custom-videos"); list.Remove(path); Settings.SaveList("custom-videos", list);
                    if (string.Equals(Settings.Get("wallpaper"), path, StringComparison.OrdinalIgnoreCase)) Settings.Set("wallpaper", "");
                    Go("Wallpaper");
                })));
            };
            vwrap.Children.Add(cell);
        }
        sp.Children.Add(vwrap);
        var addVid = Ui.Pill("Choose a video…", () => { Wallpaper.ChooseVideo(); Go("Wallpaper"); }, "", true);
        addVid.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(addVid);
        var vnote = Ui.Txt("Plays muted and on loop, and pauses by itself when an app fills the screen. .mp4 works best.", 12, null, Th.Fg3);
        vnote.Margin = new Thickness(4, 10, 0, 0); vnote.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(vnote);
        sp.Children.Add(Section("Live wallpaper"));
        sp.Children.Add(Group(
            Row("Live wallpaper", "A slow camera drift and a soft light sweep. Pauses by itself when an app fills the screen.", Toggle("liveWallpaper")),
            Row("Floating particles", "Tiny drifting lights in front of the wallpaper.", Toggle("particles"))));
        var note = Ui.Txt("The robotics wallpapers and illustrations were generated with AI for Bisman OS.", 12, null, Th.Fg3);
        note.Margin = new Thickness(4, 14, 0, 0); note.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(note);
    }

    void DockPage(StackPanel sp)
    {
        sp.Children.Add(Section("Dock"));
        sp.Children.Add(Group(
            Row("Magnification", "Icons grow as your pointer moves across the dock.", Toggle("dockMagnify")),
            Row("Hide the Windows taskbar", "Keeps the Bisman dock on screen when you open other apps. The taskbar comes back when you exit Bisman OS.", Toggle("hideTaskbar")),
            Row("Icon size", null, SliderFor("dockSize", 36, 72, 50, v => Math.Round(v) + " px"))));
        sp.Children.Add(Section("Desktop"));
        sp.Children.Add(Group(
            Row("Show desktop icons", "Shows the files and shortcuts in your Windows Desktop folder.", Toggle("showDesktopIcons")),
            Row("Show widgets", "Clock and live system widgets on the right.", Toggle("showWidgets"))));
        sp.Children.Add(Section("Menu bar clock"));
        sp.Children.Add(Group(Row("24-hour time", null, Toggle("clock24")), Row("Show seconds", null, Toggle("showSeconds"))));
    }

    void NotificationsPage(StackPanel sp)
    {
        sp.Children.Add(Section("Focus"));
        sp.Children.Add(Group(Row("Do not disturb", "Hides pop-up banners. Notifications still collect in the notification centre.", Toggle("dnd"))));
        sp.Children.Add(Section("Alerts from Bisman OS"));
        sp.Children.Add(Group(
            Row("Battery", "Charger connected or disconnected, and low battery.", Toggle("notifyBattery")),
            Row("Network", "When your PC goes online or offline.", Toggle("notifyNetwork")),
            Row("Terminal", "When a long command finishes while you're in another window.", Toggle("notifyTerminal"))));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Ui.Pill("Send a test notification", () => Notify.Post("Bisman OS", "", "Test notification", "Notifications are working."), ""));
        var clr = Ui.Pill("Clear all notifications", Notify.Clear, ""); clr.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(clr);
        sp.Children.Add(buttons);
        var n = Ui.Txt("Notifications from other Windows apps still appear in Windows' own notification centre.", 12, null, Th.Fg3);
        n.Margin = new Thickness(4, 14, 0, 0); n.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(n);
    }

    void StartupPage(StackPanel sp)
    {
        sp.Children.Add(Section("When you sign in"));
        var t = new ToggleButton { Style = Styles.Get("Switch"), IsChecked = Sys.StartsWithWindows };
        t.Checked += (_, _) => Sys.StartsWithWindows = true;
        t.Unchecked += (_, _) => Sys.StartsWithWindows = false;
        sp.Children.Add(Group(Row("Open Bisman OS automatically", "Adds Bisman OS to your user's startup apps. Windows itself is not changed.", t)));
        sp.Children.Add(Section("Exiting"));
        sp.Children.Add(Group(LinkRow("", "Exit to Windows", "Ctrl+Shift+Q · Your apps keep running", Desktop.I.ConfirmExit)));
    }

    void WindowsPage(StackPanel sp)
    {
        sp.Children.Add(Section("Opens the matching page in Windows Settings"));
        var rows = Search.WindowsSettings.Select(s => { var uri = s.uri; return LinkRow(s.glyph, s.title, null, () => Shell.Run(uri)); }).ToArray();
        sp.Children.Add(Group(rows));
    }

    void ShortcutsPage(StackPanel sp)
    {
        (string, string)[] list =
        {
            ("Alt+Windows", "Add the app you're using to the Dock (on the desktop: pick from a list)"),
            ("Ctrl+Space", "Start menu"), ("Alt+Space", "Search"), ("Ctrl+Alt+Space", "Search from any app"), ("Ctrl+Alt+B", "Bring Bisman OS to the front"),
            ("Ctrl+Alt+T", "New terminal"), ("Ctrl+Alt+E", "New Files window"), ("Ctrl+Alt+M", "Task Manager"), ("Ctrl+,", "System Settings"),
            ("Ctrl+Alt+C", "Control Centre"), ("Ctrl+Alt+N", "Notifications"), ("Ctrl+W", "Close window (or terminal tab)"), ("Ctrl+M", "Minimise window"),
            ("Ctrl+Alt+Up / Down", "Maximise / restore window"), ("Ctrl+`", "Switch between windows"), ("F5", "Refresh desktop"), ("Ctrl+Shift+Q", "Exit to Windows"),
        };
        sp.Children.Add(Section("Bisman OS"));
        sp.Children.Add(Group(list.Select(x =>
        {
            var k = new Border { CornerRadius = new CornerRadius(6), Background = Th.Card, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2, 8, 3), Child = Ui.Txt(x.Item1, 12, FontWeights.SemiBold, null, Th.Mono) };
            return Row(x.Item2, null, k);
        }).ToArray()));
    }

    void AboutPage(StackPanel sp)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        head.Children.Add(Ui.Logo(64));
        var ht = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ht.Children.Add(Ui.Txt("Bisman OS", 24, FontWeights.SemiBold, null, Th.Display));
        ht.Children.Add(Ui.Txt("Version 2.0", 13, null, Th.Fg2));
        head.Children.Add(ht);
        sp.Children.Add(head);
        var m = Sys.Memory();
        double scale = PresentationSource.FromVisual(Desktop.I)?.CompositionTarget.TransformToDevice.M11 ?? 1;
        sp.Children.Add(Section("This PC"));
        sp.Children.Add(Group(
            Row("Device name", null, Ui.Txt(Environment.MachineName, 13, null, Th.Fg2)),
            Row("Windows", null, Ui.Txt(Sys.OsName(), 13, null, Th.Fg2)),
            Row("Processor", null, Ui.Txt(Sys.CpuName() + " · " + Environment.ProcessorCount + " threads", 13, null, Th.Fg2)),
            Row("Memory", null, Ui.Txt(m.totalGb.ToString("0") + " GB", 13, null, Th.Fg2)),
            Row("Graphics", null, Ui.Txt(string.Join(", ", Sys.Gpus()), 13, null, Th.Fg2)),
            Row("Display", null, Ui.Txt(Math.Round(SystemParameters.PrimaryScreenWidth * scale) + " × " + Math.Round(SystemParameters.PrimaryScreenHeight * scale) + " at " + Math.Round(scale * 100) + "% scale", 13, null, Th.Fg2)),
            Row("Up time", null, Ui.Txt(Sys.UptimeText(), 13, null, Th.Fg2))));
        sp.Children.Add(Section("Bisman OS"));
        sp.Children.Add(Group(
            LinkRow("", "Open Bisman OS data folder", Settings.Dir, () => Shell.Run("explorer.exe", "\"" + Settings.Dir + "\"")),
            LinkRow("", "Open install folder", AppDomain.CurrentDomain.BaseDirectory, () => Shell.Run("explorer.exe", "\"" + AppDomain.CurrentDomain.BaseDirectory + "\""))));
        var n = Ui.Txt("Bisman OS is a desktop environment that runs on top of Windows. It doesn't change Windows system files, so you can always exit back to the normal Windows desktop.", 12, null, Th.Fg3);
        n.TextWrapping = TextWrapping.Wrap; n.TextTrimming = TextTrimming.None; n.Margin = new Thickness(4, 14, 0, 0);
        sp.Children.Add(n);
    }
}

// ================================================================ Calculator
public class CalcView : Grid, IKeyHandler
{
    decimal acc; string op; string entry = "0"; bool fresh = true, error;
    readonly TextBlock display, expr;
    readonly Dictionary<string, Border> opButtons = new();
    Border acButton;

    public CalcView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });
        RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 24, 8) };
        expr = Ui.Tab(Ui.Txt("", 15, null, Th.Fg3)); expr.HorizontalAlignment = HorizontalAlignment.Right;
        display = Ui.Tab(Ui.Txt("0", 58, FontWeights.Light, null, Th.Display)); display.TextTrimming = TextTrimming.None;
        top.Children.Add(expr);
        top.Children.Add(new Viewbox { Child = display, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Right, Height = 72 });
        Children.Add(top);

        var pad = new Grid { Margin = new Thickness(16, 4, 16, 18) };
        for (int i = 0; i < 4; i++) pad.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 5; i++) pad.RowDefinitions.Add(new RowDefinition());
        string[,] keys = { { "AC", "±", "%", "÷" }, { "7", "8", "9", "×" }, { "4", "5", "6", "−" }, { "1", "2", "3", "+" }, { "0", "", ".", "=" } };
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 4; c++)
            {
                var k = keys[r, c];
                if (k == "") continue;
                var b = MakeKey(k);
                Grid.SetRow(b, r); Grid.SetColumn(b, c);
                if (k == "0") Grid.SetColumnSpan(b, 2);
                pad.Children.Add(b);
            }
        SetRow(pad, 1); Children.Add(pad);
    }

    Border MakeKey(string k)
    {
        bool isOp = "÷×−+=".Contains(k), isFn = k == "AC" || k == "±" || k == "%";
        Brush bg = isOp ? (Brush)new LinearGradientBrush(Th.C("#FFAA4D"), Th.C("#F07A1A"), 90) : isFn ? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        var t = Ui.Txt(k, isOp ? 26 : 22, isFn ? FontWeights.SemiBold : FontWeights.Normal, null, Th.Display);
        t.HorizontalAlignment = k == "0" ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        if (k == "0") t.Margin = new Thickness(26, 0, 0, 0);
        var b = new Border { CornerRadius = new CornerRadius(20), Background = bg, Margin = new Thickness(5), Child = t, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1) };
        b.MouseEnter += (_, _) => b.Opacity = 0.85; b.MouseLeave += (_, _) => b.Opacity = 1;
        Ui.OnClick(b, () => Press(k));
        if ("÷×−+".Contains(k)) opButtons[k] = b;
        if (k == "AC") acButton = b;
        return b;
    }

    static decimal Parse(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static string Fmt(decimal v)
    {
        v = Math.Round(v, 10);
        var s = v.ToString("#,0.##########", System.Globalization.CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    string Pretty(string raw)
    {
        if (raw.Contains("e")) return raw;
        bool neg = raw.StartsWith("-"); var r = neg ? raw.Substring(1) : raw;
        int dot = r.IndexOf('.');
        string ip = dot >= 0 ? r.Substring(0, dot) : r, fp = dot >= 0 ? r.Substring(dot) : "";
        if (decimal.TryParse(ip, out var iv)) ip = iv.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        return (neg ? "-" : "") + ip + fp;
    }

    decimal Apply(decimal a, string o, decimal b)
    {
        switch (o)
        {
            case "+": return a + b;
            case "−": return a - b;
            case "×": return a * b;
            case "÷": if (b == 0) throw new DivideByZeroException(); return a / b;
        }
        return b;
    }

    void Press(string k)
    {
        try
        {
            if (error && k != "AC") { error = false; entry = "0"; fresh = true; }
            if (char.IsDigit(k[0]))
            {
                if (fresh || entry == "0") entry = k; else if (entry.Replace("-", "").Replace(".", "").Length < 15) entry += k;
                fresh = false;
            }
            else if (k == ".") { if (fresh) { entry = "0."; fresh = false; } else if (!entry.Contains(".")) entry += "."; }
            else if (k == "AC")
            {
                if (entry != "0" && !fresh) { entry = "0"; fresh = true; }
                else { acc = 0; op = null; entry = "0"; fresh = true; error = false; expr.Text = ""; }
            }
            else if (k == "±") { if (entry != "0") entry = entry.StartsWith("-") ? entry.Substring(1) : "-" + entry; }
            else if (k == "%") { var v = Parse(entry); entry = Fmt(op == "+" || op == "−" ? acc * v / 100 : v / 100).Replace(",", ""); fresh = true; }
            else if (k == "=")
            {
                if (op != null) { var b = Parse(entry); expr.Text = Fmt(acc) + " " + op + " " + Fmt(b) + " ="; acc = Apply(acc, op, b); entry = Fmt(acc).Replace(",", ""); op = null; fresh = true; }
            }
            else
            {
                if (op != null && !fresh) { acc = Apply(acc, op, Parse(entry)); entry = Fmt(acc).Replace(",", ""); }
                else if (op == null) acc = Parse(entry);
                op = k; fresh = true;
                expr.Text = Fmt(acc) + " " + op;
            }
        }
        catch (DivideByZeroException) { error = true; entry = "Error"; op = null; fresh = true; expr.Text = "Can't divide by zero"; }
        catch (OverflowException) { error = true; entry = "Error"; op = null; fresh = true; expr.Text = "Number too large"; }
        display.Text = error ? "Error" : Pretty(entry);
        ((TextBlock)acButton.Child).Text = entry != "0" && !fresh ? "C" : "AC";
        foreach (var kv in opButtons) kv.Value.BorderBrush = kv.Key == op && fresh ? Brushes.White : Th.Hairline;
        foreach (var kv in opButtons) kv.Value.BorderThickness = new Thickness(kv.Key == op && fresh ? 2 : 1);
    }

    public bool HandleKey(Key k, ModifierKeys m)
    {
        string s = null;
        bool shift = m == ModifierKeys.Shift;
        if (k >= Key.D0 && k <= Key.D9 && !shift) s = ((int)(k - Key.D0)).ToString();
        else if (k >= Key.NumPad0 && k <= Key.NumPad9) s = ((int)(k - Key.NumPad0)).ToString();
        else if (k == Key.Add || (k == Key.OemPlus && shift)) s = "+";
        else if (k == Key.Subtract || k == Key.OemMinus) s = "−";
        else if (k == Key.Multiply || (k == Key.D8 && shift)) s = "×";
        else if (k == Key.Divide || k == Key.Oem2) s = "÷";
        else if (k == Key.Enter || (k == Key.OemPlus && !shift)) s = "=";
        else if (k == Key.Decimal || k == Key.OemPeriod) s = ".";
        else if (k == Key.D5 && shift) s = "%";
        else if (k == Key.Escape || k == Key.Delete) s = "AC";
        else if (k == Key.Back)
        {
            if (!fresh && entry.Length > 1) entry = entry.Substring(0, entry.Length - 1); else { entry = "0"; fresh = true; }
            if (entry == "-") entry = "0";
            display.Text = Pretty(entry);
            return true;
        }
        if (s == null) return false;
        Press(s);
        return true;
    }
}

// ================================================================ Task Manager (real processes and performance)
public class TaskView : Grid
{
    readonly Grid perf, procs;
    readonly Border tabPerf, tabProcs;
    readonly List<double> cpuHist = new(), memHist = new();
    readonly (TextBlock big, TextBlock sub, Canvas graph) cpuCard, memCard;
    readonly TextBlock diskInfo, battInfo;
    readonly StackPanel rows = new();
    readonly TextBox filter;
    readonly TextBlock procCount;
    readonly Border endBtn;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<int, TimeSpan> lastCpu = new();
    DateTime lastSample = DateTime.Now;
    int selPid = -1; string sortBy = "mem"; int ticks;
    readonly Dictionary<int, Border> rowByPid = new();
    List<(int pid, string name, double cpu, long mem)> snapshot = new();

    public TaskView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        RowDefinitions.Add(new RowDefinition());
        var seg = new Border { CornerRadius = new CornerRadius(10), Background = Th.Inset, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1) };
        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        tabPerf = SegButton("Performance", () => ShowTab(true));
        tabProcs = SegButton("Processes", () => ShowTab(false));
        segs.Children.Add(tabPerf); segs.Children.Add(tabProcs);
        seg.Child = segs;
        Children.Add(seg);

        // performance
        perf = new Grid { Margin = new Thickness(20, 0, 20, 20) };
        perf.ColumnDefinitions.Add(new ColumnDefinition()); perf.ColumnDefinitions.Add(new ColumnDefinition());
        perf.RowDefinitions.Add(new RowDefinition()); perf.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cpuCard = GraphCard(perf, 0, 0, "CPU", Sys.CpuName() + " · " + Environment.ProcessorCount + " threads");
        memCard = GraphCard(perf, 0, 1, "Memory", "");
        diskInfo = InfoCard(perf, 1, 0, "Disks");
        battInfo = InfoCard(perf, 1, 1, "Power & system");
        SetRow(perf, 1); Children.Add(perf);

        // processes
        procs = new Grid { Margin = new Thickness(20, 0, 20, 16), Visibility = Visibility.Collapsed };
        procs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        procs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        procs.RowDefinitions.Add(new RowDefinition());
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var fbox = new Border { Width = 240, Height = 32, CornerRadius = new CornerRadius(9), Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0) };
        var fd = new DockPanel(); var fg = Ui.Glyph("", 12, Th.Fg3); DockPanel.SetDock(fg, WDock.Left); fd.Children.Add(fg);
        var fgr = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        filter = new TextBox { Style = Styles.Get("Field"), FontSize = 13 };
        var fh = Ui.Txt("Filter processes", 12.5, null, Th.Fg3); fh.IsHitTestVisible = false;
        filter.TextChanged += (_, _) => { fh.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderRows(); };
        fgr.Children.Add(filter); fgr.Children.Add(fh); fd.Children.Add(fgr); fbox.Child = fd;
        DockPanel.SetDock(fbox, WDock.Left); bar.Children.Add(fbox);
        endBtn = Ui.Pill("End task", EndTask, "", false, true, 12.5);
        endBtn.Opacity = 0.4; endBtn.IsEnabled = false;
        DockPanel.SetDock(endBtn, WDock.Right); bar.Children.Add(endBtn);
        procCount = Ui.Txt("", 12.5, null, Th.Fg3); procCount.Margin = new Thickness(14, 0, 0, 0);
        bar.Children.Add(procCount);
        procs.Children.Add(bar);
        var head = ProcGrid();
        HeadCell(head, 0, "Name", "name"); HeadCell(head, 1, "PID", "pid"); HeadCell(head, 2, "CPU", "cpu"); HeadCell(head, 3, "Memory", "mem");
        var hb = new Border { Child = head, Padding = new Thickness(12, 6, 12, 6), BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        SetRow(hb, 1); procs.Children.Add(hb);
        var sc = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SetRow(sc, 2); procs.Children.Add(sc);
        SetRow(procs, 1); Children.Add(procs);

        ShowTab(true);
        timer.Tick += (_, _) => Sample();
        Loaded += (_, _) => { timer.Start(); Sample(); };
        Unloaded += (_, _) => timer.Stop();
    }

    Border SegButton(string t, Action a)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 5, 18, 6), Child = Ui.Txt(t, 12.5, FontWeights.SemiBold) };
        Ui.OnClick(b, a, false);
        return b;
    }

    void ShowTab(bool performance)
    {
        perf.Visibility = performance ? Visibility.Visible : Visibility.Collapsed;
        procs.Visibility = performance ? Visibility.Collapsed : Visibility.Visible;
        tabPerf.Background = performance ? Th.Accent : Brushes.Transparent;
        tabProcs.Background = performance ? Brushes.Transparent : Th.Accent;
        if (!performance) RefreshProcs();
    }

    (TextBlock, TextBlock, Canvas) GraphCard(Grid host, int row, int col, string title, string sub)
    {
        var sp = new Grid();
        sp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sp.RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel();
        top.Children.Add(Ui.Txt(title, 13, FontWeights.SemiBold, Th.Fg2));
        var big = Ui.Tab(Ui.Txt("", 34, FontWeights.SemiBold, null, Th.Display));
        top.Children.Add(big);
        var s = Ui.Txt(sub, 12, null, Th.Fg3); top.Children.Add(s);
        sp.Children.Add(top);
        var c = new Canvas { ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0), MinHeight = 120 };
        SetRow(c, 1); sp.Children.Add(c);
        var card = new Border { CornerRadius = new CornerRadius(16), Background = Th.Card, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(18), Margin = new Thickness(col == 0 ? 0 : 8, 0, col == 0 ? 8 : 0, 16), Child = sp };
        SetRow(card, row); SetColumn(card, col); host.Children.Add(card);
        return (big, s, c);
    }

    TextBlock InfoCard(Grid host, int row, int col, string title)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Txt(title, 13, FontWeights.SemiBold, Th.Fg2));
        var t = Ui.Tab(Ui.Txt("", 12.5, null, Th.Fg)); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 8, 0, 0); t.LineHeight = 20;
        sp.Children.Add(t);
        var card = new Border { CornerRadius = new CornerRadius(16), Background = Th.Card, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(18), Margin = new Thickness(col == 0 ? 0 : 8, 0, col == 0 ? 8 : 0, 0), Child = sp };
        SetRow(card, row); SetColumn(card, col); host.Children.Add(card);
        return t;
    }

    static void Draw(Canvas c, List<double> hist, Color color)
    {
        c.Children.Clear();
        double w = c.ActualWidth, h = c.ActualHeight;
        if (w <= 0 || h <= 0 || hist.Count < 2) return;
        for (int i = 1; i < 4; i++) c.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * i / 4, Y2 = h * i / 4, Stroke = Th.Hairline, StrokeThickness = 1 });
        var pts = new PointCollection();
        double step = w / 59.0;
        int off = 60 - hist.Count;
        for (int i = 0; i < hist.Count; i++) pts.Add(new Point((off + i) * step, h - hist[i] / 100 * (h - 2) - 1));
        var area = new PointCollection(pts) { new Point(pts[pts.Count - 1].X, h), new Point(pts[0].X, h) };
        c.Children.Add(new Polygon { Points = area, Fill = new LinearGradientBrush(Color.FromArgb(110, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90) });
        c.Children.Add(new Polyline { Points = pts, Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
    }

    void Sample()
    {
        double cpu = Sys.Cpu();
        var m = Sys.Memory();
        cpuHist.Add(cpu); if (cpuHist.Count > 60) cpuHist.RemoveAt(0);
        memHist.Add(m.pct); if (memHist.Count > 60) memHist.RemoveAt(0);
        cpuCard.big.Text = Math.Round(cpu) + "%";
        memCard.big.Text = m.usedGb.ToString("0.0") + " GB";
        memCard.sub.Text = m.pct + "% of " + m.totalGb.ToString("0") + " GB in use";
        Draw(cpuCard.graph, cpuHist, Th.Accent.Color);
        Draw(memCard.graph, memHist, Th.Violet);
        var sb = new StringBuilder();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.DriveType == DriveType.Fixed) sb.AppendLine(d.Name.TrimEnd('\\') + "   " + Ui.Bytes(d.TotalSize - d.TotalFreeSpace) + " used of " + Ui.Bytes(d.TotalSize) + "   (" + Ui.Bytes(d.AvailableFreeSpace) + " free)"); } catch { }
        }
        diskInfo.Text = sb.ToString().TrimEnd();
        var p = Sys.Battery();
        battInfo.Text = (p.HasBattery ? "Battery " + p.Percent + "%" + (p.Charging ? " · charging" : p.Plugged ? " · plugged in" : " · on battery") : "No battery") +
                        "\nUp time " + Sys.UptimeText() + "\nGraphics " + string.Join(", ", Sys.Gpus());
        if (procs.Visibility == Visibility.Visible && ++ticks % 2 == 0) RefreshProcs();
    }

    void RefreshProcs()
    {
        Task.Run(() =>
        {
            var now = DateTime.Now;
            double wall = Math.Max(0.2, (now - lastSample).TotalSeconds);
            var list = new List<(int, string, double, long)>();
            Dictionary<int, TimeSpan> next = new();
            foreach (var pr in Process.GetProcesses())
            {
                try
                {
                    double cpuPct = -1;
                    try
                    {
                        var t = pr.TotalProcessorTime; next[pr.Id] = t;
                        lock (lastCpu) if (lastCpu.TryGetValue(pr.Id, out var prev)) cpuPct = Th.Clamp((t - prev).TotalSeconds / wall / Environment.ProcessorCount * 100, 0, 100);
                    }
                    catch { }
                    list.Add((pr.Id, pr.ProcessName, cpuPct, pr.WorkingSet64));
                }
                catch { }
                finally { pr.Dispose(); }
            }
            lock (lastCpu) { lastCpu.Clear(); foreach (var kv in next) lastCpu[kv.Key] = kv.Value; }
            lastSample = now;
            Dispatcher.BeginInvoke(new Action(() => { snapshot = list; RenderRows(); }));
        });
    }

    static Grid ProcGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        return g;
    }

    void HeadCell(Grid g, int col, string text, string key)
    {
        var b = new Border { Child = Ui.Txt(text, 12, FontWeights.SemiBold, Th.Fg3), Background = Brushes.Transparent, HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
        Ui.OnClick(b, () => { sortBy = key; RenderRows(); }, false);
        Grid.SetColumn(b, col); g.Children.Add(b);
    }

    void RenderRows()
    {
        var q = filter.Text.Trim();
        IEnumerable<(int pid, string name, double cpu, long mem)> v = snapshot.Where(p => q.Length == 0 || p.name.Has(q) || p.pid.ToString() == q);
        v = sortBy switch
        {
            "name" => v.OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase),
            "pid" => v.OrderBy(p => p.pid),
            "cpu" => v.OrderByDescending(p => p.cpu),
            _ => v.OrderByDescending(p => p.mem)
        };
        var listed = v.Take(400).ToList();
        rows.Children.Clear(); rowByPid.Clear();
        foreach (var p in listed)
        {
            var g = ProcGrid();
            g.Children.Add(Ui.Txt(p.name, 13));
            var pid = Ui.Tab(Ui.Txt(p.pid.ToString(), 12.5, null, Th.Fg2)); pid.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(pid, 1); g.Children.Add(pid);
            var cpu = Ui.Tab(Ui.Txt(p.cpu < 0 ? "—" : p.cpu.ToString("0.0") + "%", 12.5, null, p.cpu > 20 ? Th.Fg : Th.Fg2)); cpu.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(cpu, 2); g.Children.Add(cpu);
            var mem = Ui.Tab(Ui.Txt(Ui.Bytes(p.mem), 12.5, null, Th.Fg2)); mem.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(mem, 3); g.Children.Add(mem);
            var b = new Border { Child = g, Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 1, 6, 1), Background = p.pid == selPid ? Th.AccentSoft : Brushes.Transparent };
            int id = p.pid;
            b.MouseEnter += (_, _) => { if (selPid != id) b.Background = Th.Hover; };
            b.MouseLeave += (_, _) => { if (selPid != id) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += (_, _) => SelectPid(id);
            b.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true; SelectPid(id);
                var path = Native.ProcessPath((uint)id);
                var items = new List<(string, string, Action)> { ("", "End task", EndTask) };
                if (path != null) items.Add(("", "Open file location", () => Shell.Reveal(path)));
                Ui.ShowMenu(b, Ui.Menu(items.ToArray()));
            };
            rowByPid[id] = b;
            rows.Children.Add(b);
        }
        procCount.Text = snapshot.Count + " processes";
    }

    void SelectPid(int pid)
    {
        if (rowByPid.TryGetValue(selPid, out var old)) old.Background = Brushes.Transparent;
        selPid = pid;
        if (rowByPid.TryGetValue(pid, out var b)) b.Background = Th.AccentSoft;
        endBtn.IsEnabled = true; endBtn.Opacity = 1;
    }

    void EndTask()
    {
        if (selPid < 0) return;
        var item = snapshot.FirstOrDefault(p => p.pid == selPid);
        if (item.name == null) return;
        int pid = selPid;
        Dialog.Confirm("End “" + item.name + "”?", "Unsaved work in this app will be lost.", "End task", true, () =>
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(); Notify.Post("Task Manager", "", "Ended " + item.name, "Process " + pid); }
            catch (Exception ex) { Notify.Post("Task Manager", "", "Couldn't end " + item.name, ex.Message); }
            selPid = -1; endBtn.IsEnabled = false; endBtn.Opacity = 0.4;
            RefreshProcs();
        }, "");
    }
}

// ================================================================ About
public static class AboutApp
{
    public static void Open()
    {
        var existing = Desktop.I.Wm.For("bisman:about").FirstOrDefault();
        if (existing != null) { Desktop.I.Wm.Restore(existing); return; }
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30, 10, 30, 30) };
        var logo = Ui.Logo(84); logo.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(logo);
        var t = Ui.Txt("Bisman OS", 28, FontWeights.SemiBold, null, Th.Display); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 16, 0, 2);
        sp.Children.Add(t);
        var v = Ui.Txt("Version 2.0", 13, null, Th.Fg2); v.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(v);
        var m = Sys.Memory();
        var info = new StackPanel { Margin = new Thickness(0, 22, 0, 18) };
        foreach (var (k, val) in new[] { ("Windows", Sys.OsName()), ("Processor", Sys.CpuName()), ("Memory", m.totalGb.ToString("0") + " GB"), ("Graphics", Sys.Gpus().FirstOrDefault() ?? "") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var kt = Ui.Txt(k, 12.5, FontWeights.SemiBold, Th.Fg2); kt.Width = 90; kt.TextAlignment = TextAlignment.Right; kt.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(kt, WDock.Left); row.Children.Add(kt);
            row.Children.Add(Ui.Txt(val, 12.5, null, Th.Fg));
            info.Children.Add(row);
        }
        sp.Children.Add(info);
        var more = Ui.Pill("More info…", () => Desktop.I.OpenSettings("About"));
        more.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(more);
        var w = new ShellWin("bisman:about", "", 440, 470, sp) { MinW = 380, MinH = 400 };
        Desktop.I.Wm.Open(w);
    }
}
