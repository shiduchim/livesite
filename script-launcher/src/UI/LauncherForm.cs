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
    /// The Select &amp; Launch window.
    ///
    /// - Every script is shown as its own row: a coloured initials badge, the
    ///   script name, and underneath it the file name plus the first line of
    ///   the script's docstring (when it has one).
    /// - Search filters as you type and highlights the matching text.
    ///   Up / Down move the selection, Enter launches, Esc closes.
    /// - The window widens for long script names, up to the screen width; a
    ///   horizontal scrollbar appears only if a name is still too long.
    /// - Resizable; Settings / Refresh / Cancel stay aligned.
    /// - The large LAUNCH SCRIPT button shows which script it will run.
    /// - Closing with a script chosen sets <see cref="SelectedScript"/>; the
    ///   command runs it after the window has closed.
    /// </summary>
    sealed class LauncherForm : Form
    {
        static readonly int MinClientWidth = Dp(560);
        static readonly int MaxClientWidthFallback = Dp(1100);
        static readonly int WindowHeight = Dp(660);

        static readonly int MarginLeft = Dp(18);
        static readonly int MarginRight = Dp(18);
        static readonly int MarginTop = Dp(16);
        static readonly int Gap = Dp(12);

        static readonly int TopButtonHeight = Dp(32);
        static readonly int SearchTop = Dp(84);
        static readonly int SearchHeight = Dp(40);
        static readonly int ListTop = SearchTop + SearchHeight + Gap;
        static readonly int LaunchHeight = Dp(62);

        static readonly int RowHeight = Dp(52);
        static readonly int RowPad = Dp(14);
        static readonly int BadgeSize = Dp(34);
        static readonly int BadgeGap = Dp(12);
        static readonly int CountWidth = Dp(120);

        Label titleLabel;
        Label folderLabel;
        Button settingsButton;
        Button refreshButton;
        Button cancelButton;
        Panel searchPanel;
        TextBox searchBox;
        Label placeholder;
        Label countLabel;
        Panel listFrame;
        ListBox listBox;
        Label emptyLabel;
        Button launchButton;
        ToolTip toolTip;

        List<ScriptEntry> allScripts = new List<ScriptEntry>();
        List<ScriptEntry> visibleScripts = new List<ScriptEntry>();
        string query = "";
        int hoverIndex = -1;
        bool launchHover;
        bool launchPressed;

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
            MinimumSize = new Size(Width, Height - WindowHeight + Dp(460));
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

            // Header: title + current folder on the left, buttons on the right.
            titleLabel = new Label
            {
                Text = Title,
                Font = FontTitle,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(MarginLeft - Dp(3), MarginTop),
            };
            Controls.Add(titleLabel);

            folderLabel = new Label
            {
                Font = FontSmall,
                ForeColor = TextMuted,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(MarginLeft - Dp(1), MarginTop + Dp(34)),
                Size = new Size(width - MarginLeft - MarginRight, Dp(20)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(folderLabel);

            settingsButton = AddTopButton("Settings", OnSettings);
            refreshButton = AddTopButton("Refresh", (s, e) => RefreshScripts());
            cancelButton = AddTopButton("Cancel", (s, e) => Close());

            SizeTopButton(settingsButton, Dp(88));
            SizeTopButton(refreshButton, Dp(84));
            SizeTopButton(cancelButton, Dp(80));

            // Search field: a white box with a magnifier, placeholder and count.
            searchPanel = new Panel
            {
                BackColor = Surface,
                Location = new Point(MarginLeft, SearchTop),
                Size = new Size(width - MarginLeft - MarginRight, SearchHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.IBeam,
            };
            searchPanel.Paint += OnSearchPanelPaint;
            searchPanel.Click += (s, e) => searchBox.Focus();
            Controls.Add(searchPanel);

            searchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = FontSearch,
                BackColor = Surface,
                ForeColor = Theme.Text,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            searchBox.Location = new Point(Dp(38), (SearchHeight - searchBox.Height) / 2);
            searchBox.Width = searchPanel.Width - Dp(38) - CountWidth - Dp(8);
            searchBox.TextChanged += (s, e) => ApplyFilter();
            searchBox.KeyDown += OnSearchKeyDown;
            searchBox.GotFocus += (s, e) => searchPanel.Invalidate();
            searchBox.LostFocus += (s, e) => searchPanel.Invalidate();
            searchPanel.Controls.Add(searchBox);

            placeholder = new Label
            {
                Text = "Search scripts...",
                Font = FontSearch,
                ForeColor = TextFaint,
                BackColor = Surface,
                AutoSize = true,
                Cursor = Cursors.IBeam,
                Location = new Point(searchBox.Left + Dp(2), searchBox.Top),
            };
            placeholder.Click += (s, e) => searchBox.Focus();
            searchPanel.Controls.Add(placeholder);
            placeholder.BringToFront();

            countLabel = new Label
            {
                Font = FontSmall,
                ForeColor = TextMuted,
                BackColor = Surface,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Size = new Size(CountWidth, SearchHeight - 2),
                Location = new Point(searchPanel.Width - CountWidth - Dp(12), 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            countLabel.Click += (s, e) => searchBox.Focus();
            searchPanel.Controls.Add(countLabel);

            launchButton = new Button
            {
                Text = "LAUNCH SCRIPT",
                Font = FontLaunch,
                ForeColor = Color.White,
                BackColor = Blue,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Location = new Point(MarginLeft, ClientSize.Height - LaunchHeight - Dp(18)),
                Size = new Size(width - MarginLeft - MarginRight, LaunchHeight),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            launchButton.FlatAppearance.BorderSize = 0;
            launchButton.Click += OnLaunch;
            launchButton.Paint += OnLaunchPaint;
            launchButton.MouseEnter += (s, e) => { launchHover = true; launchButton.Invalidate(); };
            launchButton.MouseLeave += (s, e) => { launchHover = false; launchPressed = false; launchButton.Invalidate(); };
            launchButton.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { launchPressed = true; launchButton.Invalidate(); }
            };
            launchButton.MouseUp += (s, e) => { launchPressed = false; launchButton.Invalidate(); };
            Controls.Add(launchButton);

            // Script list: 1px border around an owner-drawn list.
            listFrame = new Panel
            {
                BackColor = Theme.Border,
                Padding = new Padding(1),
                Location = new Point(MarginLeft, ListTop),
                Size = new Size(width - MarginLeft - MarginRight, launchButton.Top - Gap - ListTop),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(listFrame);

            listBox = new ListBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Font = FontName,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = RowHeight,
                HorizontalScrollbar = true,
                IntegralHeight = false,
            };
            listBox.DrawItem += OnDrawItem;
            listBox.DoubleClick += OnLaunch;
            listBox.SelectedIndexChanged += (s, e) => launchButton.Invalidate();
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
                Font = FontEmpty,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false,
            };
            listFrame.Controls.Add(emptyLabel);
            emptyLabel.BringToFront();

            toolTip = new ToolTip { InitialDelay = 700 };

            AcceptButton = launchButton;
            CancelButton = cancelButton;
            ActiveControl = searchBox;

            Resize += (s, e) => PositionTopButtons();
            Shown += (s, e) => searchBox.Focus();

            PositionTopButtons();
        }

        Button AddTopButton(string text, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                Font = FontUi,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            StyleButton(button);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        void SizeTopButton(Button button, int minimumWidth)
        {
            button.Size = new Size(Math.Max(minimumWidth, TextWidth(button.Text, button.Font) + Dp(30)), TopButtonHeight);
        }

        void PositionTopButtons()
        {
            if (cancelButton == null)
                return;

            int right = ClientSize.Width - MarginRight;
            int top = MarginTop + Dp(1);

            cancelButton.Location = new Point(right - cancelButton.Width, top);
            refreshButton.Location = new Point(cancelButton.Left - Dp(8) - refreshButton.Width, top);
            settingsButton.Location = new Point(refreshButton.Left - Dp(8) - settingsButton.Width, top);
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

        int RowWidthForName(string name)
        {
            return RowPad + BadgeSize + BadgeGap + TextWidth(name, FontName) + RowPad;
        }

        int LongestRowWidth()
        {
            int longest = 0;
            foreach (ScriptEntry script in allScripts)
                longest = Math.Max(longest, RowWidthForName(script.Name));
            return longest;
        }

        int IdealWidthForNames()
        {
            // Row content + list border/scrollbar + outer margins.
            int needed = LongestRowWidth() + SystemInformation.VerticalScrollBarWidth + 2 + MarginLeft + MarginRight;

            // Also guarantee room for the title and the top buttons.
            int topNeeded = MarginLeft + TextWidth(Title, FontTitle) + Dp(24)
                + settingsButton.Width + Dp(8) + refreshButton.Width + Dp(8) + cancelButton.Width + MarginRight;

            needed = Math.Max(needed, Math.Max(topNeeded, MinClientWidth));
            return Math.Min(needed, MaxAllowedClientWidth());
        }

        void ExpandForLongNames()
        {
            int wanted = IdealWidthForNames();

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

            PositionTopButtons();
        }

        // -- data -----------------------------------------------------------

        public void RefreshScripts()
        {
            allScripts = ScriptCatalog.GetPythonFiles(ScriptsFolder);
            foreach (ScriptEntry script in allScripts)
                script.Summary = ScriptCatalog.ReadSummary(script.Path);

            folderLabel.Text = ScriptsFolder.Length > 0 ? ScriptsFolder : "No scripts folder selected";
            toolTip.SetToolTip(folderLabel, ScriptsFolder);

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
            launchButton.Invalidate();
        }

        void UpdateStatus()
        {
            int total = allScripts.Count;
            int shown = visibleScripts.Count;

            if (query.Length > 0)
                countLabel.Text = shown + " of " + total;
            else
                countLabel.Text = total == 1 ? "1 script" : total + " scripts";

            if (shown > 0)
            {
                emptyLabel.Visible = false;
                return;
            }

            if (ScriptsFolder.Length == 0 || !Directory.Exists(ScriptsFolder))
                emptyLabel.Text = "No scripts folder selected.\nClick Settings to choose one.";
            else if (total == 0)
                emptyLabel.Text = "This folder has no .py files yet.\nAdd scripts to it, then click Refresh.";
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

            FillRect(graphics, selected ? RowSelected : index == hoverIndex ? RowHover : Surface, bounds);
            FillRect(graphics, RowDivider, new Rectangle(bounds.Left, bounds.Bottom - 1, bounds.Width, 1));
            if (selected)
                FillRect(graphics, Blue, new Rectangle(bounds.Left, bounds.Top, Dp(4), bounds.Height));

            // Coloured initials badge.
            var badge = new Rectangle(
                bounds.Left + RowPad,
                bounds.Top + (bounds.Height - BadgeSize) / 2,
                BadgeSize,
                BadgeSize);
            FillRounded(graphics, BadgeColors[ScriptCatalog.ColorIndex(script.Name, BadgeColors.Length)], badge, Dp(8));
            TextRenderer.DrawText(graphics, ScriptCatalog.Initials(script.Name), FontBadge, badge, Color.White,
                TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Name on the first line, file name + description on the second.
            int textLeft = badge.Right + BadgeGap;
            int textRight = bounds.Right - RowPad;
            int nameHeight = FontName.Height;
            int detailHeight = FontDetail.Height;
            int nameTop = bounds.Top + (bounds.Height - nameHeight - detailHeight) / 2;

            if (query.Length > 0)
            {
                int start = script.Name.ToLowerInvariant().IndexOf(query, StringComparison.Ordinal);
                if (start >= 0)
                {
                    int before = TextWidth(script.Name.Substring(0, start), FontName, graphics);
                    int match = TextWidth(script.Name.Substring(start, query.Length), FontName, graphics);
                    FillRounded(graphics, MatchBack,
                        new Rectangle(textLeft + before - Dp(1), nameTop, match + Dp(2), nameHeight), Dp(3));
                }
            }

            TextRenderer.DrawText(graphics, script.Name, FontName, new Point(textLeft, nameTop), Theme.Text, TextFlags);

            string detail = script.Summary.Length > 0
                ? script.FileName + "  ·  " + script.Summary
                : script.FileName;
            TextRenderer.DrawText(graphics, detail, FontDetail,
                new Rectangle(textLeft, nameTop + nameHeight, Math.Max(0, textRight - textLeft), detailHeight),
                TextMuted, TextFlags | TextFormatFlags.EndEllipsis);
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

            toolTip.SetToolTip(listBox, index >= 0 && index < visibleScripts.Count ? visibleScripts[index].Path : "");
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

        void OnSearchPanelPaint(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            bool focused = searchBox.Focused;

            using (var pen = new Pen(focused ? Blue : Theme.Border, 1f))
                graphics.DrawRectangle(pen, 0, 0, searchPanel.Width - 1, searchPanel.Height - 1);

            // Magnifier glyph.
            int size = Dp(11);
            int x = Dp(14);
            int y = (searchPanel.Height - size) / 2 - Dp(1);
            using (var pen = new Pen(focused ? Blue : TextMuted, (float)Math.Max(1.5, 1.6 * Theme.Scale)))
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

        // -- launch button --------------------------------------------------

        void OnLaunchPaint(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Rectangle rect = launchButton.ClientRectangle;

            FillRect(graphics, launchPressed ? BlueDown : launchHover ? BlueHover : Blue, rect);

            const string mainText = "LAUNCH SCRIPT";
            const TextFormatFlags centered = TextFlags | TextFormatFlags.HorizontalCenter;
            int mainHeight = FontLaunch.Height;
            ScriptEntry selected = CurrentScript();

            if (selected == null)
            {
                TextRenderer.DrawText(graphics, mainText, FontLaunch, rect, Color.White,
                    centered | TextFormatFlags.VerticalCenter);
                return;
            }

            int subHeight = FontLaunchSub.Height;
            int top = (rect.Height - mainHeight - subHeight) / 2;

            TextRenderer.DrawText(graphics, mainText, FontLaunch,
                new Rectangle(0, top, rect.Width, mainHeight), Color.White, centered);
            TextRenderer.DrawText(graphics, selected.Name, FontLaunchSub,
                new Rectangle(Dp(16), top + mainHeight, rect.Width - Dp(32), subHeight),
                BlueSoftText, centered | TextFormatFlags.EndEllipsis);
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
