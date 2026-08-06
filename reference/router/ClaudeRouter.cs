// ClaudeRouter - receives claude:// links and hands them to the right Claude Desktop
// instance. Compiled locally at install time so that powershell.exe never appears in the
// protocol-handler chain.
//
// Deliberately narrow: it reads a fixed config, inspects windows, and starts Claude.exe.
// It cannot be repurposed to run arbitrary code the way a .ps1 handler can.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public static class ClaudeRouter
{
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] static extern bool   IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int    GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] static extern int    GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint GW_HWNDNEXT = 2;
    const uint MB_ICONERROR = 0x00000010;
    const uint MB_YESNO = 0x00000004;
    const uint MB_ICONQUESTION = 0x00000020;
    const int  IDYES = 6;
    const uint IMAGE_ICON = 1;
    const uint LR_LOADFROMFILE = 0x0010;
    const uint LR_SHARED = 0x8000;
    const uint WM_SETICON = 0x0080;

    static string Home;
    static string LogPath;

    sealed class Inst
    {
        public string Name;
        public string DataDir;
        public bool   Tint;
    }

    static void Log(string m)
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                File.Delete(LogPath);
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + "\r\n");
        }
        catch { }
    }

    static string Norm(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd('\\'); }
        catch { return p.TrimEnd('\\'); }
    }

    static void Fail(string msg)
    {
        Log("ERROR: " + msg);
        MessageBoxW(IntPtr.Zero, msg, "Claude router", MB_ICONERROR);
    }

    // ---------------------------------------------------------------- config --
    static string DefaultDir = "";

    static List<Inst> LoadInstances()
    {
        var list = new List<Inst>();
        string path = Path.Combine(Home, "instances.tsv");
        if (!File.Exists(path)) { Log("instances.tsv not found"); return list; }
        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;
                if (parts[0] == "DEFAULT") { DefaultDir = Norm(parts[1]); continue; }
                bool tint = parts.Length > 2 && parts[2] == "1";
                list.Add(new Inst { Name = parts[0], DataDir = Norm(parts[1]), Tint = tint });
            }
        }
        catch (Exception ex) { Log("could not read instances.tsv: " + ex.Message); }
        return list;
    }

    // ------------------------------------------------------------ locate exe --
    static string ResolveExe()
    {
        // 1. the path the installer recorded, refreshed by the icon watcher
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\ClaudeRouter"))
            {
                if (k != null)
                {
                    string v = k.GetValue("ClaudeExe") as string;
                    if (!string.IsNullOrEmpty(v) && File.Exists(v)) return v;
                }
            }
        }
        catch { }

        // 2. standalone / Squirrel install locations
        string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] roots =
        {
            Path.Combine(lad, "AnthropicClaude"),
            Path.Combine(lad, @"Programs\AnthropicClaude"),
            Path.Combine(lad, @"Programs\Claude"),
            Path.Combine(lad, @"Programs\claude-desktop"),
            Path.Combine(pf,  "Claude")
        };
        foreach (string root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                string stub = Path.Combine(root, "Claude.exe");
                if (File.Exists(stub)) return stub;
                string[] versioned = Directory.GetDirectories(root, "app-*");
                Array.Sort(versioned);
                for (int i = versioned.Length - 1; i >= 0; i--)
                {
                    string e = Path.Combine(versioned[i], "Claude.exe");
                    if (File.Exists(e)) return e;
                }
            }
            catch { }
        }

        // 3. last resort: ask the helper script to look it up (WindowsApps cannot be
        //    enumerated directly, so an MSIX install after an update needs this)
        try
        {
            string helper = Path.Combine(Home, "resolve-claude.ps1");
            if (File.Exists(helper))
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -File \"" + helper + "\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    foreach (string line in outp.Split('\n'))
                    {
                        string c = line.Trim();
                        if (c.Length > 0 && File.Exists(c))
                        {
                            try
                            {
                                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\ClaudeRouter"))
                                    if (k != null) k.SetValue("ClaudeExe", c);
                            }
                            catch { }
                            return c;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Log("resolver helper failed: " + ex.Message); }

        return null;
    }

    // ------------------------------------------------------- running instances --
    static string ExtractDataDir(string cl)
    {
        const string key = "--user-data-dir=";
        int i = cl.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return DefaultDir;
        int s = i + key.Length;
        if (s < cl.Length && cl[s] == '"')
        {
            int e = cl.IndexOf('"', s + 1);
            if (e > s) return Norm(cl.Substring(s + 1, e - s - 1));
            return DefaultDir;
        }
        int sp = cl.IndexOf(' ', s);
        string v = sp < 0 ? cl.Substring(s) : cl.Substring(s, sp - s);
        return Norm(v.Trim('"'));
    }

    static Dictionary<int, string> MapRunning(List<Inst> insts)
    {
        var map = new Dictionary<int, string>();
        try
        {
            string q = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%laude%'";
            using (var searcher = new ManagementObjectSearcher(q))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    object clo = mo["CommandLine"];
                    if (clo == null) continue;
                    string cl = clo.ToString();
                    if (cl.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string dd = ExtractDataDir(cl);
                    foreach (Inst inst in insts)
                    {
                        if (string.Equals(dd, inst.DataDir, StringComparison.OrdinalIgnoreCase))
                        {
                            try { map[Convert.ToInt32(mo["ProcessId"])] = inst.Name; }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Log("process scan failed: " + ex.Message); }
        return map;
    }

    // The Claude window highest in the Z-order is the one the user was in most recently,
    // and that ordering survives the browser taking focus.
    static string TopMostInstance(Dictionary<int, string> running)
    {
        IntPtr h = GetTopWindow(IntPtr.Zero);
        while (h != IntPtr.Zero)
        {
            if (IsWindowVisible(h) && GetWindowTextLength(h) > 0)
            {
                int pid;
                GetWindowThreadProcessId(h, out pid);
                string name;
                if (running.TryGetValue(pid, out name)) return name;
            }
            h = GetWindow(h, GW_HWNDNEXT);
        }
        return null;
    }

    // ------------------------------------------------------------------ launch --
    static void Launch(string exe, string dataDir, string url)
    {
        string args;
        if (string.IsNullOrEmpty(dataDir))
            args = "\"" + url + "\"";
        else if (string.IsNullOrEmpty(url))
            args = "--user-data-dir=\"" + dataDir + "\"";
        else
            args = "--user-data-dir=\"" + dataDir + "\" \"" + url + "\"";
        var psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false;
        Process.Start(psi);
    }

    // ------------------------------------------------------------ taskbar icons --
    // WM_SETICON only sticks if it lands before Windows builds the taskbar button, so the
    // launch path starts applying immediately and keeps going while Claude comes up.
    sealed class IconPair { public IntPtr Small; public IntPtr Big; }

    static Dictionary<string, IconPair> LoadIcons(List<Inst> insts)
    {
        var map = new Dictionary<string, IconPair>(StringComparer.OrdinalIgnoreCase);
        foreach (Inst inst in insts)
        {
            if (!inst.Tint) continue;
            string ico = Path.Combine(Home, "taskbar-" + inst.Name + ".ico");
            if (!File.Exists(ico)) { Log("no icon file for " + inst.Name); continue; }
            IntPtr sm = LoadImage(IntPtr.Zero, ico, IMAGE_ICON, 16, 16, LR_LOADFROMFILE | LR_SHARED);
            IntPtr bg = LoadImage(IntPtr.Zero, ico, IMAGE_ICON, 32, 32, LR_LOADFROMFILE | LR_SHARED);
            if (sm == IntPtr.Zero || bg == IntPtr.Zero) { Log("could not load " + ico); continue; }
            map[inst.DataDir] = new IconPair { Small = sm, Big = bg };
        }
        return map;
    }

    static void ApplyIcons(List<Inst> insts, int seconds)
    {
        Dictionary<string, IconPair> icons = LoadIcons(insts);
        if (icons.Count == 0) { Log("no icons to apply"); return; }

        var byName = new Dictionary<string, string>();
        foreach (Inst i in insts) byName[i.Name] = i.DataDir;

        var touched = new Dictionary<long, bool>();
        DateTime deadline = seconds <= 0 ? DateTime.MaxValue : DateTime.Now.AddSeconds(seconds);
        int sleepMs = seconds <= 0 ? 2500 : 600;

        while (DateTime.Now < deadline)
        {
            Dictionary<int, string> running = MapRunning(insts);
            if (running.Count > 0)
            {
                IntPtr h = GetTopWindow(IntPtr.Zero);
                while (h != IntPtr.Zero)
                {
                    if (IsWindowVisible(h) && GetWindowTextLength(h) > 0)
                    {
                        int pid;
                        GetWindowThreadProcessId(h, out pid);
                        string name;
                        if (running.TryGetValue(pid, out name))
                        {
                            string dd;
                            IconPair ip;
                            if (byName.TryGetValue(name, out dd) && icons.TryGetValue(dd, out ip))
                            {
                                SendMessage(h, WM_SETICON, (IntPtr)0, ip.Small);
                                SendMessage(h, WM_SETICON, (IntPtr)1, ip.Big);
                                long key = h.ToInt64();
                                if (!touched.ContainsKey(key))
                                {
                                    touched[key] = true;
                                    Log("icon '" + name + "' applied to hwnd " + key + " (pid " + pid + ")");
                                }
                            }
                        }
                    }
                    h = GetWindow(h, GW_HWNDNEXT);
                }
            }
            System.Threading.Thread.Sleep(sleepMs);
        }
    }

    // ----------------------------------------------------------- launch an instance --
    static int DoLaunch(string name)
    {
        List<Inst> insts = LoadInstances();
        Inst chosen = null;
        foreach (Inst i in insts) if (string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) { chosen = i; break; }
        if (chosen == null) { Fail("No instance called '" + name + "' is configured."); return 1; }

        string exe = ResolveExe();
        if (exe == null) { Fail("Claude Desktop could not be located."); return 1; }

        // placeholder so the Cowork VM bundle does not error on a fresh profile
        try
        {
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string bundle = Path.Combine(lad, name + @"\vm_bundles\claudevm.bundle");
            string vhdx = Path.Combine(bundle, "rootfs.vhdx");
            if (!File.Exists(vhdx))
            {
                Directory.CreateDirectory(bundle);
                using (File.Create(vhdx)) { }
                Log("created VM bundle placeholder");
            }
        }
        catch (Exception ex) { Log("vm bundle guard: " + ex.Message); }

        Log("launching '" + name + "' -> " + chosen.DataDir);
        try { Launch(exe, chosen.DataDir, null); }
        catch (Exception ex) { Fail("Could not start Claude.\n\n" + ex.Message); return 1; }

        ApplyIcons(insts, 60);
        return 0;
    }

    static int DoWatch(int seconds)
    {
        Log(seconds <= 0 ? "background icon watcher started" : "icon refresh for " + seconds + "s");
        ApplyIcons(LoadInstances(), seconds);
        return 0;
    }

    // -------------------------------------------------------------------- main --
    [STAThread]
    static int Main(string[] args)
    {
        Home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeRouter");
        LogPath = Path.Combine(Home, "router.log");

        if (args.Length > 0 && args[0] == "--launch")
            return DoLaunch(args.Length > 1 ? args[1] : "");
        if (args.Length > 0 && args[0] == "--watch")
        {
            int secs = 0;
            if (args.Length > 1) int.TryParse(args[1], out secs);
            return DoWatch(secs);
        }

        string url = args.Length > 0 ? args[0] : "";
        int q = url.IndexOf('?');
        Log("url: " + (q >= 0 ? url.Substring(0, q) : url));   // never log the query string
        if (url.Length == 0) { Log("no url supplied"); return 1; }

        string exe = ResolveExe();
        if (exe == null) { Fail("Claude Desktop could not be located."); return 1; }

        List<Inst> insts = LoadInstances();
        if (insts.Count == 0)
        {
            Log("no instances configured - handing to the default profile");
            try { Launch(exe, null, url); } catch (Exception ex) { Fail("Could not start Claude.\n\n" + ex.Message); return 1; }
            return 0;
        }

        Dictionary<int, string> running = MapRunning(insts);
        var names = new List<string>();
        foreach (KeyValuePair<int, string> kv in running) names.Add(kv.Key + "=" + kv.Value);
        Log("running: " + string.Join(", ", names.ToArray()));

        string target = null;
        string how = "";

        if (running.Count > 1)
        {
            target = TopMostInstance(running);
            if (target != null) how = "z-order (most recently used window)";
        }

        if (target == null && running.Count == 1)
        {
            foreach (KeyValuePair<int, string> kv in running) { target = kv.Value; break; }
            how = "only one running";
        }

        if (target == null && insts.Count == 2)
        {
            string text = "A Claude sign-in link arrived.\n\n" +
                          "Yes  =  " + insts[0].Name + "\n" +
                          "No   =  " + insts[1].Name;
            int r = MessageBoxW(IntPtr.Zero, text, "Which Claude?", MB_YESNO | MB_ICONQUESTION);
            target = (r == IDYES) ? insts[0].Name : insts[1].Name;
            how = "asked";
        }

        if (target == null) { target = insts[0].Name; how = "fallback to the first instance"; }

        Inst chosen = null;
        foreach (Inst inst in insts) if (inst.Name == target) { chosen = inst; break; }
        if (chosen == null) { Fail("Unknown instance '" + target + "'."); return 1; }

        Log("-> " + chosen.Name + "  (via " + how + ")");
        try { Launch(exe, chosen.DataDir, url); }
        catch (Exception ex) { Fail("Could not hand the link to " + chosen.Name + ".\n\n" + ex.Message); return 1; }
        return 0;
    }
}
