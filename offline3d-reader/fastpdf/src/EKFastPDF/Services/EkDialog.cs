using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace EKFastPDF.Services;

/// <summary>
/// Dialog frame from the style guide: a plain title bar with the word "Close", the body, and a
/// footer with an optional left link and the buttons on the right. Colours come from the
/// theme resources, so dialogs follow the chosen theme.
/// </summary>
public class EkDialog : Window
{
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ContentControl _left = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _body = new() { Focusable = false };

    public EkDialog(Window? owner, string title, double width)
    {
        Owner = owner;
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 12;
        SetResourceReference(ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 30, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

        var frame = new Border { BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Frame");
        frame.SetResourceReference(Border.BackgroundProperty, "Chrome");
        var root = new DockPanel();
        frame.Child = root;

        // Title bar
        var bar = new DockPanel { Height = 30 };
        bar.SetResourceReference(Panel.BackgroundProperty, "TitleBar");
        var close = new Button { Content = new TextBlock { Text = "Close", FontSize = 11.5 }, Padding = new Thickness(12, 0, 12, 0), Height = 30, IsCancel = true };
        close.SetResourceReference(StyleProperty, "ChromeButton");
        close.Width = double.NaN;
        ((TextBlock)close.Content).SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => { DialogResult = false; };
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // Footer
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 12, 18, 12) };
        footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        var footerGrid = new DockPanel();
        DockPanel.SetDock(_buttons, Dock.Right);
        footerGrid.Children.Add(_buttons);
        footerGrid.Children.Add(_left);
        footer.Child = footerGrid;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(_body);
        Content = frame;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    public object? Body { get => _body.Content; set => _body.Content = value; }
    public object? FooterLeft { get => _left.Content; set => _left.Content = value; }

    /// <summary>Adds a footer button. The accent one is the main action (Enter).</summary>
    public Button AddButton(string text, bool accent, Action? click = null)
    {
        var b = new Button { Content = text, Margin = new Thickness(_buttons.Children.Count == 0 ? 0 : 16, 0, 0, 0), MinWidth = 64, IsDefault = accent };
        b.SetResourceReference(StyleProperty, "SoftButton");
        if (accent) b.Tag = "Accent";
        b.Click += (_, _) => { if (click != null) click(); else DialogResult = accent; };
        _buttons.Children.Add(b);
        return b;
    }

    /// <summary>A link-style text button (Reset, Add folder…, Remove).</summary>
    public static Button Link(string text, Action click)
    {
        var tb = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        var b = new Button { Content = tb, Cursor = Cursors.Hand, Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        b.Template = LinkTemplate;
        b.Click += (_, _) => click();
        return b;
    }

    private static readonly ControlTemplate LinkTemplate = CreateLinkTemplate();

    private static ControlTemplate CreateLinkTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = cp;
        return t;
    }

    /// <summary>Simple question: text (bold parts between ** **) and Cancel / action.</summary>
    public static bool Confirm(Window owner, string title, string text, string detail, string action)
    {
        var d = new EkDialog(owner, title, 380);
        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        var main = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++) main.Inlines.Add(i % 2 == 1 ? new System.Windows.Documents.Bold(new System.Windows.Documents.Run(parts[i])) : new System.Windows.Documents.Run(parts[i]));
        panel.Children.Add(main);
        if (detail.Length > 0)
        {
            var sub = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
            panel.Children.Add(sub);
        }
        d.Body = panel;
        d.AddButton("Cancel", false);
        d.AddButton(action, true);
        return d.ShowDialog() == true;
    }
}

/// <summary>Moves files to the Recycle Bin (they can be restored from there).</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    /// <summary>Returns true when Windows reports success. Missing files are skipped.</summary>
    public static bool Send(IntPtr owner, IReadOnlyCollection<string> files, bool showErrors = true)
    {
        var existing = files.Where(File.Exists).ToList();
        if (existing.Count == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner, wFunc = FO_DELETE,
            pFrom = string.Join('\0', existing) + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | (showErrors ? 0 : FOF_NOERRORUI)),
        };
        return SHFileOperationW(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }
}
