// Bisman OS 2 - Start menu, search, Control Centre, notifications, onboarding.
using System.Windows.Controls.Primitives;

namespace BismanOS;

public abstract class Flyout : Grid
{
    public bool IsOpen { get; private set; }
    public FrameworkElement Panel;
    protected readonly ScaleTransform St = new(1, 1);
    protected readonly TranslateTransform Tt = new();
    protected double FromX, FromY = 18, FromScale = 0.96;

    protected Flyout() { Visibility = Visibility.Collapsed; }

    protected void SetPanel(FrameworkElement p, Point origin)
    {
        // Wrap the panel in a cached layer so its shadow is drawn once, not on every frame.
        var holder = new Grid { HorizontalAlignment = p.HorizontalAlignment, VerticalAlignment = p.VerticalAlignment, Margin = p.Margin, CacheMode = new BitmapCache { SnapsToDevicePixels = true } };
        p.Margin = new Thickness(0);
        p.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (p.VerticalAlignment == VerticalAlignment.Stretch) p.VerticalAlignment = VerticalAlignment.Stretch; else p.VerticalAlignment = VerticalAlignment.Top;
        holder.Children.Add(p);
        Panel = holder;
        var tg = new TransformGroup(); tg.Children.Add(St); tg.Children.Add(Tt);
        holder.RenderTransform = tg; holder.RenderTransformOrigin = origin;
        Children.Add(holder);
    }

    public virtual bool IsTrigger(DependencyObject src) => false;
    public void Toggle() { if (IsOpen) Hide(); else Show(); }

    public void Show()
    {
        if (IsOpen) return;
        foreach (var f in new Flyout[] { Desktop.I.Start, Desktop.I.Spot, Desktop.I.CC, Desktop.I.NC }) if (f != this && f != null) f.Hide();
        IsOpen = true; Visibility = Visibility.Visible; Panel.IsHitTestVisible = true;
        OnOpen();
        Anim.Run(Panel, OpacityProperty, 0, 1, 170);
        Anim.Run(St, ScaleTransform.ScaleXProperty, FromScale, 1, 280, Anim.Spring);
        Anim.Run(St, ScaleTransform.ScaleYProperty, FromScale, 1, 280, Anim.Spring);
        Anim.Run(Tt, TranslateTransform.YProperty, FromY, 0, 280, Anim.Spring);
        Anim.Run(Tt, TranslateTransform.XProperty, FromX, 0, 280, Anim.Spring);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false; Panel.IsHitTestVisible = false;
        OnClose();
        Anim.To(Panel, OpacityProperty, 0, 130, null, () => { if (!IsOpen) Visibility = Visibility.Collapsed; });
        Anim.To(Tt, TranslateTransform.YProperty, FromY * 0.6, 130);
        Anim.To(Tt, TranslateTransform.XProperty, FromX * 0.6, 130);
    }

    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }

    protected static TextBox SearchField(string hint, double size, out Grid host, out TextBlock placeholder)
    {
        host = new Grid();
        placeholder = Ui.Txt(hint, size, null, Th.Fg3, size >= 20 ? Th.Display : Th.Text);
        placeholder.IsHitTestVisible = false;
        var tb = new TextBox { Style = Styles.Get("Field"), FontSize = size, FontFamily = size >= 20 ? Th.Display : Th.Text };
        if (size >= 20) tb.FontWeight = FontWeights.Light;
        var ph = placeholder;
        tb.TextChanged += (_, _) => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        host.Children.Add(tb); host.Children.Add(placeholder);
        return tb;
    }
}

// ================================================================ search results (shared by Start and Spotlight)
public class ResultsList : ScrollViewer
{
    readonly StackPanel sp = new();
    readonly List<(Result r, Border row)> rows = new();
    int sel = -1;
    CancellationTokenSource cts;
    string lastQ = "";
    public event Action Ran;

