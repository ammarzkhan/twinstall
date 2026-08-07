using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Twinstall.Core;

namespace Twinstall.App
{
    /// <summary>Name, folder and colour for one account.</summary>
    internal sealed class InstanceDialog : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _folder = new TextBox();
        private readonly Panel _swatches = new Panel();
        private readonly CheckBox _badge = new CheckBox();
        private readonly Label _warning = new Label();
        private readonly FlatButton _ok = new FlatButton("Save", ButtonKind.Primary);

        private readonly string _profileRoot;
        private readonly List<string> _otherProfiles;
        private readonly bool _isNew;
        private string _colour;

        internal Instance Value { get; private set; }

        /// <summary>Set when the user chose to remove this account rather than save it.</summary>
        internal bool RemoveRequested { get; private set; }

        /// <summary>
        /// Chrome's profile palette, which is where this visual language comes from.
        ///
        /// Cyan used to be in here and was removed: it sat close enough to Twinstall's own teal
        /// that a badged icon could read as the app's chrome rather than as one of your
        /// accounts. The badge colours are the signal — nothing else should look like them.
        /// </summary>
        private static readonly string[] Palette =
        {
            "#2563EB", "#059669", "#DC2626", "#7C3AED", "#D97706", "#DB2777", "#BE185D", "#4B5563"
        };

        private static int _nextColour = 1;

        /// <param name="otherProfileDirs">
        /// The folders of the *other* accounts — never this one's. Passing the account being
        /// edited compares it against itself, which reads as "both instances resolve to the
        /// same folder" and disables Save on a perfectly valid account. That is exactly what
        /// happened when a single "existing profile" path was passed instead of a list.
        /// </param>
        internal InstanceDialog(Instance existing, string profileRoot, IEnumerable<string> otherProfileDirs)
        {
            _profileRoot = profileRoot;
            _isNew = existing == null;
            _otherProfiles = new List<string>();
            if (otherProfileDirs != null)
                foreach (string d in otherProfileDirs)
                    if (!string.IsNullOrWhiteSpace(d)) _otherProfiles.Add(d);

            _colour = existing?.Colour ?? Palette[_nextColour++ % Palette.Length];

            Text = existing == null ? "Add an account" : "Edit account";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(520, 330);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;

            Add(Caption("Name", 24));
            Style(_name);
            _name.Location = new Point(24, 46);
            _name.Size = new Size(472, 28);
            _name.Text = existing?.Name ?? "Second";
            _name.TextChanged += (s, e) => { SuggestFolder(); Revalidate(); };
            Add(_name);

            Add(Caption("Profile folder", 86));
            Style(_folder);
            _folder.Location = new Point(24, 108);
            _folder.Size = new Size(360, 28);
            _folder.Text = existing?.DataDir ?? string.Empty;
            _folder.TextChanged += (s, e) => Revalidate();
            Add(_folder);

            var browse = new FlatButton("Browse…", ButtonKind.Secondary)
            {
                Location = new Point(394, 106),
                Size = new Size(102, 32)
            };
            browse.Click += (s, e) => Browse();
            Add(browse);

            Add(Caption("Badge colour", 150));
            _swatches.Location = new Point(24, 172);
            _swatches.Size = new Size(472, 34);
            _swatches.BackColor = Theme.Background;
            BuildSwatches();
            Add(_swatches);

            _badge.Text = "  Show a coloured badge on this account's taskbar icon";
            _badge.Font = Theme.Body;
            _badge.ForeColor = Theme.Text;
            _badge.BackColor = Theme.Background;
            _badge.AutoSize = false;
            _badge.Location = new Point(22, 216);
            _badge.Size = new Size(474, 26);
            _badge.Checked = existing?.Badge ?? true;
            Add(_badge);

            _warning.Location = new Point(24, 246);
            _warning.Size = new Size(472, 34);
            _warning.Font = Theme.Small;
            _warning.ForeColor = Theme.Bad;
            _warning.AutoSize = false;
            Add(_warning);

            _ok.Location = new Point(386, 286);
            _ok.Size = new Size(110, 36);
            _ok.Click += (s, e) => Accept();
            Add(_ok);

            var cancel = new FlatButton("Cancel", ButtonKind.Subtle)
            {
                Location = new Point(286, 286),
                Size = new Size(92, 36)
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Add(cancel);

            // Removal lives here rather than as a "remove the last one" button on the list,
            // which only ever let you delete the account you happened to add most recently.
            if (!_isNew)
            {
                var remove = new FlatButton("Remove account", ButtonKind.Danger)
                {
                    Location = new Point(24, 286),
                    Size = new Size(160, 36)
                };
                remove.Click += (s, e) => RequestRemoval(existing);
                Add(remove);
            }

            // FlatButton is a Control, not an IButtonControl, so Esc and Enter have to be
            // wired by hand rather than through AcceptButton/CancelButton.
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.KeyCode == Keys.Enter && _ok.Enabled) { Accept(); }
            };

