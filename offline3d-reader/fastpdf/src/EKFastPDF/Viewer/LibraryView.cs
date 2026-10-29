using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using EKFastPDF.Core.Library;
using EKFastPDF.Services;
using Path = System.IO.Path;
using Glyph = System.Windows.Shapes.Path;

namespace EKFastPDF.Viewer;

/// <summary>
/// My PDFs (docs/mockups/library.png): left column with the Size slider, Recent, the user's
/// folders (drag to reorder; ··· to auto-open or remove), Browse…, Open newest PDF and, at the
/// bottom, Delete all in folder…; on the right the PDFs grouped by when they arrived. A PDF with
/// several pages has "N pages ▾": its pages appear straight down under page 1, the same size.
/// A PDF can be dragged out (into Revit, Explorer, mail) like from a Windows folder.
/// </summary>
public sealed class LibraryView : Grid
{
    private const double MinCard = 110, SideWidth = 168, ListPadding = 10, Gap = 8;
    private const int MaxCards = 300;
    private static readonly string FolderFormat = "EKPDF.FolderIndex";
    private readonly AppServices _services;
    private readonly PageImages _images;
    private readonly Slider _size = new() { Minimum = 0, Maximum = 1, SmallChange = 0.02, LargeChange = 0.1, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
    private readonly StackPanel _side = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly StackPanel _list = new() { Margin = new Thickness(ListPadding, 8, ListPadding, 12) };
    private readonly Button _newest, _deleteAll;
    private readonly DispatcherTimer _realize;
    private readonly Dictionary<string, List<LibraryFile>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Card> _cards = new();
    private LibraryFolder? _selected; // null = Recent
    private bool _showAll;
    private double _cardWidth = 150;
    private const double MaxBeyondFit = 6;
    private double _beyondFit = 1;
    private bool _settingSlider;
    private int _scanVersion;

    public LibraryView(AppServices services, PageImages images)
    {
        _services = services;
        _images = images;
        _realize = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => { _realize!.Stop(); RealizeVisible(); }, Dispatcher);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- left column
        var side = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        side.SetResourceReference(Border.BackgroundProperty, "Chrome");
        side.SetResourceReference(Border.BorderBrushProperty, "Line");
        var sideDock = new DockPanel();
        side.Child = sideDock;

        var sizeRow = new Border { Height = 30, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 4) };
        sizeRow.SetResourceReference(Border.BorderBrushProperty, "Line");
        var sizeGrid = new DockPanel();
        var sizeLabel = Soft("Size", 11.5); sizeLabel.Margin = new Thickness(0, 0, 8, 0); sizeLabel.VerticalAlignment = VerticalAlignment.Center;
        sizeGrid.Children.Add(sizeLabel);
        _size.SetResourceReference(StyleProperty, "ThinSlider");
        _size.ToolTip = "Picture size (right end: fit width)";
        sizeGrid.Children.Add(_size);
        sizeRow.Child = sizeGrid;
        DockPanel.SetDock(sizeRow, Dock.Top);
        sideDock.Children.Add(sizeRow);

        _deleteAll = SideButton("Delete all in folder…", DeleteAll_Click);
        _deleteAll.Margin = new Thickness(12, 6, 12, 10);
        _deleteAll.FontSize = 11.5;
        _deleteAll.ToolTip = "Move every PDF in this folder to the Recycle Bin (asks first)";
        DockPanel.SetDock(_deleteAll, Dock.Bottom);
        sideDock.Children.Add(_deleteAll);

