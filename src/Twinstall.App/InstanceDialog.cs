using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Twinstall.Core;

namespace Twinstall.App
{
    /// <summary>Name, folder and colour for one instance.</summary>
    internal sealed class InstanceDialog : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _folder = new TextBox();
        private readonly Button _colour = new Button();
        private readonly CheckBox _badge = new CheckBox();
        private readonly Label _warning = new Label();
        private readonly string _profileRoot;
        private readonly string _existingProfile;

        internal Instance Value { get; private set; }

        /// <summary>Chrome's own profile palette, which is where the visual language comes from.</summary>
        private static readonly string[] Palette =
        {
            "#7C3AED", "#059669", "#DC2626", "#2563EB", "#D97706", "#DB2777", "#0891B2", "#4B5563"
        };

        private static int _nextColour;

        internal InstanceDialog(Instance existing, string profileRoot, string existingProfileDir)
        {
            _profileRoot = profileRoot;
            _existingProfile = existingProfileDir;

            Text = existing == null ? "Add an instance" : "Edit instance";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(470, 240);
            Font = new Font("Segoe UI", 9f);

            Controls.Add(new Label { Text = "Name", Location = new Point(14, 18), Size = new Size(90, 20) });
            _name.Location = new Point(110, 15);
            _name.Size = new Size(200, 24);
            _name.Text = existing?.Name ?? "Second";
            _name.TextChanged += (s, e) => { SuggestFolder(); Validate2(); };
            Controls.Add(_name);

            Controls.Add(new Label { Text = "Profile folder", Location = new Point(14, 54), Size = new Size(90, 20) });
            _folder.Location = new Point(110, 51);
            _folder.Size = new Size(260, 24);
            _folder.Text = existing?.DataDir ?? string.Empty;
            _folder.TextChanged += (s, e) => Validate2();
            Controls.Add(_folder);

            var browse = new Button { Text = "Browse...", Location = new Point(378, 50), Size = new Size(78, 26), FlatStyle = FlatStyle.System };
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            Controls.Add(new Label { Text = "Badge colour", Location = new Point(14, 90), Size = new Size(90, 20) });
            _colour.Location = new Point(110, 87);
            _colour.Size = new Size(60, 26);
            _colour.FlatStyle = FlatStyle.Flat;
            _colour.Tag = existing?.Colour ?? NextColour();
            _colour.BackColor = Parse((string)_colour.Tag);
            _colour.Click += (s, e) => PickColour();
            Controls.Add(_colour);

            _badge.Text = "Show a coloured badge on the taskbar icon";
            _badge.Location = new Point(180, 90);
            _badge.Size = new Size(280, 22);
            _badge.Checked = existing?.Badge ?? true;
            Controls.Add(_badge);

            _warning.Location = new Point(14, 124);
            _warning.Size = new Size(442, 52);
            _warning.ForeColor = Color.Firebrick;
            Controls.Add(_warning);

            var ok = new Button { Text = "OK", Location = new Point(268, 194), Size = new Size(90, 30), FlatStyle = FlatStyle.System, DialogResult = DialogResult.OK };
            ok.Click += (s, e) => Accept(ok);
            Controls.Add(ok);

            var cancel = new Button { Text = "Cancel", Location = new Point(366, 194), Size = new Size(90, 30), FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (existing == null) SuggestFolder();
            Validate2();
        }

        private static string NextColour()
        {
            string c = Palette[_nextColour % Palette.Length];
            _nextColour++;
            return c;
        }

        private static Color Parse(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch (ArgumentException) { return Color.MediumPurple; }
            catch (FormatException) { return Color.MediumPurple; }
        }

        private void SuggestFolder()
        {
            if (string.IsNullOrWhiteSpace(_profileRoot)) return;
            string suggestion = PathUtil.Join(_profileRoot, AppPaths.Sanitise(_name.Text));
            if (string.IsNullOrWhiteSpace(_folder.Text) || _folder.Text.StartsWith(_profileRoot, StringComparison.OrdinalIgnoreCase))
                _folder.Text = suggestion;
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder for this instance's profile" })
            {
                if (!string.IsNullOrWhiteSpace(_folder.Text)) dlg.SelectedPath = _folder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
            }
        }

        private void PickColour()
        {
            using (var dlg = new ColorDialog { Color = _colour.BackColor, FullOpen = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _colour.BackColor = dlg.Color;
                _colour.Tag = "#" + dlg.Color.R.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
                                  + dlg.Color.G.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
                                  + dlg.Color.B.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The isolation check is the whole basis for claiming the accounts stay separate, so
        /// it runs as the user types rather than only on OK.
        /// </summary>
        private string Problem()
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) return "Give this instance a name.";
            if (string.IsNullOrWhiteSpace(_folder.Text)) return "Choose a profile folder.";

            if (!string.IsNullOrWhiteSpace(_existingProfile))
            {
                var issues = IsolationCheck.Validate(_existingProfile, _folder.Text);
                if (issues.Count > 0) return "This would not be isolated: " + issues[0] + ".";
            }
            return null;
        }

        private void Validate2()
        {
            string problem = Problem();
            _warning.Text = problem ?? string.Empty;
        }

        private void Accept(Button ok)
        {
            string problem = Problem();
            if (problem != null)
            {
                _warning.Text = problem;
                DialogResult = DialogResult.None;
                ok.DialogResult = DialogResult.None;
                return;
            }

            ok.DialogResult = DialogResult.OK;
            Value = new Instance
            {
                Name = _name.Text.Trim(),
                DataDir = PathUtil.Normalise(_folder.Text),
                Colour = (string)_colour.Tag,
                Badge = _badge.Checked
            };
        }
    }
}