            if (existing == null) SuggestFolder();
            Revalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowChrome(Handle);
        }

        private void Add(Control c) { Controls.Add(c); }

        private static Label Caption(string text, int y)
        {
            return new Label
            {
                Text = text,
                Font = Theme.BodyStrong,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, y),
                Size = new Size(300, 20)
            };
        }

        private static void Style(TextBox t)
        {
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Surface;
            t.ForeColor = Theme.Text;
            t.Font = Theme.Body;
        }

        private void BuildSwatches()
        {
            int x = 0;
            foreach (string hex in Palette)
            {
                string captured = hex;
                var dot = new ChoiceRow
                {
                    Title = string.Empty,
                    Dot = Theme.FromHex(hex),
                    Selected = string.Equals(hex, _colour, StringComparison.OrdinalIgnoreCase),
                    Location = new Point(x, 0),
                    Size = new Size(50, 34),
                    Tag2 = captured
                };
                // Deferred: rebuilding here would dispose the swatch whose Click is still running.
                dot.Click += (s, e) => BeginInvoke(new Action(() =>
                {
                    _colour = captured;
                    foreach (Control c in new List<Control>(_swatches.Controls.Cast<Control>()))
                    {
                        _swatches.Controls.Remove(c);
                        c.Dispose();
                    }
                    BuildSwatches();
                }));
                _swatches.Controls.Add(dot);
                x += 56;
            }
        }

        private void SuggestFolder()
        {
            // Only ever for a new account. Renaming an existing one must not silently move its
            // profile folder out from under a signed-in session.
            if (!_isNew || string.IsNullOrWhiteSpace(_profileRoot)) return;
            string suggestion = PathUtil.Join(_profileRoot, AppPaths.Sanitise(_name.Text));
            if (string.IsNullOrWhiteSpace(_folder.Text)
                || _folder.Text.StartsWith(_profileRoot, StringComparison.OrdinalIgnoreCase))
            {
                _folder.Text = suggestion;
            }
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder for this account's profile" })
            {
                if (!string.IsNullOrWhiteSpace(_folder.Text)) dlg.SelectedPath = _folder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
            }
        }

        /// <summary>
        /// The isolation check runs as the user types, not only on Save — it is the basis for
        /// claiming the accounts stay separate, so it should never be a surprise at the end.
        /// </summary>
        private string Problem()
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) return "Give this account a name.";
            if (string.IsNullOrWhiteSpace(_folder.Text)) return "Choose a profile folder.";

            foreach (string other in _otherProfiles)
            {
                var issues = IsolationCheck.Validate(other, _folder.Text);
                if (issues.Count > 0) return "This would not be kept separate: " + issues[0] + ".";
            }
            return null;
        }

        private void Revalidate()
        {
            string problem = Problem();
            _warning.Text = problem ?? string.Empty;
            _ok.Enabled = problem == null;
        }

        private void RequestRemoval(Instance existing)
        {
            if (!Ui.Confirm("Remove the “" + (existing?.Name ?? _name.Text) + "” account from Twinstall?\r\n\r\n"
                          + "Its shortcut and badged icon go away.\r\n\r\n"
                          + "The profile folder stays where it is, still signed in:\r\n"
                          + (existing?.DataDir ?? _folder.Text) + "\r\n\r\n"
                          + "Delete that yourself if you want the account gone for good."))
                return;

            RemoveRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Accept()
        {
            if (Problem() != null) { Revalidate(); return; }

            Value = new Instance
            {
                Name = _name.Text.Trim(),
                DataDir = PathUtil.Normalise(_folder.Text),
                Colour = _colour,
                Badge = _badge.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
