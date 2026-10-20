using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Form = System.Windows.Forms.Form;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Rectangle = System.Drawing.Rectangle;
using Panel = System.Windows.Forms.Panel;
using Label = System.Windows.Forms.Label;
using View = Autodesk.Revit.DB.View;
using Control = System.Windows.Forms.Control;
using Timer = System.Windows.Forms.Timer;

namespace EKPrint
{
    internal static class FormKit
    {
        public static Label Text(Font font, Color color, string text)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.AutoEllipsis = true;
            l.UseMnemonic = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Font = font;
            l.ForeColor = color;
            l.Text = text ?? "";
            return l;
        }

        public static LinkLabel Link(string text)
        {
            LinkLabel l = new LinkLabel();
            l.AutoSize = true;
            l.Font = Ui.Body;
            l.Text = text;
            l.LinkColor = Ui.Accent;
            l.ActiveLinkColor = Ui.AccentPressed;
            l.VisitedLinkColor = Ui.Accent;
            l.DisabledLinkColor = Ui.Disabled;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.UseMnemonic = false;
            return l;
        }

        public static void Prepare(Form f, string title)
        {
            f.Text = title;
            f.ShowIcon = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.AutoScaleMode = AutoScaleMode.None;   // all metrics are scaled through Ui.S()
            f.BackColor = Ui.Window;
            f.ForeColor = Ui.Text;
            f.Font = Ui.Body;
        }
    }

    /// <summary>
    /// Shared sheet/view picker used by Select PDF and Select DWG.
    /// Behavior is identical to the v30 forms; only the presentation changed.
    /// </summary>
    public abstract class ViewPickerForm : Form
    {
        protected const int NameItem = 0;
        protected const int NameProjectItem = 1;
        protected const int NameCustom = 2;

        private readonly List<View> _all;
        private List<View> _visible = new List<View>();
        private readonly HashSet<string> _checked = new HashSet<string>();
        private readonly View _active;
        private readonly string _projectName;
        private bool _showSheets;
        private bool _ready;
        private bool _rebuilding;
        private bool _suppressNameChange;
        private bool _countPending;
        private int _nameMode = NameProjectItem;
        private string _customText = "";
        private SettingsData _settings;

        private readonly System.ComponentModel.Container _components = new System.ComponentModel.Container();
        private readonly ToolTip _tips;
        private readonly HeaderBar _header;
        private readonly Segmented _category;
        private readonly InputBox _search;
        private readonly Panel _listHost;
        private readonly CheckList _list;
        private readonly Label _countLabel;
        private readonly LinkLabel _allLink;
        private readonly LinkLabel _noneLink;
        private readonly Card _card;
        private readonly List<Control[]> _rows = new List<Control[]>();
        private readonly Segmented _naming;
        private readonly Label _nameLabel;
        private readonly InputBox _nameBox;
        private readonly Label _preview;
        private readonly Label _hint;
        private readonly FooterBar _footer;
        private readonly Label _folderLabel;
        private readonly LinkLabel _settingsLink;
        private readonly UiButton _cancel;
        private readonly UiButton _primary;

        public List<View> SelectedViews { get; private set; }
        protected string FinalName { get; private set; }
        protected string ProjectNameSafe { get { return _projectName; } }
        protected int CurrentNameMode { get { return _nameMode; } }

        protected abstract string Extension { get; }
        protected abstract string NameNoun { get; }
        protected abstract string MessageTitle { get; }
        protected abstract string FileNameFor(Document doc, View view, int nameMode, string customBase, int index);
        protected virtual bool CombineActive { get { return false; } }
        protected virtual string CombinedNameText { get { return _projectName; } }
        protected virtual void OnCombinedNameEdited(string text) { }
        /// <summary>Hint line under the file name; refreshed after Settings are changed.</summary>
        protected virtual string HintFor(SettingsData settings) { return ""; }

        protected ViewPickerForm(List<View> allViews, View activeView, string projectName, SettingsData settings,
            string title, string subtitle, string iconName, string primaryText)
        {
            _all = allViews ?? new List<View>();
            _active = activeView;
            _projectName = FileNames.Safe(projectName);
            _showSheets = activeView == null || activeView is ViewSheet;
            SelectedViews = new List<View>();
            FinalName = _projectName;

            if (activeView != null && RevitSelection.IsPrintable(activeView))
                _checked.Add(Key(activeView));

            FormKit.Prepare(this, Ui.Product + " \u2013 " + title);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            KeyPreview = true;
            _tips = new ToolTip(_components);

            _header = new HeaderBar();
            _header.Title = title;
            _header.Subtitle = subtitle;
            _header.IconImage = Ui.LoadIcon(iconName, 32);

            _category = new Segmented("Sheets", "Views");
            _category.SelectedIndex = _showSheets ? 0 : 1;
            _category.SelectedIndexChanged += delegate { SwitchCategory(_category.SelectedIndex == 0); };

            _search = new InputBox(true);
            _search.Cue = "Search by number, name or view type   (Ctrl+F)";
            _search.ValueChanged += delegate { if (_ready) RefreshList(); };

            _list = new CheckList();
            _list.Dock = DockStyle.Fill;
            _list.ItemChecked += ListItemChecked;
            _listHost = new Panel();
            _listHost.BackColor = Ui.Border;
            _listHost.Padding = new Padding(1);
            _listHost.Controls.Add(_list);

            _countLabel = FormKit.Text(Ui.Body, Ui.Muted, "");
            _allLink = FormKit.Link("Select all");
            _allLink.LinkClicked += delegate { CheckVisible(true); };
            _noneLink = FormKit.Link("Clear");
            _noneLink.LinkClicked += delegate { CheckVisible(false); };
            _tips.SetToolTip(_allLink, "Check every item currently shown in the list");
            _tips.SetToolTip(_noneLink, "Uncheck every item currently shown in the list");

            _card = new Card();
            _naming = new Segmented("", "");
            _naming.SelectedIndexChanged += delegate { NamingChosen(); };
            _nameLabel = FormKit.Text(Ui.BodySemibold, Ui.Text, "");
            _nameBox = new InputBox(false);
            _nameBox.Value = _projectName;
            _nameBox.ValueChanged += delegate { NameEdited(); };
            AddOptionRow("File names", _naming);
            _rows.Add(new Control[] { _nameLabel, _nameBox });
            _card.Controls.Add(_nameLabel);
            _card.Controls.Add(_nameBox);
            _preview = FormKit.Text(Ui.Body, Ui.Text, "");
            _hint = FormKit.Text(Ui.Small, Ui.Muted, "");
            _card.Controls.Add(_preview);
            _card.Controls.Add(_hint);

            _footer = new FooterBar();
            _folderLabel = FormKit.Text(Ui.Body, Ui.Muted, "");
            _settingsLink = FormKit.Link("Settings");
            _settingsLink.LinkClicked += delegate { OpenSettings(); };
            _tips.SetToolTip(_settingsLink, "Save folder, multiple-sheet behavior and DWG export options");
            _cancel = new UiButton("Cancel", false);
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            _primary = new UiButton(primaryText, true);
            _primary.Click += PrimaryClick;
            _footer.Controls.Add(_folderLabel);
            _footer.Controls.Add(_settingsLink);
            _footer.Controls.Add(_cancel);
            _footer.Controls.Add(_primary);

            Controls.Add(_header);
            Controls.Add(_category);
            Controls.Add(_search);
            Controls.Add(_listHost);
            Controls.Add(_countLabel);
            Controls.Add(_allLink);
            Controls.Add(_noneLink);
            Controls.Add(_card);
            Controls.Add(_footer);

            AcceptButton = _primary;
            CancelButton = _cancel;
            _settings = settings != null ? settings.Clone() : new SettingsData();
        }

        protected void RefreshHint()
        {
            if (_hint != null) _hint.Text = HintFor(_settings);
        }

        private void ApplySettings()
        {
            string folder = (_settings.OutputFolder ?? "").Trim();
            _folderLabel.Text = folder.Length > 0 ? "Saves to   " + folder : "No save folder chosen";
            _tips.SetToolTip(_folderLabel, folder);
            _hint.Text = HintFor(_settings);
        }

        private void OpenSettings()
        {
            using (SettingsForm form = new SettingsForm(SettingsStore.Load()))
            {
                form.StartPosition = FormStartPosition.CenterParent;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    SettingsStore.Save(form.Settings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the settings.\n\n" + ex.Message, MessageTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _settings = form.Settings.Clone();
                ApplySettings();
                LayoutAll();
            }
        }

        protected void AddOptionRow(string label, Control control)
        {
            InsertOptionRow(_rows.Count, label, control);
        }

        protected void InsertOptionRow(int index, string label, Control control)
        {
            Label l = FormKit.Text(Ui.BodySemibold, Ui.Text, label);
            _card.Controls.Add(l);
            _card.Controls.Add(control);
            _rows.Insert(Math.Max(0, Math.Min(index, _rows.Count)), new Control[] { l, control });
        }

        /// <summary>Called by the derived form once all its controls exist.</summary>
        protected void Initialize()
        {
            int tab = 0;
            _header.TabStop = false;
            _category.TabIndex = tab++;
            _search.TabIndex = tab++;
            _listHost.TabIndex = tab++;
            _allLink.TabIndex = tab++;
            _noneLink.TabIndex = tab++;
            _card.TabIndex = tab++;
            _settingsLink.TabIndex = 2;
            _footer.TabIndex = tab++;
            int rowTab = 0;
            foreach (Control[] row in _rows) row[1].TabIndex = rowTab++;
            _primary.TabIndex = 0;
            _cancel.TabIndex = 1;

            Rectangle work = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            int cw = Math.Min(Ui.S(900), (int)(work.Width * 0.92));
            int ch = Math.Min(Ui.S(780), (int)(work.Height * 0.92));
            ClientSize = new Size(cw, ch);
            MinimumSize = SizeFromClientSize(new Size(Math.Min(Ui.S(720), cw), Math.Min(Ui.S(600), ch)));

            ApplySettings();
            ApplyCategory();
            _ready = true;
            ApplyModeState();
            RefreshList();
            LayoutAll();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _list.FitColumns();
            _search.FocusBox();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.F)
            {
                _search.FocusBox();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _components.Dispose();
            base.Dispose(disposing);
        }

        private void LayoutAll()
        {
            if (!_ready) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int m = Ui.S(20);
            int headerH = Ui.S(68);
            int footerH = Ui.S(64);
            int rowH = Ui.S(32);
            int buttonH = Ui.S(34);

            _header.SetBounds(0, 0, w, headerH);

            _footer.SetBounds(0, h - footerH, w, footerH);
            int pw = _primary.PreferredButtonWidth;
            int cw = _cancel.PreferredButtonWidth;
            int by = (footerH - buttonH) / 2;
            _primary.SetBounds(w - m - pw, by, pw, buttonH);
            _cancel.SetBounds(_primary.Left - Ui.S(10) - cw, by, cw, buttonH);
            // "Saves to <folder>" then a small Settings link right after it (the folder text shrinks first).
            Size linkSize = _settingsLink.PreferredSize;
            int linkGap = Ui.S(14);
            int room = Math.Max(0, _cancel.Left - Ui.S(16) - m - linkGap - linkSize.Width);
            int folderW = Math.Min(room, Ui.TextWidth(_folderLabel.Text, _folderLabel.Font) + Ui.S(8));
            _folderLabel.SetBounds(m, 0, folderW, footerH);
            _settingsLink.SetBounds(m + folderW + (folderW > 0 ? linkGap : 0), (footerH - linkSize.Height) / 2,
                linkSize.Width, linkSize.Height);

            int pad = Ui.S(16);
            int gap = Ui.S(10);
            int labelW = Ui.S(100);
            int lineH = Ui.LineHeight(Ui.Body) + Ui.S(4);
            int smallH = Ui.LineHeight(Ui.Small) + Ui.S(4);
            int cardH = pad + _rows.Count * rowH + Math.Max(0, _rows.Count - 1) * gap + Ui.S(12) + lineH + smallH + pad;
            int cardW = w - m * 2;
            _card.SetBounds(m, _footer.Top - Ui.S(16) - cardH, cardW, cardH);

            int x0 = pad + labelW;
            int avail = Math.Max(Ui.S(120), cardW - x0 - pad);
            int y = pad;
            foreach (Control[] row in _rows)
            {
                row[0].SetBounds(pad, y, labelW - Ui.S(8), rowH);
                Segmented seg = row[1] as Segmented;
                if (row[1] is ComboBox)
                {
                    // A combo box has a fixed height; center it in the row.
                    row[1].SetBounds(x0, y + Math.Max(0, (rowH - row[1].Height) / 2), Math.Min(avail, Ui.S(320)), row[1].Height);
                }
                else
                {
                    int width = seg != null ? Math.Min(avail, Math.Max(Ui.S(280), seg.PreferredWidth)) : avail;
                    row[1].SetBounds(x0, y, width, rowH);
                }
                y += rowH + gap;
            }
            y += Ui.S(12) - gap;
            _preview.SetBounds(x0, y, avail, lineH);
            y += lineH;
            _hint.SetBounds(x0, y, avail, smallH);

            int top = headerH + Ui.S(16);
            int segW = Math.Max(Ui.S(180), _category.PreferredWidth);
            _category.SetBounds(m, top, segW, rowH);
            int sx = _category.Right + Ui.S(12);
            _search.SetBounds(sx, top, Math.Max(Ui.S(120), w - m - sx), rowH);

            int countH = Ui.S(22);
            int countY = _card.Top - Ui.S(12) - countH;
            Size noneSize = _noneLink.PreferredSize;
            Size allSize = _allLink.PreferredSize;
            _noneLink.SetBounds(w - m - noneSize.Width, countY + (countH - noneSize.Height) / 2, noneSize.Width, noneSize.Height);
            _allLink.SetBounds(_noneLink.Left - Ui.S(18) - allSize.Width, countY + (countH - allSize.Height) / 2, allSize.Width, allSize.Height);
            _countLabel.SetBounds(m, countY, Math.Max(0, _allLink.Left - Ui.S(12) - m), countH);

            int listTop = top + rowH + Ui.S(12);
            _listHost.SetBounds(m, listTop, w - m * 2, Math.Max(Ui.S(80), countY - Ui.S(6) - listTop));
        }

        // ------------------------------------------------------------------ data

        private static string Key(View v) { return v.Id.ToString(); }
        private bool Matches(View v) { return _showSheets ? v is ViewSheet : !(v is ViewSheet); }

        private static string FriendlyType(View v)
        {
            string raw;
            try { raw = v.ViewType.ToString(); }
            catch { return ""; }
            if (raw == "ThreeD") return "3D View";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(raw[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string SearchText(View v)
        {
            ViewSheet s = v as ViewSheet;
            if (s != null) return ((s.SheetNumber ?? "") + " " + (s.Name ?? "")).ToLowerInvariant();
            return ((v.Name ?? "") + " " + v.ViewType.ToString() + " " + FriendlyType(v)).ToLowerInvariant();
        }

        private List<View> CurrentSelected()
        {
            return _all.Where(v => Matches(v) && _checked.Contains(Key(v))).ToList();
        }

        private void ApplyCategory()
        {
            if (_showSheets)
            {
                _list.SetColumns(new string[] { "Number", "Sheet name" }, new int[] { Ui.S(130), Ui.S(320) }, 1);
                _naming.SetItems("Sheet # - Name", "Project - Sheet # - Name");
            }
            else
            {
                _list.SetColumns(new string[] { "View name", "Type" }, new int[] { Ui.S(320), Ui.S(170) }, 0);
                _naming.SetItems("View Name", "Project - View Name");
            }
        }

        private void RefreshList()
        {
            string q = (_search.Value ?? "").Trim().ToLowerInvariant();
            _visible = _all.Where(v => Matches(v) && (q.Length == 0 || SearchText(v).Contains(q))).ToList();

            _rebuilding = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                ListViewItem[] items = new ListViewItem[_visible.Count];
                for (int i = 0; i < _visible.Count; i++)
                {
                    View view = _visible[i];
                    ViewSheet sheet = view as ViewSheet;
                    ListViewItem item = new ListViewItem(sheet != null ? (sheet.SheetNumber ?? "") : (view.Name ?? ""));
                    item.SubItems.Add(sheet != null ? (sheet.Name ?? "") : FriendlyType(view));
                    item.Tag = Key(view);
                    item.Checked = _checked.Contains(Key(view));
                    items[i] = item;
                }
                _list.Items.AddRange(items);
            }
            finally
            {
                _list.EndUpdate();
                _rebuilding = false;
            }

            int inCategory = _all.Count(v => Matches(v));
            if (inCategory == 0)
                _list.EmptyText = _showSheets ? "This project has no printable sheets." : "This project has no printable views.";
            else
                _list.EmptyText = "Nothing matches \u201C" + (_search.Value ?? "").Trim() + "\u201D.";
            UpdateCount();
        }

        private void SyncChecks()
        {
            _rebuilding = true;
            _list.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _list.Items)
                {
                    bool want = _checked.Contains(item.Tag as string ?? "");
                    if (item.Checked != want) item.Checked = want;
                }
            }
            finally
            {
                _list.EndUpdate();
                _rebuilding = false;
            }
        }

        private void ListItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_rebuilding || !_ready || e.Item == null) return;
            string key = e.Item.Tag as string;
            if (key == null) return;
            if (e.Item.Checked) _checked.Add(key); else _checked.Remove(key);

            // Coalesce bursts (Space on many rows) into one refresh.
            if (_countPending || !IsHandleCreated) return;
            _countPending = true;
            BeginInvoke(new Action(delegate
            {
                _countPending = false;
                UpdateCount();
            }));
        }

        private void CheckVisible(bool on)
        {
            foreach (View view in _visible)
            {
                if (on) _checked.Add(Key(view)); else _checked.Remove(Key(view));
            }
            SyncChecks();
            UpdateCount();
        }

        private void SwitchCategory(bool sheets)
        {
            if (_showSheets == sheets) return;
            _showSheets = sheets;
            _checked.Clear();
            if (_active != null && RevitSelection.IsPrintable(_active) && Matches(_active))
                _checked.Add(Key(_active));
            ApplyCategory();
            ApplyModeState();
            RefreshList();
            LayoutAll();
        }

        private void UpdateCount()
        {
            if (IsDisposed) return;
            int n = CurrentSelected().Count;
            int total = _all.Count(v => Matches(v));
            string noun = _showSheets ? (total == 1 ? "sheet" : "sheets") : (total == 1 ? "view" : "views");
            string text = n.ToString() + " of " + total.ToString() + " " + noun + " selected";
            if (_visible.Count != total) text += "   \u00B7   " + _visible.Count.ToString() + " shown";
            _countLabel.Text = text;
            _primary.Enabled = n > 0;
            RefreshAutoName();
            RefreshPreview();
        }

        // ------------------------------------------------------------------ naming

        protected void ApplyModeState()
        {
            bool combine = CombineActive;
            _naming.Enabled = !combine;
            _naming.SelectedIndex = combine ? 1 : (_nameMode == NameCustom ? -1 : (_nameMode == NameItem ? 0 : 1));
            _nameLabel.Text = combine ? NameNoun + " name" : (_nameMode == NameCustom ? "Base name" : "File name");
            if (combine) SetNameText(CombinedNameText);
            else if (_nameMode == NameCustom) SetNameText(_customText);
            else RefreshAutoName();
            RefreshPreview();
        }

        private void NamingChosen()
        {
            if (!_ready || CombineActive) return;
            int i = _naming.SelectedIndex;
            if (i < 0) return;
            _nameMode = i == 0 ? NameItem : NameProjectItem;
            ApplyModeState();
        }

        private void NameEdited()
        {
            if (!_ready || _suppressNameChange) return;
            if (CombineActive)
            {
                OnCombinedNameEdited(_nameBox.Value);
                RefreshPreview();
                return;
            }
            _customText = _nameBox.Value;
            if (_nameMode != NameCustom)
            {
                _nameMode = NameCustom;
                ApplyModeState();
            }
            else
            {
                RefreshPreview();
            }
        }

        private void RefreshAutoName()
        {
            if (!_ready || CombineActive || _nameMode == NameCustom) return;
            View first = CurrentSelected().FirstOrDefault() ?? _visible.FirstOrDefault();
            if (first == null) return;
            SetNameText(_nameMode == NameProjectItem ? FileNames.ProjectItemName(first.Document, first) : FileNames.ItemName(first));
        }

        private void SetNameText(string text)
        {
            text = text ?? "";
            if (_nameBox.Value == text) return;
            _suppressNameChange = true;
            try { _nameBox.Value = text; }
            finally { _suppressNameChange = false; }
        }

        private void RefreshPreview()
        {
            if (!_ready) return;
            List<View> selected = CurrentSelected();
            string unit = _showSheets ? "sheets" : "views";
            string raw = _nameBox.Value.Trim();
            string text;
            if (selected.Count == 0)
            {
                text = "Select at least one " + (_showSheets ? "sheet" : "view") + ".";
            }
            else if ((CombineActive || _nameMode == NameCustom) && raw.Length == 0)
            {
                text = "Enter a " + (CombineActive ? NameNoun : "base") + " name.";
            }
            else if (CombineActive)
            {
                text = "\u2192   " + FileNames.Safe(raw) + Extension;
                if (selected.Count > 1) text += "      " + selected.Count.ToString() + " " + unit + " combined";
            }
            else
            {
                List<View> sorted = RevitSelection.SortViews(selected);
                string customBase = FileNames.Safe(raw.Length == 0 ? _projectName : raw);
                text = "\u2192   " + FileNames.Safe(FileNameFor(sorted[0].Document, sorted[0], _nameMode, customBase, 1)) + Extension;
                if (sorted.Count > 1) text += "      + " + (sorted.Count - 1).ToString() + " more";
            }
            _preview.Text = text;
        }

        private void PrimaryClick(object sender, EventArgs e)
        {
            List<View> selected = CurrentSelected();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select at least one sheet or view.", MessageTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string raw = _nameBox.Value.Trim();
            if ((CombineActive || _nameMode == NameCustom) && raw.Length == 0)
            {
                MessageBox.Show(this, "Enter a " + NameNoun + " name.", MessageTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _nameBox.FocusBox();
                return;
            }
            SelectedViews = RevitSelection.SortViews(selected);
            FinalName = FileNames.Safe(raw.Length == 0 ? _projectName : raw);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class PdfPickerForm : ViewPickerForm
    {
        private bool _combine = true;
        private string _combineName;
        private readonly Segmented _output;
        private readonly ComboBox _paper;

        public PdfPickerForm(List<View> allViews, View activeView, string projectName, SettingsData settings)
            : base(allViews, activeView, projectName, settings,
                "Print to PDF",
                "Choose sheets or views, then choose how the PDF files are created.",
                "SelectPDF", "Print")
        {
            _combineName = ProjectNameSafe;
            _output = new Segmented("Combine into one PDF", "Separate PDFs");
            _output.SelectedIndex = 0;
            _output.SelectedIndexChanged += delegate
            {
                bool combine = _output.SelectedIndex == 0;
                if (combine == _combine) return;
                _combine = combine;
                ApplyModeState();
            };
            InsertOptionRow(0, "Output", _output);

            // Paper size: "Use sheet size" unless the user picks a fixed size for this print.
            _paper = new ComboBox();
            _paper.DropDownStyle = ComboBoxStyle.DropDownList;
            _paper.FlatStyle = FlatStyle.System;
            _paper.Font = Ui.Body;
            _paper.MaxDropDownItems = 16;
            _paper.Items.AddRange(PaperChoice.All);
            _paper.SelectedItem = PaperChoice.SheetSize;
            _paper.SelectedIndexChanged += delegate { RefreshHint(); };
            InsertOptionRow(1, "Paper size", _paper);
            Initialize();
        }

        public bool Combine { get { return _combine; } }

        public PdfNameMode NameMode
        {
            get
            {
                if (_combine) return PdfNameMode.ProjectItem;   // v30: Combine forces Project - Item naming
                if (CurrentNameMode == NameItem) return PdfNameMode.Item;
                if (CurrentNameMode == NameCustom) return PdfNameMode.CustomNumbered;
                return PdfNameMode.ProjectItem;
            }
        }

        public string PdfName { get { return FinalName; } }

        public ExportPaperFormat Paper
        {
            get
            {
                PaperChoice choice = _paper.SelectedItem as PaperChoice;
                return choice != null ? choice.Format : ExportPaperFormat.Default;
            }
        }

        protected override bool CombineActive { get { return _combine; } }
        protected override string CombinedNameText { get { return _combineName ?? ProjectNameSafe; } }
        protected override void OnCombinedNameEdited(string text) { _combineName = text; }
        protected override string HintFor(SettingsData settings)
        {
            PaperChoice choice = _paper == null ? null : _paper.SelectedItem as PaperChoice;
            string paper = choice == null || choice.UsesSheetSize
                ? "Paper size follows each sheet"
                : "Fit to page on " + choice.Label.Split(new string[] { "  (" }, StringSplitOptions.None)[0];
            return "Revit PDF export  \u00B7  Vector, color  \u00B7  " + paper +
                "  \u00B7  Crop regions, scope boxes and reference planes hidden";
        }
        protected override string Extension { get { return ".pdf"; } }
        protected override string NameNoun { get { return "PDF"; } }
        protected override string MessageTitle { get { return "EK Print"; } }

        protected override string FileNameFor(Document doc, View view, int nameMode, string customBase, int index)
        {
            PdfNameMode mode = nameMode == NameItem ? PdfNameMode.Item
                : (nameMode == NameCustom ? PdfNameMode.CustomNumbered : PdfNameMode.ProjectItem);
            return PdfExportService.NameFor(doc, view, mode, customBase, index);
        }
    }

    public sealed class DwgPickerForm : ViewPickerForm
    {
        public DwgPickerForm(List<View> allViews, View activeView, string projectName, SettingsData settings)
            : base(allViews, activeView, projectName, settings,
                "Export to DWG",
                "Choose sheets or views to export with the EK DWG settings.",
                "SelectDWG", "Export DWG")
        {
            Initialize();
        }

        public DwgNameMode NameMode
        {
            get
            {
                if (CurrentNameMode == NameItem) return DwgNameMode.Item;
                if (CurrentNameMode == NameCustom) return DwgNameMode.CustomNumbered;
                return DwgNameMode.ProjectItem;
            }
        }

        public string DwgName { get { return FinalName; } }

        protected override string HintFor(SettingsData settings)
        {
            return DwgExportService.Describe(settings) + "  \u00B7  DWG files only";
        }
        protected override string Extension { get { return ".dwg"; } }
        protected override string NameNoun { get { return "DWG"; } }
        protected override string MessageTitle { get { return "EK Select DWG"; } }

        protected override string FileNameFor(Document doc, View view, int nameMode, string customBase, int index)
        {
            DwgNameMode mode = nameMode == NameItem ? DwgNameMode.Item
                : (nameMode == NameCustom ? DwgNameMode.CustomNumbered : DwgNameMode.ProjectItem);
            return DwgExportService.NameFor(doc, view, mode, customBase, index);
        }
    }

    public sealed class SettingsForm : Form
    {
        private readonly InputBox _folder;
        private readonly Label _folderState;
        private readonly Segmented _mode;
        private readonly Label _modeHelp;
        private readonly Segmented _dwgColors;
        private readonly Segmented _dwgUnit;
        private readonly Segmented _dwgCoords;
        private readonly Label _dwgHelp;

        public SettingsData Settings { get; private set; }

        public SettingsForm(SettingsData current)
        {
            Settings = current != null ? current.Clone() : new SettingsData();

            FormKit.Prepare(this, Ui.Product + " \u2013 Settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            int w = Ui.S(640);
            int m = Ui.S(20);
            int pad = Ui.S(18);
            int rowH = Ui.S(32);
            int sectionH = Ui.LineHeight(Ui.Section) + Ui.S(2);
            int lineH = Ui.LineHeight(Ui.Body) + Ui.S(4);
            int cardW = w - m * 2;
            int inner = cardW - pad * 2;

            HeaderBar header = new HeaderBar();
            header.Title = "EK Print Settings";
            header.Subtitle = "Defaults for PDF and DWG export. Saved until you change them.";
            header.IconImage = Ui.LoadIcon("Settings", 32);
            header.SetBounds(0, 0, w, Ui.S(68));
            Controls.Add(header);

            // Save folder
            Card folderCard = new Card();
            int y = pad;
            Label folderTitle = FormKit.Text(Ui.Section, Ui.Text, "Save folder");
            folderTitle.SetBounds(pad, y, inner, sectionH);
            y += sectionH;
            Label folderHelp = FormKit.Text(Ui.Body, Ui.Muted, "PDF and DWG exports are saved to this folder.");
            folderHelp.SetBounds(pad, y, inner, lineH);
            y += lineH + Ui.S(8);

            UiButton browse = new UiButton("Browse\u2026", false);
            int browseW = browse.PreferredButtonWidth;
            _folder = new InputBox(false);
            _folder.Value = Settings.OutputFolder ?? "";
            _folder.Cue = "Choose a folder";
            _folder.SetBounds(pad, y, inner - browseW - Ui.S(10), rowH);
            browse.SetBounds(pad + inner - browseW, y, browseW, rowH);
            browse.Click += BrowseClick;
            y += rowH + Ui.S(8);

            LinkLabel open = FormKit.Link("Open folder");
            open.Location = new Point(pad, y + (lineH - open.PreferredSize.Height) / 2);
            open.LinkClicked += delegate
            {
                string f = (_folder.Value ?? "").Trim();
                if (f.Length > 0 && Directory.Exists(f)) Shell.OpenFolder(f);
            };
            _folderState = FormKit.Text(Ui.Body, Color.FromArgb(180, 52, 40), "");
            _folderState.TextAlign = ContentAlignment.MiddleRight;
            _folderState.SetBounds(pad + Ui.S(110), y, inner - Ui.S(110), lineH);
            _folder.ValueChanged += delegate { UpdateFolderState(); };
            y += lineH + pad;

            folderCard.Controls.Add(folderTitle);
            folderCard.Controls.Add(folderHelp);
            folderCard.Controls.Add(_folder);
            folderCard.Controls.Add(browse);
            folderCard.Controls.Add(open);
            folderCard.Controls.Add(_folderState);
            int top = Ui.S(68) + Ui.S(18);
            folderCard.SetBounds(m, top, cardW, y);
            Controls.Add(folderCard);

            // Multiple selection behavior
            Card modeCard = new Card();
            y = pad;
            Label modeTitle = FormKit.Text(Ui.Section, Ui.Text, "Multiple sheets or views");
            modeTitle.SetBounds(pad, y, inner, sectionH);
            y += sectionH;
            Label modeIntro = FormKit.Text(Ui.Body, Ui.Muted, "How Quick PDF and Sheet Print save when more than one item is selected.");
            modeIntro.SetBounds(pad, y, inner, lineH);
            y += lineH + Ui.S(8);
            _mode = new Segmented("Combine into one PDF", "Separate PDFs");
            _mode.SelectedIndex = Settings.QuickMultipleMode == QuickMultipleMode.Separate ? 1 : 0;
            _mode.SetBounds(pad, y, Math.Min(inner, Math.Max(Ui.S(320), _mode.PreferredWidth)), rowH);
            _mode.SelectedIndexChanged += delegate { UpdateModeHelp(); };
            y += rowH + Ui.S(8);
            _modeHelp = FormKit.Text(Ui.Body, Ui.Text, "");
            _modeHelp.SetBounds(pad, y, inner, lineH);
            y += lineH + pad;

            modeCard.Controls.Add(modeTitle);
            modeCard.Controls.Add(modeIntro);
            modeCard.Controls.Add(_mode);
            modeCard.Controls.Add(_modeHelp);
            modeCard.SetBounds(m, folderCard.Bottom + Ui.S(14), cardW, y);
            Controls.Add(modeCard);

            // DWG export options (Quick DWG and Select DWG)
            Card dwgCard = new Card();
            y = pad;
            Label dwgTitle = FormKit.Text(Ui.Section, Ui.Text, "DWG export");
            dwgTitle.SetBounds(pad, y, inner, sectionH);
            y += sectionH;
            Label dwgIntro = FormKit.Text(Ui.Body, Ui.Muted, "Used by Quick DWG and Select DWG. Kept until you change them here.");
            dwgIntro.SetBounds(pad, y, inner, lineH);
            y += lineH + Ui.S(8);
            dwgCard.Controls.Add(dwgTitle);
            dwgCard.Controls.Add(dwgIntro);

            int labelW = Ui.S(96);
            int fieldW = inner - labelW;
            _dwgColors = new Segmented("Index color", "Object styles", "View");
            _dwgColors.SelectedIndex = (int)Settings.DwgColors;
            _dwgColors.SelectedIndexChanged += delegate { UpdateDwgHelp(); };
            y = AddRow(dwgCard, "Colors", _dwgColors, pad, y, labelW, fieldW, rowH);
            _dwgHelp = FormKit.Text(Ui.Small, Ui.Muted, "");
            _dwgHelp.SetBounds(pad + labelW, y - Ui.S(4), fieldW, Ui.LineHeight(Ui.Small) + Ui.S(4));
            dwgCard.Controls.Add(_dwgHelp);
            y += Ui.LineHeight(Ui.Small) + Ui.S(8);

            _dwgUnit = new Segmented("Foot", "Inch", "Meter", "Centimeter", "Millimeter");
            _dwgUnit.SelectedIndex = (int)Settings.DwgUnit;
            y = AddRow(dwgCard, "One unit is", _dwgUnit, pad, y, labelW, fieldW, rowH);

            _dwgCoords = new Segmented("Shared coordinates", "Internal origin");
            _dwgCoords.SelectedIndex = (int)Settings.DwgCoordinates;
            y = AddRow(dwgCard, "Coordinates", _dwgCoords, pad, y, labelW, fieldW, rowH);
            y += pad - Ui.S(10);

            dwgCard.SetBounds(m, modeCard.Bottom + Ui.S(14), cardW, y);
            Controls.Add(dwgCard);

            // Footer
            int footerH = Ui.S(64);
            int buttonH = Ui.S(34);
            int h = dwgCard.Bottom + Ui.S(20) + footerH;
            FooterBar footer = new FooterBar();
            footer.SetBounds(0, h - footerH, w, footerH);
            UiButton save = new UiButton("Save", true);
            UiButton cancel = new UiButton("Cancel", false);
            int sw = save.PreferredButtonWidth;
            int cw = cancel.PreferredButtonWidth;
            save.SetBounds(w - m - sw, (footerH - buttonH) / 2, sw, buttonH);
            cancel.SetBounds(save.Left - Ui.S(10) - cw, save.Top, cw, buttonH);
            save.Click += SaveClick;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Label version = FormKit.Text(Ui.Small, Ui.Muted, Ui.Product + "   v" + Ui.Version);
            version.SetBounds(m, 0, Math.Max(0, cancel.Left - m - Ui.S(12)), footerH);
            footer.Controls.Add(version);
            footer.Controls.Add(cancel);
            footer.Controls.Add(save);
            Controls.Add(footer);

            // On a small screen at high display scale the window scrolls instead of running off the screen.
            Rectangle work = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            int maxH = (int)(work.Height * 0.92) - (SizeFromClientSize(Size.Empty).Height);
            if (h > maxH)
            {
                AutoScroll = true;
                ClientSize = new Size(w + SystemInformation.VerticalScrollBarWidth, maxH);
            }
            else
            {
                ClientSize = new Size(w, h);
            }
            AcceptButton = save;
            CancelButton = cancel;
            save.TabIndex = 0;
            cancel.TabIndex = 1;
            folderCard.TabIndex = 0;
            modeCard.TabIndex = 1;
            dwgCard.TabIndex = 2;
            footer.TabIndex = 3;

            UpdateFolderState();
            UpdateModeHelp();
            UpdateDwgHelp();
        }

        private static int AddRow(Control card, string label, Segmented field, int x, int y, int labelW, int fieldW, int rowH)
        {
            Label l = FormKit.Text(Ui.BodySemibold, Ui.Text, label);
            l.SetBounds(x, y, labelW - Ui.S(8), rowH);
            field.SetBounds(x + labelW, y, Math.Min(fieldW, Math.Max(Ui.S(240), field.PreferredWidth)), rowH);
            field.TabIndex = card.Controls.Count;
            card.Controls.Add(l);
            card.Controls.Add(field);
            return y + rowH + Ui.S(10);
        }

        private void UpdateDwgHelp()
        {
            switch (_dwgColors.SelectedIndex)
            {
                case 0: _dwgHelp.Text = "255 AutoCAD colors, from the color IDs on the layers."; break;
                case 1: _dwgHelp.Text = "True color (RGB) from Object Styles."; break;
                default: _dwgHelp.Text = "True color (RGB) as shown in the Revit view."; break;
            }
        }

        private void UpdateFolderState()
        {
            string f = (_folder.Value ?? "").Trim();
            if (f.Length == 0) _folderState.Text = "No folder selected";
            else if (!Directory.Exists(f)) _folderState.Text = "This folder does not exist";
            else _folderState.Text = "";
        }

        private void UpdateModeHelp()
        {
            _modeHelp.Text = _mode.SelectedIndex == 1
                ? "Each sheet or view is saved as its own PDF:  Project - Sheet # - Name.pdf"
                : "All selected sheets or views are merged into a single PDF.";
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose the folder for EK PDF and DWG exports";
                dlg.ShowNewFolderButton = true;
                string current = (_folder.Value ?? "").Trim();
                if (current.Length > 0 && Directory.Exists(current))
                    dlg.SelectedPath = current;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _folder.Value = dlg.SelectedPath;
            }
        }

        private void SaveClick(object sender, EventArgs e)
        {
            string folder = (_folder.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "Choose a valid save folder.", "EK Print Settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Settings.OutputFolder = folder;
            Settings.QuickMultipleMode = _mode.SelectedIndex == 1 ? QuickMultipleMode.Separate : QuickMultipleMode.Combine;
            Settings.DwgColors = (DwgColorMode)Math.Max(0, _dwgColors.SelectedIndex);
            Settings.DwgUnit = (DwgUnit)Math.Max(0, _dwgUnit.SelectedIndex);
            Settings.DwgCoordinates = (DwgCoordinates)Math.Max(0, _dwgCoords.SelectedIndex);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Progress window state shared between Revit's thread and the window's own UI thread.
    /// The window runs its own message loop, so it keeps painting and Cancel Print stays clickable
    /// while Revit is busy inside Document.Export. Revit's thread only writes plain fields here;
    /// the window polls them, so no call ever crosses threads.
    /// </summary>
    internal sealed class ExportProgress
    {
        private readonly object _lock = new object();
        private readonly ManualResetEvent _closed = new ManualResetEvent(false);
        private Thread _thread;
        private volatile bool _cancel;
        private IntPtr _dpiContext = IntPtr.Zero;

        private int _done;
        private int _total = 1;
        private double _item;
        private string _counter = "";
        private string _label = "";

        public string Title { get; private set; }
        public string Note { get; private set; }
        public bool CancelRequested { get { return _cancel; } }
        internal bool IsClosed { get { return _closed.WaitOne(0); } }

        /// <summary>Starts the window thread. With a delay, the window only appears if the job is still running then.</summary>
        public static ExportProgress Start(string title, string note, int delayMs)
        {
            ExportProgress p = new ExportProgress();
            p.Title = title ?? "";
            p.Note = note ?? "";
            try { p._dpiContext = GetThreadDpiAwarenessContext(); } catch { }
            try
            {
                Thread t = new Thread(delegate() { p.Run(delayMs); });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Name = "EK Print progress";
                p._thread = t;
                t.Start();
                return p;
            }
            catch
            {
                return null;
            }
        }

        private void Run(int delayMs)
        {
            try
            {
                if (_closed.WaitOne(Math.Max(0, delayMs))) return;   // job finished before the window was needed
                // Match Revit's DPI mode so Ui.S() metrics mean the same on this thread.
                try { if (_dpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(_dpiContext); } catch { }
                using (PdfProgressForm form = new PdfProgressForm(this))
                    System.Windows.Forms.Application.Run(form);
            }
            catch { }
        }

        internal void RequestCancel() { _cancel = true; }

        public void SetStep(int done, int total, string counter, string label)
        {
            lock (_lock)
            {
                _total = Math.Max(1, total);
                _done = Math.Max(0, Math.Min(done, _total));
                _item = 0;
                _counter = counter ?? "";
                _label = label ?? "";
            }
        }

        /// <summary>Progress inside the current step, from Revit's own progress bar (0..1). Never moves backwards.</summary>
        public void SetItemFraction(double fraction)
        {
            lock (_lock)
            {
                if (fraction > _item) _item = Math.Min(1.0, fraction);
            }
        }

        internal void Read(out string counter, out string label, out double fraction)
        {
            lock (_lock)
            {
                counter = _counter;
                label = _label;
                fraction = Math.Min(1.0, (_done + (_done < _total ? _item : 0)) / _total);
            }
        }

        public void Close()
        {
            _closed.Set();
            if (_thread != null)
            {
                try { _thread.Join(3000); } catch { }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }

    public sealed class PdfProgressForm : Form
    {
        private const int BarSteps = 1000;
        private readonly ExportProgress _host;
        private readonly Label _title;
        private readonly Label _counter;
        private readonly Label _status;
        private readonly ProgressLine _bar;
        private readonly UiButton _cancelButton;
        private readonly Timer _timer;

        internal PdfProgressForm(ExportProgress host)
        {
            _host = host;
            FormKit.Prepare(this, "EK Print - Exporting");
            BackColor = Ui.Surface;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = true;   // no owner window: Revit's thread is busy, so it cannot own this one
            FormBorderStyle = FormBorderStyle.FixedDialog;

            int w = Ui.S(480);
            int m = Ui.S(24);
            int sectionH = Ui.LineHeight(Ui.Section) + Ui.S(2);
            int lineH = Ui.LineHeight(Ui.Body) + Ui.S(4);
            int counterW = Ui.S(120);

            _title = FormKit.Text(Ui.Section, Ui.Text, host.Title);
            _title.SetBounds(m, Ui.S(22), w - m * 2 - counterW, sectionH);
            _counter = FormKit.Text(Ui.Body, Ui.Muted, "");
            _counter.TextAlign = ContentAlignment.MiddleRight;
            _counter.SetBounds(w - m - counterW, Ui.S(22), counterW, sectionH);

            _status = FormKit.Text(Ui.Body, Ui.Muted, "Preparing PDF export…");
            _status.SetBounds(m, _title.Bottom + Ui.S(6), w - m * 2, lineH);

            _bar = new ProgressLine();
            _bar.Maximum = BarSteps;
            _bar.SetBounds(m, _status.Bottom + Ui.S(12), w - m * 2, Math.Max(4, Ui.S(6)));

            int footerH = Ui.S(60);
            int h = _bar.Bottom + Ui.S(24) + footerH;
            FooterBar footer = new FooterBar();
            footer.SetBounds(0, h - footerH, w, footerH);
            _cancelButton = new UiButton("Cancel Print", false);
            int bw = _cancelButton.PreferredButtonWidth;
            int bh = Ui.S(34);
            _cancelButton.SetBounds(w - m - bw, (footerH - bh) / 2, bw, bh);
            _cancelButton.Click += delegate { RequestCancel(); };
            Label note = FormKit.Text(Ui.Small, Ui.Muted, host.Note);
            note.SetBounds(m, 0, Math.Max(0, _cancelButton.Left - m - Ui.S(12)), footerH);
            footer.Controls.Add(note);
            footer.Controls.Add(_cancelButton);

            Controls.Add(_title);
            Controls.Add(_counter);
            Controls.Add(_status);
            Controls.Add(_bar);
            Controls.Add(footer);
            ClientSize = new Size(w, h);
            CancelButton = _cancelButton;

            _timer = new Timer();
            _timer.Interval = 100;
            _timer.Tick += delegate { Poll(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Poll();
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 means Cancel Print; the window only really closes when the job is over.
            if (!_host.IsClosed)
            {
                e.Cancel = true;
                RequestCancel();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        private void RequestCancel()
        {
            if (_host.CancelRequested) return;
            _host.RequestCancel();
            _cancelButton.Enabled = false;
            _cancelButton.Text = "Stopping…";
            _status.Text = "Stopping – waiting for Revit to stop the export…";
        }

        private void Poll()
        {
            if (_host.IsClosed)
            {
                _timer.Stop();
                Close();
                return;
            }
            string counter, label;
            double fraction;
            _host.Read(out counter, out label, out fraction);
            if (_counter.Text != counter) _counter.Text = counter;
            if (!_host.CancelRequested && label.Length > 0 && _status.Text != label) _status.Text = label;
            int value = (int)Math.Round(fraction * BarSteps);
            if (_bar.Value != value) _bar.Value = value;
        }
    }
}
