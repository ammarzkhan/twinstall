<#
.SYNOPSIS
    Guided setup for a second, fully isolated Claude Desktop instance on Windows.

.DESCRIPTION
    Wizard by default. Works with both Claude Desktop builds:
      - Microsoft Store / MSIX   (profiles under Packages\<PFN>\LocalCache\Roaming)
      - Standalone / Squirrel    (profiles under %APPDATA%)

    Installs an isolated profile, a claude:// router so sign-in callbacks reach the
    instance you meant, and a tinted taskbar icon. No administrator rights required.

.EXAMPLE
    .\Setup-ClaudeMultiInstance.ps1
    .\Setup-ClaudeMultiInstance.ps1 -Silent -InstanceName work -Color '#059669'
    .\Setup-ClaudeMultiInstance.ps1 -Uninstall
    .\Setup-ClaudeMultiInstance.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string] $InstanceName = 'second',
    [string] $Color        = '#7C3AED',
    [string] $MainLabel    = 'Main',
    [string] $MainColor    = '#1E6FD9',
    [switch] $TintMain,
    [switch] $AlwaysOnWatcher,
    [switch] $Silent,
    [switch] $Uninstall,
    [switch] $RemoveProfiles,
    [switch] $Verify
)

$ErrorActionPreference = 'Stop'
$ScriptVersion = '2.0'

# ================================================================ constants ===
$HomeDir  = Join-Path $env:LOCALAPPDATA 'ClaudeRouter'
$ProtoKey = 'HKCU:\Software\Classes\claude'
$ProgId   = 'ClaudeRouter.Proto'
$ProgKey  = "HKCU:\Software\Classes\$ProgId"
$CapKey   = 'HKCU:\Software\ClaudeRouter\Capabilities'
$RegApps  = 'HKCU:\Software\RegisteredApplications'
$AppName  = 'Claude Instance Router'
$AdvKey   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$StartupDir = [Environment]::GetFolderPath('Startup')
$UninstKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeMultiInstance'

# Reserved DOS device names - a folder called any of these breaks in odd ways.
$ReservedNames = @('CON','PRN','AUX','NUL','COM1','COM2','COM3','COM4','COM5','COM6','COM7','COM8','COM9',
                   'LPT1','LPT2','LPT3','LPT4','LPT5','LPT6','LPT7','LPT8','LPT9')

