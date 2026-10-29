using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EKFastPDF.Core.Library;

namespace EKFastPDF.Services;

/// <summary>
/// Settings › New PDFs (docs/mockups/library.png, panel 3). Everything can be off:
/// the Open newest PDF button, opening recent PDFs when EKPDF starts, opening recent PDFs not
/// opened yet when the window comes forward, and per folder "Open automatically" or "Nothing".
/// </summary>
public sealed class NewPdfsDialog : EkDialog
{
    private readonly CheckBox _newest, _onStart, _onActivate;
    private readonly TextBox _minutes;
    private readonly List<(LibraryFolder Folder, Button Choice)> _folders = new();
    private readonly Button _closed;
    private static readonly (string Label, string Value)[] ClosedChoices =
    {
        ("Do nothing", "Nothing"),
        ("Start EKPDF when a PDF lands", "Watcher"),
        ("Keep EKPDF in the tray", "Tray"),
    };

    public NewPdfsDialog(Window owner, AppSettings s, IReadOnlyList<LibraryFolder> folders) : base(owner, "EKPDF Settings · New PDFs", 470)
    {
        var panel = new StackPanel { Margin = new Thickness(18, 12, 20, 10) };
        _newest = Check("Show the Open newest PDF button", s.ShowNewestButton);
        panel.Children.Add(_newest);
        panel.Children.Add(Line());

        var recent = new DockPanel { Height = 30 };
        var unit = Soft("minute(s)"); unit.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(unit, Dock.Right);
        _minutes = new TextBox { Width = 46, Text = s.RecentMinutes.ToString(CultureInfo.CurrentCulture), TextAlignment = TextAlignment.Right };
        _minutes.SetResourceReference(StyleProperty, "FieldBox");
        DockPanel.SetDock(_minutes, Dock.Right);
        recent.Children.Add(unit);
        recent.Children.Add(_minutes);
        recent.Children.Add(new TextBlock { Text = "Recent means arrived in the last", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(recent);
        _onStart = Check("When EKPDF starts, open recent PDFs", s.OpenRecentOnStart);
        panel.Children.Add(_onStart);
        _onActivate = Check("When the EKPDF window comes forward, open recent PDFs not yet opened", s.OpenRecentOnActivate);
        panel.Children.Add(_onActivate);
        panel.Children.Add(Line());

        // Open automatically folders while EKPDF is closed.
        var closedRow = new DockPanel { Height = 30 };
        _closed = Dropdown(ClosedChoices, s.ClosedMode, 210);
        DockPanel.SetDock(_closed, Dock.Right);
        closedRow.Children.Add(_closed);
        closedRow.Children.Add(new TextBlock { Text = "When EKPDF is closed", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(closedRow);
        panel.Children.Add(Line());

        var header = Soft("Folders"); header.FontSize = 11; header.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(header);
        foreach (var f in folders)
        {
            var row = new DockPanel { Height = 30 };
            var choice = Choice(f.AutoOpen);
            DockPanel.SetDock(choice, Dock.Right);
            row.Children.Add(choice);
            row.Children.Add(new TextBlock { Text = f.Name, ToolTip = f.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(row);
            _folders.Add((f, choice));
        }
        if (folders.Count == 0) panel.Children.Add(Soft("Add folders in My PDFs."));
        Body = panel;

        FooterLeft = Link("Reset", () =>
        {
            _newest.IsChecked = true; _onStart.IsChecked = true; _onActivate.IsChecked = false;
            _minutes.Text = "1";
            SetDropdown(_closed, ClosedChoices, "Nothing");
            foreach (var (_, c) in _folders) SetChoice(c, false);
        });
        AddButton("Cancel", false);
        AddButton("Save", true, () =>
        {
            s.ShowNewestButton = _newest.IsChecked == true;
            s.OpenRecentOnStart = _onStart.IsChecked == true;
            s.OpenRecentOnActivate = _onActivate.IsChecked == true;
            if (int.TryParse(_minutes.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int m)) s.RecentMinutes = Math.Clamp(m, 1, 24 * 60);
            foreach (var (f, c) in _folders) f.AutoOpen = (bool)c.Tag;
            s.ClosedMode = (string)_closed.Tag;
            s.Save();
            DialogResult = true;
        });
    }

    private static CheckBox Check(string text, bool value)
    {
        var c = new CheckBox { Content = text, IsChecked = value, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        c.SetResourceReference(StyleProperty, "EkCheck");
        return c;
    }

    private static Border Line()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        b.SetResourceReference(Border.BackgroundProperty, "Line");
        return b;
    }

    private static TextBlock Soft(string text)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        return t;
    }

    /// <summary>A dropdown with any number of choices (a button with a menu).</summary>
    private static Button Dropdown((string Label, string Value)[] choices, string value, double width)
    {
        var b = new Button { Width = width, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.SetResourceReference(StyleProperty, "SoftButton");
        SetDropdown(b, choices, value);
        b.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom, MinWidth = width };
            foreach (var (label, v) in choices)
            {
                var item = new MenuItem { Header = label, IsChecked = (string)b.Tag == v };
                item.Click += (_, _) => SetDropdown(b, choices, v);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return b;
    }

    private static void SetDropdown(Button b, (string Label, string Value)[] choices, string value)
    {
        var chosen = choices.FirstOrDefault(c => c.Value == value);
        if (chosen.Label is null) chosen = choices[0];
        b.Tag = chosen.Value;
        var row = new DockPanel { Width = b.Width - 22 };
        var arrow = new TextBlock { Text = "▾" };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock { Text = chosen.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        b.Content = row;
    }

    /// <summary>A dropdown with two choices (a button with a menu; no combo box chrome).</summary>
    private static Button Choice(bool auto)
    {
        var b = new Button { Width = 150, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.SetResourceReference(StyleProperty, "SoftButton");
        SetChoice(b, auto);
        b.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom, MinWidth = 150 };
            foreach (var (text, value) in new[] { ("Open automatically", true), ("Nothing", false) })
            {
                var item = new MenuItem { Header = text, IsChecked = (bool)b.Tag == value };
                item.Click += (_, _) => SetChoice(b, value);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return b;
    }

    private static void SetChoice(Button b, bool auto)
    {
        b.Tag = auto;
        var row = new DockPanel { Width = 128 };
        var arrow = new TextBlock { Text = "▾" };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock { Text = auto ? "Open automatically" : "Nothing" });
        b.Content = row;
    }
}
