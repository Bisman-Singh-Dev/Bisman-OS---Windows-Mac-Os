// Bisman OS 2 - Terminal. Same real cmd.exe engine as Bisman OS 1 (history, cd, stdin, Ctrl+C, Ctrl+L),
// now with tabs, colour-coded output, glass window and per-tab environment variables.
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace BismanOS;

public static class TerminalApp
{
    public static ShellWin Open(string cwd, string run)
    {
        var view = new TerminalView(cwd);
        var w = new ShellWin("bisman:terminal", "Terminal", 940, 600, view, 0.86) { MinW = 480, MinH = 300 };
        view.Win = w;
        w.Toolbar.Children.Add(Ui.IconButton("", () => view.NewTab(null), "New tab (Ctrl+T)", 28, 12));
        w.Closing += view.KillAll;
        w.OnCloseShortcut = view.CloseTabShortcut;
        Desktop.I.Wm.Open(w);
        view.FocusInput();
        if (run != null) view.RunNow(run);
        return w;
    }
}

public class TerminalView : Grid
{
    class Session
    {
        public string Cwd, Cmd;
        public readonly List<string> History = new();
        public int HIdx;
        public Process Proc;
        public DateTime Started;
        public RichTextBox Out;
        public Paragraph Para;
        public readonly Dictionary<string, string> Env = new(StringComparer.OrdinalIgnoreCase);
        public Border Tab;
        public TextBlock TabTitle;
    }

    public ShellWin Win;
    readonly List<Session> sessions = new();
    Session cur;
    readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal };
    readonly Grid outHost = new();
    readonly TextBlock prompt;
    readonly TextBox input;

    static readonly Encoding Oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    static readonly Brush OutBrush = Th.GrassBrush, ErrBrush = Th.B("#FF7B72"), CmdBrush = Th.B("#E9F2E8"), DimBrush = Th.B("#7FA87A"), PathBrush = Th.B("#8FD3FF");

    const string Banner =
@" ____  _                               ___  ____
| __ )(_)___ _ __ ___   __ _ _ __     / _ \/ ___|
|  _ \| / __| '_ ` _ \ / _` | '_ \   | | | \___ \
| |_) | \__ \ | | | | | (_| | | | |  | |_| |___) |
|____/|_|___/_| |_| |_|\__,_|_| |_|   \___/|____/
";
    const string Help =
@" Bisman OS terminal (real Windows cmd.exe)
   Any Windows command works:  dir, ipconfig, ping google.com, python file.py
   cd <folder>      change folder          cls / clear   clear the screen
   set NAME=value   keep a variable in this tab        exit   close this tab
   Up / Down        command history         Ctrl+C        stop a running command
   Ctrl+T           new tab                 Ctrl+Tab      next tab
   Ctrl+L           clear                   Ctrl+W        close tab

