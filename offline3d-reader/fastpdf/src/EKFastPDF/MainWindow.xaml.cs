using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Services;
using EKFastPDF.Viewer;
using Microsoft.Win32;

namespace EKFastPDF;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _statusTimer;
    private DocumentSession? _active;
    private bool _syncingSearch;

    public ObservableCollection<DocumentSession> Sessions { get; } = new();
    public DocumentSession? ActiveSession => _active;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        TabStrip.ItemsSource = Sessions;
        Viewport.Attach(services);
        Closed += (_,_) => Close3D();
        Viewport.ShowDebug = services.Settings.DebugOverlay;
        Viewport.ViewChanged += SyncToolbar;
        RestorePlacement();
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => UpdateRenderStatus(), Dispatcher);
        _statusTimer.Start();
        WelcomeInfo.Text = $"{services.Pool.WorkerCount} render workers  ·  {services.Hardware}  ·  " +
                           $"{(RenderCapability.Tier >> 16 == 2 ? "GPU composition" : "software composition")}" +
                           (services.Gpu.Primary is { } g ? $"  ·  {g.Name}" : "");
        RefreshState();
    }

    // ------------------------------------------------------------------ documents

    public void OpenFromShell(IReadOnlyList<string> files)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        OpenFiles(files);
    }

    public void OpenFiles(IEnumerable<string> paths)
    {
        DocumentSession? last = null;
        foreach (var raw in paths)
        {
            string path;
            try { path = Path.GetFullPath(raw); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!File.Exists(path)) { StatusText.Text = "File not found: " + path; continue; }
            var existing = Sessions.FirstOrDefault(s => string.Equals(s.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { last = existing; continue; }
            var session = new DocumentSession(_services, path);
            session.PropertyChanged += Session_PropertyChanged;
            session.Search.PropertyChanged += Search_PropertyChanged;
            Sessions.Add(session);
            last = session;
            _ = session.OpenAsync();
        }
        if (last != null) ActivateSession(last);
        RefreshState();
    }

    public void ActivateSession(DocumentSession? session)
    {
        if (!ReferenceEquals(_active,session)) Close3D();
        _active = session;
        Viewport.SetSession(session);
        _syncingSearch = true;
        SearchBox.Text = session?.Search.Query ?? "";
        _syncingSearch = false;
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSearchCounter();
        RefreshState();
        SyncToolbar();
        if (session != null) Title = session.Title + " – Offline 3D PDF Viewer v0.5.3";
        else Title = "Offline 3D PDF Viewer v0.5.3";
    }

    public void CloseSession(DocumentSession session)
    {
        int index = Sessions.IndexOf(session);
        if (index < 0) return;
        session.PropertyChanged -= Session_PropertyChanged;
        session.Search.PropertyChanged -= Search_PropertyChanged;
        Sessions.RemoveAt(index);
        if (ReferenceEquals(_active, session))
            ActivateSession(Sessions.Count == 0 ? null : Sessions[Math.Min(index, Sessions.Count - 1)]);
        session.Dispose();
        RefreshState();
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _active)) return;
        if (e.PropertyName is nameof(DocumentSession.State) or nameof(DocumentSession.Status))
        {
            RefreshState();
            Viewport.Invalidate();
        }
    }

    private void RefreshState()
    {
        Welcome.Visibility = Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var s = _active;
        bool ready = s?.IsReady == true;
        PrintButton.IsEnabled = ready;
        Box3DButton.IsEnabled = ready;
        if (s is null || ready)
        {
            StatePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = s is null ? "Drop PDF files here or press Ctrl+O" : $"{s.Title}  ·  {s.Status}";
            return;
        }
        StatePanel.Visibility = Visibility.Visible;
        StateProgress.Visibility = s.State == SessionState.Opening ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = s.State == SessionState.NeedsPassword ? Visibility.Visible : Visibility.Collapsed;
        StateTitle.Text = s.State switch
        {
            SessionState.Opening => "Opening " + s.Title,
            SessionState.NeedsPassword => s.Status,
            _ => "This PDF could not be opened",
        };
        StateDetail.Text = s.State switch
        {
            SessionState.Opening => "Pages appear as soon as they are needed.",
            SessionState.NeedsPassword => "Enter the document password to view it.",
            _ => s.Status,
        };
        StatusText.Text = s.Title + "  ·  " + s.Status;
        if (s.State == SessionState.NeedsPassword) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PasswordInput.Focus());
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Unlock(); e.Handled = true; }
    }

    private void Unlock()
    {
        if (_active is not { State: SessionState.NeedsPassword } s) return;
        string password = PasswordInput.Password;
        PasswordInput.Clear();
        _ = s.OpenAsync(password);
    }

    // ------------------------------------------------------------------ tabs

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentSession s } && e.OriginalSource is not Button) ActivateSession(s);
    }

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { Tag: DocumentSession s })
        {
            CloseSession(s);
            e.Handled = true;
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentSession s }) CloseSession(s);
        e.Handled = true;
    }

    private void SwitchTab(int delta)
    {
        if (Sessions.Count < 2 || _active is null) return;
        int i = (Sessions.IndexOf(_active) + delta + Sessions.Count) % Sessions.Count;
        ActivateSession(Sessions[i]);
    }

    // ------------------------------------------------------------------ toolbar

    private void SyncToolbar()
    {
        var s = _active;
        if (s is not { IsReady: true })
        {
            if (!PageBox.IsKeyboardFocused) PageBox.Text = "";
            PageCountText.Text = "/ –";
            if (!ZoomBox.IsKeyboardFocused) ZoomBox.Text = "";
            return;
        }
        if (!PageBox.IsKeyboardFocused) PageBox.Text = (s.CurrentPage + 1).ToString(CultureInfo.CurrentCulture);
        PageCountText.Text = "/ " + s.PageCount.ToString("N0", CultureInfo.CurrentCulture);
        if (!ZoomBox.IsKeyboardFocused) ZoomBox.Text = (s.View.Zoom * 100).ToString(s.View.Zoom < 0.1 ? "0.#" : "0", CultureInfo.CurrentCulture) + "%";
        PrevPageButton.IsEnabled = s.CurrentPage > 0;
        NextPageButton.IsEnabled = s.CurrentPage < s.PageCount - 1;
        SharpDot.Fill = Viewport.IsSharp ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0xE0, 0xA4, 0x2E));
    }

    private void UpdateRenderStatus()
    {
        var pool = _services.Pool;
        var tiles = _services.Tiles;
        if (_active is not { IsReady: true })
        {
            RenderText.Text = $"{pool.WorkerCount} workers";
            return;
        }
        string state = Viewport.IsSharp ? "Sharp" : pool.RunningCount > 0 ? "Rendering" : "Waiting";
        RenderText.Text = $"{state}  ·  {pool.RunningCount}/{pool.WorkerCount} workers  ·  cache {tiles.Bytes / (1024 * 1024)} MB";
        SyncToolbar();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Open PDF documents",
        };
        if (dialog.ShowDialog(this) == true) OpenFiles(dialog.FileNames);
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { IsReady: true } s) return;
        await PrintService.PrintAsync(this, s, _services, text => StatusText.Text = text);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => Viewport.PreviousPage();
    private void NextPage_Click(object sender, RoutedEventArgs e) => Viewport.NextPage();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Viewport.ZoomStep(+2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Viewport.ZoomStep(-2);
    private void FitPage_Click(object sender, RoutedEventArgs e) => Viewport.FitPage();
    private void FitWidth_Click(object sender, RoutedEventArgs e) => Viewport.FitWidth();

    private void SelectAllOnFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) Dispatcher.BeginInvoke(DispatcherPriority.Input, box.SelectAll);
    }

    private void PageBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Viewport.Focus(); SyncToolbar(); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int page)) Viewport.GoToPage(page - 1);
        Viewport.Focus();
        SyncToolbar();
        e.Handled = true;
    }

    private void ZoomBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Viewport.Focus(); SyncToolbar(); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        string text = ZoomBox.Text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double percent) && percent > 0)
            Viewport.ZoomTo(percent / 100);
        Viewport.Focus();
        SyncToolbar();
        e.Handled = true;
    }

    private void ZoomMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ZoomBox, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(MenuItem("Fit page", Viewport.FitPage, "Ctrl+0"));
        menu.Items.Add(MenuItem("Fit width", Viewport.FitWidth, "Ctrl+2"));
        menu.Items.Add(new Separator());
        foreach (int p in new[] { 25, 50, 75, 100, 150, 200, 400, 800, 1600, 3200 })
            menu.Items.Add(MenuItem(p + "%", () => Viewport.ZoomTo(p / 100.0), p == 100 ? "Ctrl+1" : null));
        menu.IsOpen = true;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var settings = _services.Settings;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header="Mouse wheel zooms · Ctrl+wheel scrolls", IsEnabled=false });
        menu.Items.Add(Toggle("Sub-pixel (LCD) text smoothing", settings.LcdText, v =>
        {
            settings.LcdText = v;
            settings.Save();
            foreach (var s in Sessions) _services.Tiles.ForgetDocument(s.DocId);
            Viewport.Invalidate();
        }));
        menu.Items.Add(Toggle("Show render statistics (F12)", Viewport.ShowDebug, v => { Viewport.ShowDebug = v; settings.DebugOverlay = v; settings.Save(); Viewport.Invalidate(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Diagnostics report…", ShowDiagnostics, "Ctrl+Shift+D"));
        menu.Items.Add(MenuItem("Open log folder", () => OpenFolder(Program.LogDirectory)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("About EK FastPDF", () => MessageBox.Show(this,
            $"EK FastPDF {typeof(MainWindow).Assembly.GetName().Version}\n\nRendering: PDFium in {_services.Pool.WorkerCount} parallel worker processes, GPU-composited viewport.\n" +
            "PDFium © The PDFium Authors (BSD-3-Clause / Apache-2.0).", "About EK FastPDF", MessageBoxButton.OK, MessageBoxImage.Information)));
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Toggle(string header, bool value, Action<bool> changed)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = value };
        item.Click += (_, _) => changed(item.IsChecked);
        return item;
    }

    private void ShowDiagnostics()
    {
        var text = DiagnosticsReport.Build(_services);
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0), Padding = new Thickness(10),
        };
        var copy = new Button { Content = "Copy to clipboard", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right };
        copy.Click += (_, _) => Clipboard.SetText(text);
        var panel = new DockPanel();
        DockPanel.SetDock(copy, Dock.Bottom);
        panel.Children.Add(copy);
        panel.Children.Add(box);
        new Window { Title = "EK FastPDF diagnostics", Owner = this, Width = 900, Height = 680, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { Log.Warn("Open folder failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ search

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_syncingSearch || _active is not { IsReady: true } s) return;
        _searchDebounce?.Stop();
        _searchDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _searchDebounce!.Stop();
            if (_active is { IsReady: true } current && current.Search.Query != SearchBox.Text.Trim()) current.Search.Start(SearchBox.Text);
        }, Dispatcher);
        _searchDebounce.Start();
    }

    private DispatcherTimer? _searchDebounce;

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_active is not { IsReady: true } s) return;
        if (e.Key == Key.Enter)
        {
            if (s.Search.Query != SearchBox.Text.Trim()) s.Search.Start(SearchBox.Text);
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) s.Search.Previous();
            else s.Search.Next();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            s.Search.Start("");
            Viewport.Focus();
            e.Handled = true;
        }
    }

    private void Search_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _active?.Search)) UpdateSearchCounter();
    }

    private void UpdateSearchCounter() => SearchCounter.Text = _active?.Search.Summary ?? "";

    private void NextResult_Click(object sender, RoutedEventArgs e) => _active?.Search.Next();
    private void PrevResult_Click(object sender, RoutedEventArgs e) => _active?.Search.Previous();

    // ------------------------------------------------------------------ keyboard, drag & drop

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        bool typing = e.OriginalSource is TextBox or PasswordBox;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool handled = true;
        switch (key)
        {
            case Key.O when ctrl: Open_Click(this, e); break;
            case Key.P when ctrl: Print_Click(this, e); break;
            case Key.F when ctrl: SearchBox.Focus(); SearchBox.SelectAll(); break;
            case Key.W when ctrl: if (_active != null) CloseSession(_active); break;
            case Key.F4 when ctrl: if (_active != null) CloseSession(_active); break;
            case Key.Tab when ctrl: SwitchTab(shift ? -1 : 1); break;
            case Key.D when ctrl && shift: ShowDiagnostics(); break;
            case Key.F3: if (shift) _active?.Search.Previous(); else _active?.Search.Next(); break;
            case Key.F12: Viewport.ShowDebug = !Viewport.ShowDebug; Viewport.Invalidate(); break;
            case Key.OemPlus or Key.Add when ctrl: Viewport.ZoomStep(+2); break;
            case Key.OemMinus or Key.Subtract when ctrl: Viewport.ZoomStep(-2); break;
            case Key.D0 or Key.NumPad0 when ctrl: Viewport.FitPage(); break;
            case Key.D1 or Key.NumPad1 when ctrl: Viewport.ZoomTo(1); break;
            case Key.D2 or Key.NumPad2 when ctrl: Viewport.FitWidth(); break;
            case Key.PageDown when !typing: Viewport.NextPage(); break;
            case Key.PageUp when !typing: Viewport.PreviousPage(); break;
            case Key.Home when !typing && ctrl: Viewport.GoToPage(0); break;
            case Key.End when !typing && ctrl: Viewport.GoToPage(int.MaxValue); break;
            case Key.Down when !typing: Viewport.ScrollBy(0, -60); break;
            case Key.Up when !typing: Viewport.ScrollBy(0, 60); break;
            case Key.Left when !typing: Viewport.ScrollBy(60, 0); break;
            case Key.Right when !typing: Viewport.ScrollBy(-60, 0); break;
            case Key.Space when !typing: Viewport.ScrollBy(0, shift ? ActualHeight * 0.8 : -ActualHeight * 0.8); break;
            default: handled = false; break;
        }
        if (handled) e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenFiles(files);
    }

    // ------------------------------------------------------------------ window chrome & placement

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // A maximized borderless window extends past the screen by the resize frame; pad it back.
        if (WindowState == WindowState.Maximized)
        {
            double inset = 8;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                uint d = (uint)Math.Round(96 * dpi.DpiScaleX);
                int px = GetSystemMetricsForDpi(32 /*SM_CXSIZEFRAME*/, d) + GetSystemMetricsForDpi(92 /*SM_CXPADDEDBORDER*/, d);
                if (px > 0) inset = px / dpi.DpiScaleX;
            }
            catch (EntryPointNotFoundException) { }
            RootGrid.Margin = new Thickness(inset);
            MaximizeIcon.Data = (Geometry)FindResource("IconRestore");
        }
        else
        {
            RootGrid.Margin = new Thickness(0);
            MaximizeIcon.Data = (Geometry)FindResource("IconMaximize");
        }
    }

    private void RestorePlacement()
    {
        var s = _services.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) &&
            s.WindowLeft >= SystemParameters.VirtualScreenLeft - 50 && s.WindowTop >= SystemParameters.VirtualScreenTop - 50 &&
            s.WindowLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            s.WindowTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        var s = _services.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            s.WindowLeft = bounds.Left; s.WindowTop = bounds.Top; s.WindowWidth = bounds.Width; s.WindowHeight = bounds.Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        if (!Program.Arguments.Contains("--benchmark")) s.Save();
        _statusTimer.Stop();
        foreach (var session in Sessions.ToList()) session.Dispose();
        Sessions.Clear();
    }
}
