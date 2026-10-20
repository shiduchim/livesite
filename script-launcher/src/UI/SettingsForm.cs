using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EKScriptLauncher.Core;
using static EKScriptLauncher.UI.Theme;

namespace EKScriptLauncher.UI
{
    /// <summary>The two folder paths + Browse + Save. Nothing else.</summary>
    sealed class SettingsForm : Form
    {
        readonly TextBox pathBox;
        readonly TextBox quickPathBox;


        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SavedFolder { get; private set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SavedQuickFolder { get; private set; }

        public SettingsForm(string currentFolder, string currentQuickFolder)
        {
            Text = "Launcher Settings";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dp(640), Dp(262));
            BackColor = WindowBack;
            Font = FontUi;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            pathBox = AddFolderRow(
                Dp(18),
                "Script Launcher folder",
                "Every .py file in this folder appears in the launcher list.",
                currentFolder,
                "Choose the folder containing your Python scripts");

            quickPathBox = AddFolderRow(
                Dp(108),
                "Quick Launch folder (exactly one .py file)",
                "The Quick Launch ribbon button runs the single script in here. Leave blank if unused.",
                currentQuickFolder,
                "Choose a folder containing exactly one Python script");

            var save = new Button { Text = "Save", Font = FontUiBold, Size = new Size(Dp(110), Dp(36)) };
            save.Location = new Point(ClientSize.Width - Dp(18) - save.Width, ClientSize.Height - Dp(18) - save.Height);
            StyleButton(save, primary: true);
            save.Click += OnSave;
            Controls.Add(save);

            AcceptButton = save;
        }

        TextBox AddFolderRow(int top, string title, string hint, string value, string browseDescription)
        {
            Controls.Add(new Label
            {
                Text = title,
                Font = FontUiBold,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(Dp(18), top),
            });

            Controls.Add(new Label
            {
                Text = hint,
                Font = FontSmall,
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(Dp(18), top + Dp(22)),
            });

            var browse = new Button { Text = "Browse...", Size = new Size(Dp(104), Dp(32)) };
            browse.Location = new Point(ClientSize.Width - Dp(18) - browse.Width, top + Dp(46));
            StyleButton(browse);
            Controls.Add(browse);

            // Borderless text box inside a white panel, so the field is the same
            // height as the Browse button.
            var field = new Panel
            {
                BackColor = Surface,
                Cursor = Cursors.IBeam,
                Location = new Point(Dp(18), browse.Top),
                Size = new Size(browse.Left - Dp(8) - Dp(18), browse.Height),
            };
            Controls.Add(field);

            var box = new TextBox
            {
                Text = value ?? "",
                BorderStyle = BorderStyle.None,
                BackColor = Surface,
                ForeColor = Theme.Text,
            };
            box.Location = new Point(Dp(8), (field.Height - box.Height) / 2);
            box.Width = field.Width - Dp(16);
            field.Controls.Add(box);

            field.Paint += (s, e) =>
            {
                using (var pen = new Pen(box.Focused ? Blue : Border, 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, field.Width - 1, field.Height - 1);
            };
            field.Click += (s, e) => box.Focus();
            box.GotFocus += (s, e) => field.Invalidate();
            box.LostFocus += (s, e) => field.Invalidate();

            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = browseDescription })
                {
                    if (box.Text.Length > 0 && Directory.Exists(box.Text))
                        dialog.SelectedPath = box.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        box.Text = dialog.SelectedPath;
                }
            };

            return box;
        }

        void OnSave(object sender, EventArgs e)
        {
            string folder = pathBox.Text.Trim();
            string quickFolder = quickPathBox.Text.Trim();

            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                Message(this, "Choose a valid scripts folder.", MessageBoxIcon.Warning);
                return;
            }

            if (quickFolder.Length > 0 && !Directory.Exists(quickFolder))
            {
                Message(this, "Choose a valid Quick Launch folder, or leave it blank.", MessageBoxIcon.Warning);
                return;
            }

            if (quickFolder.Length > 0)
            {
                int count = ScriptCatalog.GetPythonFiles(quickFolder).Count;
                if (count != 1)
                {
                    Message(this,
                        "The Quick Launch folder must contain exactly one .py file.\n\nFound: " + count,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            try
            {
                new LauncherSettings { ScriptsFolder = folder, QuickLaunchFolder = quickFolder }.Save();
            }
            catch (Exception ex)
            {
                Message(this, "Could not save settings:\n\n" + ex.Message, MessageBoxIcon.Error);
                return;
            }

            SavedFolder = folder;
            SavedQuickFolder = quickFolder;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