    public ResultsList()
    {
        Content = sp;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    public bool HasResults => rows.Count > 0;

    public void Query(string q)
    {
        lastQ = q;
        cts?.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        var instant = Search.Instant(q);
        Render(instant, null);
        if (q.Trim().Length < 2) return;
        Task.Delay(170).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            var files = Search.Files(q, null, 10, token);
            Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (!token.IsCancellationRequested && q == lastQ) Render(instant, files); }));
        });
    }

    void Render(List<Result> instant, List<Result> files)
    {
        sp.Children.Clear(); rows.Clear();
        var all = instant.ToList();
        if (files != null) all.InsertRange(all.FindIndex(r => r.Kind == "Commands") is int i && i >= 0 ? i : all.Count, files);
        var top = all.FirstOrDefault(r => r.Kind == "Apps") ?? all.FirstOrDefault(r => r.Kind != "Commands");
        if (top != null) { Header("Best match"); AddRow(top, true); }
        foreach (var kind in new[] { "Apps", "Settings", "Folders", "Documents", "Commands" })
        {
            var group = all.Where(r => r.Kind == kind && r != top).ToList();
            if (group.Count == 0) continue;
            Header(kind);
            foreach (var r in group) AddRow(r, false);
        }
        sel = rows.Count > 0 ? 0 : -1;
        Highlight();
        ScrollToTop();
    }

    void Header(string text)
    {
        var t = Ui.Txt(text, 12, FontWeights.SemiBold, Th.Fg3);
        t.Margin = new Thickness(12, sp.Children.Count == 0 ? 2 : 14, 0, 6);
        sp.Children.Add(t);
    }

    void AddRow(Result r, bool big)
    {
        double s = big ? 44 : 30;
        FrameworkElement icon = r.App != null ? AppIcon.Make(r.App, s)
            : r.IconPath != null ? AppIcon.FromShell(r.IconPath, s, !r.Thumb, r.Kind == "Folders" ? "" : "")
            : AppIcon.Designed(r.Glyph ?? "", r.Kind == "Settings" ? Th.C("#7C8799") : Th.C("#2F3846"), r.Kind == "Settings" ? Th.C("#454E5E") : Th.C("#161B24"), s);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s + 14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Txt(r.Title, big ? 16 : 13.5, big ? FontWeights.SemiBold : FontWeights.Medium));
        if (!string.IsNullOrEmpty(r.Sub)) text.Children.Add(Ui.Txt(r.Sub, 11.5, null, Th.Fg3));
        Grid.SetColumn(text, 1); g.Children.Add(text);
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, big ? 10 : 6, 10, big ? 10 : 6), Child = g, Background = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
        if (r.Reveal != null)
        {
            var rv = Ui.IconButton("", () => { r.Reveal(); Ran?.Invoke(); }, "Open file location", 28, 12);
            rv.Opacity = 0; Grid.SetColumn(rv, 2); g.Children.Add(rv);
            b.MouseEnter += (_, _) => rv.Opacity = 1; b.MouseLeave += (_, _) => rv.Opacity = 0;
        }
        int idx = rows.Count;
        b.MouseEnter += (_, _) => { sel = idx; Highlight(); };
        Ui.OnClick(b, () => { Ran?.Invoke(); r.Run?.Invoke(); }, false);
        rows.Add((r, b));
        sp.Children.Add(b);
    }

    void Highlight()
    {
        for (int i = 0; i < rows.Count; i++) rows[i].row.Background = i == sel ? Th.AccentSoft : Brushes.Transparent;
        if (sel >= 0 && sel < rows.Count) rows[sel].row.BringIntoView();
    }

    public bool HandleKey(Key k)
    {
        if (rows.Count == 0) return false;
        if (k == Key.Down) { sel = Math.Min(rows.Count - 1, sel + 1); Highlight(); return true; }
        if (k == Key.Up) { sel = Math.Max(0, sel - 1); Highlight(); return true; }
        if (k == Key.Enter && sel >= 0) { var r = rows[sel].r; Ran?.Invoke(); r.Run?.Invoke(); return true; }
        return false;
    }
}

// ================================================================ Start menu (Windows 11 layout, Bisman look)
public class StartMenu : Flyout
{
    readonly Glass glass;
    readonly Grid views = new();
    readonly FrameworkElement home, all;
    readonly ResultsList results = new();
    readonly TextBox search;
    readonly WrapPanel pinned = new() { ItemWidth = 102, ItemHeight = 92 };
    readonly UniformGrid recommended = new() { Columns = 2 };
    readonly StackPanel allList = new();
    readonly ScrollViewer allScroll;
    readonly Grid jump;
    readonly Dictionary<string, FrameworkElement> letterHeaders = new();
    FrameworkElement current;

