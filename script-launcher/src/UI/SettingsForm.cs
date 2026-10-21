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
        static readonly int Pad = Dp(12);
        static readonly int RowHeight = Dp(26);

        readonly TextBox pathBox = new TextBox();
        readonly TextBox quickPathBox = new TextBox();

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
            ClientSize = new Size(Dp(440), Dp(162));
            BackColor = WindowBack;
            Font = FontUi;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            pathBox.Text = currentFolder ?? "";
            quickPathBox.Text = currentQuickFolder ?? "";

            AddFolderRow(Pad, "Scripts folder", pathBox,
                "Choose the folder containing your Python scripts");
            AddFolderRow(Pad + Dp(52), "Quick Launch folder (exactly one .py file, optional)", quickPathBox,
                "Choose a folder containing exactly one Python script");

            var save = new Button { Text = "Save", Font = FontUiBold, Size = new Size(Dp(80), Dp(28)) };
            save.Location = new Point(ClientSize.Width - Pad - save.Width, ClientSize.Height - Pad - save.Height);
            StyleButton(save, primary: true);
            save.Click += OnSave;
            Controls.Add(save);

            AcceptButton = save;
        }

        void AddFolderRow(int top, string title, TextBox box, string browseDescription)
        {
            Controls.Add(new Label
            {
                Text = title,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(Pad - Dp(2), top),
            });

            var browse = new Button { Text = "...", Size = new Size(Dp(32), RowHeight) };
            browse.Location = new Point(ClientSize.Width - Pad - browse.Width, top + Dp(19));
            StyleButton(browse);
            Controls.Add(browse);

            Controls.Add(FieldFrame(
                box,
                new Rectangle(Pad, browse.Top, browse.Left - Dp(6) - Pad, RowHeight),
                Dp(6)));

            new ToolTip().SetToolTip(browse, "Browse");
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