";

    public TerminalView(string cwd)
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bg = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 2, 6, 3)), CornerRadius = new CornerRadius(0, 0, 13, 13) };
        SetRowSpan(bg, 3); Children.Add(bg);

        var tabs = new Border { BorderBrush = Th.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0, 10, 0), Child = tabStrip };
        Children.Add(tabs);
        SetRow(outHost, 1); Children.Add(outHost);

        var row = new DockPanel { Margin = new Thickness(18, 6, 18, 14) };
        prompt = new TextBlock { FontFamily = Th.Mono, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Th.GrassBrush, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(prompt, WDock.Left);
        input = new TextBox { Style = Styles.Get("Field"), Foreground = CmdBrush, CaretBrush = Th.GrassBrush, SelectionBrush = Th.GrassBrush, FontFamily = Th.Mono, FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        input.PreviewKeyDown += OnInputKey;
        row.Children.Add(prompt); row.Children.Add(input);
        SetRow(row, 2); Children.Add(row);

        NewTab(cwd);
    }

    public void FocusInput() => Dispatcher.BeginInvoke(new Action(() => { input.Focus(); Keyboard.Focus(input); }), DispatcherPriority.Input);

    public void NewTab(string cwd)
    {
        var s = new Session { Cwd = cwd != null && Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        s.Out = new RichTextBox
        {
            IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = OutBrush,
            FontFamily = Th.Mono, FontSize = 14, Padding = new Thickness(14, 10, 14, 4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SelectionBrush = Th.GrassBrush, CaretBrush = Brushes.Transparent, IsUndoEnabled = false
        };
        var doc = new FlowDocument { PagePadding = new Thickness(0), LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 20, FontFamily = Th.Mono, FontSize = 14 };
        s.Para = new Paragraph { Margin = new Thickness(0) };
        doc.Blocks.Add(s.Para);
        s.Out.Document = doc;
        s.Out.PreviewMouseLeftButtonUp += (_, _) => { if (s.Out.Selection.IsEmpty) FocusInput(); };
        s.Out.Visibility = Visibility.Collapsed;
        outHost.Children.Add(s.Out);

        s.TabTitle = Ui.Txt("", 12, FontWeights.Medium, null, Th.Mono);
        var close = new TextBlock { Text = "", FontFamily = Th.Icons, FontSize = 8, Foreground = Th.Fg3, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Background = Brushes.Transparent };
        var tsp = new StackPanel { Orientation = Orientation.Horizontal };
        tsp.Children.Add(new TextBlock { Text = "", FontFamily = Th.Icons, FontSize = 11, Foreground = Th.GrassBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        tsp.Children.Add(s.TabTitle); tsp.Children.Add(close);
        s.Tab = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5, 10, 5), Margin = new Thickness(0, 5, 6, 5), Child = tsp };
        s.Tab.MouseEnter += (_, _) => close.Opacity = 1; s.Tab.MouseLeave += (_, _) => close.Opacity = 0;
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; CloseTab(s); };
        s.Tab.MouseLeftButtonDown += (_, e) => { e.Handled = true; Select(s); };
        tabStrip.Children.Add(s.Tab);
        sessions.Add(s);
        Append(s, Banner, OutBrush);
        Append(s, " Bisman OS 2 terminal · type 'help' for tips\n\n", DimBrush);
        Select(s);
        UpdateTab(s);
    }

    void Select(Session s)
    {
        cur = s;
        foreach (var x in sessions)
        {
            x.Out.Visibility = x == s ? Visibility.Visible : Visibility.Collapsed;
            x.Tab.Background = x == s ? new SolidColorBrush(Color.FromArgb(40, 0x4C, 0xD9, 0x3A)) : Brushes.Transparent;
            x.TabTitle.Foreground = x == s ? Th.Fg : Th.Fg3;
        }
        UpdatePrompt();
        FocusInput();
    }

    void UpdateTab(Session s)
    {
        string name = Path.GetFileName(s.Cwd.TrimEnd('\\'));
        s.TabTitle.Text = (s.Proc != null ? s.Cmd.Split(' ')[0] : "cmd") + " · " + (name.Length == 0 ? s.Cwd : name);
        if (s == cur && Win != null) Win.Title = "Terminal — " + s.Cwd;
    }

    void CloseTab(Session s)
    {
        Kill(s);
        int i = sessions.IndexOf(s);
        sessions.Remove(s);
        tabStrip.Children.Remove(s.Tab); outHost.Children.Remove(s.Out);
        if (sessions.Count == 0) { Desktop.I.Wm.Close(Win); return; }
        if (cur == s) Select(sessions[Math.Min(i, sessions.Count - 1)]);
    }

    public bool CloseTabShortcut()
    {
        if (sessions.Count <= 1) return false;
        CloseTab(cur);
        return true;
    }

    public void KillAll() { foreach (var s in sessions) Kill(s); }

    string PromptText(Session s) => "bisman ❯ " + s.Cwd + " $";
    void UpdatePrompt() => prompt.Text = cur.Proc == null ? PromptText(cur) : "…";

    static void Append(Session s, string text, Brush color, bool bold = false)
    {
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) s.Para.Inlines.Add(new LineBreak());
            var p = parts[i].TrimEnd('\r');
            if (p.Length > 0) s.Para.Inlines.Add(new Run(p) { Foreground = color, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
        }
        if (s.Para.Inlines.Count > 9000) while (s.Para.Inlines.Count > 6000) s.Para.Inlines.Remove(s.Para.Inlines.FirstInline);
        s.Out.ScrollToEnd();
    }

    void OnInputKey(object sender, KeyEventArgs e)
    {
        var s = cur;
        var m = Keyboard.Modifiers;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string line = input.Text; input.Text = "";
            if (s.Proc != null) { Append(s, line + "\n", CmdBrush); try { s.Proc.StandardInput.WriteLine(line); } catch { } }
            else Run(s, line);
        }
        else if (e.Key == Key.Up && s.History.Count > 0 && s.Proc == null)
        {
            e.Handled = true; s.HIdx = Math.Max(0, s.HIdx - 1); input.Text = s.History[s.HIdx]; input.CaretIndex = input.Text.Length;
        }
        else if (e.Key == Key.Down && s.History.Count > 0 && s.Proc == null)
        {
            e.Handled = true; s.HIdx = Math.Min(s.History.Count, s.HIdx + 1);
            input.Text = s.HIdx < s.History.Count ? s.History[s.HIdx] : ""; input.CaretIndex = input.Text.Length;
        }
        else if (e.Key == Key.C && m == ModifierKeys.Control && s.Proc != null && input.SelectionLength == 0) { e.Handled = true; Kill(s); Append(s, "^C\n", ErrBrush); }
        else if (e.Key == Key.L && m == ModifierKeys.Control) { e.Handled = true; s.Para.Inlines.Clear(); }
        else if (e.Key == Key.T && m == ModifierKeys.Control) { e.Handled = true; NewTab(s.Cwd); }
        else if (e.Key == Key.Tab && m == ModifierKeys.Control) { e.Handled = true; Select(sessions[(sessions.IndexOf(s) + 1) % sessions.Count]); }
    }

    public void RunNow(string line) => Run(cur, line);

    void Run(Session s, string line)
    {
        Append(s, "bisman ❯ ", OutBrush, true);
        Append(s, s.Cwd, PathBrush, true);
        Append(s, " $ ", OutBrush, true);
        Append(s, line + "\n", CmdBrush);
        string t = line.Trim();
        if (t.Length == 0) return;
        s.History.Add(t); s.HIdx = s.History.Count;
        string lower = t.ToLowerInvariant();

        if (lower == "cls" || lower == "clear") { s.Para.Inlines.Clear(); return; }
        if (lower == "exit") { CloseTab(s); return; }
        if (lower == "help" || lower == "bisman") { Append(s, Help, DimBrush); return; }
        if (t.Length == 2 && t[1] == ':') { ChangeDir(s, t + "\\"); return; }
        if (lower == "cd") { Append(s, s.Cwd + "\n\n", OutBrush); return; }
        if (lower.StartsWith("cd ") || lower.StartsWith("cd\\") || lower.StartsWith("cd.") || lower.StartsWith("cd/"))
        {
            string arg = t.Substring(2).Trim();
            if (arg.StartsWith("/d ", StringComparison.OrdinalIgnoreCase)) arg = arg.Substring(3).Trim();
            ChangeDir(s, arg.Trim('"'));
            return;
        }
        var setm = Regex.Match(t, @"^set\s+([^\s=/][^=]*)=(.*)$", RegexOptions.IgnoreCase);
        if (setm.Success) { s.Env[setm.Groups[1].Value.Trim()] = setm.Groups[2].Value; return; }

        var psi = new ProcessStartInfo("cmd.exe", "/d /c " + t)
        {
            WorkingDirectory = s.Cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Oem, StandardErrorEncoding = Oem
        };
        foreach (var kv in s.Env) { if (kv.Value.Length == 0) psi.EnvironmentVariables.Remove(kv.Key); else psi.EnvironmentVariables[kv.Key] = kv.Value; }
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => Append(s, e.Data + "\n", OutBrush))); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Dispatcher.BeginInvoke(new Action(() => Append(s, e.Data + "\n", ErrBrush))); };
        p.Exited += (_, _) =>
        {
            int code = 0;
            try { p.WaitForExit(); code = p.ExitCode; } catch { }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (s.Proc != p) return;
                s.Proc = null;
                Append(s, code != 0 ? "exit code " + code + "\n\n" : "\n", code != 0 ? ErrBrush : OutBrush);
                if (s == cur) UpdatePrompt();
                UpdateTab(s);
                bool away = Desktop.I.Wm.Active != Win || !Desktop.I.IsActive;
                if (away && (DateTime.Now - s.Started).TotalSeconds > 5 && Settings.Bool("notifyTerminal"))
                    Notify.Post("Terminal", "", code == 0 ? "Command finished" : "Command failed (exit code " + code + ")", s.Cmd, () => { if (Win.IsMin) Desktop.I.Wm.Restore(Win); else Desktop.I.Wm.Activate(Win); });
            }));
        };
        try
        {
            p.Start();
            s.Proc = p; s.Started = DateTime.Now; s.Cmd = t;
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            UpdatePrompt(); UpdateTab(s);
        }
        catch (Exception ex) { Append(s, "error: " + ex.Message + "\n\n", ErrBrush); }
    }

    void ChangeDir(Session s, string arg)
    {
        try
        {
            foreach (var kv in s.Env) arg = Regex.Replace(arg, "%" + Regex.Escape(kv.Key) + "%", kv.Value.Replace("$", "$$"), RegexOptions.IgnoreCase);
            string path = Path.GetFullPath(Path.Combine(s.Cwd, Environment.ExpandEnvironmentVariables(arg)));
            if (Directory.Exists(path)) { s.Cwd = path; UpdatePrompt(); UpdateTab(s); }
            else Append(s, "The system cannot find the path specified.\n\n", ErrBrush);
        }
        catch (Exception ex) { Append(s, ex.Message + "\n\n", ErrBrush); }
    }

    void Kill(Session s)
    {
        var p = s.Proc;
        if (p == null) return;
        s.Proc = null;
        try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        if (s == cur) UpdatePrompt();
        UpdateTab(s);
    }
}
