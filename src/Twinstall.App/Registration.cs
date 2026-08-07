using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using Twinstall.Core;

namespace Twinstall.App
{

    /// <summary>
    /// Everything Twinstall changes outside its own folder, in one place, so it can all be
    /// undone in one place.
    ///
    /// Protocol association goes through <c>RegisteredApplications</c> — a ProgId plus a
    /// UrlAssociations capability — and then the *user* chooses Twinstall in Settings. It
    /// deliberately does not write <c>HKCU\Software\Classes\&lt;scheme&gt;</c> directly. That
    /// shortcut is what the predecessor did; it is the part of Store policy 10.2.8 this would
    /// fail, and it loses to an MSIX app's declared protocol anyway. The sanctioned route was
    /// verified working on a real machine.
    /// </summary>
    internal static class Registration
    {
        private const string CapabilitiesPath = @"Software\Twinstall\Capabilities";
        private const string RegisteredAppsPath = @"Software\RegisteredApplications";
        private const string ClassesPath = @"Software\Classes";
        private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string GlomLevel = "TaskbarGlomLevel";

        internal static string ProgIdFor(string scheme)
        {
            return "Twinstall.Url." + (scheme ?? string.Empty).ToLowerInvariant();
        }

        // ------------------------------------------------------------- protocol --
        internal static bool RegisterProtocol(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;

            string progId = ProgIdFor(scheme);
            string command = "\"" + AppPaths.SelfExe + "\" \"%1\"";

            try
            {
                using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
                using (RegistryKey prog = classes.CreateSubKey(progId))
                {
                    prog.SetValue(null, "Twinstall link (" + scheme + ")");
                    prog.SetValue("URL Protocol", string.Empty);

                    using (RegistryKey cmd = prog.CreateSubKey(@"shell\open\command"))
                        cmd.SetValue(null, command);
                }

                using (RegistryKey caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
                {
                    caps.SetValue("ApplicationName", AppPaths.ProductName);
                    caps.SetValue("ApplicationDescription",
                        "Routes sign-in links to the right copy of a desktop app.");
                    using (RegistryKey urls = caps.CreateSubKey("UrlAssociations"))
                        urls.SetValue(scheme, progId);
                }

                using (RegistryKey reg = Registry.CurrentUser.CreateSubKey(RegisteredAppsPath))
                    reg.SetValue(AppPaths.ProductName, CapabilitiesPath);

                Log.Write("registered as a candidate handler for " + scheme + "://");
                return true;
            }
            catch (UnauthorizedAccessException ex) { Log.Write("register failed: " + ex.Message); return false; }
            catch (System.Security.SecurityException ex) { Log.Write("register failed: " + ex.Message); return false; }
            catch (IOException ex) { Log.Write("register failed: " + ex.Message); return false; }
        }

        internal static void UnregisterProtocol(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return;
            try
            {
                using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(ClassesPath, true))
                    classes?.DeleteSubKeyTree(ProgIdFor(scheme), throwOnMissingSubKey: false);

                using (RegistryKey caps = Registry.CurrentUser.OpenSubKey(CapabilitiesPath + @"\UrlAssociations", true))
                    if (caps != null && caps.GetValue(scheme) != null) caps.DeleteValue(scheme, throwOnMissingValue: false);

                Log.Write("unregistered " + scheme + "://");
            }
            catch (UnauthorizedAccessException ex) { Log.Write("unregister failed: " + ex.Message); }
            catch (System.Security.SecurityException ex) { Log.Write("unregister failed: " + ex.Message); }
            catch (IOException ex) { Log.Write("unregister failed: " + ex.Message); }
        }

        internal static void RemoveAllRegistration(AppConfig cfg)
        {
            if (cfg != null) UnregisterProtocol(cfg.Scheme);
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(RegisteredAppsPath, true))
                    if (reg != null && reg.GetValue(AppPaths.ProductName) != null)
                        reg.DeleteValue(AppPaths.ProductName, throwOnMissingValue: false);

                using (RegistryKey sw = Registry.CurrentUser.OpenSubKey("Software", true))
                    sw?.DeleteSubKeyTree("Twinstall", throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException ex) { Log.Write("cleanup failed: " + ex.Message); }
            catch (System.Security.SecurityException ex) { Log.Write("cleanup failed: " + ex.Message); }
            catch (IOException ex) { Log.Write("cleanup failed: " + ex.Message); }
        }

