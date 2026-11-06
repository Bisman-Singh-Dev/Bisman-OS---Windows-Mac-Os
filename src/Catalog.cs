// Bisman OS 2 - installed apps (shell:AppsFolder, same source as the Windows Start menu), pins, recents, search.
namespace BismanOS;

public class AppEntry
{
    public string Id, Name, Parsing, ExePath, Glyph, Path;
    public bool Internal;
    public Color C1, C2;
    public Action Open;
    public string Letter => Name.Length > 0 && char.IsLetter(Name[0]) ? char.ToUpperInvariant(Name[0]).ToString() : "#";
}

public static class Catalog
{
    public static readonly List<AppEntry> Apps = new();
    public static readonly Dictionary<string, AppEntry> ById = new(StringComparer.OrdinalIgnoreCase);
    public static bool Ready;
    public static event Action Loaded;

    public static void AddInternal(string id, string name, string glyph, Color c1, Color c2, Action open)
    {
        var a = new AppEntry { Id = id, Name = name, Glyph = glyph, C1 = c1, C2 = c2, Open = open, Internal = true };
        Apps.Add(a); ById[id] = a;
    }

    public static void LoadAsync(Dispatcher ui)
    {
        var th = new Thread(() =>
        {
            var list = Enumerate();
            ui.BeginInvoke(new Action(() =>
            {
                foreach (var a in list) if (!ById.ContainsKey(a.Id)) { Apps.Add(a); ById[a.Id] = a; }
                Apps.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
                Ready = true;
                Loaded?.Invoke();
            }));
        }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
    }

    static List<AppEntry> Enumerate()
    {
        var res = new List<AppEntry>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name, path = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                if (name.Has("uninstall")) continue;
                var e = new AppEntry { Id = "app:" + path, Name = name, Parsing = "shell:AppsFolder\\" + path };
                try { Resolve(e, path, wsh); } catch { }
                res.Add(e);
            }
        }
        catch (Exception ex) { Settings.Log("App catalog: " + ex.Message); }
        return res;
    }

    // Turns a "{KnownFolder}\\app.exe" or .lnk path into a real file path so we can match running windows and open file locations.
    static void Resolve(AppEntry e, string path, dynamic wsh)
    {
        string full = path;
        if (path.StartsWith("{"))
        {
            int i = path.IndexOf('}');
            if (i > 0)
            {
                var kf = Native.KnownFolder(new Guid(path.Substring(1, i - 1)));
                if (kf != null) full = kf + path.Substring(i + 1);
            }
        }
        if (full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
        {
            e.Path = full;
            string target = wsh.CreateShortcut(full).TargetPath;
            if (!string.IsNullOrEmpty(target) && File.Exists(target)) e.ExePath = target;
        }
        else if (File.Exists(full)) { e.Path = full; e.ExePath = full; }
    }

    public static AppEntry Find(string name) =>
        Apps.FirstOrDefault(a => !a.Internal && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public static void Launch(AppEntry a)
    {
        if (a == null) return;
        Recent.Touch(a.Id);
        if (a.Internal) a.Open?.Invoke();
        else Shell.Run("explorer.exe", "\"" + a.Parsing + "\"");
    }
}

public static class Pins
{
    public static List<string> Dock = new(), Start = new();
    public static event Action Changed;

    static readonly string[] DockDefaults = { "Google Chrome", "Microsoft Edge", "Visual Studio Code", "Spotify", "Discord" };
    static readonly string[] StartDefaults =
    {
        "Microsoft Edge", "Google Chrome", "Word", "Excel", "PowerPoint", "Outlook", "Visual Studio Code", "Visual Studio 2022",
        "Paint", "Notepad", "Photos", "Spotify", "Discord", "Steam", "Microsoft Store", "Snipping Tool", "Camera", "Clock", "WhatsApp", "File Explorer"
    };

    public static void Load()
    {
        Dock = Settings.LoadList("dock-pins");
        Start = Settings.LoadList("start-pins");
    }

    // Defaults are only chosen once, from apps that are really installed.
    public static void EnsureDefaults()
    {
        if (!Settings.HasList("dock-pins"))
        {
            Dock = new List<string> { "bisman:files", "bisman:terminal" };
            bool browser = false;
            foreach (var n in DockDefaults)
            {
                var a = Catalog.Find(n); if (a == null) continue;
                if (n is "Google Chrome" or "Microsoft Edge") { if (browser) continue; browser = true; }
                Dock.Add(a.Id);
            }
            Dock.AddRange(new[] { "bisman:settings", "bisman:calculator", "bisman:taskmanager" });
            Settings.SaveList("dock-pins", Dock);
        }
        if (!Settings.HasList("start-pins"))
        {
            Start = new List<string> { "bisman:files", "bisman:terminal", "bisman:settings", "bisman:calculator", "bisman:taskmanager" };
            foreach (var n in StartDefaults) { var a = Catalog.Find(n); if (a != null) Start.Add(a.Id); }
            Settings.SaveList("start-pins", Start);
        }
        Changed?.Invoke();
    }

    public static bool InDock(string id) => Dock.Contains(id);
    public static bool InStart(string id) => Start.Contains(id);
    public static void ToggleDock(string id) { if (!Dock.Remove(id)) Dock.Add(id); Settings.SaveList("dock-pins", Dock); Changed?.Invoke(); }
    public static void StartFront(string id) { Start.Remove(id); Start.Insert(0, id); Settings.SaveList("start-pins", Start); Changed?.Invoke(); }
    public static void ToggleStart(string id) { if (!Start.Remove(id)) Start.Add(id); Settings.SaveList("start-pins", Start); Changed?.Invoke(); }
}

public static class Recent
{
    public static List<string> Apps = Settings.LoadList("recent-apps");

    public static void Touch(string id)
    {
        Apps.Remove(id); Apps.Insert(0, id);
        if (Apps.Count > 16) Apps.RemoveAt(Apps.Count - 1);
        Settings.SaveList("recent-apps", Apps);
    }

    // Recently opened documents, straight from Windows' own Recent folder.
    public static List<(string name, string path, DateTime time)> Files(int max)
    {
        var res = new List<(string, string, DateTime)>();
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(max * 2))
            {
                var name = Path.GetFileNameWithoutExtension(f.Name);
                if (name.Length == 0) continue;
                res.Add((name, f.FullName, f.LastWriteTime));
                if (res.Count >= max) break;
            }
        }
        catch { }
        return res;
    }
}