    public StartMenu()
    {
        FromY = 26;
        glass = new Glass(20, 0.6) { Width = 680, Height = 730, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 94) };
        glass.Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.55, RenderingBias = RenderingBias.Performance };
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var box = new Border { CornerRadius = new CornerRadius(20), Height = 42, Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Margin = new Thickness(30, 28, 30, 14), Padding = new Thickness(16, 0, 16, 0) };
        var bd = new DockPanel();
        var sg = Ui.Glyph("", 14, Th.Fg2); DockPanel.SetDock(sg, WDock.Left); bd.Children.Add(sg);
        search = SearchField("Search for apps, settings and documents", 14, out var host, out _);
        host.Margin = new Thickness(12, 0, 0, 0);
        bd.Children.Add(host);
        box.Child = bd;
        search.TextChanged += (_, _) =>
        {
            if (search.Text.Trim().Length == 0) Switch(home, -1);
            else { if (current != results) Switch(results, 1); results.Query(search.Text); }
        };
        g.Children.Add(box);

        home = BuildHome();
        (all, allScroll, jump) = BuildAll();
        results.Margin = new Thickness(22, 0, 22, 10);
        results.Ran += Hide;
        foreach (var v in new[] { home, all, results }) { v.Visibility = Visibility.Collapsed; v.RenderTransform = new TranslateTransform(); views.Children.Add(v); }
        home.Visibility = Visibility.Visible; current = home;
        Grid.SetRow(views, 1); g.Children.Add(views);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2); g.Children.Add(footer);
        glass.Body = g;
        SetPanel(glass, new Point(0.5, 1));
    }

    public override bool IsTrigger(DependencyObject src) => Desktop.I.Dock.StartVisual != null && Ui.IsInside(src, Desktop.I.Dock.StartVisual);

    FrameworkElement BuildHome()
    {
        var sp = new StackPanel { Margin = new Thickness(36, 4, 36, 0) };
        var h1 = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        h1.Children.Add(Ui.Txt("Pinned", 15, FontWeights.SemiBold));
        var allBtn = Ui.Pill("All apps", () => Switch(all, 1), null, false, false, 12);
        allBtn.HorizontalAlignment = HorizontalAlignment.Right; allBtn.Padding = new Thickness(12, 4, 12, 5);
        ((StackPanel)allBtn.Child).Children.Add(new TextBlock { Text = "", FontFamily = Th.Icons, FontSize = 9, Foreground = Th.Fg2, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        h1.Children.Add(allBtn);
        sp.Children.Add(h1);
        sp.Children.Add(new ScrollViewer { Content = pinned, Height = 3 * 92 + 4, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var h2 = Ui.Txt("Recommended", 15, FontWeights.SemiBold); h2.Margin = new Thickness(4, 18, 0, 8);
        sp.Children.Add(h2);
        sp.Children.Add(recommended);
        return sp;
    }

    (FrameworkElement, ScrollViewer, Grid) BuildAll()
    {
        var g = new Grid { Margin = new Thickness(36, 4, 30, 0) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var h = new Grid { Margin = new Thickness(4, 0, 10, 8) };
        h.Children.Add(Ui.Txt("All apps", 15, FontWeights.SemiBold));
        var back = Ui.Pill("Back", () => Switch(home, -1), "", false, false, 12);
        back.HorizontalAlignment = HorizontalAlignment.Right; back.Padding = new Thickness(12, 4, 12, 5);
        h.Children.Add(back);
        g.Children.Add(h);
        var sc = new ScrollViewer { Content = allList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(sc, 1); g.Children.Add(sc);
        var jg = new Grid { Background = new SolidColorBrush(Color.FromArgb(235, 0x10, 0x14, 0x1E)), Visibility = Visibility.Collapsed };
        Grid.SetRow(jg, 1); g.Children.Add(jg);
        return (g, sc, jg);
    }

    FrameworkElement BuildFooter()
    {
        var f = new Border { Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), CornerRadius = new CornerRadius(0, 0, 19, 19), BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Height = 66 };
        var g = new Grid { Margin = new Thickness(40, 0, 36, 0) };
        var user = new StackPanel { Orientation = Orientation.Horizontal };
        user.Children.Add(Avatar(34));
        var name = Ui.Txt(Environment.UserName, 13.5, FontWeights.SemiBold); name.Margin = new Thickness(12, 0, 0, 0);
        user.Children.Add(name);
        var ub = new Border { Child = user, Padding = new Thickness(8, 6, 14, 6), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Ui.Hoverable(ub);
        Ui.OnClick(ub, () => Ui.ShowMenu(ub, Ui.Menu(
            ("", "Account settings", () => { Hide(); Shell.Run("ms-settings:yourinfo"); }),
            ("", "Lock", () => { Hide(); Sys.Lock(); }),
            ("", "Sign out…", () => Dialog.Confirm("Sign out of Windows?", "Apps will close. Save your work first.", "Sign out", true, Sys.SignOut, ""))), PlacementMode.Top), false);
        g.Children.Add(ub);
        var pw = Ui.IconButton("", null, "Power", 38, 15);
        pw.HorizontalAlignment = HorizontalAlignment.Right; pw.VerticalAlignment = VerticalAlignment.Center;
        Ui.OnClick(pw, () => Desktop.I.PowerMenu(pw, PlacementMode.Top));
        g.Children.Add(pw);
        f.Child = g;
        return f;
    }

    public static FrameworkElement Avatar(double size)
    {
        string pic = null;
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid);
            pic = (k?.GetValue("Image192") ?? k?.GetValue("Image96")) as string;
        }
        catch { }
        if (pic != null && File.Exists(pic))
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(pic); bi.DecodePixelWidth = 192; bi.EndInit();
                return new Ellipse { Width = size, Height = size, Fill = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }, Stroke = Th.Hairline, StrokeThickness = 1 };
            }
            catch { }
        }
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Th.Electric, Th.Violet, 45) });
        var initials = string.Concat(Environment.UserName.Split(' ').Where(s => s.Length > 0).Take(2).Select(s => char.ToUpperInvariant(s[0])));
        g.Children.Add(new TextBlock { Text = initials, FontFamily = Th.Display, FontWeight = FontWeights.SemiBold, FontSize = size * 0.38, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return g;
    }

    public void Rebuild()
    {
        pinned.Children.Clear();
        foreach (var id in Pins.Start)
            if (Catalog.ById.TryGetValue(id, out var a)) pinned.Children.Add(Tile(a));
        BuildAllList();
    }

    FrameworkElement Tile(AppEntry a)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ic = AppIcon.Make(a, 38); ic.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(ic);
        var t = Ui.Txt(a.Name, 12, null, Th.Fg); t.HorizontalAlignment = HorizontalAlignment.Center; t.MaxWidth = 88; t.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(t);
        var b = new Border { CornerRadius = new CornerRadius(10), Child = sp, Margin = new Thickness(3) };
        Ui.Hoverable(b);
        Ui.OnClick(b, () => { Hide(); Catalog.Launch(a); });
        b.MouseRightButtonUp += (_, e) => { e.Handled = true; AppMenu(b, a); };
        return b;
    }

    void AppMenu(UIElement target, AppEntry a)
    {
        var items = new List<(string, string, Action)>
        {
            ("", "Open", () => { Hide(); Catalog.Launch(a); }),
            Ui.Sep,
            (Pins.InStart(a.Id) ? "" : "", Pins.InStart(a.Id) ? "Unpin from Start" : "Pin to Start", () => Pins.ToggleStart(a.Id)),
            (Pins.InDock(a.Id) ? "" : "", Pins.InDock(a.Id) ? "Remove from Dock" : "Keep in Dock", () => Pins.ToggleDock(a.Id)),
        };
        if (Pins.InStart(a.Id) && Pins.Start.IndexOf(a.Id) > 0)
            items.Add(("", "Move to front", () => Pins.StartFront(a.Id)));
        if (a.Path != null) { items.Add(Ui.Sep); items.Add(("", "Open file location", () => { Hide(); Shell.Reveal(a.Path); })); }
        Ui.ShowMenu(target, Ui.Menu(items.ToArray()));
    }

    void BuildAllList()
    {
        allList.Children.Clear(); letterHeaders.Clear();
        foreach (var grp in Catalog.Apps.GroupBy(a => a.Letter).OrderBy(g => g.Key == "#" ? "0" : g.Key))
        {
            var key = grp.Key;
            var hb = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.Txt(key, 13, FontWeights.SemiBold, Th.Accent) };
            Ui.Hoverable(hb);
            Ui.OnClick(hb, ShowJump, false);
            letterHeaders[key] = hb;
            allList.Children.Add(hb);
            foreach (var a in grp)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(AppIcon.Make(a, 28));
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txt.Children.Add(Ui.Txt(a.Name, 13.5));
                if (a.Internal) txt.Children.Add(Ui.Txt("Bisman OS", 11, null, Th.Fg3));
                Grid.SetColumn(txt, 1); row.Children.Add(txt);
                var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Child = row, Margin = new Thickness(0, 1, 10, 1) };
                Ui.Hoverable(b);
                var app = a;
                Ui.OnClick(b, () => { Hide(); Catalog.Launch(app); }, false);
                b.MouseRightButtonUp += (_, e) => { e.Handled = true; AppMenu(b, app); };
                allList.Children.Add(b);
            }
        }
    }

    void ShowJump()
    {
        jump.Children.Clear();
        var ug = new UniformGrid { Columns = 6, Width = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var l in new[] { "#" }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString())))
        {
            bool has = letterHeaders.ContainsKey(l);
            var b = new Border { Height = 52, CornerRadius = new CornerRadius(10), Margin = new Thickness(3), Child = Ui.Txt(l, 18, FontWeights.SemiBold, has ? Th.Fg : Th.Fg3, Th.Display) };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            if (has)
            {
                Ui.Hoverable(b);
                var key = l;
                Ui.OnClick(b, () => { jump.Visibility = Visibility.Collapsed; var hdr = letterHeaders[key]; var p = hdr.TransformToAncestor(allList).Transform(new Point()); allScroll.ScrollToVerticalOffset(p.Y - 4); });
            }
            ug.Children.Add(b);
        }
        jump.Children.Add(ug);
        jump.Visibility = Visibility.Visible;
        Anim.Run(jump, OpacityProperty, 0, 1, 160);
    }

    void BuildRecommended()
    {
        recommended.Children.Clear();
        var list = new List<FrameworkElement>();
        foreach (var id in Recent.Apps.Take(2))
            if (Catalog.ById.TryGetValue(id, out var a)) { var app = a; list.Add(RecRow(AppIcon.Make(a, 32), a.Name, "Recently opened", () => Catalog.Launch(app))); }
        foreach (var f in Recent.Files(6 - list.Count))
        {
            var path = f.path;
            list.Add(RecRow(AppIcon.FromShell(path, 32, true, ""), f.name, Ui.Ago(f.time), () => Shell.Open(path)));
        }
        if (list.Count == 0)
        {
            var t = Ui.Txt("Files and apps you open will appear here.", 13, null, Th.Fg3); t.Margin = new Thickness(6, 4, 0, 0);
            recommended.Children.Add(t);
        }
        foreach (var e in list) recommended.Children.Add(e);
    }

    FrameworkElement RecRow(FrameworkElement icon, string title, string sub, Action act)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(icon);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Ui.Txt(title, 13, FontWeights.Medium));
        s.Children.Add(Ui.Txt(sub, 11.5, null, Th.Fg3));
        Grid.SetColumn(s, 1); g.Children.Add(s);
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Child = g, Margin = new Thickness(2) };
        Ui.Hoverable(b);
        Ui.OnClick(b, () => { Hide(); act(); }, false);
        return b;
    }

    void Switch(FrameworkElement to, int dir)
    {
        if (current == to) return;
        var from = current; current = to;
        jump.Visibility = Visibility.Collapsed;
        var ft = (TranslateTransform)from.RenderTransform;
        Anim.To(from, OpacityProperty, 0, 120, null, () => { if (current != from) from.Visibility = Visibility.Collapsed; });
        Anim.To(ft, TranslateTransform.XProperty, -30 * dir, 120);
        to.Visibility = Visibility.Visible;
        Anim.Run(to, OpacityProperty, 0, 1, 200);
        Anim.Run((TranslateTransform)to.RenderTransform, TranslateTransform.XProperty, 30 * dir, 0, 240);
    }

    protected override void OnOpen()
    {
        search.Text = "";
        foreach (var v in new[] { home, all, results }) { v.Visibility = v == home ? Visibility.Visible : Visibility.Collapsed; v.Opacity = 1; ((TranslateTransform)v.RenderTransform).X = 0; }
        current = home; jump.Visibility = Visibility.Collapsed;
        BuildRecommended();
        int i = 0;
        foreach (UIElement t in pinned.Children) { if (i > 24) break; Anim.Run(t, OpacityProperty, 0, 1, 220, null, null, 40 + i++ * 12); }
        Dispatcher.BeginInvoke(new Action(() => { search.Focus(); Keyboard.Focus(search); }), DispatcherPriority.Input);
    }

    public bool HandleKey(Key k) => current == results && results.HandleKey(k);
}

