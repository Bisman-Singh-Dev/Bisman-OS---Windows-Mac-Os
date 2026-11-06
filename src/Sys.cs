// Bisman OS 2 - real system information and controls.
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace BismanOS;

public static class Sys
{
    // ---------- battery
    public struct Power { public bool HasBattery, Charging, Plugged; public int Percent, SecondsLeft; }

    public static Power Battery()
    {
        var p = new Power();
        if (!Native.GetSystemPowerStatus(out var s)) return p;
        p.HasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        p.Percent = s.BatteryLifePercent == 255 ? 100 : s.BatteryLifePercent;
        p.Plugged = s.ACLineStatus == 1;
        p.Charging = (s.BatteryFlag & 8) != 0 || (p.Plugged && p.Percent < 100);
        p.SecondsLeft = s.BatteryLifeTime;
        return p;
    }

    public static string BatteryGlyph(Power p)
    {
        int idx = (int)Math.Round(p.Percent / 10.0);
        return ((char)((p.Charging ? 0xEBAB : 0xEBA0) + Math.Max(0, Math.Min(10, idx)))).ToString();
    }

    // ---------- CPU (whole system, from GetSystemTimes deltas)
    static long lastIdle, lastTotal;
    public static double Cpu()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long total = kernel + user;
        long dIdle = idle - lastIdle, dTotal = total - lastTotal;
        lastIdle = idle; lastTotal = total;
        if (dTotal <= 0) return 0;
        return Th.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
    }

    // ---------- memory
    public static (double usedGb, double totalGb, int pct) Memory()
    {
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
        if (!Native.GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
        double total = m.ullTotalPhys / 1073741824.0, avail = m.ullAvailPhys / 1073741824.0;
        return (total - avail, total, (int)m.dwMemoryLoad);
    }

    // ---------- network
    public struct Net { public bool Up; public string Kind, Name, Ip; }

    // Reading network adapters can take tens of milliseconds, so it's refreshed in the background every few seconds.
    static Net netCache; static DateTime netAt = DateTime.MinValue; static int netBusy;
    public static Net Network()
    {
        if (netAt == DateTime.MinValue) { netCache = ReadNetwork(); netAt = DateTime.Now; }
        else if ((DateTime.Now - netAt).TotalSeconds > 3 && Interlocked.Exchange(ref netBusy, 1) == 0)
            Task.Run(() => { try { netCache = ReadNetwork(); netAt = DateTime.Now; } finally { netBusy = 0; } });
        return netCache;
    }

    static Net ReadNetwork()
    {
        var n = new Net { Kind = "Offline", Name = "Not connected" };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                var props = ni.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;
                n.Up = true;
                n.Kind = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                n.Name = n.Kind == "Wi-Fi" && !string.IsNullOrEmpty(Ssid) ? Ssid : ni.Name;
                foreach (var a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { n.Ip = a.Address.ToString(); break; }
                if (n.Kind == "Wi-Fi") break;
            }
        }
        catch { }
        return n;
    }

    public static string Ssid; public static int WifiSignal;
    public static void RefreshWifiAsync()
    {
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd(); p.WaitForExit(4000);
                string ssid = null; int sig = 0;
                foreach (var raw in outp.Split('\n'))
                {
                    var line = raw.Trim();
                    int i = line.IndexOf(':'); if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "SSID") ssid = v;
                    if (k == "Signal") int.TryParse(v.TrimEnd('%'), out sig);
                }
                Ssid = ssid; WifiSignal = sig;
            }
            catch { }
        });
    }

    // ---------- sound (Windows Core Audio)
    public static class Audio
    {
        static IAudioEndpointVolume ep;
        static IAudioEndpointVolume Ep()
        {
            if (ep != null) return ep;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDefaultAudioEndpoint(0, 1, out var dev) != 0) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return null;
                ep = (IAudioEndpointVolume)o;
            }
            catch { ep = null; }
            return ep;
        }
        public static bool Available => Ep() != null;
        public static float Volume { get { var e = Ep(); if (e == null) return 0; e.GetMasterVolumeLevelScalar(out var v); return v; } }
        public static bool Muted { get { var e = Ep(); if (e == null) return false; e.GetMute(out var m); return m; } }
        public static void SetVolume(float v) { var e = Ep(); if (e == null) return; var g = Guid.Empty; e.SetMasterVolumeLevelScalar((float)Th.Clamp(v, 0, 1), ref g); if (v > 0 && Muted) SetMute(false); }
        public static void SetMute(bool m) { var e = Ep(); if (e == null) return; var g = Guid.Empty; e.SetMute(m, ref g); }
        public static void Reset() { ep = null; }
    }

    // ---------- display brightness (WMI, works on built-in laptop panels)
    public static int? Brightness()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static void SetBrightness(int v)
    {
        Task.Run(() =>
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (System.Management.ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Th.Clamp(v, 0, 100) });
            }
            catch { }
        });
    }

    // ---------- Wi-Fi / Bluetooth radios (Windows.Devices.Radios via Windows PowerShell)
    const string RadioScript = @"$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$m = [System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select -First 1
