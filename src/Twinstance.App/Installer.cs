using System;
using System.Diagnostics;
using System.IO;
using Twinstance.Core;

namespace Twinstance.App
{
    /// <summary>
    /// Puts Twinstance where an installed program lives, on first run.
    ///
    /// Without this, "installing" meant downloading a zip, unpacking it somewhere, finding the
    /// executable among a handful of DLLs, and running it from wherever it happened to land —
    /// usually Downloads, which people empty. That matters more here than for most tools,
    /// because the Start-menu shortcuts and the registered protocol handler both record an
    /// absolute path: move or delete the folder afterwards and sign-in routing silently stops
    /// working, with nothing to indicate why.
    ///
    /// So the first thing a freshly-downloaded copy does is offer to move in properly.
    /// </summary>
    internal static class Installer
    {
        internal static string InstallDirectory
        {
            get { return PathUtil.Join(AppPaths.LocalAppData, "Programs", AppPaths.ProductName); }
        }

        internal static string InstalledExe
        {
            get { return PathUtil.Join(InstallDirectory, "Twinstance.exe"); }
        }

        /// <summary>True when this copy is running from somewhere other than its install folder.</summary>
        internal static bool RunningFromElsewhere()
        {
            string here = Path.GetDirectoryName(AppPaths.SelfExe);
            return !string.IsNullOrEmpty(here) && !PathUtil.SamePath(here, InstallDirectory);
        }

        /// <summary>
        /// Offers to install, and if accepted copies everything, registers, and relaunches from
        /// the new location. Returns true when the caller should exit because the installed
        /// copy has taken over.
        /// </summary>
        internal static bool OfferInstall()
        {
            if (!RunningFromElsewhere()) return false;

            bool upgrade = File.Exists(InstalledExe);
            string question = upgrade
                ? "Twinstance is already installed.\r\n\r\nReplace it with this copy?\r\n\r\n"
                  + InstallDirectory + "\r\n\r\nYour accounts and settings are kept."
                : "Install Twinstance?\r\n\r\nIt will be copied to:\r\n\r\n" + InstallDirectory
                  + "\r\n\r\nThat is inside your own user folder — no administrator prompt, and "
                  + "nothing is written outside your profile. It will appear in the Start menu "
                  + "and in Settings › Apps, where you can remove it later.\r\n\r\n"
                  + "Choose No to run this copy as-is instead.";

            if (!Ui.Confirm(question)) return false;

            try
            {
                CopyProgram();
            }
            catch (IOException ex)
            {
                Ui.Error("Could not install to:\r\n\r\n" + InstallDirectory + "\r\n\r\n" + ex.Message
                       + "\r\n\r\nTwinstance will keep running from where it is.");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Ui.Error("Could not install to:\r\n\r\n" + InstallDirectory + "\r\n\r\n" + ex.Message
                       + "\r\n\r\nTwinstance will keep running from where it is.");
                return false;
            }

            Log.Write("installed to " + InstallDirectory);
            return HandOver();
        }

        private static void CopyProgram()
        {
            string from = Path.GetDirectoryName(AppPaths.SelfExe);
            if (string.IsNullOrEmpty(from)) throw new IOException("cannot determine the source folder");

            Directory.CreateDirectory(InstallDirectory);

            // Copies siblings as well as the executable, so both layouts work: a single-file
            // build is one file, the framework-dependent one is a handful of DLLs plus
            // presets\apps.json.
            foreach (string file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(InstallDirectory, name), overwrite: true);
            }

            foreach (string dir in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(dir);
                // Only the folders the app actually reads. A downloads folder can contain
                // anything, and copying it wholesale is not this program's business.
                if (!string.Equals(name, "presets", StringComparison.OrdinalIgnoreCase)) continue;

                string target = Path.Combine(InstallDirectory, name);
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(dir))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
        }

        /// <summary>
        /// Starts the installed copy and reports that this one should stand down. The new
        /// process registers itself in Installed apps and adds its own Start-menu entry, so
        /// that work happens from the path that will actually persist.
        /// </summary>
        private static bool HandOver()
        {
            try
            {
                var psi = new ProcessStartInfo(InstalledExe)
                {
                    Arguments = "--installed",
                    UseShellExecute = false,
                    WorkingDirectory = InstallDirectory
                };
                using (Process p = Process.Start(psi)) { return p != null; }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Write("could not start the installed copy: " + ex.Message);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                Log.Write("could not start the installed copy: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Run by the freshly-installed copy: claim the Start menu and Installed apps, then
        /// carry on into the normal UI.
        /// </summary>
        internal static void CompleteInstall()
        {
            Registration.CreateAppShortcut();
            Registration.RegisterUninstallEntry();
            Log.Write("install completed from " + AppPaths.SelfExe);
        }
    }
}