// ================================================================ Spotlight-style search
public class Spotlight : Flyout
{
    readonly TextBox input;
    readonly ResultsList results = new();
    readonly FrameworkElement hint;

    public Spotlight()
    {
        FromY = -10; FromScale = 0.97;
        var glass = new Glass(22, 0.62) { Width = 720, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 150, 0, 0) };
        glass.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 16, Direction = 270, Opacity = 0.55, RenderingBias = RenderingBias.Performance };
        var sp = new StackPanel();
        var row = new DockPanel { Margin = new Thickness(22, 16, 22, 16) };
        var g = Ui.Glyph("", 20, Th.Fg2); DockPanel.SetDock(g, WDock.Left); row.Children.Add(g);
        input = SearchField("Bisman Search", 24, out var host, out _);
        host.Margin = new Thickness(16, 0, 0, 0);
        row.Children.Add(host);
        sp.Children.Add(row);
        var line = Ui.Hairline(); sp.Children.Add(line);
        results.MaxHeight = 470; results.Margin = new Thickness(12, 10, 12, 12);
        results.Ran += Hide;
        sp.Children.Add(results);
        var h = Ui.Txt("Apps, files, folders, settings and commands. Press Enter to open, Esc to close.", 12.5, null, Th.Fg3);
        h.Margin = new Thickness(24, 12, 24, 14); hint = h;
        sp.Children.Add(h);
        input.TextChanged += (_, _) =>
        {
            bool any = input.Text.Trim().Length > 0;
            results.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            hint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            if (any) results.Query(input.Text);
        };
        results.Visibility = Visibility.Collapsed;
        glass.Body = sp;
        SetPanel(glass, new Point(0.5, 0));
    }

    public override bool IsTrigger(DependencyObject src) => Ui.IsInside(src, Desktop.I.Bar.SearchTrigger);

    protected override void OnOpen()
    {
        input.Text = "";
        Dispatcher.BeginInvoke(new Action(() => { input.Focus(); Keyboard.Focus(input); }), DispatcherPriority.Input);
    }

    public bool HandleKey(Key k) => results.HandleKey(k);
}

