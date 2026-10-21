using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace EKFastPDF;

public partial class MainWindow : Window
{
    public ObservableCollection<PdfTab> Tabs { get; } = new();
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshEmptyState();
        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs().Skip(1)
                .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                .ToArray();
            if (args.Length > 0) OpenFiles(args);
        };
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf", Multiselect = true, Title = "Open PDF documents" };
        if (picker.ShowDialog(this) == true) OpenFiles(picker.FileNames);
    }

    private void OpenFiles(IEnumerable<string> paths)
    {
        foreach (var file in paths)
        {
            if (!File.Exists(file) || !file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            string fullPath = Path.GetFullPath(file);
            var alreadyOpen = Tabs.FirstOrDefault(t => string.Equals(t.PathName, fullPath, StringComparison.OrdinalIgnoreCase));
            if (alreadyOpen is not null) { Documents.SelectedItem = alreadyOpen; continue; }
            var tab = new PdfTab(fullPath);
            Tabs.Add(tab);
            Documents.SelectedItem = tab;
            _ = LoadTabAsync(tab);
        }
        RefreshEmptyState();
    }

    private async Task LoadTabAsync(PdfTab tab)
    {
        await tab.OpenAsync();
        if (tab.IsClosed) return;
        if (tab.Document == null)
        {
            BottomStatus.Text = tab.Status;
            return;
        }
        if (Documents.SelectedItem == tab) BottomStatus.Text = tab.Status;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PdfTab tab }) CloseTab(tab);
        e.Handled = true;
    }

    private void CloseTab(PdfTab tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.RemoveAt(idx);
        tab.Dispose();
        if (Tabs.Count > 0 && Documents.SelectedItem == null) Documents.SelectedIndex = Math.Min(idx, Tabs.Count - 1);
        RefreshEmptyState();
    }

    private void RefreshEmptyState()
    {
        if (Welcome == null) return;
        Welcome.Visibility = Tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Tabs.Count == 0) BottomStatus.Text = "Ready · Drop PDF files anywhere";
    }

    private void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BottomStatus != null && Documents.SelectedItem is PdfTab tab) BottomStatus.Text = tab.Status;
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

    private PdfViewerControl? ActiveViewer => FindVisualChild<PdfViewerControl>(Documents);
    private static T? FindVisualChild<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.O) { Open_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (control && e.Key == Key.F) { ActiveViewer?.FocusSearch(); e.Handled = true; }
        else if (control && e.Key == Key.W && Documents.SelectedItem is PdfTab current) { CloseTab(current); e.Handled = true; }
        else if (control && e.Key == Key.Tab && Tabs.Count > 1)
        {
            int next = (Documents.SelectedIndex + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Tabs.Count - 1 : 1)) % Tabs.Count;
            Documents.SelectedIndex = next;
            e.Handled = true;
        }
        else if (e.OriginalSource is not TextBox && !control)
        {
            if (e.Key == Key.PageDown) { ActiveViewer?.NextPage(); e.Handled = true; }
            if (e.Key == Key.PageUp) { ActiveViewer?.PreviousPage(); e.Handled = true; }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var tab in Tabs.ToList()) tab.Dispose();
        Tabs.Clear();
    }
}
