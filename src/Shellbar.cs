// Bisman OS 2 - the dock's own always-on-top window (reserves the bottom of the screen like a taskbar),
// hiding/restoring the Windows taskbar, the "New shortcut" dialog, and the fast-GPU preference.
using Microsoft.Win32;

namespace BismanOS;

static class AppBarApi
{
    [StructLayout(LayoutKind.Sequential)] public struct R { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Data { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public R rc; public IntPtr lParam; }
    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref Data d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public const uint NEW = 0, REMOVE = 1, QUERYPOS = 2, SETPOS = 3, GETSTATE = 4, SETSTATE = 10, BOTTOM = 3;
    public static Data Make(IntPtr h) => new() { cbSize = Marshal.SizeOf(typeof(Data)), hWnd = h };
}

// ================================================================ dock window
public class DockWindow : Window
{
    public readonly Grid Host = new();
    public readonly Canvas Labels = new() { IsHitTestVisible = false };
    IntPtr hwnd; uint cbMsg; bool registered; double scale = 1;

    public DockWindow()
    {
        Title = "Bisman OS Dock";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = Th.Text; Foreground = Th.Fg; UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Content = Host;
        // Covers the 1-pixel strip the main window leaves free (see Desktop), so nothing shows through.
        Host.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Th.Midnight) });
        Host.Children.Add(Labels);

        Host.MouseMove += (_, e) =>
        {
            var d = Desktop.I.Dock; if (d == null) return;
            var p = e.GetPosition(Host);
            d.MouseNear = p.Y > Host.ActualHeight - (d.MaxSize + 34) && Math.Abs(p.X - Host.ActualWidth / 2) < d.TotalBase / 2 + 30;
            d.MouseX = p.X;
        };
        Host.MouseLeave += (_, _) => { if (Desktop.I.Dock != null) Desktop.I.Dock.MouseNear = false; };

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // Never steal focus from the app you're using, and stay out of Alt+Tab.
            AppBarApi.SetWindowLong(hwnd, -20, AppBarApi.GetWindowLong(hwnd, -20) | 0x08000000 | 0x00000080);
            var src = HwndSource.FromHwnd(hwnd);
            scale = src.CompositionTarget.TransformToDevice.M11;
            cbMsg = AppBarApi.RegisterWindowMessage("BismanOS.AppBar");
            src.AddHook(Proc);
            var d = AppBarApi.Make(hwnd); d.uCallbackMessage = cbMsg;
            AppBarApi.SHAppBarMessage(AppBarApi.NEW, ref d);
            registered = true;
            Place();
        };
        Closed += (_, _) => Unregister();
    }

    double Band => (Desktop.I.Dock?.Base ?? 50) + 28;

    // Reserves a strip at the bottom of the screen, so maximised apps stop above the dock.
    public void Place()
    {
        double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight;
        double bottom = H;
        if (registered)
        {
            var d = AppBarApi.Make(hwnd);
            d.uEdge = AppBarApi.BOTTOM;
            int sw = (int)Math.Round(W * scale), sh = (int)Math.Round(H * scale), band = (int)Math.Round(Band * scale);
            d.rc = new AppBarApi.R { Left = 0, Right = sw, Bottom = sh, Top = sh - band };
            AppBarApi.SHAppBarMessage(AppBarApi.QUERYPOS, ref d);
            d.rc.Top = d.rc.Bottom - band;
            AppBarApi.SHAppBarMessage(AppBarApi.SETPOS, ref d);
            bottom = d.rc.Bottom / scale;
        }
        double h = (Desktop.I.Dock?.MaxSize ?? 80) + 90;
        Left = 0; Width = W; Height = h; Top = bottom - h;
    }

    public void Unregister()
    {
        if (!registered) return;
        var d = AppBarApi.Make(hwnd);
        AppBarApi.SHAppBarMessage(AppBarApi.REMOVE, ref d);
        registered = false;
    }

    IntPtr Proc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (cbMsg != 0 && msg == cbMsg && w.ToInt32() == 1) Dispatcher.BeginInvoke(new Action(Place)); // another bar moved
        return IntPtr.Zero;
    }
}

