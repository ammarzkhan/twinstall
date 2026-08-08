using System;
using System.IO;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    /// <summary>
    /// Reads the saved configuration, healing the one part of it that goes stale on its own:
    /// the path to a Store-packaged application.
    ///
    /// Every runtime entry point loads the config and then trusts <c>ExePath</c>, so the repair
    /// belongs here rather than at each of them — routing a link, launching an account, and
    /// badging icons all broke together when Claude updated, because all three ask the same
    /// question of the same stale value.
    ///
    /// Teardown deliberately does not use this. <c>Uninstaller</c> and <c>UndoEverything</c>
    /// want the config exactly as written so they remove what was actually created; re-pointing
    /// on the way out would write a file that is about to be deleted.
    /// </summary>
    internal static class ConfigStore
    {
        /// <summary>The saved configuration, re-pointed at the installed app if it moved.</summary>
        internal static AppConfig Load()
        {
            AppConfig cfg = InstanceConfig.Load(AppPaths.ConfigFile);
            Repoint(cfg);
            return cfg;
        }

        /// <summary>
        /// True when <paramref name="cfg"/> was re-pointed. Silent by design: the user did
        /// nothing wrong and has nothing to decide, so this is a log line and not a dialog.
        /// </summary>
        internal static bool Repoint(AppConfig cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.ExePath)) return false;

            // The overwhelmingly common case, and the reason this check is first: nothing moved.
            if (File.Exists(cfg.ExePath)) return false;

            string moved = PackagePaths.RepointToInstalledPackage(
                cfg.ExePath, PackageIndex.InstalledPackageRoots(), File.Exists);

            if (moved == null) return false;

            Log.Write("the configured app moved — re-pointing at " + moved);
            cfg.ExePath = moved;

            // The in-memory value is what this run uses, so a failed write must not stop it.
            // The worst case is that the repair repeats on the next launch, which is invisible;
            // failing here would strand the sign-in this was meant to rescue.
            try
            {
                AppPaths.EnsureHome();
                File.WriteAllText(AppPaths.ConfigFile, InstanceConfig.Serialise(cfg));
            }
            catch (IOException ex) { Log.Write("could not save the new path: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("could not save the new path: " + ex.Message); }

            return true;
        }
    }
}