// ---------------------------------------------------------------- system-wide search
public class Result
{
    public string Kind, Title, Sub, IconPath, Glyph;
    public bool Thumb;
    public AppEntry App;
    public Action Run, Reveal;
}

public static class Search
{
    public static readonly (string title, string uri, string glyph)[] WindowsSettings =
    {
        ("Display", "ms-settings:display", ""), ("Sound", "ms-settings:sound", ""),
        ("Bluetooth & devices", "ms-settings:bluetooth", ""), ("Wi-Fi", "ms-settings:network-wifi", ""),
        ("Network & internet", "ms-settings:network", ""), ("Personalisation", "ms-settings:personalization", ""),
        ("Installed apps", "ms-settings:appsfeatures", ""), ("Accounts", "ms-settings:yourinfo", ""),
        ("Time & language", "ms-settings:dateandtime", ""), ("Power & battery", "ms-settings:powersleep", ""),
        ("Privacy & security", "ms-settings:privacy", ""), ("Windows Update", "ms-settings:windowsupdate", ""),
        ("Storage", "ms-settings:storagesense", ""), ("Notifications (Windows)", "ms-settings:notifications", ""),
        ("Mouse & touchpad", "ms-settings:mousetouchpad", ""), ("Typing & keyboard", "ms-settings:typing", ""),
        ("Default apps", "ms-settings:defaultapps", ""), ("Multitasking", "ms-settings:multitasking", ""),
    };