// ================================================================ Control Centre (only real, working controls)
public class ControlCentre : Flyout
{
    readonly (Border tile, Border circle, TextBlock sub) wifi, bt, dnd, live;
    readonly Slider vol, bright;
    readonly TextBlock volGlyph, brightNote, battBig, battSub, netLine;
    readonly FrameworkElement brightRow;
    bool syncing;
    Dictionary<string, bool> radios = new();
    readonly DispatcherTimer brightDebounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public ControlCentre()
    {
        FromY = -10; FromX = 10; FromScale = 0.96;
        var glass = new Glass(22, 0.56) { Width = 364, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 10, 0) };
        glass.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.5, RenderingBias = RenderingBias.Performance };
        var sp = new StackPanel { Margin = new Thickness(14) };

        var r1 = new UniformGrid { Columns = 2 };
        wifi = Tile("Wi-Fi", "", () => ToggleRadio("WiFi"), () => Shell.Run("ms-settings:network-wifi"));
        bt = Tile("Bluetooth", "", () => ToggleRadio("Bluetooth"), () => Shell.Run("ms-settings:bluetooth"));
        r1.Children.Add(wifi.tile); r1.Children.Add(bt.tile);
        sp.Children.Add(r1);
        var r2 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        dnd = Tile("Focus", "", () => { Settings.SetBool("dnd", !Settings.Bool("dnd")); Sync(); }, () => Desktop.I.OpenSettings("Notifications"));
        live = Tile("Live wallpaper", "", () => { Settings.SetBool("liveWallpaper", !Settings.Bool("liveWallpaper")); Sync(); }, () => Desktop.I.OpenSettings("Wallpaper"));
        r2.Children.Add(dnd.tile); r2.Children.Add(live.tile);
        sp.Children.Add(r2);

        // Display
        var dc = Card("Display", "ms-settings:display");
        var br = new DockPanel();
        var bg = Ui.Glyph("", 15, Th.Fg2); DockPanel.SetDock(bg, WDock.Left); br.Children.Add(bg);
        bright = new Slider { Style = Styles.Get("Glider"), Minimum = 0, Maximum = 100, Margin = new Thickness(12, 0, 0, 0) };
        br.Children.Add(bright);
        brightRow = br;
        brightNote = Ui.Txt("Brightness control isn't available for this display.", 12, null, Th.Fg3);
        brightNote.TextWrapping = TextWrapping.Wrap; brightNote.Visibility = Visibility.Collapsed;
        ((StackPanel)dc.Child).Children.Add(br); ((StackPanel)dc.Child).Children.Add(brightNote);
        bright.ValueChanged += (_, _) => { if (!syncing) { brightDebounce.Stop(); brightDebounce.Start(); } };
        brightDebounce.Tick += (_, _) => { brightDebounce.Stop(); Sys.SetBrightness((int)bright.Value); };
        sp.Children.Add(dc);

        // Sound
        var sc = Card("Sound", "ms-settings:sound");
        var vr = new DockPanel();
        volGlyph = Ui.Glyph("", 15, Th.Fg2);
        var mute = new Border { Child = volGlyph, Width = 28, Height = 28, CornerRadius = new CornerRadius(8), ToolTip = "Mute" };
        Ui.Hoverable(mute);
        Ui.OnClick(mute, () => { Sys.Audio.SetMute(!Sys.Audio.Muted); Sync(); });
        DockPanel.SetDock(mute, WDock.Left); vr.Children.Add(mute);
        vol = new Slider { Style = Styles.Get("Glider"), Minimum = 0, Maximum = 100, Margin = new Thickness(8, 0, 0, 0) };
        vol.ValueChanged += (_, _) => { if (!syncing) { Sys.Audio.SetVolume((float)(vol.Value / 100)); UpdateVolGlyph(); } };
        vr.Children.Add(vol);
        ((StackPanel)sc.Child).Children.Add(vr);
        sp.Children.Add(sc);

        // Battery & network
        var pc = Card("Power & network", "ms-settings:batterysaver");
        battBig = Ui.Tab(Ui.Txt("", 26, FontWeights.SemiBold, null, Th.Display));
        battSub = Ui.Txt("", 12.5, null, Th.Fg2);
        netLine = Ui.Txt("", 12.5, null, Th.Fg2); netLine.Margin = new Thickness(0, 8, 0, 0);
        ((StackPanel)pc.Child).Children.Add(battBig); ((StackPanel)pc.Child).Children.Add(battSub); ((StackPanel)pc.Child).Children.Add(netLine);
        sp.Children.Add(pc);

        glass.Body = sp;
        SetPanel(glass, new Point(1, 0));
        tick.Tick += (_, _) => SyncLight();
    }

    public override bool IsTrigger(DependencyObject src) => Desktop.I.Bar.CcTriggers.Any(t => Ui.IsInside(src, t));

    (Border, Border, TextBlock) Tile(string title, string glyph, Action toggle, Action settings)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var circle = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Th.Card, Child = Ui.Glyph(glyph, 15), HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(circle);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Ui.Txt(title, 13, FontWeights.SemiBold));
        var sub = Ui.Txt("…", 11.5, null, Th.Fg2);
        s.Children.Add(sub);
        Grid.SetColumn(s, 1); g.Children.Add(s);
        var b = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 10, 10, 10), Margin = new Thickness(0, 0, 5, 0), Child = g, ToolTip = "Right-click for settings" };
        Ui.Hoverable(b, Th.Card, Th.Press);
        Ui.OnClick(b, toggle);
        b.MouseRightButtonUp += (_, e) => { e.Handled = true; Hide(); settings(); };
        return (b, circle, sub);
    }

    Border Card(string title, string settingsUri)
    {
        var sp = new StackPanel();
        var h = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        h.Children.Add(Ui.Txt(title, 13, FontWeights.SemiBold));
        var more = Ui.IconButton("", () => { Hide(); Shell.Run(settingsUri); }, "Open Windows settings", 24, 10);
        more.HorizontalAlignment = HorizontalAlignment.Right;
        h.Children.Add(more);
        sp.Children.Add(h);
        return new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 10, 0, 0), Background = Th.Card, Child = sp };
    }

    void SetTile((Border tile, Border circle, TextBlock sub) t, bool? on, string sub)
    {
        t.circle.Background = on == true ? Th.Accent : Th.Card;
        t.sub.Text = sub;
        t.tile.Opacity = on == null ? 0.5 : 1;
    }

    void ToggleRadio(string kind)
    {
        if (!radios.TryGetValue(kind, out var on)) return;
        var t = kind == "WiFi" ? wifi : bt;
        t.sub.Text = on ? "Turning off…" : "Turning on…";
        Sys.Radios(r => { radios = r; Sync(); if (kind == "WiFi") Sys.RefreshWifiAsync(); }, kind, !on);
    }

    protected override void OnOpen()
    {
        Sync();
        Sys.Radios(r => { radios = r; Sync(); });
        Task.Run(() =>
        {
            var b = Sys.Brightness();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                brightRow.Visibility = b.HasValue ? Visibility.Visible : Visibility.Collapsed;
                brightNote.Visibility = b.HasValue ? Visibility.Collapsed : Visibility.Visible;
                if (b.HasValue) { syncing = true; bright.Value = b.Value; syncing = false; }
            }));
        });
        tick.Start();
    }

    protected override void OnClose() => tick.Stop();

    void Sync()
    {
        var n = Sys.Network();
        if (radios.TryGetValue("WiFi", out var w)) SetTile(wifi, w, w ? (n.Kind == "Wi-Fi" && n.Up ? n.Name : "On") : "Off");
        else SetTile(wifi, null, radios.Count == 0 ? "Checking…" : "Not available");
        if (radios.TryGetValue("Bluetooth", out var b)) SetTile(bt, b, b ? "On" : "Off");
        else SetTile(bt, null, radios.Count == 0 ? "Checking…" : "Not available");
        SetTile(dnd, Settings.Bool("dnd"), Settings.Bool("dnd") ? "Notifications silenced" : "Off");
        SetTile(live, Settings.Bool("liveWallpaper"), Settings.Bool("liveWallpaper") ? "On" : "Off");
        SyncLight();
    }

    void SyncLight()
    {
        syncing = true;
        if (Sys.Audio.Available) vol.Value = Sys.Audio.Volume * 100;
        syncing = false;
        UpdateVolGlyph();
        var p = Sys.Battery();
        battBig.Text = p.HasBattery ? p.Percent + "%" : "Plugged in";
        battSub.Text = p.HasBattery ? (p.Charging ? "Charging" : p.Plugged ? "Plugged in" : (p.SecondsLeft > 0 ? TimeSpan.FromSeconds(p.SecondsLeft).ToString(@"h\h\ mm\m") + " remaining" : "On battery")) : "No battery detected";
        var n = Sys.Network();
        netLine.Text = n.Up ? n.Kind + " · " + n.Name + (n.Ip != null ? " · " + n.Ip : "") : "Not connected to a network";
    }

    void UpdateVolGlyph()
    {
        float v = Sys.Audio.Volume; bool m = Sys.Audio.Muted;
        volGlyph.Text = m || v < 0.01 ? "" : v < 0.34 ? "" : v < 0.67 ? "" : "";
    }
}

