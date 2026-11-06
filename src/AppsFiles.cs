// Bisman OS 2 - Files: a real file browser on the Windows file system.
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace BismanOS;

public static class FilesApp
{
    public static ShellWin Open(string path)
    {
        var v = new FilesView(path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var w = new ShellWin("bisman:files", "Files", 1080, 670, v) { MinW = 640, MinH = 380 };
        v.Win = w;
        Desktop.I.Wm.Open(w);
        return w;
    }
}

public class FilesView : Grid, IKeyHandler
{
    public ShellWin Win;
    string path;
    readonly Stack<string> back = new(), fwd = new();
    readonly WrapPanel grid = new();
    readonly StackPanel list = new();
    readonly ScrollViewer scroll;
    readonly StackPanel crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox search;
    readonly TextBlock searchHint, status;
    readonly Grid emptyHost = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel sidebar = new();
    readonly Border preview;
    readonly StackPanel previewBody = new() { Margin = new Thickness(18) };
    readonly Border viewBtn;
    readonly Dictionary<string, Border> sideRows = new(StringComparer.OrdinalIgnoreCase);
    List<FileSystemInfo> items = new();
    readonly Dictionary<FileSystemInfo, Border> cards = new();
    FileSystemInfo sel;
    bool listMode = Settings.Bool("filesList"), showPreview = !Settings.Bool("filesNoPreview"), searchMode;
    CancellationTokenSource searchCts;