function Await($op,$t){ $task = $m.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(-1) | Out-Null; $task.Result }
[void][Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime]
$null = Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus])
$radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
%SET%
$radios | % { ""$($_.Kind)=$($_.State)"" }";

    // Returns e.g. { "WiFi": true, "Bluetooth": false }. Radios that do not exist are absent.
    public static void Radios(Action<Dictionary<string, bool>> cb, string setKind = null, bool setOn = false)
    {
        string set = setKind == null ? "" :
            "$r = $radios | ? { $_.Kind -eq '" + setKind + "' } | Select -First 1; if ($r) { $null = Await ($r.SetStateAsync('" + (setOn ? "On" : "Off") + "')) ([Windows.Devices.Radios.RadioAccessStatus]) }";
        string script = RadioScript.Replace("%SET%", set);
        Task.Run(() =>
        {
            var res = new Dictionary<string, bool>();
            try
            {
                var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                var outp = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000);
                foreach (var line in outp.Split('\n'))
                {
                    var l = line.Trim(); int i = l.IndexOf('='); if (i < 0) continue;
                    var kind = l.Substring(0, i); if (!res.ContainsKey(kind)) res[kind] = l.Substring(i + 1) == "On";
                }
            }
            catch { }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => cb(res)));
        });
    }

    // ---------- machine info for About / Task Manager
    public static string OsName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string name = k.GetValue("ProductName") as string ?? "Windows";
            string build = k.GetValue("CurrentBuild") as string ?? "";
            string ver = k.GetValue("DisplayVersion") as string ?? "";
            if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
            return name + (ver.Length > 0 ? " " + ver : "") + " (build " + build + ")";
        }
        catch { return Environment.OSVersion.ToString(); }
    }

    public static string CpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (k.GetValue("ProcessorNameString") as string ?? "Processor").Trim();
        }
        catch { return "Processor"; }
    }

    static List<string> gpus;
    public static List<string> Gpus()
    {
        if (gpus != null) return gpus;
        gpus = new List<string>();
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (System.Management.ManagementObject o in s.Get()) gpus.Add(o["Name"] as string);
        }
        catch { }
        return gpus;
    }

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Native.GetTickCount64());

    public static string UptimeText()
    {
        var u = Uptime;
        return u.Days > 0 ? u.Days + "d " + u.Hours + "h" : u.Hours + "h " + u.Minutes + "m";
    }

    // ---------- power actions
    public static void Lock() => Native.LockWorkStation();
    public static void Sleep() => Native.SetSuspendState(false, false, false);
    public static void SignOut() => Native.ExitWindowsEx(0, 0);
    public static void Restart() => Shell.Run("shutdown.exe", "/r /t 0");
    public static void ShutDown() => Shell.Run("shutdown.exe", "/s /t 0");

    // ---------- start with Windows (current user only)
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool StartsWithWindows
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("BismanOS") != null; } catch { return false; } }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (value) k.SetValue("BismanOS", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else k.DeleteValue("BismanOS", false);
            }
            catch (Exception ex) { Notify.Post("Bisman OS", "", "Couldn't change startup setting", ex.Message); }
        }
    }
}

// ---------- Core Audio COM interfaces
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
    [PreserveSig] int GetChannelCount(out uint c);
    [PreserveSig] int SetMasterVolumeLevel(float l, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float l, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float l);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float l);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float l, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float l, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float l);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float l);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
}
