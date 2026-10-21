using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using EKScriptLauncher.Core;
using static EKScriptLauncher.UI.Theme;

namespace EKScriptLauncher.UI
{
    /// <summary>
    /// The Select &amp; Launch window: a compact, searchable list of scripts.
    ///
    /// - Search filters as you type and highlights the matching text.
    ///   Up / Down move the selection, Enter launches, Esc closes.
    /// - Double-click a script to launch it. Hovering shows its description
    ///   (first docstring line) and full path.
    /// - The window widens for long script names, up to the screen width; a
    ///   horizontal scrollbar appears only if a name is still too long.
    /// - Closing with a script chosen sets <see cref="SelectedScript"/>; the
    ///   command runs it after the window has closed.
    /// </summary>
    sealed class LauncherForm : Form
    {
        static readonly int MinClientWidth = Dp(400);
        static readonly int MaxClientWidthFallback = Dp(1000);
        static readonly int WindowHeight = Dp(470);

        static readonly int Pad = Dp(10);
        static readonly int Gap = Dp(8);
        static readonly int ControlHeight = Dp(28);
        static readonly int RowHeight = Dp(26);
        static readonly int RowPad = Dp(10);
        static readonly int SearchTextLeft = Dp(26);
        static readonly int CountWidth = Dp(70);

        Panel searchFrame;
        TextBox searchBox;
        Label placeholder;
        Label countLabel;
        Panel listFrame;
        ListBox listBox;
        Label emptyLabel;
        Button settingsButton;
        Button refreshButton;
        Button cancelButton;
        Button launchButton;
        ToolTip toolTip;

