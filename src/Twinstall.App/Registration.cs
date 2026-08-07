using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Twinstall.Core;

namespace Twinstall.App
{
    /// <summary>
    /// Shell API for "let the user pick defaults for this registered application". Declared
    /// here rather than in Twinstall.Platform because it drives UI, and Platform observes.
    /// </summary>
    [ComImport]
    [Guid("1f76a169-f994-40ac-8fc8-0959e8874710")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationAssociationRegistrationUI
    {
        void LaunchAdvancedAssociationUI([MarshalAs(UnmanagedType.LPWStr)] string appRegistryName);
    }

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
        internal static bool WeHandle(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Explorer\UrlAssociations\"
                           + scheme + @"\UserChoice"))
                {
                    string progId = key?.GetValue("ProgId") as string;
                    if (!string.IsNullOrEmpty(progId))
                        return string.Equals(progId, ProgIdFor(scheme), StringComparison.OrdinalIgnoreCase);
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
        /// True when nothing at all claims the scheme. In that state, opening a link of that
        /// scheme makes Windows show its own "How do you want to open this?" chooser — which is
        /// a far kinder way to set the default than describing where to click in Settings.
        /// </summary>
        internal static bool NobodyHandles(string scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme)) return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Explorer\UrlAssociations\"
                           + scheme + @"\UserChoice"))
                {
                    if (!string.IsNullOrEmpty(key?.GetValue("ProgId") as string)) return false;
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }

            return string.IsNullOrEmpty(CurrentHandler(scheme));
        }

        /// <summary>
        /// Opens Settings **on Twinstall's own Default-apps page**, where the scheme is listed
        /// with a single "Choose a default" row.
        ///
        /// This replaces two approaches that did not work. Opening a link of the scheme does
        /// nothing at all when nothing is registered for it — ShellExecute returns without
        /// starting anything and without an error, because Windows has no "how do you want to
        /// open this?" chooser for URL protocols the way it does for unknown file types. And
        /// the plain ms-settings:defaultapps page drops the user in front of two search boxes,
        /// where typing the scheme into the wrong one silently reports finding nothing.
        ///
        /// This is the documented API for the job, and it lands exactly where it should.
        ///
        /// It blocks until the user closes Settings, so callers must not run it on the UI
        /// thread.
        /// </summary>
        internal static bool OpenOurDefaultsPage()
        {
            try
            {
                Type t = Type.GetTypeFromCLSID(AssociationUiClsid);
                if (t == null) return false;

                object o = Activator.CreateInstance(t);
                var ui = o as IApplicationAssociationRegistrationUI;
                if (ui == null) return false;

                try
                {
                    Log.Write("opening the Default apps page for " + AppPaths.ProductName);
                    ui.LaunchAdvancedAssociationUI(AppPaths.ProductName);
                    return true;
                }
                finally { Marshal.ReleaseComObject(ui); }
            }
            catch (COMException ex) { Log.Write("association UI failed: " + ex.Message); return false; }
            catch (InvalidCastException ex) { Log.Write("association UI failed: " + ex.Message); return false; }
            catch (NotSupportedException ex) { Log.Write("association UI failed: " + ex.Message); return false; }
        }

        private static readonly Guid AssociationUiClsid = new Guid("1968106d-f3b5-44cf-890e-116fcb9ecef1");

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
        internal static int CreateShortcuts(AppConfig cfg)
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

                foreach (Instance inst in cfg.Instances)
                {
                    string linkPath = Path.Combine(ShortcutFolder, AppPaths.Sanitise(inst.Name) + ".lnk");
                    object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                        null, shell, new object[] { linkPath }, CultureInfo.InvariantCulture);
                    if (link == null) continue;

                    Type linkType = link.GetType();
                    Set(linkType, link, "TargetPath", AppPaths.SelfExe);
                    Set(linkType, link, "Arguments", "--launch \"" + inst.Name + "\"");
                    Set(linkType, link, "Description", inst.Name + " (via Twinstall)");
                    Set(linkType, link, "WorkingDirectory", AppPaths.Home);

                    string ico = AppPaths.IconFor(inst.Name);
                    if (File.Exists(ico)) Set(linkType, link, "IconLocation", ico + ",0");

                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null,
                        CultureInfo.InvariantCulture);
                    made++;
                }
                Log.Write("created " + made.ToString(CultureInfo.InvariantCulture) + " shortcut(s)");
            }
            catch (TargetInvocationException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (MissingMethodException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            catch (IOException ex) { Log.Write("shortcut creation failed: " + ex.Message); }
            return made;
        }

        private static void Set(Type type, object target, string property, string value)
        {
            type.InvokeMember(property, BindingFlags.SetProperty, null, target,
                new object[] { value }, CultureInfo.InvariantCulture);
        }

        internal static void RemoveShortcuts()
        {
            try
            {
                if (Directory.Exists(ShortcutFolder)) Directory.Delete(ShortcutFolder, recursive: true);
            }
            catch (IOException ex) { Log.Write("could not remove shortcuts: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("could not remove shortcuts: " + ex.Message); }
        }

        /// <summary>A plain-language list of what setup will change. Shown before it happens.</summary>
        internal static IList<string> DescribeChanges(AppConfig cfg, bool taskbarOptIn)
        {
            var lines = new List<string>
            {
                "Write " + AppPaths.ConfigFile,
                "Compose badged icons into " + AppPaths.IconsDir,
                "Add a Start-menu shortcut for each account",
                "Register Twinstall as a candidate handler for " + (cfg?.Scheme ?? "(none)") + "://"
            };

            if (!string.IsNullOrWhiteSpace(cfg?.Scheme))
                lines.Add("Open Settings so you can choose Twinstall for " + cfg.Scheme + ":// yourself");

            if (taskbarOptIn)
                lines.Add("Set \"Combine taskbar buttons\" to Never - affects ALL apps, needs a sign-out");

            return lines;
        }
    }
}