// ================================================================ Windows taskbar
public static class Taskbar
{
    static bool hidden;

    static List<IntPtr> Bars()
    {
        var list = new List<IntPtr>();
        var main = AppBarApi.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        IntPtr s = IntPtr.Zero;
        while ((s = AppBarApi.FindWindowEx(IntPtr.Zero, s, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(s);
        return list;
    }

    static int GetState(IntPtr tray) { var d = AppBarApi.Make(tray); return (int)AppBarApi.SHAppBarMessage(AppBarApi.GETSTATE, ref d).ToUInt32(); }
    static void SetState(IntPtr tray, int state) { var d = AppBarApi.Make(tray); d.lParam = (IntPtr)state; AppBarApi.SHAppBarMessage(AppBarApi.SETSTATE, ref d); }

    // Hides the Windows taskbar while Bisman OS runs. Its original setting is saved and put back on exit.
    public static void Hide()
    {
        if (!Settings.Bool("hideTaskbar")) return;
        var bars = Bars(); if (bars.Count == 0) return;
        try
        {
            if (Settings.Get("taskbarSavedState") == "") Settings.Set("taskbarSavedState", GetState(bars[0]).ToString());
            SetState(bars[0], 1); // auto-hide, so it gives its screen space to the dock
            foreach (var b in bars) AppBarApi.ShowWindow(b, 0);
            hidden = true;
        }
        catch (Exception ex) { Settings.Log("Taskbar hide: " + ex.Message); }
    }

    public static void Restore()
    {
        try
        {
            var bars = Bars();
            foreach (var b in bars) AppBarApi.ShowWindow(b, 5);
            var saved = Settings.Get("taskbarSavedState");
            if (saved != "" && bars.Count > 0 && int.TryParse(saved, out var st)) SetState(bars[0], st);
            Settings.Set("taskbarSavedState", "");
            hidden = false;
        }
        catch (Exception ex) { Settings.Log("Taskbar restore: " + ex.Message); }
    }

    // Windows sometimes brings the taskbar back on its own; hide it again.
    public static void KeepHidden()
    {
        if (!hidden) return;
        foreach (var b in Bars()) if (AppBarApi.IsWindowVisible(b)) AppBarApi.ShowWindow(b, 0);
    }
}

// ================================================================ fast GPU
public static class Gpu
{
    // Same as Windows Settings > Display > Graphics > "High performance" for BismanOS.exe only. Applies on next start.
    public static void Apply()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            if (Settings.Bool("fastGpu")) { if ((k.GetValue(exe) as string) != "GpuPreference=2;") k.SetValue(exe, "GpuPreference=2;"); }
            else k.DeleteValue(exe, false);
        }
        catch (Exception ex) { Settings.Log("GPU preference: " + ex.Message); }
    }
}

// ================================================================ Alt + Windows: add an app to the Dock
// Windows doesn't allow modifier-only hotkeys, so this watches the keyboard with a low-level hook.
public static class DockHotkey
{
    delegate IntPtr LLProc(int n, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KB { public uint vk, scan, flags, time; public IntPtr extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static LLProc proc; static IntPtr hook;
    static bool alt, win, other, fired;

    public static void Install()
    {
        if (hook != IntPtr.Zero) return;
        proc = Hook;
        hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) Settings.Log("Alt+Win hook failed: " + Marshal.GetLastWin32Error());
    }

    public static void Remove() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }

    static IntPtr Hook(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0)
        {
            var k = Marshal.PtrToStructure<KB>(l);
            if ((k.flags & 0x10) == 0) // ignore keys we injected ourselves
            {
                int msg = w.ToInt32();
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                bool isWin = k.vk == 0x5B || k.vk == 0x5C, isAlt = k.vk == 0xA4 || k.vk == 0xA5 || k.vk == 0x12;
                if (down)
                {
                    if (isWin) win = true; else if (isAlt) alt = true; else other = true;
                    if (alt && win && !other && !fired)
                    {
                        fired = true;
                        // A harmless dummy key stops Windows opening the Start menu when the Windows key is let go.
                        keybd_event(0xE8, 0, 0, UIntPtr.Zero); keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                        Application.Current.Dispatcher.BeginInvoke(new Action(DockAdder.Trigger));
                    }
                }
                else if (up)
                {
                    if (isWin) win = false; else if (isAlt) alt = false;
                    if (!alt && !win) { fired = false; other = false; }
                }
            }
        }
        return CallNextHookEx(hook, n, w, l);
    }
}