// ================================================================ notification centre + toasts
public class NotificationCentre : Flyout
{
    readonly StackPanel list = new();
    readonly TextBlock day, dateText;
    readonly FrameworkElement empty;
    public readonly StackPanel Toasts;

    public NotificationCentre()
    {
        FromX = 30; FromY = 0; FromScale = 0.98;
        var glass = new Glass(22, 0.56) { Width = 384, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 40, 10, 96) };
        glass.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.5, RenderingBias = RenderingBias.Performance };
        var g = new Grid { Margin = new Thickness(18, 18, 14, 14) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var head = new Grid { Margin = new Thickness(4, 0, 4, 14) };
        var hs = new StackPanel();
        day = Ui.Txt("", 13, FontWeights.Medium, Th.Fg2);
        dateText = Ui.Txt("", 24, FontWeights.SemiBold, null, Th.Display);
        hs.Children.Add(day); hs.Children.Add(dateText);
        head.Children.Add(hs);
        var clear = Ui.Pill("Clear all", Notify.Clear, null, false, false, 12);
        clear.HorizontalAlignment = HorizontalAlignment.Right; clear.VerticalAlignment = VerticalAlignment.Bottom; clear.Padding = new Thickness(12, 4, 12, 5);
        head.Children.Add(clear);
        g.Children.Add(head);
        var sc = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(sc, 1); g.Children.Add(sc);
        empty = EmptyState();
        Grid.SetRow(empty, 1); g.Children.Add(empty);
        glass.Body = g;
        SetPanel(glass, new Point(1, 0));

        Toasts = new StackPanel { Width = 350, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 44, 12, 0) };
        Notify.Changed += () => { if (IsOpen) Render(); };
        Notify.Posted += Toast;
    }

    public override bool IsTrigger(DependencyObject src) => Desktop.I.Bar.NcTriggers.Any(t => Ui.IsInside(src, t));

    static FrameworkElement EmptyState()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
        var art = Path.Combine(Wallpaper.AssetDir, "illustrations", "companion.jpg");
        if (File.Exists(art))
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(art); bi.DecodePixelWidth = 520; bi.EndInit();
            var img = new Border { Width = 230, Height = 150, CornerRadius = new CornerRadius(18), Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill } };
            img.OpacityMask = new RadialGradientBrush(Colors.Black, Colors.Transparent) { GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.6, RadiusY = 0.6 };
            sp.Children.Add(img);
        }
        else sp.Children.Add(Ui.Glyph("", 44, Th.Fg3));
        var t = Ui.Txt("No notifications", 16, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 14, 0, 4);
        sp.Children.Add(t);
        var s = Ui.Txt("You're all caught up.", 13, null, Th.Fg3); s.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(s);
        return sp;
    }

    protected override void OnOpen()
    {
        day.Text = DateTime.Now.ToString("dddd");
        dateText.Text = DateTime.Now.ToString("d MMMM");
        foreach (UIElement t in Toasts.Children.OfType<UIElement>().ToList()) Toasts.Children.Remove(t);
        Render();
    }

    void Render()
    {
        list.Children.Clear();
        empty.Visibility = Notify.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var grp in Notify.Items.GroupBy(n => n.Source))
        {
            var h = Ui.Txt(grp.Key + (grp.Count() > 1 ? "  ·  " + grp.Count() : ""), 12, FontWeights.SemiBold, Th.Fg3);
            h.Margin = new Thickness(6, list.Children.Count == 0 ? 0 : 12, 0, 6);
            list.Children.Add(h);
            foreach (var n in grp) list.Children.Add(Card(n, false));
        }
    }

    FrameworkElement Card(Note n, bool toast)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Th.AccentSoft, Child = Ui.Glyph(n.Glyph ?? "", 14, Th.Fg), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left });
        var s = new StackPanel();
        var top = new DockPanel();
        var time = Ui.Txt(Ui.Ago(n.Time), 11.5, null, Th.Fg3); DockPanel.SetDock(time, WDock.Right);
        top.Children.Add(time);
        top.Children.Add(Ui.Txt(n.Title, 13, FontWeights.SemiBold));
        s.Children.Add(top);
        if (!string.IsNullOrEmpty(n.Body)) { var b = Ui.Txt(n.Body, 12.5, null, Th.Fg2); b.TextWrapping = TextWrapping.Wrap; b.TextTrimming = TextTrimming.None; b.MaxHeight = 54; b.Margin = new Thickness(0, 2, 0, 0); s.Children.Add(b); }
        Grid.SetColumn(s, 1); g.Children.Add(s);
        var x = Ui.IconButton("", null, "Dismiss", 22, 9);
        x.HorizontalAlignment = HorizontalAlignment.Right; x.VerticalAlignment = VerticalAlignment.Top; x.Margin = new Thickness(0, -6, -6, 0); x.Opacity = 0;
        Grid.SetColumnSpan(x, 2); g.Children.Add(x);
        FrameworkElement card;
        if (toast)
        {
            var gl = new Glass(16, 0.62) { Margin = new Thickness(0, 0, 0, 10) };
            gl.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance };
            gl.Body = new Border { Padding = new Thickness(14, 12, 14, 12), Child = g };
            card = gl;
        }
        else
        {
            var bd = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 11, 12, 11), Margin = new Thickness(0, 0, 0, 8), Child = g };
            Ui.Hoverable(bd, Th.Card, Th.Press);
            card = bd;
        }
        card.MouseEnter += (_, _) => x.Opacity = 1; card.MouseLeave += (_, _) => x.Opacity = 0;
        Ui.OnClick(x, () => { if (toast) RemoveToast(card); else Notify.Dismiss(n); });
        Ui.OnClick(card, () => { if (toast) RemoveToast(card); if (n.Click != null) { Hide(); n.Click(); } else if (toast) Show(); }, false);
        return card;
    }

    void Toast(Note n)
    {
        if (Settings.Bool("dnd") || IsOpen) return;
        var card = Card(n, true);
        var tt = new TranslateTransform(380, 0); card.RenderTransform = tt;
        Toasts.Children.Insert(0, card);
        while (Toasts.Children.Count > 3) Toasts.Children.RemoveAt(Toasts.Children.Count - 1);
        Anim.Run(tt, TranslateTransform.XProperty, 380, 0, 340, Anim.Spring);
        Anim.Run(card, OpacityProperty, 0, 1, 200);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
        timer.Tick += (_, _) => { timer.Stop(); if (!card.IsMouseOver) RemoveToast(card); else timer.Start(); };
        timer.Start();
    }

    void RemoveToast(FrameworkElement card)
    {
        if (!Toasts.Children.Contains(card)) return;
        card.IsHitTestVisible = false;
        Anim.To((TranslateTransform)card.RenderTransform, TranslateTransform.XProperty, 380, 260, Anim.InOut);
        Anim.To(card, OpacityProperty, 0, 260, null, () => Toasts.Children.Remove(card));
    }
}

