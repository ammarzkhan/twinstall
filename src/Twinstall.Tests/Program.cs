using System;
using System.Collections.Generic;
using Twinstall.Core;

// Plain console runner rather than a test framework: it runs identically under `dotnet run`
// in CI and under mono locally, with no package restore.
static class Tests
{
    static readonly string[] Crlf = { "\r\n" };

    static int passed, failed;
    static readonly List<string> failures = new List<string>();

    static void Check(bool condition, string name)
    {
        if (condition) { passed++; }
        else { failed++; failures.Add(name); }
    }

    static void Eq(object actual, object expected, string name)
    {
        bool ok = (actual == null && expected == null) ||
                  (actual != null && actual.Equals(expected));
        if (!ok) failures.Add(name + "  (expected <" + expected + "> got <" + actual + ">)");
        if (ok) passed++; else failed++;
    }

    // ---------------------------------------------------------------- paths --
    static void PathTests()
    {
        Check(PathUtil.SamePath(@"C:\a\b", @"C:\a\b\"), "trailing separator ignored");
        Check(PathUtil.SamePath(@"C:\A\B", @"c:\a\b"), "case-insensitive compare");
        Check(PathUtil.IsInside(@"C:\a\b\c", @"C:\a\b"), "nested path detected");
        Check(!PathUtil.IsInside(@"C:\a\b", @"C:\a\b"), "a path is not inside itself");

        // The bug that shipped in the Claude-specific version: substring matching meant
        // 'work' and 'work2' were treated as the same instance.
        Check(!PathUtil.IsInside(@"C:\x\work2", @"C:\x\work"), "work2 is NOT inside work");
        Check(!PathUtil.SamePath(@"C:\x\work", @"C:\x\work2"), "work and work2 are different");

        Eq(PathUtil.Join(null), "", "null parts joins to empty rather than throwing");
    }

    // ------------------------------------------------------------- isolation --
    static void IsolationTests()
    {
        Check(IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\second"), "siblings are isolated");
        Check(IsolationCheck.IsValid(@"C:\p\work", @"C:\p\work2"), "work / work2 accepted");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\Claude"), "identical rejected");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude", @"C:\p\Claude\sub"), "child rejected");
        Check(!IsolationCheck.IsValid(@"C:\p\Claude\sub", @"C:\p\Claude"), "parent rejected");
        Check(!IsolationCheck.IsValid("", @"C:\p\x"), "empty rejected");
    }

    // ---------------------------------------------------------- command line --
    static void CommandLineTests()
    {
        Eq(CommandLine.ExtractDataDir("\"C:\\a\\App.exe\" --user-data-dir=\"C:\\p\\second\"", @"C:\p\Default"),
           @"C:\p\second", "quoted value");
        Eq(CommandLine.ExtractDataDir(@"C:\a\App.exe --user-data-dir=C:\p\second --other", @"C:\p\Default"),
           @"C:\p\second", "bare value stops at space");
        Eq(CommandLine.ExtractDataDir("\"C:\\a\\App.exe\" --user-data-dir=\"C:\\my profile\\x\"", @"C:\p\Default"),
           @"C:\my profile\x", "quoted value containing spaces");
        Eq(CommandLine.ExtractDataDir(@"C:\a\App.exe", @"C:\p\Default"),
           @"C:\p\Default", "absent flag falls back to default");
        Eq(CommandLine.ExtractDataDir(null, @"C:\p\Default"), @"C:\p\Default", "null command line");

        Check(CommandLine.IsChildProcess(@"App.exe --type=renderer"), "renderer detected");
        Check(!CommandLine.IsChildProcess(@"App.exe --user-data-dir=C:\p\x"), "main process not a child");
    }

    // ------------------------------------------------------------- packaging --
    static void PackageTests()
    {
        Eq(PackagePaths.FamilyFromFullName("Claude_1.24012.11.0_x64__pzs8sxrjxfjjc"),
           "Claude_pzs8sxrjxfjjc", "family name derived from full name");
        Eq(PackagePaths.FamilyFromFullName("Nonsense"), null, "malformed full name rejected");

        const string packaged = @"C:\Program Files\WindowsApps\Claude_1.24012.11.0_x64__pzs8sxrjxfjjc\app\Claude.exe";
        const string plain    = @"C:\Users\a\AppData\Local\Programs\slack\slack.exe";

        Check(PackagePaths.IsPackaged(packaged), "WindowsApps path is packaged");
        Check(!PackagePaths.IsPackaged(plain), "Programs path is unpackaged");

        Eq(PackagePaths.ProfileRoot(packaged, @"C:\Users\a\AppData\Local", @"C:\Users\a\AppData\Roaming"),
           @"C:\Users\a\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming",
           "packaged profile root is virtualised");
        Eq(PackagePaths.ProfileRoot(plain, @"C:\Users\a\AppData\Local", @"C:\Users\a\AppData\Roaming"),
           @"C:\Users\a\AppData\Roaming",
           "unpackaged profile root is roaming appdata");
    }

    // -------------------------------------------------------------- chromium --
    static void ChromiumTests()
    {
        var electron = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(@"C:\app", @"resources\app.asar"),
            PathUtil.Join(@"C:\app", "icudtl.dat"),
            PathUtil.Join(@"C:\app", "chrome_100_percent.pak")
        };
        var webview2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"C:\t\WebView2Loader.dll"
        };
        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);

        Check(ChromiumDetector.IsChromiumApp(@"C:\app\App.exe", fs(electron)), "electron app detected");
        Check(!ChromiumDetector.IsChromiumApp(@"C:\t\Teams.exe", fs(webview2)), "webview2 app rejected");
        Check(!ChromiumDetector.IsChromiumApp(@"C:\n\Notepad.exe", fs(new HashSet<string>())), "plain win32 rejected");
        Eq(ChromiumDetector.Score(@"C:\app\App.exe", fs(electron)), 3, "marker score counted");
    }

    // --------------------------------------------------------------- routing --
    static void RoutingTests()
    {
        var insts = new List<Instance> {
            new Instance { Name = "Main",   DataDir = @"C:\p\Claude" },
            new Instance { Name = "Second", DataDir = @"C:\p\second" }
        };

        var bothRunning = new Dictionary<int, string> { { 100, "Main" }, { 200, "Second" } };

        var r = RouteDecision.Choose(insts, bothRunning, new List<int> { 999, 200, 100 });
        Eq(r.Target, "Second", "z-order picks the most recently used window");
        Eq(r.Reason, RouteReason.ZOrder, "reason is z-order");

        r = RouteDecision.Choose(insts, bothRunning, new List<int> { 100, 200 });
        Eq(r.Target, "Main", "z-order flips when the other window is on top");

        r = RouteDecision.Choose(insts, new Dictionary<int, string> { { 200, "Second" } }, new List<int>());
        Eq(r.Reason, RouteReason.OnlyOneRunning, "single running instance wins without z-order");
        Eq(r.Target, "Second", "single running instance is the target");

        r = RouteDecision.Choose(insts, new Dictionary<int, string>(), new List<int>());
        Eq(r.Reason, RouteReason.NeedsPrompt, "nothing running asks the user");
        Eq(r.Target, null, "prompt has no target");

        r = RouteDecision.Choose(new List<Instance>(), null, null);
        Eq(r.Reason, RouteReason.NoInstances, "no configuration is reported, not guessed");

        // several pids of the same instance must not look like ambiguity
        var oneInstanceTwoPids = new Dictionary<int, string> { { 1, "Second" }, { 2, "Second" } };
        r = RouteDecision.Choose(insts, oneInstanceTwoPids, new List<int> { 2, 1 });
        Eq(r.Reason, RouteReason.OnlyOneRunning, "multiple pids of one instance is not ambiguous");
    }

    // ---------------------------------------------------------------- config --
    static void ConfigTests()
    {
        string[] lines = {
            "# comment",
            "EXE\tC:\\app\\App.exe",
            "SCHEME\tslack",
            "DEFAULT\tC:\\p\\Slack",
            "Work\tC:\\p\\work\t#059669\t1",
            "Personal\tC:\\p\\personal\t#7C3AED\t0",
            "malformed"
        };
        AppConfig cfg = InstanceConfig.Parse(lines);
        Eq(cfg.Scheme, "slack", "scheme parsed");
        Eq(cfg.Instances.Count, 2, "malformed and comment lines skipped");
        Eq(cfg.Instances[0].Name, "Work", "instance name parsed");
        Eq(cfg.Instances[0].Badge, true, "badge flag parsed");
        Eq(cfg.Instances[1].Badge, false, "badge flag false parsed");

        AppConfig round = InstanceConfig.Parse(
            InstanceConfig.Serialise(cfg).Split(Crlf, StringSplitOptions.RemoveEmptyEntries));
        Eq(round.Instances.Count, 2, "round trip keeps instances");
        Eq(round.Scheme, "slack", "round trip keeps scheme");
        Eq(round.Instances[0].Colour, "#059669", "round trip keeps colour");

        Eq(InstanceConfig.Parse(null).Instances.Count, 0, "null input is safe");
    }

    // --------------------------------------------------------- launch probe --
    static void LaunchProbeTests()
    {
        const string temp = @"C:\Users\a\AppData\Local\Temp";
        string dir = LaunchProbe.ProbeDirectory(temp, "abc123");
        Eq(dir, @"C:\Users\a\AppData\Local\Temp\Twinstall\probe-abc123", "probe directory derived");

        // A token is never user-supplied today, but this path is handed to another vendor's
        // exe as somewhere to write, so escaping it must be impossible rather than unlikely.
        Eq(LaunchProbe.ProbeDirectory(temp, @"..\..\Windows"), "", "token with separators rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, "a.b"), "", "token with a dot rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, "C:x"), "", "token with a drive letter rejected");
        Eq(LaunchProbe.ProbeDirectory("", "abc"), "", "empty temp root rejected");
        Eq(LaunchProbe.ProbeDirectory(temp, null), "", "null token rejected");

        // The launcher writes the flag and ProcessMap reads it back; they must agree.
        Eq(LaunchProbe.Arguments(dir), "--user-data-dir=\"" + dir + "\"", "arguments are quoted");
        Eq(CommandLine.ExtractDataDir(LaunchProbe.Arguments(dir), @"C:\fallback"), dir,
           "arguments round-trip through ExtractDataDir");
        const string spaced = @"C:\Users\Ammar Khan\AppData\Local\Temp\Twinstall\probe-x";
        Eq(CommandLine.ExtractDataDir(LaunchProbe.Arguments(spaced), @"C:\fallback"), spaced,
           "a path containing spaces survives the round trip");
        Eq(LaunchProbe.Arguments(""), "", "no arguments without a directory");

        Func<HashSet<string>, Func<string, bool>> fs = set => p => set.Contains(p);
        var populated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(dir, "Local State")
        };
        var lateMarker = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            PathUtil.Join(dir, @"Default\Preferences")
        };
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Check(LaunchProbe.ProfileCreated(dir, fs(populated)), "Local State counts as created");
        Check(LaunchProbe.ProfileCreated(dir, fs(lateMarker)), "Default\\Preferences counts as created");
        Check(!LaunchProbe.ProfileCreated(dir, fs(empty)), "empty directory is not created");
        Check(!LaunchProbe.ProfileCreated(dir, null), "null probe is not created");

        Eq(LaunchProbe.Evaluate(dir, fs(populated), 1, 30), ProbeVerdict.Honoured, "marker means honoured");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 1, 30), ProbeVerdict.Pending, "no marker yet is pending");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 30, 30), ProbeVerdict.NotHonoured, "timeout means not honoured");

        // An app that writes its profile exactly as the window closes is still supported.
        Eq(LaunchProbe.Evaluate(dir, fs(populated), 30, 30), ProbeVerdict.Honoured,
           "creation is checked before the clock");

        Eq(LaunchProbe.Evaluate(dir, null, 1, 30), ProbeVerdict.Invalid, "no file probe is invalid");
        Eq(LaunchProbe.Evaluate(dir, fs(empty), 1, 0), ProbeVerdict.Invalid, "zero timeout is invalid");
        Eq(LaunchProbe.Evaluate("", fs(empty), 1, 30), ProbeVerdict.Invalid, "no directory is invalid");

        // The probe launches the real app, so it must never point at real data.
        var live = new List<string> { @"C:\p\Claude", @"C:\p\second" };
        Check(LaunchProbe.IsSafeTarget(dir, live), "a temp probe directory is safe");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p\Claude", live), "probing a live profile is refused");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p\Claude\probe", live), "probing inside a live profile is refused");
        Check(!LaunchProbe.IsSafeTarget(@"C:\p", live), "a probe containing a live profile is refused");
        Check(LaunchProbe.IsSafeTarget(@"C:\p\Claude2", live), "Claude2 is not Claude");
        Check(LaunchProbe.IsSafeTarget(dir, null), "no known profiles is safe");
        Check(!LaunchProbe.IsSafeTarget("", live), "an empty probe directory is refused");
    }

    static int Main()
    {
        PathTests();
        IsolationTests();
        CommandLineTests();
        PackageTests();
        ChromiumTests();
        RoutingTests();
        ConfigTests();
        LaunchProbeTests();

        Console.WriteLine();
        Console.WriteLine("passed: " + passed + "   failed: " + failed);
        if (failed > 0)
        {
            Console.WriteLine();
            foreach (string f in failures) Console.WriteLine("  FAIL  " + f);
            return 1;
        }
        Console.WriteLine("all green");
        return 0;
    }
}