        List<ScriptEntry> allScripts = new List<ScriptEntry>();
        List<ScriptEntry> visibleScripts = new List<ScriptEntry>();
        string query = "";
        int hoverIndex = -1;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ScriptsFolder { get; private set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string QuickLaunchFolder { get; private set; }

        /// <summary>The script to run, set when the user launches one.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ScriptEntry SelectedScript { get; private set; }

        public LauncherForm(LauncherSettings settings)
        {
            ScriptsFolder = settings.ScriptsFolder ?? "";
            QuickLaunchFolder = settings.QuickLaunchFolder ?? "";

            Text = Title;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(MinClientWidth, WindowHeight);
            MinimumSize = new Size(Width - MinClientWidth + Dp(340), Height - WindowHeight + Dp(260));
            BackColor = WindowBack;
            Font = FontUi;
            DoubleBuffered = true;

            BuildUi();
            RefreshScripts();
        }

        // -- layout ---------------------------------------------------------

        void BuildUi()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;

            // Search field with magnifier, placeholder and count.
            searchBox = new TextBox { Font = FontList };
            searchFrame = FieldFrame(searchBox, new Rectangle(Pad, Pad, width - 2 * Pad, ControlHeight), SearchTextLeft);
            searchFrame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            searchBox.Width = searchFrame.Width - SearchTextLeft - CountWidth - Dp(8);
            searchFrame.Paint += OnSearchFramePaint;
            searchBox.TextChanged += (s, e) => ApplyFilter();
            searchBox.KeyDown += OnSearchKeyDown;
            Controls.Add(searchFrame);

            placeholder = new Label
            {
                Text = "Search scripts...",
                Font = FontList,
                ForeColor = TextFaint,
                BackColor = Surface,
                AutoSize = true,
                Cursor = Cursors.IBeam,
                Location = new Point(searchBox.Left + Dp(1), searchBox.Top),
            };
            placeholder.Click += (s, e) => searchBox.Focus();
            searchFrame.Controls.Add(placeholder);
            placeholder.BringToFront();

            countLabel = new Label
            {
                Font = FontSmall,
                ForeColor = TextFaint,
                BackColor = Surface,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Size = new Size(CountWidth, ControlHeight - 2),
                Location = new Point(searchFrame.Width - CountWidth - Dp(8), 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            countLabel.Click += (s, e) => searchBox.Focus();
            searchFrame.Controls.Add(countLabel);

            // Footer: Settings / Refresh on the left, Cancel / Launch on the right.
            int footerTop = height - Pad - ControlHeight;
            settingsButton = AddFooterButton("Settings", Dp(72), OnSettings, AnchorStyles.Left);
            refreshButton = AddFooterButton("Refresh", Dp(72), (s, e) => RefreshScripts(), AnchorStyles.Left);
            cancelButton = AddFooterButton("Cancel", Dp(72), (s, e) => Close(), AnchorStyles.Right);
            launchButton = AddFooterButton("Launch", Dp(96), OnLaunch, AnchorStyles.Right);
            launchButton.Font = FontLaunch;
            StyleButton(launchButton, primary: true);

            settingsButton.Location = new Point(Pad, footerTop);
            refreshButton.Location = new Point(settingsButton.Right + Dp(6), footerTop);
            launchButton.Location = new Point(width - Pad - launchButton.Width, footerTop);
            cancelButton.Location = new Point(launchButton.Left - Dp(6) - cancelButton.Width, footerTop);

            // Script list: 1 px border around an owner-drawn list.
            int listTop = searchFrame.Bottom + Gap;
            listFrame = new Panel
            {
                BackColor = Theme.Border,
                Padding = new Padding(1),
                Location = new Point(Pad, listTop),
                Size = new Size(width - 2 * Pad, footerTop - Gap - listTop),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(listFrame);

            listBox = new ListBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Font = FontList,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = RowHeight,
                HorizontalScrollbar = true,
                IntegralHeight = false,
            };
            listBox.DrawItem += OnDrawItem;
            listBox.DoubleClick += OnLaunch;
            listBox.MouseMove += OnListMouseMove;
            listBox.MouseLeave += (s, e) => { InvalidateRow(hoverIndex); hoverIndex = -1; };
            listBox.KeyPress += OnListKeyPress;
            listBox.Resize += (s, e) => listBox.Invalidate();
            listFrame.Controls.Add(listBox);

            emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false,
            };
            listFrame.Controls.Add(emptyLabel);
            emptyLabel.BringToFront();

            toolTip = new ToolTip { InitialDelay = 600 };

            AcceptButton = launchButton;
            CancelButton = cancelButton;
            ActiveControl = searchBox;
            Shown += (s, e) => searchBox.Focus();
        }

        Button AddFooterButton(string text, int width, EventHandler handler, AnchorStyles side)
        {
            var button = new Button
            {
                Text = text,
                Font = FontUi,
                Size = new Size(width, ControlHeight),
                Anchor = AnchorStyles.Bottom | side,
            };
            StyleButton(button);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        int MaxAllowedClientWidth()
        {
            try
            {
                return Math.Max(MinClientWidth, Screen.FromControl(this).WorkingArea.Width - Dp(70));
            }
            catch
            {
                return MaxClientWidthFallback;
            }
        }

        int LongestRowWidth()
        {
            int longest = 0;
            foreach (ScriptEntry script in allScripts)
                longest = Math.Max(longest, RowPad + TextWidth(script.Name, FontList) + RowPad);
            return longest;
        }

        void ExpandForLongNames()
        {
            // Row content + list border/scrollbar + outer margins.
            int wanted = LongestRowWidth() + SystemInformation.VerticalScrollBarWidth + 2 + 2 * Pad;
            wanted = Math.Min(Math.Max(wanted, MinClientWidth), MaxAllowedClientWidth());

            // Only expand automatically. Do not shrink while the user works.
            if (wanted <= ClientSize.Width)
                return;

            int oldCenterX = Left + Width / 2;
            int oldCenterY = Top + Height / 2;

            ClientSize = new Size(wanted, ClientSize.Height);

            // Keep the expanded launcher approximately centered.
            try
            {
                Rectangle working = Screen.FromControl(this).WorkingArea;
                Left = Math.Max(working.Left, Math.Min(oldCenterX - Width / 2, working.Right - Width));
                Top = Math.Max(working.Top, Math.Min(oldCenterY - Height / 2, working.Bottom - Height));
            }
            catch
            {
            }
        }

        // -- data -----------------------------------------------------------

        public void RefreshScripts()
        {
            allScripts = ScriptCatalog.GetPythonFiles(ScriptsFolder);
            foreach (ScriptEntry script in allScripts)
                script.Summary = ScriptCatalog.ReadSummary(script.Path);

            string folderName = ScriptsFolder.Length > 0
                ? Path.GetFileName(ScriptsFolder.TrimEnd('\\', '/'))
                : "";
            Text = folderName.Length > 0 ? Title + "  –  " + folderName : Title;
            toolTip.SetToolTip(settingsButton, ScriptsFolder.Length > 0 ? ScriptsFolder : "Choose the scripts folder");

            // Lets the list scroll sideways if a name is wider than the window.
            listBox.HorizontalExtent = LongestRowWidth();

            ExpandForLongNames();
            ApplyFilter();
        }

        void ApplyFilter()
        {
            query = searchBox.Text.Trim().ToLowerInvariant();
            placeholder.Visible = searchBox.Text.Length == 0;

            visibleScripts = query.Length > 0
                ? allScripts.FindAll(s => s.Name.ToLowerInvariant().Contains(query))
                : new List<ScriptEntry>(allScripts);

            hoverIndex = -1;
            listBox.BeginUpdate();
            try
            {
                listBox.Items.Clear();
                foreach (ScriptEntry script in visibleScripts)
                    listBox.Items.Add(script.Name);
                if (listBox.Items.Count > 0)
                    listBox.SelectedIndex = 0;
            }
            finally
            {
                listBox.EndUpdate();
            }

            UpdateStatus();
        }

        void UpdateStatus()
        {
            int total = allScripts.Count;
            int shown = visibleScripts.Count;

            countLabel.Text = query.Length > 0 ? shown + " / " + total : total.ToString();

            if (shown > 0)
            {
                emptyLabel.Visible = false;
                return;
            }

            if (ScriptsFolder.Length == 0 || !Directory.Exists(ScriptsFolder))
                emptyLabel.Text = "No scripts folder selected.\nClick Settings to choose one.";
            else if (total == 0)
                emptyLabel.Text = "No .py files in this folder yet.\nAdd scripts, then click Refresh.";
            else
                emptyLabel.Text = "No scripts match “" + searchBox.Text.Trim() + "”";

            emptyLabel.Visible = true;
        }

        ScriptEntry CurrentScript()
        {
            int index = listBox.SelectedIndex;
            return index >= 0 && index < visibleScripts.Count ? visibleScripts[index] : null;
        }

        // -- list drawing ---------------------------------------------------

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Rectangle bounds = e.Bounds;
            int index = e.Index;

            if (index < 0 || index >= visibleScripts.Count)
            {
                FillRect(graphics, Surface, bounds);
                return;
            }

            ScriptEntry script = visibleScripts[index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            FillRect(graphics, selected ? AccentTint : index == hoverIndex ? RowHover : Surface, bounds);
            if (selected)
                FillRect(graphics, Accent, new Rectangle(bounds.Left, bounds.Top, Dp(3), bounds.Height));

            int textLeft = bounds.Left + RowPad;
            int textTop = bounds.Top + (bounds.Height - FontList.Height) / 2;

            if (query.Length > 0)
            {
                int start = script.Name.ToLowerInvariant().IndexOf(query, StringComparison.Ordinal);
                if (start >= 0)
                {
                    int before = TextWidth(script.Name.Substring(0, start), FontList, graphics);
                    int match = TextWidth(script.Name.Substring(start, query.Length), FontList, graphics);
                    FillRounded(graphics, MatchBack,
                        new Rectangle(textLeft + before - 1, textTop, match + 2, FontList.Height), Dp(2));
                }
            }

            TextRenderer.DrawText(graphics, script.Name, FontList, new Point(textLeft, textTop), Theme.Text, TextFlags);
        }

        void InvalidateRow(int index)
        {
            if (index >= 0 && index < listBox.Items.Count)
                listBox.Invalidate(listBox.GetItemRectangle(index));
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            int index = listBox.IndexFromPoint(e.Location);
            if (index == hoverIndex)
                return;

            InvalidateRow(hoverIndex);
            hoverIndex = index;
            InvalidateRow(index);

            string tip = "";
            if (index >= 0 && index < visibleScripts.Count)
            {
                ScriptEntry script = visibleScripts[index];
                tip = script.Summary.Length > 0 ? script.Summary + "\n" + script.Path : script.Path;
            }
            toolTip.SetToolTip(listBox, tip);
        }

        void OnListKeyPress(object sender, KeyPressEventArgs e)
        {
            // Typing while the list has focus goes into the search box.
            if (char.IsControl(e.KeyChar))
                return;
            searchBox.Focus();
            searchBox.AppendText(e.KeyChar.ToString());
            e.Handled = true;
        }

        // -- search box -----------------------------------------------------

        void OnSearchFramePaint(object sender, PaintEventArgs e)
        {
            // Magnifier glyph.
            Graphics graphics = e.Graphics;
            int size = Dp(9);
            int x = Dp(9);
            int y = (searchFrame.Height - size) / 2 - Dp(1);
            using (var pen = new Pen(searchBox.Focused ? Accent : TextFaint, (float)Math.Max(1.25, 1.4 * Theme.Scale)))
            {
                SmoothingMode old = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawEllipse(pen, x, y, size, size);
                graphics.DrawLine(pen, x + size * 0.85f, y + size * 0.85f, x + size * 1.3f, y + size * 1.3f);
                graphics.SmoothingMode = old;
            }
        }

        void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            int count = listBox.Items.Count;
            if (count == 0)
                return;

            int page = Math.Max(1, listBox.ClientSize.Height / RowHeight - 1);
            int step;
            switch (e.KeyCode)
            {
                case Keys.Down: step = 1; break;
                case Keys.Up: step = -1; break;
                case Keys.PageDown: step = page; break;
                case Keys.PageUp: step = -page; break;
                default: return;
            }

            int current = Math.Max(0, listBox.SelectedIndex);
            listBox.SelectedIndex = Math.Max(0, Math.Min(count - 1, current + step));
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // -- commands -------------------------------------------------------

        void OnSettings(object sender, EventArgs e)
        {
            using (var settings = new SettingsForm(ScriptsFolder, QuickLaunchFolder))
            {
                if (settings.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(settings.SavedFolder))
                {
                    ScriptsFolder = settings.SavedFolder;
                    QuickLaunchFolder = settings.SavedQuickFolder ?? "";
                    RefreshScripts();
                }
            }
        }

        void OnLaunch(object sender, EventArgs e)
        {
            ScriptEntry selected = CurrentScript();
            if (selected == null)
            {
                Message(this, "Select a script first.", MessageBoxIcon.Information);
                return;
            }

            // One launch per opening: close first, so dialogs from the script
            // are not trapped behind this window. The command runs it next.
            SelectedScript = selected;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