    public FilesView(string start)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- sidebar
        var side = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), CornerRadius = new CornerRadius(0, 0, 0, 13), BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 0, 1, 0) };
        side.Child = new ScrollViewer { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 4, 10, 10) };
        Children.Add(side);
        BuildSidebar();

        // ---- main
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        SetColumn(main, 1); Children.Add(main);

        var tb = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Ui.IconButton("", GoBack, "Back (Backspace)"));
        nav.Children.Add(Ui.IconButton("", GoForward, "Forward"));
        nav.Children.Add(Ui.IconButton("", GoUp, "Up"));
        DockPanel.SetDock(nav, WDock.Left); tb.Children.Add(nav);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var sbox = new Border { Width = 220, Height = 32, CornerRadius = new CornerRadius(9), Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 6, 0) };
        var sd = new DockPanel();
        var sg = Ui.Glyph("", 12, Th.Fg3); DockPanel.SetDock(sg, WDock.Left); sd.Children.Add(sg);
        var sgrid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        search = new TextBox { Style = Styles.Get("Field"), FontSize = 13 };
        searchHint = Ui.Txt("Search (Enter for subfolders)", 12.5, null, Th.Fg3); searchHint.IsHitTestVisible = false;
        sgrid.Children.Add(search); sgrid.Children.Add(searchHint);
        sd.Children.Add(sgrid); sbox.Child = sd;
        search.TextChanged += (_, _) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (!searchMode) Render(); else if (search.Text.Length == 0) Navigate(path, false); };
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) DeepSearch(); else if (e.Key == Key.Escape) { search.Text = ""; Keyboard.ClearFocus(); } };
        tools.Children.Add(sbox);
        viewBtn = Ui.IconButton(listMode ? "" : "", ToggleView, "Grid or list view");
        tools.Children.Add(viewBtn);
        tools.Children.Add(Ui.IconButton("", NewFolder, "New folder (Ctrl+Shift+N)"));
        tools.Children.Add(Ui.IconButton("", TogglePreview, "Preview pane"));
        DockPanel.SetDock(tools, WDock.Right); tb.Children.Add(tools);
        var crumbHost = new Border { Margin = new Thickness(8, 0, 4, 0), Child = crumbs, ClipToBounds = true };
        tb.Children.Add(crumbHost);
        main.Children.Add(tb);

        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 4, 8, 8), Background = Brushes.Transparent };
        scroll.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is ScrollViewer || e.OriginalSource == grid || e.OriginalSource == list) Select(null); };
        scroll.MouseRightButtonUp += (_, e) => { if (!(e.OriginalSource is DependencyObject d) || !cards.Values.Any(c => Ui.IsInside(d, c))) BackgroundMenu(); };
        SetRow(scroll, 1); main.Children.Add(scroll);
        SetRow(emptyHost, 1); main.Children.Add(emptyHost);
        status = Ui.Txt("", 11.5, null, Th.Fg3); status.Margin = new Thickness(18, 0, 0, 0);
        SetRow(status, 2); main.Children.Add(status);

        // ---- preview
        preview = new Border { Width = 270, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), CornerRadius = new CornerRadius(0, 0, 13, 0), Child = new ScrollViewer { Content = previewBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed };
        SetColumn(preview, 2); Children.Add(preview);

        Navigate(start, false);
    }

    // ---------------- sidebar
    void BuildSidebar()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SideHeader("Favourites");
        SideRow("", "Home", home);
        SideRow("", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        SideRow("", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        SideRow("", "Downloads", Native.Downloads);
        SideRow("", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        SideRow("", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        SideRow("", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        SideHeader("Locations");
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string label = (string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "USB Drive" : "Local Disk") : d.VolumeLabel) + " (" + d.Name.TrimEnd('\\') + ")";
                var row = SideRow(d.DriveType == DriveType.Removable ? "" : "", label, d.RootDirectory.FullName);
                double used = 1 - (double)d.AvailableFreeSpace / Math.Max(1, d.TotalSize);
                var sp = (StackPanel)((Grid)row.Child).Children[1];
                var track = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Margin = new Thickness(0, 4, 0, 0) };
                var fill = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = used > 0.9 ? Th.Danger : (Brush)Th.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 130 * used };
                var g = new Grid(); g.Children.Add(track); g.Children.Add(fill);
                sp.Children.Add(g);
                sp.Children.Add(Ui.Txt(Ui.Bytes(d.AvailableFreeSpace) + " free of " + Ui.Bytes(d.TotalSize), 10.5, null, Th.Fg3));
            }
            catch { }
        }
        var rb = SideRow("", "Recycle Bin", null);
        Ui.OnClick(rb, () => Shell.Run("explorer.exe", "shell:RecycleBinFolder"), false);
    }

    void SideHeader(string t)
    {
        var h = Ui.Txt(t, 11.5, FontWeights.SemiBold, Th.Fg3);
        h.Margin = new Thickness(10, sidebar.Children.Count == 0 ? 10 : 18, 0, 6);
        sidebar.Children.Add(h);
    }

    Border SideRow(string glyph, string name, string target)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Ui.Glyph(glyph, 14, Th.Accent));
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Ui.Txt(name, 13));
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1, 0, 1), Child = g, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => { if (target == null || !string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) b.Background = Th.Hover; };
        b.MouseLeave += (_, _) => HighlightSide();
        if (target != null)
        {
            sideRows[target] = b;
            Ui.OnClick(b, () => Navigate(target), false);
            b.MouseRightButtonUp += (_, e) => { e.Handled = true; Ui.ShowMenu(b, Ui.Menu(("", "Open", () => Navigate(target)), ("", "Open in Terminal", () => Desktop.I.OpenTerminal(target, null)), ("", "Open in Windows Explorer", () => Shell.Run("explorer.exe", "\"" + target + "\"")))); };
        }
        sidebar.Children.Add(b);
        return b;
    }

    void HighlightSide()
    {
        foreach (var kv in sideRows) kv.Value.Background = string.Equals(kv.Key.TrimEnd('\\'), path?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? Th.AccentSoft : Brushes.Transparent;
    }

    // ---------------- navigation
    public void Navigate(string p, bool record = true)
    {
        if (string.IsNullOrEmpty(p)) return;
        searchCts?.Cancel();
        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(p).EnumerateFileSystemInfos()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => f is DirectoryInfo ? 0 : 1).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(3000).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            if (record && path != null && path != p) { back.Push(path); fwd.Clear(); }
            path = p; items = new List<FileSystemInfo>(); searchMode = false;
            Render("Bisman OS doesn't have permission to open this folder.", true);
            return;
        }
        catch (Exception ex) { Notify.Post("Files", "", "Couldn't open folder", ex.Message); return; }
        if (record && path != null && !string.Equals(path, p, StringComparison.OrdinalIgnoreCase)) { back.Push(path); fwd.Clear(); }
        path = p; items = entries; searchMode = false;
        if (search.Text.Length > 0) { search.Text = ""; }
        Render();
    }

    void GoBack() { if (back.Count == 0) return; fwd.Push(path); Navigate(back.Pop(), false); }
    void GoForward() { if (fwd.Count == 0) return; back.Push(path); Navigate(fwd.Pop(), false); }
    void GoUp() { var parent = Directory.GetParent(path.TrimEnd('\\') + (path.EndsWith(":\\") ? "\\" : "")); if (parent != null && !path.EndsWith(":\\")) Navigate(parent.FullName); }
    void Refresh() => Navigate(path, false);

    void DeepSearch()
    {
        var q = search.Text.Trim();
        if (q.Length < 2) return;
        searchCts?.Cancel(); searchCts = new CancellationTokenSource();
        var token = searchCts.Token; var scope = path;
        status.Text = "Searching “" + q + "” in " + Path.GetFileName(scope.TrimEnd('\\')) + "…";
        Task.Run(() =>
        {
            var found = Search.Files(q, scope, 300, token);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested) return;
                searchMode = true;
                items = found.Select(r => Directory.Exists(r.IconPath) ? (FileSystemInfo)new DirectoryInfo(r.IconPath) : new FileInfo(r.IconPath)).ToList();
                Render(found.Count == 0 ? "No results for “" + q + "”. Only folders indexed by Windows Search are searched." : null);
            }));
        });
    }

    // ---------------- rendering
    void Render(string emptyMessage = null, bool denied = false)
    {
        grid.Children.Clear(); list.Children.Clear(); cards.Clear(); sel = null;
        var q = searchMode ? "" : search.Text.Trim();
        var shown = q.Length == 0 ? items : items.Where(f => f.Name.Has(q)).ToList();
        if (listMode)
        {
            list.Children.Add(ListHeader());
            foreach (var f in shown.Take(1500)) list.Children.Add(ListRow(f));
            scroll.Content = list;
        }
        else
        {
            foreach (var f in shown.Take(1500)) grid.Children.Add(Card(f));
            scroll.Content = grid;
        }
        scroll.ScrollToTop();
        bool empty = shown.Count == 0;
        emptyHost.Children.Clear();
        if (empty) emptyHost.Children.Add(EmptyState(emptyMessage ?? (q.Length > 0 ? "No items match “" + q + "”." : "This folder is empty."), denied));
        emptyHost.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BuildCrumbs();
        HighlightSide();
        string name = Path.GetFileName(path.TrimEnd('\\'));
        if (Win != null) Win.Title = searchMode ? "Search results" : (name.Length == 0 ? path : name);
        UpdateStatus(shown.Count);
        ShowPreview(null);
    }

    FrameworkElement EmptyState(string msg, bool denied)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var art = Path.Combine(Wallpaper.AssetDir, "illustrations", "companion.jpg");
        if (File.Exists(art))
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(art); bi.DecodePixelWidth = 520; bi.EndInit();
            sp.Children.Add(new Border { Width = 240, Height = 150, Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }, OpacityMask = new RadialGradientBrush(Colors.Black, Colors.Transparent) { RadiusX = 0.6, RadiusY = 0.6 } });
        }
        var t = Ui.Txt(msg, 14, FontWeights.Medium, Th.Fg2); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 10, 0, 12);
        sp.Children.Add(t);
        if (denied)
        {
            var b = Ui.Pill("Open in Windows Explorer", () => Shell.Run("explorer.exe", "\"" + path + "\""), "");
            b.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(b);
        }
        return sp;
    }

    void BuildCrumbs()
    {
        crumbs.Children.Clear();
        if (searchMode) { crumbs.Children.Add(Ui.Txt("Search results", 13, FontWeights.SemiBold)); return; }
        var parts = new List<string>();
        var di = new DirectoryInfo(path);
        while (di != null) { parts.Insert(0, di.FullName); di = di.Parent; }
        if (parts.Count > 4) parts = parts.Skip(parts.Count - 4).ToList();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) crumbs.Children.Add(new TextBlock { Text = "", FontFamily = Th.Icons, FontSize = 9, Foreground = Th.Fg3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
            var p = parts[i];
            string label = Path.GetFileName(p.TrimEnd('\\')); if (label.Length == 0) label = p.TrimEnd('\\');
            bool last = i == parts.Count - 1;
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 4), Child = Ui.Txt(label, 13, last ? FontWeights.SemiBold : FontWeights.Normal, last ? Th.Fg : Th.Fg2) };
            Ui.Hoverable(b);
            Ui.OnClick(b, () => Navigate(p), false);
            crumbs.Children.Add(b);
        }
    }

    void UpdateStatus(int count)
    {
        string free = "";
        try { if (!searchMode) { var d = new DriveInfo(Path.GetPathRoot(path)); free = "  ·  " + Ui.Bytes(d.AvailableFreeSpace) + " free"; } } catch { }
        status.Text = count + (count == 1 ? " item" : " items") + (sel != null ? "  ·  1 selected" : "") + free;
    }

    static string Kind(FileSystemInfo f) => f is DirectoryInfo ? "Folder" : (f.Extension.Length > 1 ? f.Extension.Substring(1).ToUpperInvariant() + " file" : "File");

    Border Card(FileSystemInfo f)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ic = AppIcon.FromShell(f.FullName, 64, false, f is DirectoryInfo ? "" : "");
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(ic);
        var t = new TextBlock { Text = f.Name, FontSize = 12, Foreground = Th.Fg, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Width = 96, Margin = new Thickness(0, 8, 0, 0), FontFamily = Th.Text };
        sp.Children.Add(t);
        var b = new Border { Width = 112, Height = 124, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 10, 6, 6), Margin = new Thickness(3), Child = sp, Background = Brushes.Transparent };
        Wire(b, f);
        return b;
    }

    FrameworkElement ListHeader()
    {
        var g = Cols();
        string[] heads = { "", "Name", "Date modified", "Type", "Size" };
        for (int i = 1; i < 5; i++) { var t = Ui.Txt(heads[i], 11.5, FontWeights.SemiBold, Th.Fg3); Grid.SetColumn(t, i); g.Children.Add(t); }
        return new Border { Padding = new Thickness(10, 4, 10, 6), Child = g, BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4) };
    }

    static Grid Cols()
    {
        var g = new Grid();
        foreach (var w in new[] { 30.0, -1, 170, 130, 90 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        return g;
    }

    Border ListRow(FileSystemInfo f)
    {
        var g = Cols();
        g.Children.Add(AppIcon.FromShell(f.FullName, 20, true, f is DirectoryInfo ? "" : ""));
        var n = Ui.Txt(f.Name, 13); Grid.SetColumn(n, 1); g.Children.Add(n);
        var d = Ui.Tab(Ui.Txt(f.LastWriteTime.ToString("d MMM yyyy, HH:mm"), 12, null, Th.Fg2)); Grid.SetColumn(d, 2); g.Children.Add(d);
        var k = Ui.Txt(Kind(f), 12, null, Th.Fg2); Grid.SetColumn(k, 3); g.Children.Add(k);
        var s = Ui.Tab(Ui.Txt(f is FileInfo fi ? Ui.Bytes(fi.Length) : "", 12, null, Th.Fg2)); Grid.SetColumn(s, 4); g.Children.Add(s);
        var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 1, 6, 1), Child = g, Background = Brushes.Transparent };
        Wire(b, f);
        return b;
    }

    void Wire(Border b, FileSystemInfo f)
    {
        cards[f] = b;
        b.MouseEnter += (_, _) => { if (sel != f) b.Background = Th.Hover; };
        b.MouseLeave += (_, _) => { if (sel != f) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; Select(f); if (e.ClickCount == 2) Open(f); };
        b.MouseRightButtonUp += (_, e) => { e.Handled = true; Select(f); ItemMenu(b, f); };
    }

    void Select(FileSystemInfo f)
    {
        if (sel != null && cards.TryGetValue(sel, out var old)) old.Background = Brushes.Transparent;
        sel = f;
        if (f != null && cards.TryGetValue(f, out var c)) c.Background = Th.AccentSoft;
        UpdateStatus(cards.Count);
        ShowPreview(f);
    }

    void ShowPreview(FileSystemInfo f)
    {
        previewBody.Children.Clear();
        if (!showPreview) return;
        if (f == null)
        {
            var t = Ui.Txt("Select a file to see a preview.", 12.5, null, Th.Fg3); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 30, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center;
            previewBody.Children.Add(t);
            return;
        }
        var box = new Border { Height = 170, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), ClipToBounds = true };
        var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        box.Child = img;
        ShellImages.Get(f.FullName, 256, false, s => img.Source = s);
        previewBody.Children.Add(box);
        var name = Ui.Txt(f.Name, 15, FontWeights.SemiBold); name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.None; name.Margin = new Thickness(0, 14, 0, 2);
        previewBody.Children.Add(name);
        previewBody.Children.Add(Ui.Txt(Kind(f) + (f is FileInfo fi ? "  ·  " + Ui.Bytes(fi.Length) : ""), 12.5, null, Th.Fg2));
        var info = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
        void Row(string k, string v) { var g = new DockPanel { Margin = new Thickness(0, 0, 0, 6) }; var kt = Ui.Txt(k, 12, null, Th.Fg3); kt.Width = 72; DockPanel.SetDock(kt, WDock.Left); g.Children.Add(kt); var vt = Ui.Txt(v, 12, null, Th.Fg2); vt.TextWrapping = TextWrapping.Wrap; vt.TextTrimming = TextTrimming.None; g.Children.Add(vt); info.Children.Add(g); }
        Row("Modified", f.LastWriteTime.ToString("d MMM yyyy, HH:mm"));
        Row("Created", f.CreationTime.ToString("d MMM yyyy, HH:mm"));
        Row("Where", Path.GetDirectoryName(f.FullName.TrimEnd('\\')) ?? f.FullName);
        previewBody.Children.Add(info);
        var open = Ui.Pill("Open", () => Open(f), "", true); open.Margin = new Thickness(0, 0, 0, 8);
        previewBody.Children.Add(open);
        previewBody.Children.Add(Ui.Pill("Show in Explorer", () => Shell.Reveal(f.FullName), ""));
    }

    void Open(FileSystemInfo f) { if (f is DirectoryInfo) Navigate(f.FullName); else Shell.Open(f.FullName); }

    // ---------------- actions
    void ItemMenu(UIElement target, FileSystemInfo f)
    {
        var items = new List<(string, string, Action)> { ("", "Open", () => Open(f)) };
        if (f is FileInfo) items.Add(("", "Open with…", () => Shell.OpenWith(f.FullName)));
        else items.Add(("", "Open in Terminal", () => Desktop.I.OpenTerminal(f.FullName, null)));
        items.Add(("", "Show in Windows Explorer", () => Shell.Reveal(f.FullName)));
        if (searchMode) items.Add(("", "Open enclosing folder", () => Navigate(Path.GetDirectoryName(f.FullName))));
        items.Add(Ui.Sep);
        items.Add(("", "Copy", () => CopyToClipboard(f, false)));
        items.Add(("", "Cut", () => CopyToClipboard(f, true)));
        items.Add(("", "Rename", () => BeginRename(f)));
        items.Add(("", "Move to Recycle Bin", () => Delete(f)));
        items.Add(Ui.Sep);
        items.Add(("", "Copy path", () => Clipboard.SetText(f.FullName)));
        items.Add(("", "Properties", () => Shell.Properties(f.FullName)));
        Ui.ShowMenu(target, Ui.Menu(items.ToArray()));
    }

    void BackgroundMenu()
    {
        if (searchMode) return;
        var cm = Ui.Menu(
            ("", "New folder", NewFolder),
            ("", "Paste", Paste),
            ("", "Refresh", Refresh),
            Ui.Sep,
            ("", "Open in Terminal", () => Desktop.I.OpenTerminal(path, null)),
            ("", "Open in Windows Explorer", () => Shell.Run("explorer.exe", "\"" + path + "\"")),
            (listMode ? "" : "", listMode ? "View as grid" : "View as list", ToggleView));
        ((MenuItem)cm.Items[1]).IsEnabled = Clipboard.ContainsFileDropList();
        Ui.ShowMenu(scroll, cm);
    }

    void CopyToClipboard(FileSystemInfo f, bool cut)
    {
        var data = new DataObject();
        var sc = new System.Collections.Specialized.StringCollection { f.FullName };
        data.SetFileDropList(sc);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        Clipboard.SetDataObject(data, true);
    }

    void Paste()
    {
        if (!Clipboard.ContainsFileDropList()) return;
        var files = Clipboard.GetFileDropList().Cast<string>().ToList();
        bool move = false;
        if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms) { var b = new byte[4]; ms.Read(b, 0, 4); move = (BitConverter.ToInt32(b, 0) & 2) != 0; }
        string dest = path;
        var th = new Thread(() =>
        {
            foreach (var src in files)
            {
                try
                {
                    string target = Path.Combine(dest, Path.GetFileName(src.TrimEnd('\\')));
                    if (string.Equals(Path.GetDirectoryName(src), dest, StringComparison.OrdinalIgnoreCase) && !move)
                        target = Path.Combine(dest, Path.GetFileNameWithoutExtension(src) + " - Copy" + Path.GetExtension(src));
                    if (Directory.Exists(src)) { if (move) FileSystem.MoveDirectory(src, target, UIOption.AllDialogs); else FileSystem.CopyDirectory(src, target, UIOption.AllDialogs); }
                    else { if (move) FileSystem.MoveFile(src, target, UIOption.AllDialogs); else FileSystem.CopyFile(src, target, UIOption.AllDialogs); }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Notify.Post("Files", "", "Couldn't paste " + Path.GetFileName(src), ex.Message); }
            }
            Dispatcher.BeginInvoke(new Action(() => { if (move) Clipboard.Clear(); Refresh(); }));
        });
        th.SetApartmentState(ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
    }

    void Delete(FileSystemInfo f) =>
        Dialog.Confirm("Move “" + f.Name + "” to the Recycle Bin?", "You can restore it from the Recycle Bin later.", "Move to Recycle Bin", true, () => { if (Shell.Recycle(f.FullName)) Refresh(); }, "");

    void NewFolder()
    {
        if (searchMode) return;
        var p = Shell.NewFolder(path);
        if (p == null) return;
        Refresh();
        var f = items.FirstOrDefault(i => string.Equals(i.FullName, p, StringComparison.OrdinalIgnoreCase));
        if (f != null) { Select(f); BeginRename(f); }
    }

    void BeginRename(FileSystemInfo f)
    {
        if (!cards.TryGetValue(f, out var card)) return;
        TextBlock label = null; Panel host = null;
        if (card.Child is StackPanel sp) { host = sp; label = sp.Children.OfType<TextBlock>().LastOrDefault(); }
        else if (card.Child is Grid g) { host = g; label = g.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 1); }
        if (label == null) return;
        var tb = new TextBox { Text = f.Name, Style = Styles.Get("Field"), FontSize = label.FontSize, Background = new SolidColorBrush(Color.FromArgb(220, 0x10, 0x14, 0x1E)), Padding = new Thickness(3, 1, 3, 1), Margin = label.Margin, Width = double.IsNaN(label.Width) ? double.NaN : label.Width + 8 };
        if (host is Grid) Grid.SetColumn(tb, 1);
        int idx = host.Children.IndexOf(label);
        host.Children.RemoveAt(idx); host.Children.Insert(idx, tb);
        bool done = false;
        void Finish(bool commit)
        {
            if (done) return; done = true;
            var nn = tb.Text.Trim();
            if (commit && nn.Length > 0 && nn != f.Name) Shell.Rename(f.FullName, nn);
            Refresh();
        }
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Finish(true); } else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); } };
        tb.LostKeyboardFocus += (_, _) => Finish(true);
        tb.Focus();
        int dot = f is FileInfo ? f.Name.LastIndexOf('.') : -1;
        tb.Select(0, dot > 0 ? dot : f.Name.Length);
    }

    void ToggleView()
    {
        listMode = !listMode; Settings.SetBool("filesList", listMode);
        ((TextBlock)viewBtn.Child).Text = listMode ? "" : "";
        Render();
    }

    void TogglePreview()
    {
        showPreview = !showPreview; Settings.SetBool("filesNoPreview", !showPreview);
        preview.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        ShowPreview(sel);
    }

    public bool HandleKey(Key k, ModifierKeys m)
    {
        if (m == ModifierKeys.Control && k == Key.F) { search.Focus(); return true; }
        if (m == (ModifierKeys.Control | ModifierKeys.Shift) && k == Key.N) { NewFolder(); return true; }
        if (m == ModifierKeys.Control && k == Key.V) { Paste(); return true; }
        if (k == Key.Back) { GoBack(); return true; }
        if (k == Key.F5) { Refresh(); return true; }
        if (m == ModifierKeys.Alt && k == Key.Left) { GoBack(); return true; }
        if (m == ModifierKeys.Alt && k == Key.Right) { GoForward(); return true; }
        if (m == ModifierKeys.Alt && k == Key.Up) { GoUp(); return true; }
        if (sel == null) return false;
        if (k == Key.Enter) { Open(sel); return true; }
        if (k == Key.Delete) { Delete(sel); return true; }
        if (k == Key.F2) { BeginRename(sel); return true; }
        if (m == ModifierKeys.Control && k == Key.C) { CopyToClipboard(sel, false); return true; }
        if (m == ModifierKeys.Control && k == Key.X) { CopyToClipboard(sel, true); return true; }
        return false;
    }
}