        var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = _side };
        sideDock.Children.Add(sideScroll);
        Children.Add(side);

        _newest = SideButton("Open newest PDF", () => OpenNewest?.Invoke());
        _newest.ToolTip = "Open the newest PDF, or all PDFs that arrived together with it";

        // ---- PDFs
        _scroll.Content = _list;
        _scroll.SetResourceReference(BackgroundProperty, "DocBackground");
        SetColumn(_scroll, 1);
        Children.Add(_scroll);

        _size.Value = Math.Clamp(services.Settings.LibrarySize, 0, 1);
        _beyondFit = Math.Clamp(services.Settings.LibraryBeyondFit, 1, MaxBeyondFit);
        // The slider goes up to fit width; Ctrl+wheel can go further.
        _size.ValueChanged += (_, _) => { if (!_settingSlider) _beyondFit = 1; _services.Settings.LibrarySize = _size.Value; _services.Settings.LibraryBeyondFit = _beyondFit; ApplySize(); };
        _scroll.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(_scroll)); e.Handled = true; } };
        _size.PreviewMouseUp += (_, _) => _services.Settings.Save();
        _scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0) ApplySize(); _realize.Stop(); _realize.Start(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { _realize.Stop(); _realize.Start(); } };
        BuildSide();
    }

    /// <summary>Open these PDFs (page: for a single file, the page to show first).</summary>
    public event Action<IReadOnlyList<string>, int>? Open;
    public event Action? Browse;
    public event Action? OpenNewest;
    /// <summary>The folder list changed (added, removed, reordered, auto-open changed).</summary>
    public event Action? FoldersChanged;
    /// <summary>These files are about to be deleted: close them first.</summary>
    public event Action<IReadOnlyCollection<string>>? Deleting;

    public List<LibraryFolder> Folders => _services.Settings.Folders ??= new List<LibraryFolder> { new() { Path = PdfLibrary.DownloadsFolder } };

    /// <summary>Every PDF in every folder, newest first (as last scanned).</summary>
    public IEnumerable<LibraryFile> AllFiles => _files.Values.SelectMany(f => f).OrderByDescending(f => f.Arrived);

    public void SetNewestVisible(bool visible) => _newest.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Rescans folders (all when null) in the background, then redraws.</summary>
    public async Task RescanAsync(IEnumerable<string>? folders = null)
    {
        int version = ++_scanVersion;
        var targets = (folders ?? Folders.Select(f => f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = await Task.Run(() => targets.Select(f => (f, PdfLibrary.Scan(f))).ToList());
        foreach (var (folder, files) in results) _files[folder] = files;
        foreach (var gone in _files.Keys.Where(k => !Folders.Any(f => PdfLibrary.SamePath(f.Path, k))).ToList()) _files.Remove(gone);
        if (version != _scanVersion && folders == null) return;
        BuildSide();
        BuildList();
    }

    // ------------------------------------------------------------------ left column

    private void BuildSide()
    {
        _side.Children.Clear();
        _side.Children.Add(Row("Recent", "IconClock", Math.Min(200, _files.Values.Sum(f => f.Count)), _selected is null, () => Select(null), null, -1));
        var header = Soft("Folders", 10.5); header.Margin = new Thickness(12, 6, 12, 2);
        _side.Children.Add(header);
        for (int i = 0; i < Folders.Count; i++)
        {
            var f = Folders[i];
            int count = _files.TryGetValue(f.Path, out var list) ? list.Count : 0;
            bool downloads = PdfLibrary.SamePath(f.Path, PdfLibrary.DownloadsFolder);
            _side.Children.Add(Row(f.Name, downloads ? "IconDownload" : "IconFolder", count, ReferenceEquals(_selected, f), () => Select(f), f, i));
        }
        var add = EkDialog.Link("Add folder…", AddFolder);
        add.Margin = new Thickness(12, 4, 12, 0); add.HorizontalAlignment = HorizontalAlignment.Left; add.FontSize = 11.5;
        ((TextBlock)add.Content).TextDecorations = null;
        _side.Children.Add(add);
        var sep = new Border { Height = 1, Margin = new Thickness(12, 10, 12, 10) };
        sep.SetResourceReference(Border.BackgroundProperty, "Line");
        _side.Children.Add(sep);
        var browse = SideButton("Browse…", () => Browse?.Invoke());
        browse.ToolTip = "Open PDF files (Ctrl+O)";
        _side.Children.Add(browse);
        _newest.Margin = new Thickness(12, 6, 12, 0);
        if (_newest.Parent is Panel p) p.Children.Remove(_newest);
        _side.Children.Add(_newest);
        SetNewestVisible(_services.Settings.ShowNewestButton);
        _deleteAll.IsEnabled = _selected != null && _files.TryGetValue(_selected.Path, out var sel) && sel.Count > 0;
    }

    private Border Row(string name, string icon, int count, bool selected, Action select, LibraryFolder? folder, int index)
    {
        var row = new Border { Height = 24, Padding = new Thickness(selected ? 9 : 12, 0, 6, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Accent");
        if (selected) row.SetResourceReference(Border.BackgroundProperty, "Selected"); else row.Background = Brushes.Transparent;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        var glyph = new Glyph { Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(Glyph.DataProperty, icon);
        glyph.SetResourceReference(Shape.StrokeProperty, "InkSoft");
        grid.Children.Add(glyph);
        var text = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = folder?.Path };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        SetColumn(text, 1); grid.Children.Add(text);
        var n = Soft(count.ToString(System.Globalization.CultureInfo.CurrentCulture), 10.5);
        n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(6, 0, 0, 0);
        SetColumn(n, 2); grid.Children.Add(n);
        if (folder != null)
        {
            var dots = new TextBlock { Text = "···", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Folder options", Visibility = selected ? Visibility.Visible : Visibility.Hidden };
            dots.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
            dots.MouseLeftButtonUp += (_, e) => { FolderMenu(folder, dots); e.Handled = true; };
            row.MouseEnter += (_, _) => dots.Visibility = Visibility.Visible;
            row.MouseLeave += (_, _) => { if (!selected) dots.Visibility = Visibility.Hidden; };
            SetColumn(dots, 3); grid.Children.Add(dots);
            row.MouseRightButtonUp += (_, e) => { FolderMenu(folder, row); e.Handled = true; };
            if (folder.AutoOpen) text.ToolTip = folder.Path + "\nNew PDFs here open automatically";
            EnableReorder(row, index);
        }
        row.Child = grid;
        if (!selected)
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Hover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }
        row.MouseLeftButtonUp += (_, e) => { if (!_dragStarted) select(); _dragStarted = false; };
        return row;
    }

    private void Select(LibraryFolder? folder)
    {
        _selected = folder;
        _showAll = false;
        BuildSide();
        BuildList();
        _scroll.ScrollToTop();
    }

    private void FolderMenu(LibraryFolder folder, FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        var auto = new MenuItem { Header = "Open new PDFs automatically", IsCheckable = true, IsChecked = folder.AutoOpen };
        auto.Click += (_, _) => { folder.AutoOpen = auto.IsChecked; _services.Settings.Save(); BuildSide(); FoldersChanged?.Invoke(); };
        menu.Items.Add(auto);
        var explorer = new MenuItem { Header = "Open in File Explorer" };
        explorer.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder.Path + "\"") { UseShellExecute = true }); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { } };
        menu.Items.Add(explorer);
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "Remove folder", ToolTip = "Removes it from this list only. The folder and its files stay on disk." };
        remove.Click += (_, _) =>
        {
            Folders.Remove(folder);
            _files.Remove(folder.Path);
            if (ReferenceEquals(_selected, folder)) _selected = null;
            _services.Settings.Save();
            BuildSide(); BuildList();
            FoldersChanged?.Invoke();
        };
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder to My PDFs", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string path = dialog.FolderName;
        var existing = Folders.FirstOrDefault(f => PdfLibrary.SamePath(f.Path, path));
        if (existing == null)
        {
            existing = new LibraryFolder { Path = path };
            Folders.Add(existing);
            _services.Settings.Save();
            FoldersChanged?.Invoke();
        }
        _selected = existing;
        _ = RescanAsync(new[] { path });
        BuildSide();
    }

    // ---- drag folders up and down

    private Point _press;
    private bool _dragStarted;

    private void EnableReorder(Border row, int index)
    {
        row.AllowDrop = true;
        row.PreviewMouseLeftButtonDown += (_, e) => { _press = e.GetPosition(this); _dragStarted = false; };
        row.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragStarted) return;
            var d = e.GetPosition(this) - _press;
            if (Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _dragStarted = true;
            DragDrop.DoDragDrop(row, new DataObject(FolderFormat, index), DragDropEffects.Move);
            ClearDropLine();
            // The button is already up when the drag ends; let the next click select again.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _dragStarted = false);
        };
        row.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(FolderFormat)) return;
            e.Effects = DragDropEffects.Move;
            bool above = e.GetPosition(row).Y < row.ActualHeight / 2;
            ShowDropLine(row, above);
            e.Handled = true;
        };
        row.DragLeave += (_, _) => ClearDropLine();
        row.Drop += (_, e) =>
        {
            ClearDropLine();
            if (e.Data.GetData(FolderFormat) is not int from) return;
            bool above = e.GetPosition(row).Y < row.ActualHeight / 2;
            int to = above ? index : index + 1;
            if (from < to) to--;
            PdfLibrary.Move(Folders, from, to);
            _services.Settings.Save();
            BuildSide();
            FoldersChanged?.Invoke();
            e.Handled = true;
        };
    }

    private Border? _dropRow;

    private void ShowDropLine(Border row, bool above)
    {
        ClearDropLine();
        _dropRow = row;
        row.Tag = row.BorderThickness;
        row.BorderThickness = new Thickness(row.BorderThickness.Left, above ? 2 : 0, 0, above ? 0 : 2);
    }

    private void ClearDropLine()
    {
        if (_dropRow?.Tag is Thickness t) _dropRow.BorderThickness = t;
        _dropRow = null;
    }

    // ------------------------------------------------------------------ PDFs

    private IReadOnlyList<LibraryFile> Current =>
        _selected is null ? AllFiles.Take(200).ToList()
        : _files.TryGetValue(_selected.Path, out var list) ? list : Array.Empty<LibraryFile>();

    private void BuildList()
    {
        _list.Children.Clear();
        _cards.Clear();
        var files = Current;
        if (files.Count == 0)
        {
            var empty = Soft(_selected is null ? "No PDFs yet. Add a folder on the left, or use Browse…" : "No PDFs in " + _selected.Name + ".", 12);
            empty.Margin = new Thickness(2, 6, 0, 0);
            _list.Children.Add(empty);
            return;
        }
        var shown = _showAll ? files : files.Take(MaxCards).ToList();
        var now = DateTime.Now;
        foreach (var batch in PdfLibrary.Batches(shown))
        {
            var header = new DockPanel { Height = 22, Margin = new Thickness(0, _list.Children.Count == 0 ? 0 : 6, 0, 4), LastChildFill = false };
            var label = new TextBlock { Text = PdfLibrary.BatchLabel(batch.Newest, now), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            header.Children.Add(label);
            var count = Soft(batch.Files.Count == 1 ? "1 PDF" : batch.Files.Count + " PDFs", 11.5);
            count.Margin = new Thickness(6, 0, 0, 0); count.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(count);
            if (now - batch.Newest < TimeSpan.FromHours(1))
            {
                var badge = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                var t = new TextBlock { Text = "New", FontSize = 10 };
                t.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                badge.Child = t;
                header.Children.Add(badge);
            }
            if (batch.Files.Count > 1)
            {
                var paths = batch.Files.Select(f => f.Path).Reverse().ToList();
                var all = EkDialog.Link("Open all " + batch.Files.Count, () => Open?.Invoke(paths, 0));
                all.FontSize = 11.5;
                DockPanel.SetDock(all, Dock.Right);
                header.Children.Add(all);
            }
            _list.Children.Add(header);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, -Gap, 4) };
            foreach (var file in batch.Files)
            {
                var card = new Card(this, file);
                _cards.Add(card);
                wrap.Children.Add(card);
            }
            _list.Children.Add(wrap);
        }
        if (!_showAll && files.Count > MaxCards)
        {
            var more = EkDialog.Link($"Show all {files.Count} PDFs", () => { _showAll = true; BuildList(); });
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(0, 6, 0, 0);
            _list.Children.Add(more);
        }
        ApplySize();
        _realize.Stop(); _realize.Start();
    }

    private double FitWidth()
    {
        double fit = Math.Max(MinCard, _scroll.ViewportWidth - 2 * ListPadding - 1);
        if (double.IsNaN(fit) || _scroll.ViewportWidth <= 0) fit = Math.Max(MinCard, ActualWidth - SideWidth - 2 * ListPadding - 20);
        return fit;
    }

    /// <summary>Slider 0..1 → card width from 110 px to fit width (logarithmic, so the small end has
    /// room); past fit width only with Ctrl+wheel, scrolling sideways.</summary>
    private void ApplySize()
    {
        double fit = FitWidth();
        double v = _size.Value;
        double width = v >= 0.999 ? fit * _beyondFit : MinCard * Math.Pow(fit / MinCard, v);
        width = Math.Floor(Math.Clamp(width, MinCard, fit * MaxBeyondFit));
        bool beyond = width > fit + 0.5;
        _scroll.HorizontalScrollBarVisibility = beyond ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        // Past fit width the list is as wide as one card, so cards still go one per row.
        _list.Width = beyond ? width + 2 * ListPadding + Gap : double.NaN;
        if (Math.Abs(width - _cardWidth) < 0.5 && _cards.All(c => Math.Abs(c.Width - width) < 0.5)) return;
        _cardWidth = width;
        foreach (var c in _cards) c.SetWidth(width);
        _realize.Stop(); _realize.Start();
    }

    /// <summary>Ctrl+wheel: bigger or smaller pictures, keeping the spot under the pointer in place.</summary>
    private void ZoomBy(double factor, Point pointer)
    {
        double fit = FitWidth(), old = _cardWidth;
        double next = Math.Clamp(old * factor, MinCard, fit * MaxBeyondFit);
        if (Math.Abs(next - old) < 0.5) return;
        _settingSlider = true;
        if (next >= fit) { _beyondFit = next / fit; _size.Value = 1; }
        else { _beyondFit = 1; _size.Value = Math.Clamp(Math.Log(next / MinCard) / Math.Log(fit / MinCard), 0, 1); }
        _settingSlider = false;
        _services.Settings.LibrarySize = _size.Value; _services.Settings.LibraryBeyondFit = _beyondFit;
        ApplySize();
        double ratio = _cardWidth / old;
        double x = (_scroll.HorizontalOffset + pointer.X) * ratio - pointer.X, y = (_scroll.VerticalOffset + pointer.Y) * ratio - pointer.Y;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _scroll.ScrollToHorizontalOffset(Math.Max(0, x)); _scroll.ScrollToVerticalOffset(Math.Max(0, y)); });
        _saveSize ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSize!.Stop(); _services.Settings.Save(); }, Dispatcher);
        _saveSize.Stop(); _saveSize.Start();
    }

    private DispatcherTimer? _saveSize;

    private void RealizeVisible()
    {
        if (!IsVisible || _scroll.ViewportHeight <= 0) return;
        var view = new Rect(0, -400, _scroll.ViewportWidth, _scroll.ViewportHeight + 800);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var card in _cards)
        {
            if (!card.IsLoaded) continue;
            Rect r;
            try { r = card.TransformToAncestor(_scroll).TransformBounds(new Rect(card.RenderSize)); }
            catch (InvalidOperationException) { continue; }
            if (r.IntersectsWith(view)) card.Realize(view, dpi);
        }
    }

    private async void DeleteAll_Click()
    {
        if (_selected is not { } folder || !_files.TryGetValue(folder.Path, out var files) || files.Count == 0) return;
        var owner = Window.GetWindow(this)!;
        string what = files.Count == 1 ? "**1 PDF**" : $"**{files.Count} PDFs**";
        if (!EkDialog.Confirm(owner, "Delete PDFs", $"Move all {what} in **{folder.Name}** to the Recycle Bin?",
            "Other files and subfolders are not touched. You can restore the PDFs from the Recycle Bin.", "Move to Recycle Bin")) return;
        var paths = files.Select(f => f.Path).ToList();
        Deleting?.Invoke(paths);
        _images.ReleaseFiles();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        // Tabs and pictures close their files at once, but the render workers let go a moment
        // later: try quietly a few times before Windows shows its "file in use" message.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            bool last = attempt == 5;
            if (RecycleBin.Send(hwnd, paths.Where(File.Exists).ToList(), showErrors: last)) break;
            if (last) Core.Diagnostics.Log.Warn("Some PDFs could not be moved to the Recycle Bin");
            else await Task.Delay(400);
        }
        _ = RescanAsync(new[] { folder.Path });
    }

    // ------------------------------------------------------------------ helpers

    private static TextBlock Soft(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size };
        t.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft");
        return t;
    }

    private static Button SideButton(string text, Action click)
    {
        var b = new Button { Content = text, Margin = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.SetResourceReference(StyleProperty, "SoftButton");
        b.Click += (_, _) => click();
        return b;
    }

    private static readonly BitmapImage Badge3D = LoadBadge();
    private static Brush Paper = Brushes.White;

    /// <summary>The theme or page look changed: pictures are made again in the new look.</summary>
    public void ApplyTheme()
    {
        var paper = new SolidColorBrush(PageTint.Paper);
        paper.Freeze();
        Paper = paper;
        _images.ClearMemory();
        BuildList();
    }

    private static BitmapImage LoadBadge()
    {
        var b = new BitmapImage(new Uri("pack://application:,,,/Assets/ClickTo3D-32.png"));
        b.Freeze();
        return b;
    }

    /// <summary>One PDF: page 1 picture, name, size and "N pages", and when expanded the other
    /// pages under it, each as wide as page 1.</summary>
    private sealed class Card : StackPanel
    {
        private readonly LibraryView _owner;
        private readonly LibraryFile _file;
        private readonly List<PageBox> _pages = new();
        private readonly TextBlock _name, _meta;
        private readonly TextBlock _pagesLink;
        private PdfFacts? _facts;
        private bool _factsRequested;
        private Point _press;
        private bool _pressed, _dragged;

        public Card(LibraryView owner, LibraryFile file)
        {
            _owner = owner;
            _file = file;
            Margin = new Thickness(0, 0, Gap, Gap);
            Cursor = Cursors.Hand;
            ToolTip = file.Path;
            _facts = owner._images.Known(file.Path);
            _pages.Add(new PageBox(this, 0));
            Children.Add(_pages[0]);
            _name = new TextBlock { Text = file.Name, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            _name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            Children.Add(_name);
            var meta = new StackPanel { Orientation = Orientation.Horizontal };
            _meta = Soft(PdfLibrary.FormatSize(file.Size), 10.5);
            meta.Children.Add(_meta);
            _pagesLink = new TextBlock { FontSize = 10.5, Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
            _pagesLink.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            _pagesLink.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
            meta.Children.Add(_pagesLink);
            Children.Add(meta);
            ApplyFacts();
            if (_owner._expanded.Contains(file.Path)) Expand(true);

            PreviewMouseLeftButtonDown += (_, e) => { _press = e.GetPosition(this); _pressed = true; _dragged = false; };
            PreviewMouseMove += (_, e) =>
            {
                if (!_pressed || e.LeftButton != MouseButtonState.Pressed || _dragged) return;
                var d = e.GetPosition(this) - _press;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _dragged = true;
                // Like dragging from a Windows folder: Revit, Explorer, mail and chat accept it.
                var data = new DataObject(DataFormats.FileDrop, new[] { _file.Path });
                data.SetData("EKPDF.Library", true);
                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Link);
                _pressed = false;
            };
            MouseLeftButtonUp += (_, e) =>
            {
                if (_pressed && !_dragged)
                {
                    int page = e.OriginalSource is DependencyObject d ? PageAt(d) : 0;
                    _owner.Open?.Invoke(new[] { _file.Path }, page);
                }
                _pressed = false;
            };
            MouseRightButtonUp += (_, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = this };
                var open = new MenuItem { Header = "Open" }; open.Click += (_, _) => _owner.Open?.Invoke(new[] { _file.Path }, 0); menu.Items.Add(open);
                if (_facts?.Pages > 1)
                {
                    var toggle = new MenuItem { Header = _owner._expanded.Contains(_file.Path) ? "Hide pages" : "Show all " + _facts.Pages + " pages" };
                    toggle.Click += (_, _) => Toggle(); menu.Items.Add(toggle);
                }
                var loc = new MenuItem { Header = "Open file location" };
                loc.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + _file.Path + "\"") { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } };
                menu.Items.Add(loc);
                menu.IsOpen = true; e.Handled = true;
            };
        }

        public PdfFacts? Facts => _facts;
        public string FilePath => _file.Path;
        public LibraryView Owner => _owner;

        private int PageAt(DependencyObject d)
        {
            for (var x = d; x != null && !ReferenceEquals(x, this); x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
                if (x is PageBox p) return p.Page;
            return 0;
        }

        public void SetWidth(double width)
        {
            Width = width;
            foreach (var p in _pages) p.SetWidth(width);
        }

        private void ApplyFacts()
        {
            if (_facts is { Pages: > 1 } f)
            {
                bool open = _owner._expanded.Contains(_file.Path);
                _pagesLink.Text = " · " + f.Pages + " pages " + (open ? "▴" : "▾");
                _pagesLink.Visibility = Visibility.Visible;
                _pagesLink.ToolTip = open ? "Hide the pages" : "Show every page under the first";
            }
            else _pagesLink.Visibility = Visibility.Collapsed;
            if (_facts?.Unreadable == true) _meta.Text = PdfLibrary.FormatSize(_file.Size) + " · cannot preview";
            foreach (var p in _pages) p.ApplyFacts();
        }

        private void Toggle() => Expand(!_owner._expanded.Contains(_file.Path));

        private void Expand(bool open)
        {
            if (open) _owner._expanded.Add(_file.Path); else _owner._expanded.Remove(_file.Path);
            foreach (var p in _pages.Skip(1)) Children.Remove(p);
            _pages.RemoveRange(1, _pages.Count - 1);
            if (open && _facts is { Pages: > 1 } f)
                for (int i = 1; i < f.Pages; i++)
                {
                    var box = new PageBox(this, i) { Margin = new Thickness(0, 6, 0, 0) };
                    box.SetWidth(Width is double w && !double.IsNaN(w) ? w : _owner._cardWidth);
                    _pages.Add(box);
                    Children.Add(box);
                }
            foreach (var p in _pages) p.ShowNumber(open);
            ApplyFacts();
            _owner._realize.Stop(); _owner._realize.Start();
        }

        public async void Realize(Rect view, double dpi)
        {
            try { await RealizeCore(view, dpi); }
            catch (Exception e) when (e is not OutOfMemoryException) { Core.Diagnostics.Log.Warn("My PDFs: " + e.Message); }
        }

        private async Task RealizeCore(Rect view, double dpi)
        {
            if (_facts is null && !_factsRequested)
            {
                _factsRequested = true;
                _facts = await _owner._images.FactsAsync(_file.Path);
                ApplyFacts();
                if (_owner._expanded.Contains(_file.Path)) Expand(true);
            }
            else if (_facts is { Has3D: null } && !_factsRequested)
            {
                _factsRequested = true;
                _facts = await _owner._images.FactsAsync(_file.Path);
                ApplyFacts();
            }
            foreach (var p in _pages)
            {
                Rect r;
                try { r = p.TransformToAncestor(_owner._scroll).TransformBounds(new Rect(p.RenderSize)); }
                catch (InvalidOperationException) { continue; }
                if (r.IntersectsWith(view)) p.Load(dpi);
            }
        }
    }

    /// <summary>A page picture on white paper, with the 3D mark (page 1) and the page number when expanded.</summary>
    private sealed class PageBox : Border
    {
        private readonly Card _card;
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly Image _badge = new() { Source = Badge3D, Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 3, 0), Visibility = Visibility.Collapsed, ToolTip = "Has clickable 3D (ClickTo3D)" };
        private readonly Border _number = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 2), Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)) };
        private int _loadedWidth;
        private bool _loading;

        public PageBox(Card card, int page)
        {
            _card = card;
            Page = page;
            BorderThickness = new Thickness(1);
            SetResourceReference(BorderBrushProperty, "PageEdge");
            Background = Paper;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            var grid = new Grid();
            grid.Children.Add(_image);
            grid.Children.Add(_badge);
            var n = new TextBlock { Text = (page + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)) };
            _number.Child = n;
            grid.Children.Add(_number);
            Child = grid;
        }

        public int Page { get; }

        public void SetWidth(double width)
        {
            Width = width;
            Height = Math.Max(24, Math.Round(width * (_card.Facts?.Aspect(Page) ?? 0.647)));
        }

        public void ApplyFacts()
        {
            if (!double.IsNaN(Width)) SetWidth(Width);
            _badge.Visibility = Page == 0 && _card.Facts?.Has3D == true ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ShowNumber(bool show) => _number.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        public async void Load(double dpi)
        {
            try { await LoadCore(dpi); }
            catch (Exception e) when (e is not OutOfMemoryException) { _loading = false; Core.Diagnostics.Log.Warn("My PDFs picture: " + e.Message); }
        }

        private async Task LoadCore(double dpi)
        {
            if (_card.Facts?.Unreadable == true || double.IsNaN(Width)) return;
            int want = PageImages.Bucket((int)Math.Ceiling(Width * dpi));
            if (_loadedWidth >= want || _loading) return;
            var images = _card.Owner._images;
            var quick = images.Cached(_card.FilePath, Page, want, out bool exact);
            if (quick != null && _image.Source is null) _image.Source = quick;
            if (exact) { _image.Source = quick; _loadedWidth = want; return; }
            _loading = true;
            try
            {
                var image = await images.GetAsync(_card.FilePath, Page, want);
                if (image != null) { _image.Source = image; _loadedWidth = want; }
                else _loadedWidth = int.MaxValue; // unreadable: do not retry
            }
            finally { _loading = false; }
        }
    }
}