    // Bisman's own settings pages, searchable too.
    public static readonly (string title, string page, string glyph)[] BismanSettings =
    {
        ("Accent colour", "Appearance", ""), ("Glass blur & transparency", "Appearance", ""), ("Reduce motion", "Appearance", ""),
        ("Wallpaper", "Wallpaper", ""), ("Live wallpaper", "Wallpaper", ""),
        ("Dock size & magnification", "Desktop & Dock", ""), ("Desktop icons & widgets", "Desktop & Dock", ""), ("Clock format", "Desktop & Dock", ""),
        ("Do not disturb", "Notifications", ""), ("Start Bisman OS when I sign in", "Startup", ""),
        ("Keyboard shortcuts", "Keyboard Shortcuts", ""), ("About Bisman OS", "About", ""),
    };

    static int Score(string name, string q)
    {
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        foreach (var w in name.Split(' ', '-', '_', '.')) if (w.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 2;
        return name.Has(q) ? 1 : 0;
    }

    public static List<Result> Instant(string q)
    {
        var res = new List<Result>();
        q = q.Trim();
        if (q.Length == 0) return res;

        foreach (var a in Catalog.Apps.Select(a => (a, s: Score(a.Name, q))).Where(x => x.s > 0).OrderByDescending(x => x.s).ThenBy(x => x.a.Name.Length).Take(8).Select(x => x.a))
            res.Add(new Result
            {
                Kind = "Apps", Title = a.Name, Sub = a.Internal ? "Bisman OS app" : "App", App = a, IconPath = a.Parsing, Glyph = a.Glyph,
                Run = () => Catalog.Launch(a), Reveal = a.Path != null ? () => Shell.Reveal(a.Path) : null
            });

        foreach (var s in BismanSettings.Where(s => s.title.Has(q) || s.page.Has(q)).Take(4))
        {
            var page = s.page;
            res.Add(new Result { Kind = "Settings", Title = s.title, Sub = "Bisman OS settings · " + s.page, Glyph = s.glyph, Run = () => Desktop.I.OpenSettings(page) });
        }
        foreach (var s in WindowsSettings.Where(s => s.title.Has(q)).Take(4))
        {
            var uri = s.uri;
            res.Add(new Result { Kind = "Settings", Title = s.title, Sub = "Windows settings", Glyph = s.glyph, Run = () => Shell.Run(uri) });
        }

        var cmd = q;
        res.Add(new Result { Kind = "Commands", Title = "Run “" + q + "” in Terminal", Sub = "Opens the Bisman terminal and runs this command", Glyph = "", Run = () => Desktop.I.OpenTerminal(null, cmd) });
        res.Add(new Result { Kind = "Commands", Title = "Search the web for “" + q + "”", Sub = "Opens your default browser", Glyph = "", Run = () => Shell.Web("https://www.google.com/search?q=" + Uri.EscapeDataString(cmd)) });
        return res;
    }

    // Files and folders through the Windows Search index (the same index File Explorer uses).
    public static List<Result> Files(string q, string scope, int max, CancellationToken ct)
    {
        var res = new List<Result>();
        q = new string(q.Where(c => c != '\'' && c != '%' && c != '[' && c != ']').ToArray()).Trim();
        if (q.Length < 2) return res;
        scope ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            using var conn = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\"");
            conn.Open();
            var sql = "SELECT TOP " + max + " System.ItemPathDisplay, System.ItemNameDisplay, System.ItemTypeText, System.DateModified FROM SYSTEMINDEX " +
                      "WHERE SCOPE='file:" + scope.Replace('\\', '/') + "' AND System.ItemNameDisplay LIKE '%" + q + "%' ORDER BY System.DateModified DESC";
            using var cmd = new System.Data.OleDb.OleDbCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read() && !ct.IsCancellationRequested)
            {
                var path = r[0] as string; if (path == null) continue;
                bool dir = Directory.Exists(path);
                var p = path;
                res.Add(new Result
                {
                    Kind = dir ? "Folders" : "Documents", Title = r[1] as string ?? Path.GetFileName(path), Sub = Path.GetDirectoryName(path),
                    IconPath = path, Thumb = !dir,
                    Run = dir ? () => Desktop.I.OpenFiles(p) : () => Shell.Open(p), Reveal = () => Shell.Reveal(p)
                });
            }
        }
        catch (Exception ex) { Settings.Log("Search index: " + ex.Message); }
        return res;
    }
}