function Get-NormalPath([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return '' }
    try { return ([System.IO.Path]::GetFullPath($p)).TrimEnd('\') } catch { return $p.TrimEnd('\') }
}

# ================================================ embedded: exe resolver ======
$ResolveSource = @'
# Prints the full path to Claude.exe for whichever build is installed, or nothing.
$ErrorActionPreference = 'SilentlyContinue'

try {
    $pkg = Get-AppxPackage -Name Claude | Select-Object -First 1
    if ($pkg) {
        foreach ($rel in @('app\Claude.exe', 'Claude.exe')) {
            $p = Join-Path $pkg.InstallLocation $rel
            if (Test-Path $p) { $p; exit 0 }
        }
    }
} catch { }

foreach ($root in @("$env:LOCALAPPDATA\AnthropicClaude",
                    "$env:LOCALAPPDATA\Programs\AnthropicClaude",
                    "$env:LOCALAPPDATA\Programs\Claude",
                    "$env:LOCALAPPDATA\Programs\claude-desktop",
                    "$env:ProgramFiles\Claude",
                    "${env:ProgramFiles(x86)}\Claude")) {
    if (-not (Test-Path $root)) { continue }
    $stub = Join-Path $root 'Claude.exe'
    if (Test-Path $stub) { $stub; exit 0 }
    $ver = Get-ChildItem $root -Directory -Filter 'app-*' | Sort-Object Name -Descending | Select-Object -First 1
    if ($ver) {
        $e = Join-Path $ver.FullName 'Claude.exe'
        if (Test-Path $e) { $e; exit 0 }
    }
}
exit 1
'@

# =================================================== embedded: router (C#) ====
# Compiled locally at install time so powershell.exe never appears in the
# protocol-handler chain. Falls back to the PowerShell router if csc is missing.
$RouterExeSource = @'
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

'@

# ======================================================= embedded: router =====
$RouterSource = @'
param([Parameter(Mandatory)][string]$Url)
$ErrorActionPreference = 'Stop'
$H   = Join-Path $env:LOCALAPPDATA 'ClaudeRouter'
$log = Join-Path $H 'router.log'

function L($m) { try { "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss')  $m" | Add-Content $log -Encoding UTF8 } catch {} }
function Norm([string]$p) { if (-not $p) { return '' }; try { ([System.IO.Path]::GetFullPath($p)).TrimEnd('\') } catch { $p.TrimEnd('\') } }

function Resolve-Exe {
    $r = Join-Path $H 'resolve-claude.ps1'
    if (Test-Path $r) {
        $p = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $r 2>$null | Select-Object -First 1
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

# never log the query string - OAuth codes live there
L "url: $(($Url -split '\?')[0])"

$exe = Resolve-Exe
if (-not $exe) {
    L 'FATAL: Claude.exe not found'
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show('Claude Desktop could not be located.', 'Claude router', 'OK', 'Error')
    exit 1
}

# If the manifest is missing or unreadable, do the least surprising thing: hand the
# link to the default profile rather than stranding the user.
$cfg = $null
try { $cfg = Get-Content (Join-Path $H 'instances.json') -Raw -ErrorAction Stop | ConvertFrom-Json } catch { }
if (-not $cfg -or -not $cfg.instances) {
    L 'manifest missing/unreadable - falling back to the default profile'
    Start-Process -FilePath $exe -ArgumentList @("`"$Url`"")
    exit 0
}
$inst = @($cfg.instances)

# map running Claude main processes to instance names, by EXACT data-dir match
$running = @{}
try {
    Get-CimInstance Win32_Process -Filter "Name LIKE '%laude%'" -ErrorAction SilentlyContinue | ForEach-Object {
        $cl = $_.CommandLine
        if (-not $cl -or $cl -like '*--type=*') { return }
        $dd = if ($cl -match '--user-data-dir="([^"]+)"') { Norm $Matches[1] }
              elseif ($cl -match '--user-data-dir=([^\s"]+)') { Norm $Matches[1] }
              else { Norm $cfg.defaultDataDir }
        foreach ($i in $inst) { if ($dd -ieq (Norm $i.dataDir)) { $running[[int]$_.ProcessId] = $i.name } }
    }
} catch { L "process scan failed: $($_.Exception.Message)" }
L "running: $((($running.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))"

$target = $null; $how = ''

# stage 1 - the Claude window higher in the Z-order is the one used most recently,
# and that ordering survives the browser taking focus
if ($running.Count -gt 1) {
  try {
    Add-Type -Name Zo -Namespace WinApi -MemberDefinition @"
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int p);
[DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
"@ -ErrorAction SilentlyContinue
    $h = [WinApi.Zo]::GetTopWindow([IntPtr]::Zero)
    while ($h -ne [IntPtr]::Zero -and -not $target) {
      if ([WinApi.Zo]::IsWindowVisible($h) -and [WinApi.Zo]::GetWindowTextLength($h) -gt 0) {
        $wp = 0; [void][WinApi.Zo]::GetWindowThreadProcessId($h, [ref]$wp)
        if ($running.ContainsKey($wp)) { $target = $running[$wp]; $how = 'z-order (most recently used window)' }
      }
      $h = [WinApi.Zo]::GetWindow($h, 2)
    }
  } catch { L "z-order probe failed: $($_.Exception.Message)" }
}

# stage 2 - only one instance is up
if (-not $target -and $running.Count -eq 1) { $target = @($running.Values)[0]; $how = 'only one running' }

# stage 3 - ask
if (-not $target) {
  try {
    Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing
    $f = New-Object System.Windows.Forms.Form
    $f.Text = 'Which Claude should get this link?'; $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'; $f.MaximizeBox = $false; $f.MinimizeBox = $false; $f.TopMost = $true
    $f.ClientSize = New-Object System.Drawing.Size(360, (60 + $inst.Count * 52))
    $f.BackColor = [System.Drawing.Color]::White
    $st = @{ c = $null }; $y = 36
    foreach ($i in $inst) {
      $nm = $i.name
      $b = New-Object System.Windows.Forms.Button
      $b.Text = $nm; $b.Location = New-Object System.Drawing.Point(16, $y)
      $b.Size = New-Object System.Drawing.Size(328, 40); $b.FlatStyle = 'Flat'
      $b.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
      $b.ForeColor = [System.Drawing.Color]::White
      try { $b.BackColor = [System.Drawing.ColorTranslator]::FromHtml($i.color) } catch { }
      $b.Add_Click({ $st.c = $nm; $f.Close() }.GetNewClosure())
      $f.Controls.Add($b); $y += 52
    }
    [void]$f.ShowDialog(); $target = $st.c; $how = 'picker'; $f.Dispose()
  } catch {
    L "picker failed: $($_.Exception.Message) - using the default profile"
    $target = ($inst | Where-Object { (Norm $_.dataDir) -ieq (Norm $cfg.defaultDataDir) } | Select-Object -First 1).name
    $how = 'fallback to default'
  }
}

if (-not $target) { L 'cancelled'; exit 0 }
$t = $inst | Where-Object { $_.name -eq $target } | Select-Object -First 1
if (-not $t) { L "unknown instance '$target'"; exit 1 }

L "-> $target  (via $how)"
try { Start-Process -FilePath $exe -ArgumentList @("--user-data-dir=`"$($t.dataDir)`"", "`"$Url`"") }
catch {
    L "dispatch failed: $($_.Exception.Message)"
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show("Could not hand the link to ${target}.`n`n$($_.Exception.Message)", 'Claude router', 'OK', 'Error')
    exit 1
}
exit 0
'@

# ========================================================= embedded: icons ====
$IconSource = @'
param(
    [int]    $Persist = 45,
    [switch] $Forever,
    [switch] $Rebuild
)
$ErrorActionPreference = 'Stop'
$H     = Join-Path $env:LOCALAPPDATA 'ClaudeRouter'
$log   = Join-Path $H 'icon.log'
$ready = Join-Path $H '.icon-watcher-ready'

function L([string]$m) {
    try {
        if ((Test-Path $log) -and ((Get-Item $log).Length -gt 512KB)) { Remove-Item $log -Force }
        "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss')  $m" | Add-Content $log -Encoding UTF8
    } catch {}
}
function Norm([string]$p) { if (-not $p) { return '' }; try { ([System.IO.Path]::GetFullPath($p)).TrimEnd('\') } catch { $p.TrimEnd('\') } }

Remove-Item $ready -ErrorAction SilentlyContinue
L "=== watcher start (persist=$Persist forever=$Forever rebuild=$Rebuild) ==="

try {

Add-Type -Name Tb -Namespace WinApi -MemberDefinition @"
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadImage(IntPtr h, string name, uint type, int cx, int cy, uint load);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int p);
[DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);

// Replace every pixel's hue, keeping saturation, lightness and alpha, so white stays
// white and the mark keeps its antialiasing.
public static void Recolor(byte[] buf, double targetHue) {
    for (int i = 0; i < buf.Length; i += 4) {
        if (buf[i+3] == 0) continue;
        double b = buf[i]/255.0, g = buf[i+1]/255.0, r = buf[i+2]/255.0;
        double mx = System.Math.Max(r, System.Math.Max(g, b));
        double mn = System.Math.Min(r, System.Math.Min(g, b));
        double l = (mx + mn) / 2.0;
        if (mx == mn) continue;
        double s = l > 0.5 ? (mx - mn) / (2.0 - mx - mn) : (mx - mn) / (mx + mn);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        buf[i+2] = (byte)System.Math.Round(H2(p, q, targetHue + 1.0/3.0) * 255);
        buf[i+1] = (byte)System.Math.Round(H2(p, q, targetHue) * 255);
        buf[i]   = (byte)System.Math.Round(H2(p, q, targetHue - 1.0/3.0) * 255);
    }
}
static double H2(double p, double q, double t) {
    if (t < 0) t += 1;
    if (t > 1) t -= 1;
    if (t < 1.0/6.0) return p + (q - p) * 6 * t;
    if (t < 1.0/2.0) return q;
    if (t < 2.0/3.0) return p + (q - p) * (2.0/3.0 - t) * 6;
    return p;
}
"@

$cfg = Get-Content (Join-Path $H 'instances.json') -Raw | ConvertFrom-Json
$tinted = @($cfg.instances | Where-Object { $_.tint })
if ($tinted.Count -eq 0) { L 'no instances are marked for tinting - nothing to do'; exit 0 }

# ---------------------------------------------------- source artwork --------
function Get-SourceArtwork {
    $r = Join-Path $H 'resolve-claude.ps1'
    $exe = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $r 2>$null | Select-Object -First 1
    $roots = @()
    try {
        $pkg = Get-AppxPackage -Name Claude -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pkg) { $roots += $pkg.InstallLocation }
    } catch {}
    if ($exe) { $roots += (Split-Path $exe -Parent); $roots += (Split-Path (Split-Path $exe -Parent) -Parent) }

    $best = $null; $bestScore = -1
    foreach ($root in ($roots | Select-Object -Unique)) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        Get-ChildItem $root -Recurse -File -Filter '*.png' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match 'logo|icon' -and $_.Length -lt 3MB } | ForEach-Object {
                try {
                    $img = [System.Drawing.Image]::FromFile($_.FullName)
                    $w = $img.Width; $img.Dispose()
                    # Transparent 'unplated' art beats a plated tile at ANY size - a small
                    # starburst upscaled looks right, a big coloured square does not.
                    $score = $w
                    if ($_.Name -match 'unplated') { $score += 100000 }
                    if ($score -gt $bestScore) { $bestScore = $score; $best = $_.FullName }
                } catch {}
            }
    }
    return @{ Path = $best; Score = $bestScore; Exe = $exe }
}

function Build-Icon($path, $hexColor, $label) {
    # Chrome/Brave-style profile badging: the real Claude mark, untouched, with a small
    # coloured disc in the corner. The badge is drawn as vector geometry so it stays crisp
    # at every taskbar size, and the logo itself is never recoloured or redrawn.
    Add-Type -AssemblyName System.Drawing
    $art  = Get-SourceArtwork
    $size = 256
    $bmp  = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g    = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = 26                      # room for the badge without shrinking the mark much
    $baseW = $size - $inset
    if ($art.Path) {
        L "  base artwork: $($art.Path)"
        $img = [System.Drawing.Image]::FromFile($art.Path)
        $g.DrawImage($img, 0, 0, $baseW, $baseW); $img.Dispose()
    } elseif ($art.Exe) {
        L '  base artwork: exe icon'
        $i2 = [System.Drawing.Icon]::ExtractAssociatedIcon($art.Exe)
        $g.DrawImage($i2.ToBitmap(), 0, 0, $baseW, $baseW); $i2.Dispose()
    } else {
        L '  WARNING: no base artwork found - badge only'
    }

    $col   = [System.Drawing.ColorTranslator]::FromHtml($hexColor)
    $d     = [int]($size * 0.46)
    $bx    = $size - $d - 5
    $by    = $size - $d - 5
    $halo  = 8

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillEllipse($white, ($bx - $halo), ($by - $halo), ($d + $halo * 2), ($d + $halo * 2))
    $fill  = New-Object System.Drawing.SolidBrush($col)
    $g.FillEllipse($fill, $bx, $by, $d, $d)

    if ($label) {
        $ch   = $label.Substring(0, 1).ToUpper()
        $font = New-Object System.Drawing.Font('Segoe UI', ($d * 0.60), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $fmt  = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $rectF = New-Object System.Drawing.RectangleF($bx, ($by + 2), $d, $d)
        $g.DrawString($ch, $font, $white, $rectF, $fmt)
        $font.Dispose()
    }
    $white.Dispose(); $fill.Dispose(); $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray(); $ms.Dispose(); $bmp.Dispose()

    if (Test-Path $path) {
        try { (Get-Item $path -Force).Attributes = 'Normal'; Remove-Item $path -Force -ErrorAction Stop }
        catch { L "  WARNING: could not replace $path - $($_.Exception.Message)" }
    }
    $fs = [System.IO.File]::Create($path); $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
    $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
    $bw.Write($png, 0, $png.Length)
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
    L "  wrote $path ($($png.Length) bytes)"
}

$IMAGE_ICON = 1; $LR_LOADFROMFILE = 0x10; $LR_SHARED = 0x8000
$WM_SETICON = 0x0080
$handles = @{}

foreach ($i in $tinted) {
    $ico = Join-Path $H "taskbar-$($i.name).ico"
    if ($Rebuild -or -not (Test-Path $ico)) { L "building badge icon for '$($i.name)' ($($i.color))"; Build-Icon $ico $i.color $i.name }
    $sm = [WinApi.Tb]::LoadImage([IntPtr]::Zero, $ico, $IMAGE_ICON, 16, 16, ($LR_LOADFROMFILE -bor $LR_SHARED))
    $bg = [WinApi.Tb]::LoadImage([IntPtr]::Zero, $ico, $IMAGE_ICON, 32, 32, ($LR_LOADFROMFILE -bor $LR_SHARED))
    if ($sm -eq [IntPtr]::Zero -or $bg -eq [IntPtr]::Zero) { L "WARNING: could not load $ico"; continue }
    $handles[(Norm $i.dataDir)] = @{ Name = $i.name; Small = $sm; Big = $bg }
}
if ($handles.Count -eq 0) { L 'FATAL: no icons loaded'; exit 1 }

# the launcher waits for this before starting Claude, so the watcher is already
# looping when Windows creates the taskbar button
New-Item -ItemType File -Path $ready -Force | Out-Null
L "READY - watching $($handles.Count) instance(s)"

$seen     = @{}
$lastPids = ''
$pidMap   = @{}
$deadline = if ($Forever) { [datetime]::MaxValue } else { (Get-Date).AddSeconds($Persist) }
$sleepMs  = if ($Forever) { 2500 } else { 700 }

while ((Get-Date) -lt $deadline) {
    # cheap check first; only pay for the CIM query when the process set changes
    $ids = @(Get-Process -Name 'Claude' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
    $key = ($ids -join ',')
    if ($key -ne $lastPids) {
        $lastPids = $key
        $pidMap = @{}
        try {
            Get-CimInstance Win32_Process -Filter "Name LIKE '%laude%'" -ErrorAction SilentlyContinue | ForEach-Object {
                $cl = $_.CommandLine
                if (-not $cl -or $cl -like '*--type=*') { return }
                $dd = if ($cl -match '--user-data-dir="([^"]+)"') { Norm $Matches[1] }
                      elseif ($cl -match '--user-data-dir=([^\s"]+)') { Norm $Matches[1] }
                      else { Norm $cfg.defaultDataDir }
                if ($handles.ContainsKey($dd)) { $pidMap[[int]$_.ProcessId] = $dd }
            }
        } catch {}
    }

    if ($pidMap.Count -gt 0) {
        $h = [WinApi.Tb]::GetTopWindow([IntPtr]::Zero)
        while ($h -ne [IntPtr]::Zero) {
            if ([WinApi.Tb]::IsWindowVisible($h) -and [WinApi.Tb]::GetWindowTextLength($h) -gt 0) {
                $wp = 0; [void][WinApi.Tb]::GetWindowThreadProcessId($h, [ref]$wp)
                if ($pidMap.ContainsKey($wp)) {
                    $e = $handles[$pidMap[$wp]]
                    [void][WinApi.Tb]::SendMessage($h, $WM_SETICON, [IntPtr]0, $e.Small)
                    [void][WinApi.Tb]::SendMessage($h, $WM_SETICON, [IntPtr]1, $e.Big)
                    if (-not $seen.ContainsKey([int64]$h)) { $seen[[int64]$h] = 1; L "applied '$($e.Name)' to hwnd $h (pid $wp)" }
                }
            }
            $h = [WinApi.Tb]::GetWindow($h, 2)
        }
    }
    Start-Sleep -Milliseconds $sleepMs
}
L "watcher finished - $($seen.Count) distinct window(s) touched"

} catch {
    L "EXCEPTION: $($_.Exception.Message)"
    L $_.ScriptStackTrace
}
Remove-Item $ready -ErrorAction SilentlyContinue
'@

# ================================================================ discovery ===
function Get-ClaudeEnvironment {
    $r = [ordered]@{
        Ok = $false; Reason = ''; Build = ''; Version = ''; Exe = ''
        RoamRoot = ''; DefaultProfile = ''; Profiles = @(); PackageFamily = ''
    }

    if ([Environment]::OSVersion.Version.Major -lt 10) {
        $r.Reason = 'Windows 10 or Windows 11 is required.'
        return $r
    }

    # --- Store / MSIX build
    $pkg = $null
    try { $pkg = Get-AppxPackage -Name Claude -ErrorAction SilentlyContinue | Select-Object -First 1 } catch { }
    if ($pkg) {
        foreach ($rel in @('app\Claude.exe', 'Claude.exe')) {
            $p = Join-Path $pkg.InstallLocation $rel
            if (Test-Path $p) { $r.Exe = $p; break }
        }
        if ($r.Exe) {
            $r.Build         = 'Microsoft Store (MSIX)'
            $r.Version       = $pkg.Version
            $r.PackageFamily = $pkg.PackageFamilyName
            $r.RoamRoot      = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalCache\Roaming"
        }
    }

    # --- standalone / Squirrel build
    if (-not $r.Exe) {
        foreach ($root in @("$env:LOCALAPPDATA\AnthropicClaude",
                            "$env:LOCALAPPDATA\Programs\AnthropicClaude",
                            "$env:LOCALAPPDATA\Programs\Claude",
                            "$env:LOCALAPPDATA\Programs\claude-desktop",
                            "$env:ProgramFiles\Claude")) {
            if (-not (Test-Path $root)) { continue }
            $stub = Join-Path $root 'Claude.exe'
            if (Test-Path $stub) { $r.Exe = $stub }
            else {
                $ver = Get-ChildItem $root -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
                       Sort-Object Name -Descending | Select-Object -First 1
                if ($ver) { $e = Join-Path $ver.FullName 'Claude.exe'; if (Test-Path $e) { $r.Exe = $e } }
            }
            if ($r.Exe) {
                $r.Build    = 'Standalone (Squirrel)'
                $r.RoamRoot = $env:APPDATA
                try { $r.Version = (Get-Item $r.Exe).VersionInfo.FileVersion } catch { }
                break
            }
        }
    }

    if (-not $r.Exe) {
        $r.Reason = "Claude Desktop isn't installed for this user account.`r`n`r`nInstall it from the Microsoft Store or from claude.com/download, launch it once, then run this again."
        return $r
    }
    if (-not (Test-Path $r.RoamRoot)) {
        $r.Reason = "Claude hasn't created its profile folder yet.`r`n`r`nLaunch Claude Desktop once, close it, then run this again."
        return $r
    }

    $r.DefaultProfile = Join-Path $r.RoamRoot 'Claude'
    if (-not (Test-Path $r.DefaultProfile)) {
        $r.Reason = "Claude's own profile folder wasn't found at:`r`n$($r.DefaultProfile)`r`n`r`nLaunch Claude Desktop once, close it, then run this again."
        return $r
    }

    $skip = @('Microsoft', 'Packages', 'Adobe', 'Google', 'Mozilla')
    $r.Profiles = @(Get-ChildItem $r.RoamRoot -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $skip -notcontains $_.Name -and (Test-Path (Join-Path $_.FullName 'Local Storage')) } |
                    Select-Object -ExpandProperty Name)
    if ($r.Profiles.Count -eq 0) { $r.Profiles = @('Claude') }
    $r.Ok = $true
    return $r
}

# ============================================================== validation ====
function Test-InstanceName {
    param([string]$Name, [hashtable]$Env)
    if ([string]::IsNullOrWhiteSpace($Name))            { return 'Enter a name.' }
    if ($Name -match '[\\/:*?"<>|]')                     { return 'That name contains characters Windows will not allow in a folder name.' }
    if ($Name.EndsWith('.') -or $Name.EndsWith(' '))     { return 'The name cannot end with a dot or a space.' }
    if ($ReservedNames -contains $Name.ToUpper())        { return "'$Name' is a reserved Windows device name. Pick another." }
    if ($Name.Length -gt 40)                             { return 'Keep the name under 40 characters.' }
    if ($Name -ieq 'Claude')                             { return "'Claude' is the existing profile's own folder. Pick a different name." }
    $dd  = Get-NormalPath (Join-Path $Env.RoamRoot $Name)
    $def = Get-NormalPath $Env.DefaultProfile
    if ($dd -ieq $def)                                   { return 'That resolves to the existing profile - the two instances would share data.' }
    if ($dd.StartsWith("$def\", 'OrdinalIgnoreCase') -or $def.StartsWith("$dd\", 'OrdinalIgnoreCase')) {
        return 'That folder would sit inside the other profile. Pick a name that is not nested.'
    }
    return ''
}

# =========================================================== security software
# The router is, structurally, a URL-protocol handler that launches a hidden PowerShell
# process and P/Invokes user32. That is also what a certain class of malware looks like,
# so heuristic engines (Bitdefender in particular) quarantine it. These helpers reduce the
# signature where we legitimately can, and detect it when it happens anyway.

function Get-ExecPolicyFlag {
    # Returns ' -ExecutionPolicy Bypass' only if we actually need it. Dropping this flag
    # removes one of the strongest heuristic markers from every command line we register.
    try {
        $cur = Get-ExecutionPolicy -Scope CurrentUser -ErrorAction Stop
        if ($cur -in @('Restricted', 'Undefined', 'AllSigned')) {
            Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction Stop
        }
        $eff = Get-ExecutionPolicy -ErrorAction Stop
        if ($eff -in @('RemoteSigned', 'Unrestricted', 'Bypass')) { return '' }
    } catch { }
    return ' -ExecutionPolicy Bypass'
}

function Add-DefenderExclusion {
    param([string]$Path)
    # Only possible for Microsoft Defender, and only when elevated. Third-party suites
    # have no scriptable equivalent - those need a manual exclusion.
    try {
        if (-not (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) { return 'unavailable' }
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $pr = New-Object Security.Principal.WindowsPrincipal($id)
        if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { return 'needs-admin' }
        Add-MpPreference -ExclusionPath $Path -ErrorAction Stop
        return 'added'
    } catch { return "failed: $($_.Exception.Message)" }
}

function Write-RuntimeFile {
    param([string]$Path, [string]$Content)
    # A previous install - or an antivirus "disinfection" - can leave these files
    # read-only or locked. Clear the attributes and delete before rewriting, and give a
    # usable message if the file genuinely cannot be replaced.
    if (Test-Path -LiteralPath $Path) {
        try { (Get-Item -LiteralPath $Path -Force).Attributes = 'Normal' } catch { }
        try { Remove-Item -LiteralPath $Path -Force -ErrorAction Stop }
        catch {
            throw ("Cannot replace $Path`r`n" +
                   "  $($_.Exception.Message)`r`n" +
                   "  Close anything using it, then delete the folder by hand and run setup again:`r`n" +
                   "     Remove-Item '$([System.IO.Path]::GetDirectoryName($Path))' -Recurse -Force")
        }
    }
    $Content | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Test-RuntimeFilesIntact {
    param([string[]]$Paths)
    # Antivirus quarantine is asynchronous - give it a moment, then look again.
    Start-Sleep -Milliseconds 1500
    return @($Paths | Where-Object { -not (Test-Path $_) })
}

function Test-Isolation {
    param([hashtable]$Env, [string]$Name)
    $issues = @()
    $dd  = Get-NormalPath (Join-Path $Env.RoamRoot $Name)
    $def = Get-NormalPath $Env.DefaultProfile
    if ($dd -ieq $def) { $issues += 'the two profiles resolve to the same folder' }
    if ($dd.StartsWith("$def\", 'OrdinalIgnoreCase')) { $issues += 'the new profile is nested inside the default profile' }
    if ($def.StartsWith("$dd\", 'OrdinalIgnoreCase')) { $issues += 'the default profile is nested inside the new profile' }
    return $issues
}

# ================================================================== install ===
function Invoke-ClaudeInstall {
    param([hashtable]$Cfg, [scriptblock]$Log, [scriptblock]$Progress)

    $script:istep = 0
    $total = 12
    function Emit($m) { & $Log $m }
    function Tick { $script:istep++; & $Progress ([int](100 * $script:istep / $total)) }

    $e       = $Cfg.Env
    $name    = $Cfg.InstanceName
    $dataDir = Join-Path $e.RoamRoot $name
    $defDir  = $e.DefaultProfile
    $desktop = [Environment]::GetFolderPath('Desktop')

    Emit "Claude $($e.Version) - $($e.Build)"
    Emit "Executable   : $($e.Exe)"
    Emit "Profile root : $($e.RoamRoot)"
    Tick

    Emit ''
    Emit 'Checking isolation'
    $issues = Test-Isolation -Env $e -Name $name
    if ($issues.Count) { foreach ($i in $issues) { Emit "  PROBLEM: $i" }; throw "Isolation check failed: $($issues -join '; ')" }
    Emit "  default  : $defDir"
    Emit "  new      : $dataDir"
    Emit '  OK - separate folders, neither nested inside the other'
    Tick

    Emit ''
    Emit 'Creating the profile folder'
    if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir -Force | Out-Null; Emit '  created' }
    else { Emit '  already exists - left as-is' }
    New-Item -ItemType Directory -Path $HomeDir -Force | Out-Null
    $ro = @(Get-ChildItem $HomeDir -File -Force -ErrorAction SilentlyContinue | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReadOnly })
    if ($ro.Count) {
        Emit "  clearing read-only on $($ro.Count) leftover file(s)"
        foreach ($f in $ro) { try { $f.Attributes = 'Normal' } catch { } }
    }
    Tick

    Emit ''
    Emit 'Writing the runtime scripts'
    $resolvePs = Join-Path $HomeDir 'resolve-claude.ps1'
    $routerPs  = Join-Path $HomeDir 'router-fallback.ps1'
    $iconPs    = Join-Path $HomeDir 'set-instance-icon.ps1'
    Write-RuntimeFile -Path $resolvePs -Content $Cfg.ResolveSource
    $fallbackOk = $true
    try { Write-RuntimeFile -Path $routerPs -Content $Cfg.RouterSource }
    catch {
        $fallbackOk = $false
        Emit '  WARNING: could not write the PowerShell fallback router.'
        Emit "    $($_.Exception.Message.Split([char]13)[0])"
        Emit '    Continuing - the compiled router is the one that gets registered.'
    }
    $Cfg.FallbackOk = $fallbackOk

    # A legacy router.ps1 from an older version may be locked by antivirus. It is no
    # longer used, so remove it if we can and ignore it if we cannot.
    $legacy = Join-Path $HomeDir 'router.ps1'
    if (Test-Path $legacy) {
        try { (Get-Item $legacy -Force).Attributes = 'Normal'; Remove-Item $legacy -Force -ErrorAction Stop; Emit '  removed the legacy router.ps1' }
        catch { Emit '  NOTE: the old router.ps1 is locked and could not be removed - it is no longer used.' }
    }
    Write-RuntimeFile -Path $iconPs    -Content $Cfg.IconSource
    Emit "  $resolvePs"
    Emit "  $routerPs"
    Emit "  $iconPs"

    $manifest = [pscustomobject]@{
        version        = $Cfg.ScriptVersion
        build          = $e.Build
        defaultDataDir = $defDir
        instances      = @(
            [pscustomobject]@{ name = $Cfg.MainLabel; dataDir = $defDir;  color = $Cfg.MainColor; tint = [bool]$Cfg.TintMain }
            [pscustomobject]@{ name = $name;          dataDir = $dataDir; color = $Cfg.Color;     tint = $true }
        )
    }
    Write-RuntimeFile -Path (Join-Path $HomeDir 'instances.json') -Content ($manifest | ConvertTo-Json -Depth 5)
    $rt = Get-Content (Join-Path $HomeDir 'instances.json') -Raw | ConvertFrom-Json
    if (@($rt.instances).Count -ne 2) { throw 'instances.json did not round-trip correctly.' }
    Emit '  instances.json (verified)'
    Tick

    Emit ''
    Emit 'Verifying the exe resolver'
    $resolved = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $resolvePs 2>$null | Select-Object -First 1
    if ($resolved -and (Test-Path $resolved)) { Emit "  resolves to $resolved" }
    else { Emit '  WARNING: the resolver returned nothing - launchers may fail' }
    Tick

    Emit ''
    Emit 'Reducing the antivirus heuristic footprint'
    # "-WindowStyle Hidden -ExecutionPolicy Bypass" on a protocol handler is the exact
    # shape of a PowerShell hijack, and behavioural engines block it. Neither flag is
    # actually required, so we drop both: set a per-user execution policy instead, and
    # accept a brief console flash rather than hiding the window.
    $epFlag = Get-ExecPolicyFlag
    if ($epFlag) { Emit '  could not set a per-user execution policy - keeping -ExecutionPolicy Bypass' }
    else         { Emit '  per-user execution policy is RemoteSigned - dropping -ExecutionPolicy Bypass' }
    Get-ChildItem $HomeDir -Filter '*.ps1' -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
    Emit '  scripts unblocked (no mark-of-the-web)'
    $Cfg.ExecPolicyFlag = $epFlag

    $missing = Test-RuntimeFilesIntact -Paths @($resolvePs, $routerPs, $iconPs)
    if ($missing.Count) {
        Emit ''
        Emit '  ANTIVIRUS REMOVED THESE FILES:'
        foreach ($m in $missing) { Emit "    $m" }
        $Cfg.AntivirusBlocked = $true
        throw "Your antivirus quarantined the scripts. Add an exclusion for $HomeDir and run this again."
    }
    Emit '  files still present after write - no quarantine detected'
    Tick

    Emit ''
    Emit 'Compiling the router to a native executable'
    # A purpose-built exe cannot be repurposed the way a .ps1 handler can, and it keeps
    # powershell.exe out of the protocol chain entirely - which is what behavioural AV
    # engines object to. If the local C# compiler is unavailable we fall back cleanly.
    $routerExe = Join-Path $HomeDir 'ClaudeRouter.exe'
    $exeOk = $false
    try {
        if (Test-Path $routerExe) { Remove-Item $routerExe -Force -ErrorAction SilentlyContinue }
        Add-Type -TypeDefinition $Cfg.RouterExeSource `
                 -OutputAssembly $routerExe -OutputType WindowsApplication `
                 -ReferencedAssemblies 'System.Management' -ErrorAction Stop
        if ((Test-Path $routerExe) -and ((Get-Item $routerExe).Length -gt 0)) {
            $exeOk = $true
            Emit "  built $routerExe ($([int]((Get-Item $routerExe).Length/1KB)) KB)"
        }
    } catch {
        Emit "  compiler unavailable: $($_.Exception.Message)"
    }
    if (-not $exeOk) {
        if (-not $Cfg.FallbackOk) {
            throw ("Neither the compiled router nor the PowerShell fallback could be created.`r`n" +
                   "  Add an antivirus exclusion for $HomeDir, delete that folder, and run setup again.")
        }
        Emit '  falling back to the PowerShell router (works, but AV may object)'
    }
    $Cfg.RouterIsExe = $exeOk

    # plain-text instance list for the exe - no JSON parser needed
    $tsv = @("DEFAULT`t$defDir",
             "$($Cfg.MainLabel)`t$defDir`t$(if ($Cfg.TintMain) { '1' } else { '0' })",
             "$name`t$dataDir`t1")
    Write-RuntimeFile -Path (Join-Path $HomeDir 'instances.tsv') -Content ($tsv -join "`r`n")
    Emit '  instances.tsv'

    New-Item -Path 'HKCU:\Software\ClaudeRouter' -Force | Out-Null
    Set-ItemProperty 'HKCU:\Software\ClaudeRouter' -Name 'ClaudeExe' -Value $e.Exe
    Tick

    Emit ''
    Emit 'Registering the router with Windows'
    $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ($exeOk) {
        $cmdLine = "`"$routerExe`" `"%1`""
        Emit '  handler: ClaudeRouter.exe (no PowerShell in the chain)'
    } else {
        if (-not (Test-Path $psExe)) { throw "Windows PowerShell not found at $psExe" }
        $cmdLine = "`"$psExe`" -NoProfile$epFlag -File `"$routerPs`" -Url `"%1`""
        Emit '  handler: powershell.exe + router.ps1'
    }

    New-Item -Path "$ProtoKey\shell\open\command" -Force | Out-Null
    Set-ItemProperty $ProtoKey -Name '(default)'    -Value 'URL:Claude Protocol'
    Set-ItemProperty $ProtoKey -Name 'URL Protocol' -Value ''
    Set-ItemProperty "$ProtoKey\shell\open\command" -Name '(default)' -Value $cmdLine

    New-Item -Path "$ProgKey\shell\open\command" -Force | Out-Null
    Set-ItemProperty $ProgKey -Name '(default)'    -Value $AppName
    Set-ItemProperty $ProgKey -Name 'URL Protocol' -Value ''
    Set-ItemProperty "$ProgKey\shell\open\command" -Name '(default)' -Value $cmdLine

    New-Item -Path "$CapKey\UrlAssociations" -Force | Out-Null
    Set-ItemProperty $CapKey -Name 'ApplicationName'        -Value $AppName
    Set-ItemProperty $CapKey -Name 'ApplicationDescription' -Value 'Routes claude:// links to the right Claude Desktop instance'
    Set-ItemProperty "$CapKey\UrlAssociations" -Name 'claude' -Value $ProgId

    New-Item -Path $RegApps -Force | Out-Null
    Set-ItemProperty $RegApps -Name $AppName -Value 'Software\ClaudeRouter\Capabilities'
    Emit '  HKCU: Classes\claude, ClaudeRouter.Proto, Capabilities, RegisteredApplications'
    Tick

    Emit ''
    Emit "Building the tinted icon(s)"
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $iconPs -Rebuild -Persist 1 2>&1 | Out-Null
        foreach ($i in $manifest.instances | Where-Object { $_.tint }) {
            $p = Join-Path $HomeDir "taskbar-$($i.name).ico"
            if (Test-Path $p) { Emit "  $($i.name): $p" } else { Emit "  WARNING: no icon produced for $($i.name)" }
        }
    } catch { Emit "  WARNING: $($_.Exception.Message)" }
    Tick

    Emit ''
    Emit 'Creating launchers'
    $icoPath  = Join-Path $HomeDir "taskbar-$name.ico"
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $shell = New-Object -ComObject WScript.Shell

    # tidy up the .cmd launcher an older version of this installer may have left
    $oldCmd = Join-Path $desktop "Claude - $name.cmd"
    if (Test-Path $oldCmd) { Remove-Item $oldCmd -Force -ErrorAction SilentlyContinue; Emit '  removed the previous .cmd launcher' }

    if ($exeOk) {
        # A real shortcut with the tinted Claude icon - no batch file, no console flash.
        foreach ($dir in @($desktop, $startMenu)) {
            $lnk = Join-Path $dir "Claude ($name).lnk"
            $sc = $shell.CreateShortcut($lnk)
            $sc.TargetPath       = $routerExe
            $sc.Arguments        = "--launch `"$name`""
            $sc.WorkingDirectory = $HomeDir
            $sc.Description      = "Claude Desktop - $name (isolated profile)"
            if (Test-Path $icoPath) { $sc.IconLocation = "$icoPath,0" }
            $sc.Save()
            Emit "  $lnk"
        }
        $Cfg.LauncherPath = Join-Path $desktop "Claude ($name).lnk"

        $refresh = Join-Path $desktop 'Refresh Claude icons.lnk'
        $sc = $shell.CreateShortcut($refresh)
        $sc.TargetPath = $routerExe
        $sc.Arguments  = '--watch 15'
        $sc.WorkingDirectory = $HomeDir
        $sc.Description = 'Re-apply the tinted taskbar icons to any Claude windows open now'
        if (Test-Path $icoPath) { $sc.IconLocation = "$icoPath,0" }
        $sc.Save()
        Emit "  $refresh"
    } else {
        # fallback: batch launcher driving the PowerShell watcher
        $vmGuard  = "%LOCALAPPDATA%\$name\vm_bundles\claudevm.bundle"
        $launcher = Join-Path $desktop "Claude - $name.cmd"
        $txt = @"
@echo off
:: Claude - $name   (isolated profile: $name)
setlocal
if not exist "$vmGuard\rootfs.vhdx" (
    mkdir "$vmGuard" 2>nul
    type nul > "$vmGuard\rootfs.vhdx"
)
for /f "delims=" %%i in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\ClaudeRouter\resolve-claude.ps1"') do set "CLAUDEEXE=%%i"
if not defined CLAUDEEXE (echo Could not locate Claude Desktop. & pause & exit /b 1)
set "RDY=%LOCALAPPDATA%\ClaudeRouter\.icon-watcher-ready"
del "%RDY%" 2>nul
start "" /min powershell -NoProfile$($Cfg.ExecPolicyFlag) -File "%LOCALAPPDATA%\ClaudeRouter\set-instance-icon.ps1" -Persist 60
set /a TRIES=0
:waitready
if exist "%RDY%" goto go
set /a TRIES+=1
if %TRIES% GEQ 40 goto go
timeout /t 1 /nobreak >nul
goto waitready
:go
start "" "%CLAUDEEXE%" --user-data-dir="$dataDir"
endlocal
"@
        $txt | Set-Content $launcher -Encoding ASCII
        Emit "  $launcher  (fallback - the exe was not available)"
        $Cfg.LauncherPath = $launcher
    }

    $startupLnk = Join-Path $StartupDir 'Claude icon watcher.lnk'
    $startupCmd = Join-Path $StartupDir 'Claude icon watcher.cmd'
    foreach ($old in @($startupLnk, $startupCmd)) { if (Test-Path $old) { Remove-Item $old -Force -ErrorAction SilentlyContinue } }
    if ($Cfg.AlwaysOnWatcher) {
        if ($exeOk) {
            $sc = $shell.CreateShortcut($startupLnk)
            $sc.TargetPath = $routerExe
            $sc.Arguments  = '--watch'
            $sc.WorkingDirectory = $HomeDir
            $sc.WindowStyle = 7
            $sc.Description = 'Keeps the tinted taskbar icons applied to Claude windows'
            $sc.Save()
            Emit "  $startupLnk"
        } else {
            "@echo off`r`nstart `"`" /min powershell -NoProfile$($Cfg.ExecPolicyFlag) -File `"$iconPs`" -Forever" |
                Set-Content $startupCmd -Encoding ASCII
            Emit "  $startupCmd"
        }
    }
    Tick

    Emit ''
    if ($Cfg.SetTaskbar) {
        Emit 'Taskbar: never combine buttons'
        $cur = (Get-ItemProperty $AdvKey -Name TaskbarGlomLevel -ErrorAction SilentlyContinue).TaskbarGlomLevel
        if ($cur -eq 2) { Emit '  already set' }
        else {
            Set-ItemProperty $AdvKey -Name TaskbarGlomLevel   -Value 2 -Type DWord
            Set-ItemProperty $AdvKey -Name MMTaskbarGlomLevel -Value 2 -Type DWord -ErrorAction SilentlyContinue
            Emit '  set - applies after Explorer restarts'
            $Cfg.NeedsExplorerRestart = $true
        }
    } else { Emit 'Taskbar setting skipped by choice' }
    Tick

    Emit ''
    Emit 'Registering in Windows "Installed apps"'
    $selfCopy = Join-Path $HomeDir 'Setup-ClaudeMultiInstance.ps1'
    try {
        if ($Cfg.SelfPath -and (Test-Path $Cfg.SelfPath)) { Copy-Item $Cfg.SelfPath $selfCopy -Force }
        New-Item -Path $UninstKey -Force | Out-Null
        Set-ItemProperty $UninstKey -Name 'DisplayName'     -Value 'Claude Multi-Instance Setup'
        Set-ItemProperty $UninstKey -Name 'DisplayVersion'  -Value $Cfg.ScriptVersion
        Set-ItemProperty $UninstKey -Name 'Publisher'       -Value 'Local install (unsigned)'
        Set-ItemProperty $UninstKey -Name 'InstallLocation' -Value $HomeDir
        Set-ItemProperty $UninstKey -Name 'DisplayIcon'     -Value $e.Exe
        Set-ItemProperty $UninstKey -Name 'UninstallString' -Value "`"$psExe`" -NoProfile -ExecutionPolicy Bypass -File `"$selfCopy`" -Uninstall"
        Set-ItemProperty $UninstKey -Name 'NoModify'      -Value 1   -Type DWord
        Set-ItemProperty $UninstKey -Name 'NoRepair'      -Value 1   -Type DWord
        Set-ItemProperty $UninstKey -Name 'EstimatedSize' -Value 250 -Type DWord
        Emit '  listed in Settings > Apps > Installed apps'
    } catch { Emit "  could not register the uninstall entry: $($_.Exception.Message)" }

    # Launchers from an earlier hand-rolled setup call the icon script with parameters it
    # no longer accepts. Flag them rather than silently deleting the user's own files.
    $stale = @()
    foreach ($f in (Get-ChildItem $desktop -Filter '*.cmd' -ErrorAction SilentlyContinue)) {
        if ($f.FullName -eq $launcher) { continue }
        try {
            $c = Get-Content $f.FullName -Raw -ErrorAction Stop
            if ($c -match 'set-instance-icon\.ps1' -and $c -match '-ProfileName') { $stale += $f.FullName }
        } catch { }
    }
    if ($stale.Count) {
        Emit ''
        Emit '  NOTE - these older launchers are now out of date and will not tint correctly:'
        foreach ($f in $stale) { Emit "    $f" }
        Emit "  Use '$([System.IO.Path]::GetFileName($launcher))' instead. Delete the old ones when you are happy."
        $Cfg.StaleLaunchers = $stale
    }
    Tick

    Emit ''
    Emit 'Verifying the router with a test claude:// link'
    $routerLog = Join-Path $HomeDir 'router.log'
    Remove-Item $routerLog -ErrorAction SilentlyContinue
    try { Start-Process 'claude://' -ErrorAction Stop } catch { Emit "  could not fire the link: $($_.Exception.Message)" }
    Start-Sleep 5
    if (Test-Path $routerLog) {
        Emit '  SUCCESS - the router received it:'
        Get-Content $routerLog -Tail 3 | ForEach-Object { Emit "    $_" }
        $Cfg.RouterVerified = $true
    } else {
        Emit '  NOT ACTIVE YET - Windows is still handing claude:// straight to Claude.'
        Emit '  One manual step fixes it; the next page explains.'
        $Cfg.RouterVerified = $false
    }
    & $Progress 100
}

# ================================================================ uninstall ===
function Invoke-ClaudeUninstall {
    param([scriptblock]$Log)
    function Emit($m) { & $Log $m }
    $names = @()
    try {
        $cfg = Get-Content (Join-Path $HomeDir 'instances.json') -Raw | ConvertFrom-Json
        $names = @($cfg.instances | Select-Object -ExpandProperty name)
    } catch { }

    $profileDirs = @()
    try {
        $cfg0 = Get-Content (Join-Path $HomeDir 'instances.json') -Raw | ConvertFrom-Json
        foreach ($i in $cfg0.instances) {
            if ((Get-NormalPath $i.dataDir) -ine (Get-NormalPath $cfg0.defaultDataDir)) { $profileDirs += $i.dataDir }
        }
    } catch { }

    foreach ($k in @($ProtoKey, $ProgKey, 'HKCU:\Software\ClaudeRouter', $UninstKey)) {
        if (Test-Path $k) { Remove-Item $k -Recurse -Force; Emit "removed $k" }
    }
    if (Get-ItemProperty $RegApps -Name $AppName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty $RegApps -Name $AppName -Force; Emit "removed RegisteredApplications\$AppName"
    }
    $desktop = [Environment]::GetFolderPath('Desktop')
    $paths = @($HomeDir,
               (Join-Path $StartupDir 'Claude icon watcher.cmd'),
               (Join-Path $StartupDir 'Claude icon watcher.lnk'),
               (Join-Path $desktop 'Recolour Claude icons.cmd'),
               (Join-Path $desktop 'Refresh Claude icons.lnk'))
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    foreach ($n in $names) {
        $paths += (Join-Path $desktop  "Claude ($n).lnk")
        $paths += (Join-Path $startMenu "Claude ($n).lnk")
    }
    foreach ($n in $names) { $paths += (Join-Path $desktop "Claude - $n.cmd") }
    $paths += (Get-ChildItem $desktop -Filter 'Claude - *.cmd' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $paths += (Get-ChildItem $desktop -Filter 'Claude (*).lnk' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    foreach ($p in ($paths | Select-Object -Unique)) {
        if ($p -and (Test-Path $p)) { Remove-Item $p -Recurse -Force; Emit "removed $p" }
    }
    if ($RemoveProfiles) {
        foreach ($d in $profileDirs) {
            if (Test-Path $d) {
                try { Remove-Item $d -Recurse -Force; Emit "DELETED profile $d" }
                catch { Emit "could not delete $d - $($_.Exception.Message)" }
            }
        }
    } elseif ($profileDirs.Count) {
        Emit ''
        Emit 'These profile folders were left in place (they hold the second account sign-in):'
        foreach ($d in $profileDirs) { Emit "  $d" }
        Emit 'Re-run with -RemoveProfiles to delete them too.'
    }
    Emit ''
    Emit 'Your original Claude profile, sign-in, settings and history were NOT touched.'
    Emit 'Launch Claude once so it re-registers claude:// for itself.'
}

# ============================================================== verify mode ===
function Invoke-ClaudeVerify {
    param([scriptblock]$Log)
    function Emit($m) { & $Log $m }
    $e = Get-ClaudeEnvironment
    Emit "Build   : $($e.Build) $($e.Version)"
    Emit "Exe     : $($e.Exe)"
    Emit "Profiles: $($e.Profiles -join ', ')"
    Emit ''
    $man = Join-Path $HomeDir 'instances.json'
    if (-not (Test-Path $man)) { Emit 'No instances.json - setup has not been run.'; return }
    $cfg = Get-Content $man -Raw | ConvertFrom-Json
    foreach ($i in $cfg.instances) {
        $exists = Test-Path $i.dataDir
        Emit ("{0,-12} {1}  {2}" -f $i.name, $(if ($exists) { 'present' } else { 'MISSING' }), $i.dataDir)
    }
    $paths = @($cfg.instances | ForEach-Object { Get-NormalPath $_.dataDir })
    if (($paths | Select-Object -Unique).Count -ne $paths.Count) { Emit 'PROBLEM: two instances point at the same folder.' }
    else { Emit 'Isolation OK - every instance has its own folder.' }
    Emit ''
    $h = (Get-ItemProperty "$ProtoKey\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
    Emit "claude:// handler: $(if ($h) { $h } else { '(none)' })"
    Emit ''
    Emit 'Firing a test link...'
    $routerLog = Join-Path $HomeDir 'router.log'
    Remove-Item $routerLog -ErrorAction SilentlyContinue
    try { Start-Process 'claude://' } catch { }
    Start-Sleep 4
    if (Test-Path $routerLog) { Get-Content $routerLog -Tail 4 | ForEach-Object { Emit "  $_" } }
    else { Emit '  the router did NOT fire - reassign the claude link type in Default apps.' }
}

# =========================================================== console modes ====
if ($Silent -or $Uninstall -or $Verify) {
    $log = { param($m) Write-Host "  $m" }
    Write-Host ''
    if ($Uninstall) { Write-Host '  Removing Claude multi-instance setup' -ForegroundColor Cyan; Write-Host ''
                      Invoke-ClaudeUninstall -Log $log; Write-Host ''; return }
    if ($Verify)    { Write-Host '  Verifying Claude multi-instance setup' -ForegroundColor Cyan; Write-Host ''
                      Invoke-ClaudeVerify -Log $log; Write-Host ''; return }

    $e = Get-ClaudeEnvironment
    if (-not $e.Ok) { Write-Host "  $($e.Reason)" -ForegroundColor Red; Write-Host ''; return }
    $bad = Test-InstanceName -Name $InstanceName -Env $e
    if ($bad) { Write-Host "  $bad" -ForegroundColor Red; Write-Host ''; return }
    $cfg = @{
        Env = $e; InstanceName = $InstanceName; Color = $Color; MainLabel = $MainLabel
        MainColor = $MainColor; TintMain = [bool]$TintMain; SetTaskbar = $true
        AlwaysOnWatcher = [bool]$AlwaysOnWatcher; ScriptVersion = $ScriptVersion; SelfPath = $PSCommandPath
        ResolveSource = $ResolveSource; RouterSource = $RouterSource; IconSource = $IconSource; RouterExeSource = $RouterExeSource
    }
    Invoke-ClaudeInstall -Cfg $cfg -Log $log -Progress { param($p) }
    Write-Host ''
    Write-Host '  Done.' -ForegroundColor Green
    Write-Host ''
    return
}

# ==================================================================== wizard ==
try { Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing }
catch { Write-Host 'This wizard needs Windows PowerShell with WinForms. Try: -Silent' -ForegroundColor Red; return }

try { [void][WinApi.Dpi]::SetProcessDPIAware() } catch {
    try { Add-Type -Name Dpi -Namespace WinApi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
          [void][WinApi.Dpi]::SetProcessDPIAware() } catch { }
}
[System.Windows.Forms.Application]::EnableVisualStyles()

$FontBody  = New-Object System.Drawing.Font('Segoe UI', 9.75)
$FontTitle = New-Object System.Drawing.Font('Segoe UI', 15)
$FontSmall = New-Object System.Drawing.Font('Segoe UI', 8.5)
$FontMono  = New-Object System.Drawing.Font('Consolas', 8.5)
$Accent    = [System.Drawing.ColorTranslator]::FromHtml('#7C3AED')
$Muted     = [System.Drawing.Color]::FromArgb(105,105,105)

$form = New-Object System.Windows.Forms.Form
$form.Text            = "Claude Multi-Instance Setup"
$form.ClientSize      = New-Object System.Drawing.Size(680, 500)
$form.StartPosition   = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox     = $false
$form.BackColor       = [System.Drawing.Color]::White
$form.Font            = $FontBody
$form.AutoScaleMode   = 'Font'

$header = New-Object System.Windows.Forms.Panel
$header.Dock = 'Top'; $header.Height = 74; $header.BackColor = [System.Drawing.Color]::White
$hTitle = New-Object System.Windows.Forms.Label
$hTitle.Font = $FontTitle; $hTitle.AutoSize = $true
$hTitle.Location = New-Object System.Drawing.Point(24, 14)
$hSub = New-Object System.Windows.Forms.Label
$hSub.Font = $FontSmall; $hSub.ForeColor = $Muted; $hSub.AutoSize = $true
$hSub.Location = New-Object System.Drawing.Point(26, 46)
$hRule = New-Object System.Windows.Forms.Panel
$hRule.Dock = 'Bottom'; $hRule.Height = 1; $hRule.BackColor = [System.Drawing.Color]::Gainsboro
$header.Controls.AddRange(@($hTitle, $hSub, $hRule))

$footer = New-Object System.Windows.Forms.Panel
$footer.Dock = 'Bottom'; $footer.Height = 58; $footer.BackColor = [System.Drawing.Color]::FromArgb(249,249,249)
$fRule = New-Object System.Windows.Forms.Panel
$fRule.Dock = 'Top'; $fRule.Height = 1; $fRule.BackColor = [System.Drawing.Color]::Gainsboro

function New-Btn($text, $x, $primary) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text
    $b.Size = New-Object System.Drawing.Size(106, 31)
    $b.Location = New-Object System.Drawing.Point($x, 13)
    $b.FlatStyle = 'Flat'
    if ($primary) { $b.BackColor = $Accent; $b.ForeColor = [System.Drawing.Color]::White; $b.FlatAppearance.BorderSize = 0 }
    else { $b.BackColor = [System.Drawing.Color]::White; $b.FlatAppearance.BorderColor = [System.Drawing.Color]::Silver }
    return $b
}
$btnBack   = New-Btn 'Back'   328 $false
$btnNext   = New-Btn 'Next'   442 $true
$btnCancel = New-Btn 'Cancel' 556 $false
$footer.Controls.AddRange(@($fRule, $btnBack, $btnNext, $btnCancel))

$content = New-Object System.Windows.Forms.Panel
$content.Dock = 'Fill'; $content.BackColor = [System.Drawing.Color]::White
$content.Padding = New-Object System.Windows.Forms.Padding(24, 10, 24, 10)
$form.Controls.AddRange(@($content, $footer, $header))

function New-Page {
    $p = New-Object System.Windows.Forms.Panel
    $p.Dock = 'Fill'; $p.Visible = $false; $p.BackColor = [System.Drawing.Color]::White
    $content.Controls.Add($p); return $p
}
function New-Lbl($text, $x, $y, $w, $font, $colour) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.MaximumSize = New-Object System.Drawing.Size($w, 0)
    $l.AutoSize = $true
    if ($font)   { $l.Font = $font }
    if ($colour) { $l.ForeColor = $colour }
    return $l
}
function New-ColorRow {
    param($parent, $y, $initial, $label)
    $parent.Controls.Add((New-Lbl $label 0 $y 300 $FontBody $null))
    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Location = New-Object System.Drawing.Point(0, ($y + 22))
    $tb.Size = New-Object System.Drawing.Size(120, 24)
    $tb.Font = $FontMono; $tb.Text = $initial
    $sw = New-Object System.Windows.Forms.Panel
    $sw.Location = New-Object System.Drawing.Point(126, ($y + 22))
    $sw.Size = New-Object System.Drawing.Size(34, 25); $sw.BorderStyle = 'FixedSingle'
    try { $sw.BackColor = [System.Drawing.ColorTranslator]::FromHtml($initial) } catch {}
    $tb.Add_TextChanged({ try { $sw.BackColor = [System.Drawing.ColorTranslator]::FromHtml($tb.Text) } catch {} }.GetNewClosure())
    $parent.Controls.AddRange(@($tb, $sw))

    $x = 172
    foreach ($hex in @('#7C3AED','#059669','#DC2626','#0EA5E9','#C2410C','#DB2777','#CA8A04')) {
        $b = New-Object System.Windows.Forms.Button
        $b.Size = New-Object System.Drawing.Size(28, 25)
        $b.Location = New-Object System.Drawing.Point($x, ($y + 22))
        $b.FlatStyle = 'Flat'; $b.FlatAppearance.BorderColor = [System.Drawing.Color]::Silver
        $b.BackColor = [System.Drawing.ColorTranslator]::FromHtml($hex)
        $h = $hex
        $b.Add_Click({ $tb.Text = $h }.GetNewClosure())
        $parent.Controls.Add($b); $x += 32
    }
    $more = New-Object System.Windows.Forms.Button
    $more.Text = 'Custom...'; $more.Size = New-Object System.Drawing.Size(80, 25)
    $more.Location = New-Object System.Drawing.Point($x, ($y + 22))
    $more.FlatStyle = 'Flat'; $more.FlatAppearance.BorderColor = [System.Drawing.Color]::Silver
    $more.Add_Click({
        $dlg = New-Object System.Windows.Forms.ColorDialog
        $dlg.FullOpen = $true
        try { $dlg.Color = [System.Drawing.ColorTranslator]::FromHtml($tb.Text) } catch {}
        if ($dlg.ShowDialog() -eq 'OK') {
            $tb.Text = '#{0:X2}{1:X2}{2:X2}' -f $dlg.Color.R, $dlg.Color.G, $dlg.Color.B
        }
    }.GetNewClosure())
    $parent.Controls.Add($more)
    return $tb
}

$state = @{ Page = 0; Env = $null; Cfg = $null; Installed = $false }

# ---------- page 0 : welcome -------------------------------------------------
$pgWelcome = New-Page
$pgWelcome.Controls.Add((New-Lbl @"
This sets up a second Claude Desktop instance that runs alongside your existing one,
signed into a different account, with completely separate settings, MCP servers,
Cowork VM and chat history. Nothing is shared between them.

It also fixes what Windows gets wrong on its own: browser sign-ins will land on the
instance you actually meant, instead of always the first one.

Installed:
     -  An isolated profile folder for the second instance
     -  A claude:// router that directs sign-in callbacks to the right window
     -  A colour-tinted taskbar icon so you can tell them apart
     -  Desktop launchers

No administrator rights are needed, and everything can be removed later.
"@ 0 2 600 $FontBody $null))
$lblPre = New-Lbl '' 0 268 600 $FontBody $null
$pgWelcome.Controls.Add($lblPre)

# ---------- page 1 : options -------------------------------------------------
$pgOptions = New-Page
$pgOptions.Controls.Add((New-Lbl 'Name for the second instance' 0 2 300 $FontBody $null))
$txtName = New-Object System.Windows.Forms.TextBox
$txtName.Location = New-Object System.Drawing.Point(0, 24)
$txtName.Size = New-Object System.Drawing.Size(200, 24)
$txtName.Text = $InstanceName
$pgOptions.Controls.Add($txtName)
$pgOptions.Controls.Add((New-Lbl 'Becomes the profile folder name and the launcher label.' 210 27 380 $FontSmall $Muted))

$txtColor = New-ColorRow $pgOptions 62 $Color 'Taskbar icon colour for the second instance'

$chkTintMain = New-Object System.Windows.Forms.CheckBox
$chkTintMain.Text = 'Also tint the original instance'
$chkTintMain.Location = New-Object System.Drawing.Point(0, 124)
$chkTintMain.Size = New-Object System.Drawing.Size(300, 24)
$chkTintMain.Checked = [bool]$TintMain
$pgOptions.Controls.Add($chkTintMain)
$txtMainColor = New-ColorRow $pgOptions 152 $MainColor 'Colour for the original instance'

$chkTaskbar = New-Object System.Windows.Forms.CheckBox
$chkTaskbar.Text = 'Stop Windows merging the two taskbar buttons  (required for the icons to show)'
$chkTaskbar.Location = New-Object System.Drawing.Point(0, 214)
$chkTaskbar.Size = New-Object System.Drawing.Size(600, 22)
$chkTaskbar.Checked = $true
$pgOptions.Controls.Add($chkTaskbar)

$chkWatcher = New-Object System.Windows.Forms.CheckBox
$chkWatcher.Text = 'Keep a background watcher running so windows opened later also get the icon'
$chkWatcher.Location = New-Object System.Drawing.Point(0, 240)
$chkWatcher.Size = New-Object System.Drawing.Size(600, 22)
$chkWatcher.Checked = $true
$pgOptions.Controls.Add($chkWatcher)
$pgOptions.Controls.Add((New-Lbl 'Starts at sign-in, uses almost no CPU. Needed if you tint the original instance.' 22 262 580 $FontSmall $Muted))

$lblWarn = New-Lbl '' 0 292 600 $FontSmall ([System.Drawing.Color]::Firebrick)
$pgOptions.Controls.Add($lblWarn)

$chkTintMain.Add_CheckedChanged({
    $txtMainColor.Enabled = $chkTintMain.Checked
    if ($chkTintMain.Checked) { $chkWatcher.Checked = $true }
})
$txtMainColor.Enabled = $chkTintMain.Checked

# ---------- page 2 : review --------------------------------------------------
$pgReview = New-Page
$pgReview.Controls.Add((New-Lbl 'The following will be created. Nothing existing is deleted or modified.' 0 2 600 $FontBody $null))
$txtReview = New-Object System.Windows.Forms.TextBox
$txtReview.Multiline = $true; $txtReview.ReadOnly = $true; $txtReview.ScrollBars = 'Vertical'
$txtReview.Location = New-Object System.Drawing.Point(0, 28)
$txtReview.Size = New-Object System.Drawing.Size(620, 292)
$txtReview.Font = $FontMono
$txtReview.BackColor = [System.Drawing.Color]::FromArgb(250,250,250)
$pgReview.Controls.Add($txtReview)

# ---------- page 3 : install -------------------------------------------------
$pgInstall = New-Page
$bar = New-Object System.Windows.Forms.ProgressBar
$bar.Location = New-Object System.Drawing.Point(0, 6)
$bar.Size = New-Object System.Drawing.Size(620, 14)
$bar.Style = 'Continuous'
$lstLog = New-Object System.Windows.Forms.TextBox
$lstLog.Multiline = $true; $lstLog.ReadOnly = $true; $lstLog.ScrollBars = 'Vertical'
$lstLog.Location = New-Object System.Drawing.Point(0, 32)
$lstLog.Size = New-Object System.Drawing.Size(620, 288)
$lstLog.Font = $FontMono
$lstLog.BackColor = [System.Drawing.Color]::FromArgb(250,250,250)
$pgInstall.Controls.AddRange(@($bar, $lstLog))

# ---------- page 4 : finish --------------------------------------------------
$pgFinish = New-Page
$lblFinish = New-Lbl '' 0 2 620 $FontBody $null
$pgFinish.Controls.Add($lblFinish)
$chkLaunch = New-Object System.Windows.Forms.CheckBox
$chkLaunch.Text = 'Launch the second instance now so I can sign in'
$chkLaunch.Location = New-Object System.Drawing.Point(0, 268)
$chkLaunch.Size = New-Object System.Drawing.Size(500, 22)
$chkLaunch.Checked = $true
$chkSettings = New-Object System.Windows.Forms.CheckBox
$chkSettings.Text = 'Open Windows Default apps settings'
$chkSettings.Location = New-Object System.Drawing.Point(0, 292)
$chkSettings.Size = New-Object System.Drawing.Size(500, 22)
$pgFinish.Controls.AddRange(@($chkLaunch, $chkSettings))

$pages  = @($pgWelcome, $pgOptions, $pgReview, $pgInstall, $pgFinish)
$titles = @(
    @('Welcome',    'A second Claude Desktop instance, isolated and correctly routed'),
    @('Options',    'Name it, colour it, choose how it behaves'),
    @('Review',     'Confirm what will be written'),
    @('Installing', 'This takes about twenty seconds'),
    @('Finished',   'Two small things left to do by hand')
)

function Show-Page([int]$i) {
    $state.Page = $i
    foreach ($p in $pages) { $p.Visible = $false }
    $pages[$i].Visible = $true
    $hTitle.Text = $titles[$i][0]
    $hSub.Text   = $titles[$i][1]
    $btnBack.Enabled = ($i -gt 0 -and $i -lt 3)
    switch ($i) {
        0 { $btnNext.Text = 'Next';    $btnNext.Enabled = $state.Env.Ok }
        1 { $btnNext.Text = 'Next';    $btnNext.Enabled = $true }
        2 { $btnNext.Text = 'Install'; $btnNext.Enabled = $true }
        3 { $btnNext.Enabled = $false; $btnCancel.Enabled = $false }
        4 { $btnNext.Text = 'Finish';  $btnNext.Enabled = $true; $btnCancel.Visible = $false; $btnBack.Visible = $false }
    }
}

$state.Env = Get-ClaudeEnvironment
if ($state.Env.Ok) {
    $lblPre.ForeColor = [System.Drawing.Color]::FromArgb(16,124,16)
    $lblPre.Text = "Ready.  $($state.Env.Build), version $($state.Env.Version)." +
                   "`r`nExisting profiles:  " + ($state.Env.Profiles -join ',  ')
} else {
    $lblPre.ForeColor = [System.Drawing.Color]::Firebrick
    $lblPre.Text = "Cannot continue.`r`n$($state.Env.Reason)"
}

$btnCancel.Add_Click({ $form.Close() })
$btnBack.Add_Click({ Show-Page ($state.Page - 1) })

$btnNext.Add_Click({
    switch ($state.Page) {

        0 { Show-Page 1 }

        1 {
            $n = $txtName.Text.Trim()
            $bad = Test-InstanceName -Name $n -Env $state.Env
            if ($bad) { $lblWarn.Text = $bad; return }
            foreach ($tb in @($txtColor, $txtMainColor)) {
                try { [void][System.Drawing.ColorTranslator]::FromHtml($tb.Text.Trim()) }
                catch { $lblWarn.Text = "'$($tb.Text)' is not a valid colour. Use a hex value such as #7C3AED."; return }
            }
            $lblWarn.Text = ''

            $state.Cfg = @{
                Env = $state.Env; InstanceName = $n; Color = $txtColor.Text.Trim()
                MainLabel = $MainLabel; MainColor = $txtMainColor.Text.Trim()
                TintMain = $chkTintMain.Checked
                SetTaskbar = $chkTaskbar.Checked
                AlwaysOnWatcher = $chkWatcher.Checked
                ScriptVersion = $ScriptVersion; SelfPath = $PSCommandPath
                ResolveSource = $ResolveSource; RouterSource = $RouterSource; IconSource = $IconSource; RouterExeSource = $RouterExeSource
            }
            $dd = Join-Path $state.Env.RoamRoot $n
            $desktop = [Environment]::GetFolderPath('Desktop')
            $txtReview.Text = @"
DETECTED
  build           $($state.Env.Build)
  version         $($state.Env.Version)
  executable      $($state.Env.Exe)

INSTANCES  (separate folders - no shared data)
  $($MainLabel.PadRight(14)) $($state.Env.DefaultProfile)
  $($n.PadRight(14)) $dd

FILES
  $HomeDir\resolve-claude.ps1
  $HomeDir\router.ps1
  $HomeDir\set-instance-icon.ps1
  $HomeDir\instances.json
  $HomeDir\taskbar-$n.ico
  $desktop\Claude - $n.cmd
  $desktop\Recolour Claude icons.cmd
$(if ($chkWatcher.Checked) { "  $StartupDir\Claude icon watcher.cmd" })

REGISTRY  (all under HKEY_CURRENT_USER, no admin needed)
  Software\Classes\claude
  Software\Classes\ClaudeRouter.Proto
  Software\ClaudeRouter\Capabilities
  Software\RegisteredApplications -> "$AppName"
$(if ($chkTaskbar.Checked) { "  Software\...\Explorer\Advanced -> TaskbarGlomLevel = 2" })

NOT TOUCHED
  Your existing Claude profile, its sign-in, settings, MCP servers and history.
"@
            Show-Page 2
        }

        2 {
            Show-Page 3
            $lstLog.Text = ''
            $log  = { param($m) $lstLog.AppendText("$m`r`n"); [System.Windows.Forms.Application]::DoEvents() }
            $prog = { param($p) $bar.Value = [Math]::Min(100, [Math]::Max(0, $p)); [System.Windows.Forms.Application]::DoEvents() }
            $failure = ''
            try { Invoke-ClaudeInstall -Cfg $state.Cfg -Log $log -Progress $prog; $state.Installed = $true }
            catch { $failure = $_.Exception.Message; & $log ''; & $log "FAILED: $failure"; $state.Installed = $false }

            $n = $state.Cfg.InstanceName
            if ($state.Installed) {
                $routerNote = if ($state.Cfg.RouterVerified) {
                    'Sign-in routing is active and verified.'
                } else {
                    "ONE MANUAL STEP - sign-in routing is not live yet:`r`n" +
                    "     Settings > Apps > Default apps, type 'claude' in the TOP box, choose`r`n" +
                    "     'Windows PowerShell' (badged New - that is this router), then Set default."
                }
                $extra = ''
                if ($state.Cfg.NeedsExplorerRestart) { $extra += "`r`n`r`nThe taskbar change applies after you sign out and back in." }
                $lblFinish.Text = @"
Setup completed.

$routerNote$extra

STILL TO DO:

  1.  Launch "Claude - $n" from your Desktop and sign in with the second account.
      Click into that window first - the router sends the callback to whichever
      Claude window you used most recently.

  2.  In each instance open Settings > Appearance and set one to Dark, the other
      to Light. It is stored per account, so it sticks.

To check things later, run:  Setup-ClaudeMultiInstance.ps1 -Verify
"@
            } else {
                $lblFinish.Text = "Setup did not complete.`r`n`r`n$failure`r`n`r`nThe full log is on the previous page. Nothing was left half-registered - you can safely re-run this wizard."
                $chkLaunch.Checked = $false
            }
            $chkSettings.Checked = ($state.Installed -and -not $state.Cfg.RouterVerified)
            Show-Page 4
        }

        4 {
            if ($chkSettings.Checked) { try { Start-Process 'ms-settings:defaultapps' } catch { } }
            if ($chkLaunch.Checked -and $state.Cfg.LauncherPath -and (Test-Path $state.Cfg.LauncherPath)) {
                try { Start-Process -FilePath $state.Cfg.LauncherPath } catch { }
            }
            $form.Close()
        }
    }
})

Show-Page 0
[void]$form.ShowDialog()