public static class DockAdder
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

    public static void Trigger()
    {
        try
        {
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out uint pid);
            bool ours = fg == IntPtr.Zero || pid == (uint)Process.GetCurrentProcess().Id;
            var cls = new StringBuilder(64); GetClassName(fg, cls, 64);
            if (ours || cls.ToString() == "Progman" || cls.ToString() == "WorkerW" || cls.ToString() == "Shell_TrayWnd") ShowPicker();
            else PinWindow(fg, pid);
        }
        catch (Exception ex) { Settings.Log("Alt+Win: " + ex); }
    }

    // Pins the app that owns the window you're using.
    static void PinWindow(IntPtr h, uint pid)
    {
        int len = Native.GetWindowTextLength(h); var sb = new StringBuilder(len + 1); Native.GetWindowText(h, sb, sb.Capacity);
        string title = sb.ToString(), exe = Native.ProcessPath(pid);
        string exeName = exe != null ? Path.GetFileName(exe) : null;
        AppEntry app = null;
        if (exeName != null && exeName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            app = Catalog.Apps.FirstOrDefault(a => !a.Internal && (a.Name == title || title.EndsWith("- " + a.Name)));
        else if (exeName != null && exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            app = Catalog.Find("File Explorer");
        else if (exeName != null)
            app = Catalog.Apps.FirstOrDefault(a => a.ExePath != null && Path.GetFileName(a.ExePath).Equals(exeName, StringComparison.OrdinalIgnoreCase));
        if (app == null && exe != null && File.Exists(exe))
        {
            string name = exeName;
            try { var d = FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(d)) name = d.Trim(); } catch { }
            if (name == exeName) name = Path.GetFileNameWithoutExtension(exe);
            app = Shortcuts.Register(exe, name);
            app.ExePath = exe;
            Shortcuts.Remember(exe, name);
        }
        if (app == null) { Notify.Post("Dock", "", "Couldn't add this app", "Press Alt+Windows on the Bisman desktop to choose an app from the list."); return; }
        Pin(app);
    }

    static void Pin(AppEntry a)
    {
        if (Pins.InDock(a.Id)) { Notify.Post("Dock", "", a.Name + " is already in the Dock", "Right-click it in the Dock to remove it."); return; }
        Pins.ToggleDock(a.Id);
        Notify.Post("Dock", "", "Added " + a.Name + " to the Dock", null);
    }

    // On the Bisman desktop: pick any installed app from a searchable list.
    public static void ShowPicker()
    {
        Desktop.I.BringFront();
        var card = new Glass(20, 0.82) { Width = 480, Height = 560 };
        card.Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.6 };
        var g = new Grid { Margin = new Thickness(24, 22, 24, 20) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var t = Ui.Txt("Add an app to the Dock", 18, FontWeights.SemiBold, null, Th.Display);
        g.Children.Add(t);
        var box = new Border { Height = 38, CornerRadius = new CornerRadius(10), Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 14, 0, 10) };
        var bg = new Grid();
        var q = new TextBox { Style = Styles.Get("Field"), FontSize = 14 };
        var hint = Ui.Txt("Search apps", 14, null, Th.Fg3); hint.IsHitTestVisible = false;
        bg.Children.Add(q); bg.Children.Add(hint); box.Child = bg;
        Grid.SetRow(box, 1); g.Children.Add(box);
        var list = new StackPanel();
        var sc = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(sc, 2); g.Children.Add(sc);
        AppEntry first = null;
        void Fill()
        {
            list.Children.Clear(); first = null;
            var s = q.Text.Trim();
            foreach (var a in Catalog.Apps.Where(a => s.Length == 0 || a.Name.Has(s)).OrderBy(a => Pins.InDock(a.Id)).Take(80))
            {
                bool pinned = Pins.InDock(a.Id);
                first ??= pinned ? null : a;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(AppIcon.Make(a, 28));
                var n = Ui.Txt(a.Name, 13.5); Grid.SetColumn(n, 1); row.Children.Add(n);
                var st = Ui.Txt(pinned ? "In Dock" : "Add", 12, FontWeights.SemiBold, pinned ? Th.Fg3 : Th.Accent); Grid.SetColumn(st, 2); row.Children.Add(st);
                var b = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 1, 6, 1) };
                Ui.Hoverable(b);
                var app = a;
                Ui.OnClick(b, () => { if (!Pins.InDock(app.Id)) Pin(app); Fill(); }, false);
                list.Children.Add(b);
            }
        }
        q.TextChanged += (_, _) => { hint.Visibility = q.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
        Fill();
        card.Body = g;
        Dialog.Show(card, () => { if (first != null) Pin(first); });
        q.Dispatcher.BeginInvoke(new Action(() => { q.Focus(); Keyboard.Focus(q); }), DispatcherPriority.Input);
    }
}

