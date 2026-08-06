using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    internal sealed class MainForm : Form
    {
        private readonly ComboBox _preset = new ComboBox();
        private readonly TextBox _exePath = new TextBox();
        private readonly Button _detect = new Button();
        private readonly TextBox _report = new TextBox();
        private readonly ListView _instances = new ListView();
        private readonly CheckBox _taskbar = new CheckBox();
        private readonly Label _status = new Label();
        private readonly Button _apply = new Button();

        private readonly AppConfig _config = new AppConfig();
        private DetectionResult _detection;
        private IList<Preset> _presets = new List<Preset>();

        internal MainForm()
        {
            Text = "Twinstall";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 660);
            MinimumSize = new Size(700, 600);
            Font = new Font("Segoe UI", 9f);

            BuildAppSection();
            BuildReportSection();
            BuildInstanceSection();
            BuildActionSection();

            Load += (s, e) => Initialise();
        }

        // ------------------------------------------------------------- layout --
        private void BuildAppSection()
        {
            Controls.Add(Header("1.  Which application?", 14));

            _preset.Location = new Point(20, 44);
            _preset.Size = new Size(240, 24);
            _preset.DropDownStyle = ComboBoxStyle.DropDownList;
            _preset.SelectedIndexChanged += (s, e) => PresetChosen();
            Controls.Add(_preset);

            _exePath.Location = new Point(272, 44);
            _exePath.Size = new Size(360, 24);
            _exePath.PlaceholderText = "Path to the application's .exe";
            Controls.Add(_exePath);

            var browse = new Button { Text = "Browse...", Location = new Point(640, 43), Size = new Size(100, 26), FlatStyle = FlatStyle.System };
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            _detect.Text = "Check this app";
            _detect.Location = new Point(20, 78);
            _detect.Size = new Size(160, 30);
            _detect.FlatStyle = FlatStyle.System;
            _detect.Click += async (s, e) => await DetectAsync().ConfigureAwait(true);
            Controls.Add(_detect);

            Controls.Add(new Label
            {
                Text = "This starts the app once with a throwaway profile to prove it really supports them.\r\n"
                     + "A window will appear and close again.",
                Location = new Point(190, 78),
                Size = new Size(560, 34),
                ForeColor = Color.DimGray
            });
        }

        private void BuildReportSection()
        {
            _report.Location = new Point(20, 118);
            _report.Size = new Size(720, 168);
            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Vertical;
            _report.BackColor = Color.White;
            _report.Font = new Font("Consolas", 9f);
            _report.Text = "No application checked yet.";
            Controls.Add(_report);
        }

        private void BuildInstanceSection()
        {
            Controls.Add(Header("2.  Which accounts?", 298));

            _instances.Location = new Point(20, 328);
            _instances.Size = new Size(580, 150);
            _instances.View = View.Details;
            _instances.FullRowSelect = true;
            _instances.GridLines = true;
            _instances.Columns.Add("Name", 130);
            _instances.Columns.Add("Profile folder", 350);
            _instances.Columns.Add("Badge", 80);
            _instances.DoubleClick += (s, e) => EditInstance();
            Controls.Add(_instances);

            var add = new Button { Text = "Add", Location = new Point(612, 328), Size = new Size(128, 30), FlatStyle = FlatStyle.System };
            add.Click += (s, e) => AddInstance();
            Controls.Add(add);

            var edit = new Button { Text = "Edit", Location = new Point(612, 364), Size = new Size(128, 30), FlatStyle = FlatStyle.System };
            edit.Click += (s, e) => EditInstance();
            Controls.Add(edit);

            var remove = new Button { Text = "Remove", Location = new Point(612, 400), Size = new Size(128, 30), FlatStyle = FlatStyle.System };
            remove.Click += (s, e) => RemoveInstance();
            Controls.Add(remove);
        }

        private void BuildActionSection()
        {
            Controls.Add(Header("3.  Apply", 492));

            _taskbar.Text = "Also set Windows to never combine taskbar buttons";
            _taskbar.Location = new Point(20, 522);
            _taskbar.Size = new Size(400, 22);
            _taskbar.Checked = Registration.TaskbarNeverCombine();
            Controls.Add(_taskbar);

            Controls.Add(new Label
            {
                Text = "Required for per-window icons. Affects every app on the system and only takes\r\n"
                     + "effect after you sign out and back in. Off by default, and never changed silently.",
                Location = new Point(40, 544),
                Size = new Size(560, 34),
                ForeColor = Color.DimGray
            });

            _apply.Text = "Set up Twinstall";
            _apply.Location = new Point(20, 588);
            _apply.Size = new Size(190, 34);
            _apply.FlatStyle = FlatStyle.System;
            _apply.Click += (s, e) => Apply();
            Controls.Add(_apply);

            var log = new Button { Text = "Open log", Location = new Point(220, 588), Size = new Size(110, 34), FlatStyle = FlatStyle.System };
            log.Click += (s, e) => OpenLog();
            Controls.Add(log);

            var removeAll = new Button { Text = "Remove Twinstall's changes", Location = new Point(490, 588), Size = new Size(250, 34), FlatStyle = FlatStyle.System };
            removeAll.Click += (s, e) => RemoveEverything();
            Controls.Add(removeAll);

            _status.Location = new Point(20, 630);
            _status.Size = new Size(720, 20);
            _status.ForeColor = Color.DimGray;
            Controls.Add(_status);
        }

        private static Label Header(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(18, y),
                Size = new Size(600, 24),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold)
            };
        }

        // ------------------------------------------------------------- startup --
        private void Initialise()
        {
            _presets = Presets.Load();
            _preset.Items.Add("Choose an app...");
            foreach (Preset p in _presets) _preset.Items.Add(p.DisplayName);
            _preset.Items.Add("Something else (Browse)");
            _preset.SelectedIndex = 0;

            AppConfig saved = InstanceConfig.Load(AppPaths.ConfigFile);
            if (!string.IsNullOrWhiteSpace(saved.ExePath))
            {
                _config.ExePath = saved.ExePath;
                _config.Scheme = saved.Scheme;
                _config.DefaultDataDir = saved.DefaultDataDir;
                foreach (Instance i in saved.Instances) _config.Instances.Add(i);
                _exePath.Text = saved.ExePath;
                RefreshInstances();
                Say("Loaded your existing setup. Re-check the app if anything has changed.");
            }
            else
            {
                Say("Pick an application to begin.");
            }
        }

        private void PresetChosen()
        {
            int index = _preset.SelectedIndex - 1;
            if (index < 0 || index >= _presets.Count) return;

            Preset p = _presets[index];
            string found = Presets.FindInstalled(p);
            if (found != null)
            {
                _exePath.Text = found;
                Say("Found " + p.DisplayName + ". Now check it.");
            }
            else
            {
                Say(p.DisplayName + " was not found in the usual places — use Browse to point at it.");
            }
        }

        private void Browse()
        {
            using (var dlg = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Choose the application" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) _exePath.Text = dlg.FileName;
            }
        }

        // ----------------------------------------------------------- detection --
        private async Task DetectAsync()
        {
            string exe = _exePath.Text.Trim().Trim('"');
            if (!File.Exists(exe)) { Say("That file does not exist."); return; }

            _detect.Enabled = false;
            _apply.Enabled = false;
            Say("Checking... a window from the app will appear and close.");
            _report.Text = "Working...";
            UseWaitCursor = true;

            DetectionResult result = await Task.Run(() => Detector.Run(exe, runProbe: true)).ConfigureAwait(true);

            UseWaitCursor = false;
            _detect.Enabled = true;
            _apply.Enabled = true;
            _detection = result;
            _report.Text = Describe(result);

            if (result.Probe != ProbeVerdict.Honoured)
            {
                Say("This app cannot run separate instances. Nothing has been changed.");
                return;
            }

            _config.ExePath = result.ExePath;
            _config.Scheme = result.Scheme;
            _config.DefaultDataDir = result.Profiles?.Best?.Directory;

            if (_config.Instances.Count == 0 && result.Profiles?.Best != null)
            {
                // The profile that already exists becomes the first instance, so the account
                // the user is signed into keeps working exactly as it did.
                _config.Instances.Add(new Instance
                {
                    Name = result.Profiles.Best.Name,
                    DataDir = result.Profiles.Best.Directory,
                    Colour = "#2563EB",
                    Badge = true
                });
                RefreshInstances();
            }

            Say("Supported. Add a second account below, then apply.");
        }

        private static string Describe(DetectionResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Application   : " + r.ExePath);
            if (r.StubResolved) sb.AppendLine("                (resolved from a launcher stub)");

            sb.AppendLine("Chromium      : " + r.MarkerScore.ToString(CultureInfo.InvariantCulture)
                          + " markers -> " + (r.IsChromium ? "yes" : "NO"));
            if (r.IsChromium && !string.IsNullOrEmpty(r.MarkerDirectory))
                sb.AppendLine("                markers in " + r.MarkerDirectory);

            sb.AppendLine("Packaged      : " + (r.Packaged ? "yes (Microsoft Store)" : "no"));
            sb.AppendLine("Profile root  : " + r.ProfileRoot);

            if (r.Profiles != null)
            {
                sb.AppendLine("Existing      : " + r.Profiles.Outcome
                              + (r.Profiles.Best != null ? "  -> " + r.Profiles.Best.Name : string.Empty));
                if (r.Profiles.Outcome == ProfileDiscoveryOutcome.Ambiguous)
                    sb.AppendLine("                several candidates - check the folder below is right");
            }

            if (r.Schemes != null && r.Schemes.Count > 0)
            {
                foreach (SchemeRegistration s in r.Schemes)
                {
                    sb.AppendLine("Scheme        : " + s.Scheme + "://   currently handled by: " + OwnerText(s));
                    if (s.Owner == SchemeOwner.Foreign)
                        sb.AppendLine("                " + (s.Command ?? string.Empty));
                }
            }
            else
            {
                sb.AppendLine("Scheme        : none found - this app does not use browser sign-in,");
                sb.AppendLine("                so separate profiles will work but no routing is needed.");
            }

            sb.AppendLine();
            sb.AppendLine("Launch test   : " + r.Probe);
            sb.AppendLine("                " + r.ProbeDetail);
            return sb.ToString();
        }

        private static string OwnerText(SchemeRegistration s)
        {
            switch (s.Owner)
            {
                case SchemeOwner.Direct: return "the app itself";
                case SchemeOwner.SamePackage: return "the app's own package";
                case SchemeOwner.Foreign: return "ANOTHER PROGRAM";
                default: return s.DeclaredByPackage ? "nothing yet" : "unknown";
            }
        }

        // ----------------------------------------------------------- instances --
        private void RefreshInstances()
        {
            _instances.Items.Clear();
            foreach (Instance i in _config.Instances)
            {
                var row = new ListViewItem(i.Name);
                row.SubItems.Add(i.DataDir);
                row.SubItems.Add(i.Badge ? "yes" : "no");
                _instances.Items.Add(row);
            }
        }

        private string ExistingProfileDir()
        {
            return _config.Instances.Count > 0 ? _config.Instances[0].DataDir : _config.DefaultDataDir;
        }

        private void AddInstance()
        {
            if (string.IsNullOrWhiteSpace(_config.ExePath)) { Say("Check an application first."); return; }

            using (var dlg = new InstanceDialog(null, _detection?.ProfileRoot, ExistingProfileDir()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value == null) return;
                if (!EnsureIsolated(dlg.Value, -1)) return;
                _config.Instances.Add(dlg.Value);
            }
            RefreshInstances();
        }

        private void EditInstance()
        {
            int index = SelectedIndex();
            if (index < 0) return;

            using (var dlg = new InstanceDialog(_config.Instances[index], _detection?.ProfileRoot, ExistingProfileDir()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value == null) return;
                if (!EnsureIsolated(dlg.Value, index)) return;
                _config.Instances[index] = dlg.Value;
            }
            RefreshInstances();
        }

        private void RemoveInstance()
        {
            int index = SelectedIndex();
            if (index < 0) return;
            _config.Instances.RemoveAt(index);
            RefreshInstances();
        }

        private int SelectedIndex()
        {
            return _instances.SelectedIndices.Count == 0 ? -1 : _instances.SelectedIndices[0];
        }

        /// <summary>
        /// No two instances may share or nest their profile folders. This is the guarantee the
        /// whole product rests on, so it is enforced here and not merely suggested.
        /// </summary>
        private bool EnsureIsolated(Instance candidate, int ignoreIndex)
        {
            for (int i = 0; i < _config.Instances.Count; i++)
            {
                if (i == ignoreIndex) continue;
                Instance other = _config.Instances[i];

                if (string.Equals(other.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Ui.Error("There is already an instance called \"" + candidate.Name + "\".");
                    return false;
                }

                var issues = IsolationCheck.Validate(other.DataDir, candidate.DataDir);
                if (issues.Count > 0)
                {
                    Ui.Error("These two instances would not be isolated from each other:\r\n\r\n"
                           + "  " + other.Name + "  ->  " + other.DataDir + "\r\n"
                           + "  " + candidate.Name + "  ->  " + candidate.DataDir + "\r\n\r\n"
                           + issues[0] + ".");
                    return false;
                }
            }
            return true;
        }

        // --------------------------------------------------------------- apply --
        private void Apply()
        {
            if (string.IsNullOrWhiteSpace(_config.ExePath)) { Say("Check an application first."); return; }
            if (_config.Instances.Count < 2)
            {
                Ui.Error("Add at least two instances — that is the point of Twinstall.");
                return;
            }

            var summary = new StringBuilder("Twinstall will:\r\n\r\n");
            foreach (string line in Registration.DescribeChanges(_config, _taskbar.Checked))
                summary.Append("  •  ").Append(line).Append("\r\n");
            summary.Append("\r\nGo ahead?");

            if (!Ui.Confirm(summary.ToString())) return;

            try
            {
                AppPaths.EnsureHome();
                foreach (Instance i in _config.Instances) Directory.CreateDirectory(i.DataDir);
                File.WriteAllText(AppPaths.ConfigFile, InstanceConfig.Serialise(_config));
            }
            catch (IOException ex) { Ui.Error("Could not write the configuration.\r\n\r\n" + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { Ui.Error("Could not write the configuration.\r\n\r\n" + ex.Message); return; }

            int icons = Router.ComposeIcons(_config);
            int shortcuts = Registration.CreateShortcuts(_config);

            bool registered = false;
            if (!string.IsNullOrWhiteSpace(_config.Scheme))
                registered = Registration.RegisterProtocol(_config.Scheme);

            if (_taskbar.Checked != Registration.TaskbarNeverCombine())
                Registration.SetTaskbarNeverCombine(_taskbar.Checked);

            Log.Write("setup applied: " + _config.Instances.Count + " instances, "
                      + icons + " icons, " + shortcuts + " shortcuts");

            var done = new StringBuilder("Twinstall is set up.\r\n\r\n");
            done.Append("Start-menu shortcuts: ").Append(shortcuts.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            done.Append("Badged icons: ").Append(icons.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");

            if (registered)
            {
                done.Append("One last step, and it has to be you: Windows only lets *you* choose a\r\n");
                done.Append("default handler. Settings will open now — find ").Append(_config.Scheme);
                done.Append(":// and choose Twinstall.\r\n\r\n");
            }
            if (_taskbar.Checked)
                done.Append("The taskbar setting takes effect after you sign out and back in.\r\n\r\n");

            done.Append("Then open your second account from the Start menu.");
            Ui.Info(done.ToString());

            if (registered) Registration.OpenDefaultAppsSettings();
            Say("Set up. " + shortcuts + " shortcut(s) created.");
        }

        private void RemoveEverything()
        {
            if (!Ui.Confirm("Remove Twinstall's shortcuts, icons, registry entries and configuration?\r\n\r\n"
                          + "Your profile folders and everything signed into them are NOT touched — "
                          + "delete those yourself if you want them gone."))
                return;

            AppConfig saved = InstanceConfig.Load(AppPaths.ConfigFile);
            Registration.RemoveAllRegistration(saved);
            Registration.RemoveShortcuts();

            try
            {
                if (File.Exists(AppPaths.ConfigFile)) File.Delete(AppPaths.ConfigFile);
                if (Directory.Exists(AppPaths.IconsDir)) Directory.Delete(AppPaths.IconsDir, recursive: true);
            }
            catch (IOException ex) { Log.Write("cleanup: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("cleanup: " + ex.Message); }

            _config.Instances.Clear();
            RefreshInstances();
            Say("Removed. Your profile folders were left alone.");
            Ui.Info("Twinstall's changes are gone.\r\n\r\n"
                  + "Windows may still list Twinstall under Default apps until you pick something else there.\r\n\r\n"
                  + "Your profile folders were left untouched.");
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) { Say("Nothing logged yet."); return; }
                using (System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception ex) { Say("Could not open the log: " + ex.Message); }
            catch (InvalidOperationException ex) { Say("Could not open the log: " + ex.Message); }
        }

        private void Say(string message)
        {
            _status.Text = message;
        }
    }
}