// ================================================================ first-run welcome
public static class Onboarding
{
    public static void Show()
    {
        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
        var card = new Glass(26, 0.7) { Width = 820, Height = 470, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.Effect = new DropShadowEffect { BlurRadius = 70, ShadowDepth = 18, Direction = 270, Opacity = 0.6 };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var art = Path.Combine(Wallpaper.AssetDir, "illustrations", "companion.jpg");
        if (File.Exists(art))
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(art); bi.DecodePixelWidth = 900; bi.EndInit();
            g.Children.Add(new Border { CornerRadius = new CornerRadius(25, 0, 0, 25), Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center } });
        }
        var sp = new StackPanel { Margin = new Thickness(40, 40, 40, 30) };
        sp.Children.Add(new ContentControl { Content = Ui.Logo(46), HorizontalAlignment = HorizontalAlignment.Left });
        var t = Ui.Txt("Welcome to Bisman OS", 30, FontWeights.SemiBold, null, Th.Display); t.Margin = new Thickness(0, 18, 0, 6);
        sp.Children.Add(t);
        var s = Ui.Txt("Your desktop, rebuilt on top of Windows. All your apps, files and settings are right here.", 14, null, Th.Fg2);
        s.TextWrapping = TextWrapping.Wrap; s.TextTrimming = TextTrimming.None; s.Margin = new Thickness(0, 0, 0, 20);
        sp.Children.Add(s);
        foreach (var (keys, what) in new[] { ("Ctrl+Space", "Start menu with every installed app"), ("Alt+Space", "Search apps, files, folders and settings"), ("Ctrl+Alt+T", "Open the terminal"), ("Ctrl+Alt+B", "Bring Bisman OS back from any app"), ("Ctrl+Shift+Q", "Exit to the normal Windows desktop") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            var k = new Border { CornerRadius = new CornerRadius(6), Background = Th.Card, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2, 8, 3), Width = 112, Child = Ui.Txt(keys, 12, FontWeights.SemiBold, null, Th.Mono) };
            DockPanel.SetDock(k, WDock.Left); row.Children.Add(k);
            var w = Ui.Txt(what, 13, null, Th.Fg2); w.Margin = new Thickness(12, 0, 0, 0); row.Children.Add(w);
            sp.Children.Add(row);
        }
        var go = Ui.Pill("Get started", () =>
        {
            Settings.SetBool("onboarded", true);
            Anim.To(layer, UIElement.OpacityProperty, 0, 200, null, () => Dialog.Host.Children.Remove(layer));
        }, null, true, false, 14);
        go.HorizontalAlignment = HorizontalAlignment.Left; go.Margin = new Thickness(0, 14, 0, 0); go.Padding = new Thickness(22, 9, 22, 10);
        sp.Children.Add(go);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        card.Body = g;
        layer.Children.Add(card);
        Dialog.Host.Children.Add(layer);
        var st = new ScaleTransform(1, 1); card.RenderTransform = st; card.RenderTransformOrigin = new Point(0.5, 0.5);
        Anim.Run(layer, UIElement.OpacityProperty, 0, 1, 300);
        Anim.Run(st, ScaleTransform.ScaleXProperty, 0.94, 1, 420, Anim.Spring);
        Anim.Run(st, ScaleTransform.ScaleYProperty, 0.94, 1, 420, Anim.Spring);
    }
}