// ================================================================ New shortcut
public static class Shortcuts
{
    static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    // Shortcuts that were also added to the Dock come back on every start.
    public static void LoadIntoCatalog()
    {
        foreach (var line in Settings.LoadList("shortcut-apps"))
        {
            var p = line.Split('|');
            if (p.Length == 2 && File.Exists(p[0])) Register(p[0], p[1]);
        }
    }

    public static void Remember(string file, string name)
    {
        var list = Settings.LoadList("shortcut-apps");
        if (!list.Any(l => l.StartsWith(file + "|", StringComparison.OrdinalIgnoreCase))) { list.Add(file + "|" + name); Settings.SaveList("shortcut-apps", list); }
    }

    public static AppEntry Register(string file, string name)
    {
        var id = "sc:" + file;
        if (Catalog.ById.TryGetValue(id, out var existing)) return existing;
        var a = new AppEntry { Id = id, Name = name, Parsing = file, Path = file, ExePath = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : null };
        Catalog.Apps.Add(a); Catalog.ById[id] = a;
        return a;
    }

    public static void Show()
    {
        Desktop.I.BringFront();
        var card = new Glass(20, 0.82) { Width = 460 };
        card.Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.6 };
        var sp = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
        sp.Children.Add(new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(16), Background = Th.AccentSoft, Child = Ui.Glyph("", 24, Th.Accent), HorizontalAlignment = HorizontalAlignment.Left });
        var t = Ui.Txt("New shortcut", 18, FontWeights.SemiBold, null, Th.Display); t.Margin = new Thickness(0, 16, 0, 4);
        sp.Children.Add(t);
        var s = Ui.Txt("Put any app, file, folder or website on your desktop.", 13.5, null, Th.Fg2); s.TextWrapping = TextWrapping.Wrap; s.TextTrimming = TextTrimming.None;
        sp.Children.Add(s);

        var dock = new ToggleButton { Style = Styles.Get("Switch"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        bool AddDock() => dock.IsChecked == true;

        var opts = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        opts.Children.Add(Option("", "An app or file…", () =>
        {
            var dlg = new OpenFileDialog { Title = "Choose an app or file", Filter = "All files|*.*", DereferenceLinks = false };
            if (dlg.ShowDialog(Desktop.I) == true) { Dialog.Close(); CreateForPath(dlg.FileName, AddDock()); }
        }));
        opts.Children.Add(Option("", "A folder…", () =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder", ShowNewFolderButton = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { Dialog.Close(); CreateForPath(dlg.SelectedPath, AddDock()); }
        }));
        sp.Children.Add(opts);

        var urlLabel = Ui.Txt("A website", 13, FontWeights.SemiBold, Th.Fg2); urlLabel.Margin = new Thickness(2, 14, 0, 6);
        sp.Children.Add(urlLabel);
        var urlRow = new DockPanel();
        var box = new Border { Height = 36, CornerRadius = new CornerRadius(10), Background = Th.Inset, BorderBrush = Th.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0) };
        var g = new Grid();
        var url = new TextBox { Style = Styles.Get("Field"), FontSize = 13.5 };
        var hint = Ui.Txt("youtube.com", 13.5, null, Th.Fg3); hint.IsHitTestVisible = false;
        url.TextChanged += (_, _) => hint.Visibility = url.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        g.Children.Add(url); g.Children.Add(hint); box.Child = g;
        void AddUrl() { if (url.Text.Trim().Length == 0) return; Dialog.Close(); CreateForUrl(url.Text.Trim(), AddDock()); }
        var add = Ui.Pill("Add", AddUrl, null, true); add.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(add, WDock.Right); urlRow.Children.Add(add); urlRow.Children.Add(box);
        sp.Children.Add(urlRow);

        var dockRow = new DockPanel { Margin = new Thickness(2, 18, 0, 0) };
        DockPanel.SetDock(dock, WDock.Right); dockRow.Children.Add(dock);
        dockRow.Children.Add(Ui.Txt("Also keep it in the Dock", 13.5, FontWeights.Medium));
        sp.Children.Add(dockRow);

        var cancel = Ui.Pill("Cancel", Dialog.Close); cancel.HorizontalAlignment = HorizontalAlignment.Right; cancel.Margin = new Thickness(0, 20, 0, 0);
        sp.Children.Add(cancel);
        card.Body = sp;
        Dialog.Show(card, AddUrl);
    }

    static FrameworkElement Option(string glyph, string text, Action act)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Th.Card, Child = Ui.Glyph(glyph, 14), HorizontalAlignment = HorizontalAlignment.Left });
        var tx = Ui.Txt(text, 13.5, FontWeights.Medium); Grid.SetColumn(tx, 1); g.Children.Add(tx);
        var ch = Ui.Glyph("", 10, Th.Fg3); Grid.SetColumn(ch, 2); g.Children.Add(ch);
        var b = new Border { Child = g, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Background = Th.Card };
        Ui.Hoverable(b, Th.Card, Th.Press);
        Ui.OnClick(b, act, false);
        return b;
    }

    static string Unique(string dir, string name, string ext)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string p = Path.Combine(dir, name + ext);
        for (int i = 2; File.Exists(p) || Directory.Exists(p); i++) p = Path.Combine(dir, name + " (" + i + ")" + ext);
        return p;
    }

    public static void CreateForPath(string target, bool dock)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = "Drive " + target.TrimEnd('\\', ':');
            string lnk = Unique(DesktopDir, name, ".lnk");
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) File.Copy(target, lnk);
            else
            {
                dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = wsh.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                sc.Save();
            }
            Done(lnk, name, dock);
        }
        catch (Exception ex) { Notify.Post("Shortcuts", "", "Couldn't make that shortcut", ex.Message); }
    }

    public static void CreateForUrl(string url, bool dock)
    {
        try
        {
            if (!url.Contains("://")) url = "https://" + url;
            var u = new Uri(url);
            string name = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            string file = Unique(DesktopDir, name, ".url");
            File.WriteAllText(file, "[InternetShortcut]\r\nURL=" + u.AbsoluteUri + "\r\n");
            Done(file, name, dock);
        }
        catch (Exception ex) { Notify.Post("Shortcuts", "", "That doesn't look like a web address", ex.Message); }
    }

    static void Done(string file, string name, bool dock)
    {
        if (dock)
        {
            var a = Register(file, name);
            var list = Settings.LoadList("shortcut-apps"); list.Add(file + "|" + name); Settings.SaveList("shortcut-apps", list);
            if (!Pins.InDock(a.Id)) Pins.ToggleDock(a.Id);
        }
        Desktop.I.Icons.Reload();
        Notify.Post("Shortcuts", "", "Shortcut added", name + (dock ? " is on your desktop and in the Dock." : " is on your desktop."));
    }
}
