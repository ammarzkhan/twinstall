using System;
using System.IO;
using System.Text;
using Twinstall.Core;

namespace Twinstall.App
{
    /// <summary>
    /// Removes Twinstall, as invoked from Settings → Apps → Installed apps.
    ///
    /// The line this draws, and will not cross: **profile folders are never deleted.** They
    /// hold the accounts the user signed into, they were often adopted rather than created by
    /// Twinstall in the first place, and an uninstaller that silently logs someone out of two
    /// accounts is not one anybody should trust. Their paths are printed instead, so removing
    /// them stays a deliberate act.
    /// </summary>
    internal static class Uninstaller
    {
        internal static int Run()
        {
            AppConfig cfg = InstanceConfig.Load(AppPaths.ConfigFile);

            var what = new StringBuilder("Remove Twinstall?\r\n\r\nThis will delete:\r\n\r\n");
            what.Append("  •  its shortcuts, in the Start menu and on the Desktop\r\n");
            what.Append("  •  the badged icons it composed\r\n");
            what.Append("  •  its settings and registry entries\r\n");
            if (Registration.IsOurProgramFolder(Path.GetDirectoryName(AppPaths.SelfExe)))
                what.Append("  •  the Twinstall program folder itself\r\n");

            what.Append("\r\nYour profile folders are NOT touched — including the accounts you are "
                      + "signed into. You can delete those yourself afterwards if you want them gone.");

            if (!Ui.Confirm(what.ToString()))
            {
                Log.Write("uninstall cancelled");
                return 1;
            }

            Log.Write("--- uninstalling ---");
            Registration.RemoveAllRegistration(cfg);
            Registration.RemoveShortcuts(cfg);
            Registration.RemoveUninstallEntry();

            // The taskbar setting is left alone. It is system-wide, the user may have set it
            // themselves long before Twinstall existed, and reverting it would change how every
            // other application behaves on the way out.
            try
            {
                if (File.Exists(AppPaths.ConfigFile)) File.Delete(AppPaths.ConfigFile);
                if (Directory.Exists(AppPaths.IconsDir)) Directory.Delete(AppPaths.IconsDir, recursive: true);
            }
            catch (IOException ex) { Log.Write("cleanup: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("cleanup: " + ex.Message); }

            var done = new StringBuilder("Twinstall has been removed.\r\n\r\n");
            if (cfg.Instances.Count > 0)
            {
                done.Append("These profile folders were left in place, with their sign-ins intact:\r\n\r\n");
                foreach (Instance i in cfg.Instances) done.Append("  ").Append(i.DataDir).Append("\r\n");
                done.Append("\r\nDelete them yourself if you no longer want those accounts.\r\n\r\n");
            }

            if (Registration.ScheduleSelfDelete())
                done.Append("The program folder will disappear a moment after this window closes.");
            else
                done.Append("Twinstall was run from ")
                    .Append(Path.GetDirectoryName(AppPaths.SelfExe))
                    .Append(", which it did not create, so that folder has been left alone.");

            Ui.Info(done.ToString());
            Log.Write("uninstall complete");
            return 0;
        }
    }
}