        /// <summary>Who currently handles the scheme, as a display string.</summary>
        internal static string CurrentHandler(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           ClassesPath + "\\" + scheme + @"\shell\open\command"))
                {
                    if (key != null) return key.GetValue(null) as string;
                }
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                           ClassesPath + "\\" + scheme + @"\shell\open\command"))
                {
                    if (key != null) return key.GetValue(null) as string;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return null;
        }

        /// <summary>
        /// True when Windows will actually hand this scheme to us.
        ///
        /// Two places have to be consulted, and the second is the one that matters. Choosing an
        /// app in Settings does not rewrite HKCU\Software\Classes\&lt;scheme&gt; — it writes a
        /// UserChoice pointing at a ProgId, and that takes precedence. Checking only the class
        /// key reports "not the handler" immediately after the user has successfully set us as
        /// the handler, which is worse than not checking at all.
        /// </summary>
        private const string UrlAssociations = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations";

        private static RegistryKey SafeOpen(RegistryKey parent, string name)
        {
            try { return parent.OpenSubKey(name); }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (IOException) { return null; }
        }

        internal static bool WeHandle(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;
            string wanted = ProgIdFor(scheme);

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UrlAssociations + "\\" + scheme))
                {
                    if (key != null && HasProgId(key, wanted, 0)) return true;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            string cmd = CurrentHandler(scheme);
            if (string.IsNullOrEmpty(cmd)) return false;
            return PathUtil.SamePath(SchemeMatcher.ExtractExecutable(cmd), AppPaths.SelfExe);
        }

        /// <summary>
        /// Looks for our ProgId anywhere under the scheme's association key, rather than at one
        /// exact path — because Microsoft keeps moving it.
        ///
        /// Measured on Windows 11, 7 Aug 2026, after choosing Twinstall in Settings:
        ///
        ///   ...\Shell\Associations\UrlAssociations\claude\UserChoiceLatest\ProgId  ProgId = Twinstall.Url.claude
        ///
        /// Note all three surprises. It is <b>UserChoiceLatest</b>, not UserChoice, which held
        /// only a Hash. The ProgId is a level deeper still, in a subkey of the same name. And
        /// existing associations on the same machine use the old shape — https had both, with
        /// the ProgId under UserChoice — so a check written against either layout alone is
        /// right for some schemes and wrong for others.
        ///
        /// The cost of getting this wrong is not cosmetic: the final screen told a user their
        /// setup had failed at the exact moment it had succeeded, while a claude:// link was
        /// already routing correctly.
        /// </summary>
        private static bool HasProgId(RegistryKey key, string wanted, int depth)
        {
            if (depth > 3) return false;   // the nesting is shallow; this only stops runaway

            if (string.Equals(key.GetValue("ProgId") as string, wanted, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string name in key.GetSubKeyNames())
            {
                using (RegistryKey child = SafeOpen(key, name))
                    if (child != null && HasProgId(child, wanted, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when nothing at all claims the scheme. In that state, opening a link of that
        /// scheme makes Windows show its own "How do you want to open this?" chooser — which is
        /// a far kinder way to set the default than describing where to click in Settings.
        /// </summary>
        internal static bool NobodyHandles(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UrlAssociations + "\\" + scheme))
                {
                    if (key != null && HasAnyProgId(key, 0)) return false;
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }

            return string.IsNullOrEmpty(CurrentHandler(scheme));
        }

        private static bool HasAnyProgId(RegistryKey key, int depth)
        {
            if (depth > 3) return false;
            if (!string.IsNullOrEmpty(key.GetValue("ProgId") as string)) return true;

            foreach (string name in key.GetSubKeyNames())
            {
                using (RegistryKey child = SafeOpen(key, name))
                    if (child != null && HasAnyProgId(child, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// Opens Settings **on Twinstall's own Default-apps page**, where the scheme is the only
        /// thing listed and "Choose a default" is one click away.
        ///
        /// Three approaches were tried; only this one works, and the failures are recorded so
        /// they are not attempted again:
        ///
        /// - Opening a link of the scheme does nothing at all while nothing is registered for
        ///   it. ShellExecute returns without starting anything and without raising an error —
        ///   Windows has no "how do you want to open this?" chooser for URL protocols, only for
        ///   unknown file types.
        /// - IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI, which is the
        ///   API documented for exactly this, now shows a message box reading "To change your
        ///   default apps, go to Settings > Apps > Default apps" and opens nothing. It is
        ///   deprecated in all but name.
        /// - Plain ms-settings:defaultapps lands on a page with two search boxes, where typing
        ///   the scheme into the wrong one reports finding nothing.
        ///
        /// The registeredAppUser parameter is the value name under RegisteredApplications, so
        /// it must stay in step with <see cref="RegisterProtocol"/>.
        /// </summary>
        internal static bool OpenOurDefaultsPage()
        {
            string uri = "ms-settings:defaultapps?registeredAppUser="
                       + Uri.EscapeDataString(AppPaths.ProductName);

            if (Launch(uri))
            {
                Log.Write("opened the Default apps page for " + AppPaths.ProductName);
                return true;
            }

            // Better the generic page than nothing at all.
            Log.Write("deep link failed; falling back to the generic Default apps page");
            return Launch("ms-settings:defaultapps");
        }

        private static bool Launch(string uri)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })) { }
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("could not open " + uri + ": " + ex.Message); return false; }
            catch (InvalidOperationException ex) { Log.Write("could not open " + uri + ": " + ex.Message); return false; }
        }

        /// <summary>
        /// Fires the app's own scheme once, to prove routing works end to end. Only meaningful
        /// after the handler has been set; before that Windows silently does nothing.
        /// </summary>
        internal static bool SelfTest(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;
            try
            {
                using (Process p = Process.Start(
                           new ProcessStartInfo(scheme + "://" + SelfTestHost) { UseShellExecute = true }))
                {
                    Log.Write("self-test link opened for " + scheme + "://");
                    return p != null;
                }
            }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("self-test failed: " + ex.Message); return false; }
            catch (InvalidOperationException ex) { Log.Write("self-test failed: " + ex.Message); return false; }
        }

        /// <summary>
        /// The host used by the self-test link. Distinctive enough that no real callback will
        /// collide with it, so the router can recognise it and stop rather than forward.
        /// </summary>
        internal const string SelfTestHost = "twinstall-selftest";

        internal static void OpenDefaultAppsSettings()
        {
            try
            {
                using (Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("could not open Settings: " + ex.Message); }
            catch (InvalidOperationException ex) { Log.Write("could not open Settings: " + ex.Message); }
        }

        // ------------------------------------------------------- taskbar setting --
        /// <summary>
        /// Per-window taskbar icons only appear with "Combine taskbar buttons: Never"
        /// (TaskbarGlomLevel = 2), and the change only takes effect after a sign-out.
        ///
        /// This is a Windows-wide setting that affects every application, not just the one
        /// being configured, so it is never written silently — Store policy 10.2.8 requires an
        /// explicit, clearly-labelled opt-in and it is the right behaviour regardless.
        /// </summary>
        internal static bool TaskbarNeverCombine()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ExplorerAdvanced))
                {
                    object v = key?.GetValue(GlomLevel);
                    return v != null && Convert.ToInt32(v, CultureInfo.InvariantCulture) == 2;
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (FormatException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        internal static bool SetTaskbarNeverCombine(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ExplorerAdvanced))
                    key.SetValue(GlomLevel, enabled ? 2 : 0, RegistryValueKind.DWord);

                Log.Write("TaskbarGlomLevel set to " + (enabled ? "2 (never combine)" : "0 (always combine)"));
                return true;
            }
            catch (UnauthorizedAccessException ex) { Log.Write("taskbar setting failed: " + ex.Message); return false; }
            catch (System.Security.SecurityException ex) { Log.Write("taskbar setting failed: " + ex.Message); return false; }
        }

        // ------------------------------------------------------------ shortcuts --
        private static string ShortcutFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppPaths.ProductName);
            }
        }

        /// <summary>
        /// A Start-menu shortcut per instance. Without these there is no way to open the
        /// second account at all — the app's own shortcut always opens the first.
        ///
        /// Created through WScript.Shell by late binding rather than a COM reference, which
        /// keeps the build free of an interop assembly for four lines of work.
        /// </summary>
        internal static int CreateShortcuts(AppConfig cfg, bool alsoOnDesktop)
        {
            if (cfg == null || cfg.Instances.Count == 0) return 0;

            int made = 0;
            try
            {
                Directory.CreateDirectory(ShortcutFolder);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) { Log.Write("WScript.Shell unavailable; no shortcuts created"); return 0; }

                object shell = Activator.CreateInstance(shellType);
                if (shell == null) return 0;

                string app = AppLabel(cfg);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                foreach (Instance inst in cfg.Instances)
                {
                    // In the Start menu they sit inside a Twinstall folder, so the account name
                    // alone is unambiguous. On the desktop they sit among everything else, so
                    // they carry the application name too — a bare "Second" on someone's
                    // desktop means nothing a week later.
                    if (Write(shellType, shell, Path.Combine(ShortcutFolder, AppPaths.Sanitise(inst.Name) + ".lnk"),
                              inst, app)) made++;

                    if (alsoOnDesktop && Directory.Exists(desktop))
                    {
                        Write(shellType, shell,
                              Path.Combine(desktop, AppPaths.Sanitise(app + " - " + inst.Name) + ".lnk"),
                              inst, app);
                    }
                }
                Log.Write("created " + made.ToString(CultureInfo.InvariantCulture) + " Start-menu shortcut(s)"
                          + (alsoOnDesktop ? " and matching desktop shortcuts" : string.Empty));
            }
            catch (TargetInvocationException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (MissingMethodException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (IOException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            return made;
        }

        private static bool Write(Type shellType, object shell, string linkPath, Instance inst, string app)
        {
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { linkPath }, CultureInfo.InvariantCulture);
            if (link == null) return false;

            Type linkType = link.GetType();
            Set(linkType, link, "TargetPath", AppPaths.SelfExe);
            Set(linkType, link, "Arguments", "--launch \"" + inst.Name + "\"");
            Set(linkType, link, "Description", app + " — " + inst.Name + " (via Twinstall)");
            Set(linkType, link, "WorkingDirectory", AppPaths.Home);

            string ico = AppPaths.IconFor(inst.Name);
            if (File.Exists(ico)) Set(linkType, link, "IconLocation", ico + ",0");

            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>The target application's name, for labelling shortcuts.</summary>
        internal static string AppLabel(AppConfig cfg)
        {
            string name = cfg == null ? null : Path.GetFileNameWithoutExtension(cfg.ExePath);
            if (string.IsNullOrEmpty(name)) return "App";
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static void Set(Type type, object target, string property, string value)
        {
            type.InvokeMember(property, BindingFlags.SetProperty, null, target,
                new object[] { value }, CultureInfo.InvariantCulture);
        }

        internal static void RemoveShortcuts(AppConfig cfg)
        {
            try
            {
                if (Directory.Exists(ShortcutFolder)) Directory.Delete(ShortcutFolder, recursive: true);
            }
            catch (IOException ex) { Log.Write("could not remove shortcuts: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("could not remove shortcuts: " + ex.Message); }

            // Desktop shortcuts are removed by exact name only. A wildcard sweep of someone's
            // desktop is not a risk worth taking to save a few lines.
            if (cfg == null) return;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string app = AppLabel(cfg);

            foreach (Instance inst in cfg.Instances)
            {
                string link = Path.Combine(desktop, AppPaths.Sanitise(app + " - " + inst.Name) + ".lnk");
                try { if (File.Exists(link)) File.Delete(link); }
                catch (IOException ex) { Log.Write("could not remove " + link + ": " + ex.Message); }
                catch (UnauthorizedAccessException ex) { Log.Write("could not remove " + link + ": " + ex.Message); }
            }
        }

        /// <summary>A plain-language list of what setup will change. Shown before it happens.</summary>
        internal static IList<string> DescribeChanges(AppConfig cfg, bool taskbarOptIn, bool desktopShortcuts)
        {
            var lines = new List<string>
            {
                "Write " + AppPaths.ConfigFile,
                "Compose badged icons into " + AppPaths.IconsDir,
                "Add a Start-menu shortcut for each account",
                "Register Twinstall as a candidate handler for " + (cfg?.Scheme ?? "(none)") + "://"
            };

            if (desktopShortcuts)
                lines.Insert(3, "Add a desktop shortcut for each account");

            if (!string.IsNullOrWhiteSpace(cfg?.Scheme))
                lines.Add("Open Settings so you can choose Twinstall for " + cfg.Scheme + ":// yourself");

            if (taskbarOptIn)
                lines.Add("Set \"Combine taskbar buttons\" to Never - affects ALL apps, needs a sign-out");

            return lines;
        }
    }
}
