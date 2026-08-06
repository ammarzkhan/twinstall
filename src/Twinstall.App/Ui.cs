using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Twinstall.Core;

namespace Twinstall.App
{
    /// <summary>
    /// The only dialogs the routing path can raise. Kept apart from the management UI so the
    /// common case — a link arrives, Z-order answers it, the app opens — never constructs a
    /// window at all.
    /// </summary>
    internal static class Ui
    {
        internal static void Error(string message)
        {
            MessageBox.Show(message, AppPaths.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        internal static void Info(string message)
        {
            MessageBox.Show(message, AppPaths.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal static bool Confirm(string message)
        {
            return MessageBox.Show(message, AppPaths.ProductName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>
        /// Asks which instance an incoming link belongs to. Reached only when nothing is
        /// running, so Z-order has nothing to say — the honest move there is to ask rather
        /// than to pick one and be wrong half the time.
        /// </summary>
        internal static string AskWhichInstance(IList<Instance> instances)
        {
            if (instances == null || instances.Count == 0) return null;

            string picked = null;

            using (var form = new Form())
            {
                form.Text = "Which account?";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.TopMost = true;
                form.ClientSize = new Size(360, 90 + (instances.Count * 40));
                form.Font = new Font("Segoe UI", 9f);

                var prompt = new Label
                {
                    Text = "A sign-in link arrived. Which one is it for?",
                    Location = new Point(16, 14),
                    Size = new Size(330, 22),
                    AutoSize = false
                };
                form.Controls.Add(prompt);

                int y = 44;
                foreach (Instance inst in instances)
                {
                    string name = inst.Name;
                    var button = new Button
                    {
                        Text = name,
                        Location = new Point(16, y),
                        Size = new Size(328, 32),
                        FlatStyle = FlatStyle.System
                    };
                    button.Click += (s, e) => { picked = name; form.Close(); };
                    form.Controls.Add(button);
                    y += 38;
                }

                var cancel = new Button
                {
                    Text = "Cancel",
                    Location = new Point(16, y + 6),
                    Size = new Size(328, 28),
                    FlatStyle = FlatStyle.System,
                    DialogResult = DialogResult.Cancel
                };
                form.Controls.Add(cancel);
                form.CancelButton = cancel;
                form.ClientSize = new Size(360, y + 46);

                form.ShowDialog();
            }

            return picked;
        }
    }
}
