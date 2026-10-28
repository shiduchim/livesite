using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EKFastPDF.Core.Library;
using EKFastPDF.Services;
using EKFastPDF.Viewer;

using EKFastPDF.Core.Rendering;

namespace EKFastPDF;

/// <summary>
/// My PDFs (the first tab) and the New PDFs options: Open newest PDF, opening recent PDFs when
/// EKPDF starts or comes forward, and folders whose new PDFs open automatically.
/// </summary>
public partial class MainWindow
{
    private PageImages? _images;
    private LibraryView? _library;
    private FolderWatcher? _watcher;
    private bool _libraryShown;
    /// <summary>Every PDF opened in this run (recent PDFs open only once).</summary>
    private readonly HashSet<string> _opened = new(StringComparer.OrdinalIgnoreCase);

    private void InitLibrary()
    {
        _images = new PageImages(_services.Pool);
        _images.TrimDiskCache();
        _library = new LibraryView(_services, _images);
        LibraryHost.Content = _library;
        _library.Open += (paths, page) => OpenFiles(paths, page);
        _library.Browse += () => Open_Click(this, new RoutedEventArgs());
        _library.OpenNewest += OpenNewest;
        _library.FoldersChanged += () => { _watcher?.Watch(_library.Folders.Select(f => f.Path)); _ = _library.RescanAsync(); };
        _library.Deleting += paths =>
        {
            foreach (var s in Sessions.Where(s => paths.Contains(s.FilePath, StringComparer.OrdinalIgnoreCase)).ToList()) CloseSession(s);
        };
        _watcher = new FolderWatcher();
        _watcher.Changed += folders => _ = _library.RescanAsync(folders);
        _watcher.Arrived += OnPdfArrived;
        _watcher.Watch(_library.Folders.Select(f => f.Path));
        Activated += (_, _) => { if (_started && _services.Settings.OpenRecentOnActivate) OpenRecent(); };
        ShowLibrary();
    }

    private bool _started;

    /// <summary>After the window is shown: scan the folders, then (when EKPDF was started
    /// without files) open PDFs that arrived in the last minute(s).</summary>
    public async Task StartLibraryAsync(bool startedWithFiles)
    {
        if (_library is null) return;
        await _library.RescanAsync();
        if (!startedWithFiles && _services.Settings.OpenRecentOnStart) OpenRecent();
        _started = true;
    }

    private TimeSpan RecentWindow => TimeSpan.FromMinutes(Math.Max(1, _services.Settings.RecentMinutes));

    private void OpenRecent()
    {
        if (_library is null) return;
        var recent = PdfLibrary.Recent(_library.AllFiles, DateTime.Now, RecentWindow, _opened);
        if (recent.Count > 0) OpenFiles(recent.Select(f => f.Path));
    }

    private void OpenNewest()
    {
        if (_library is null) return;
        var batch = PdfLibrary.NewestBatch(_library.AllFiles);
        if (batch.Count == 0) { ShowToast("No PDFs in your folders yet"); return; }
        // Oldest first, so the newest PDF ends up as the active tab.
        OpenFiles(batch.Reverse().Select(f => f.Path));
    }

    private void OnPdfArrived(string folder, string path)
    {
        if (_library is null || _opened.Contains(path)) return;
        var f = _library.Folders.FirstOrDefault(x => PdfLibrary.SamePath(x.Path, folder));
        if (f?.AutoOpen == true) OpenFromShell(new[] { path });
    }

    private void LibraryTab_Click(object sender, MouseButtonEventArgs e)
    {
        ShowLibrary();
        e.Handled = true;
    }

    private void ShowLibrary()
    {
        _libraryShown = true;
        if (_active != null) ActivateSession(null);
        else ApplyLibraryState();
    }

    /// <summary>Shows My PDFs or the document; the page tools only make sense for a document.</summary>
    private void ApplyLibraryState()
    {
        bool lib = _libraryShown || _active is null;
        _libraryShown = lib;
        LibraryHost.Visibility = lib ? Visibility.Visible : Visibility.Collapsed;
        NavTools.Visibility = lib ? Visibility.Collapsed : Visibility.Visible;
        SearchToggle.Visibility = !lib && _compact ? Visibility.Visible : Visibility.Collapsed;
        SearchField.Visibility = lib ? Visibility.Collapsed : Visibility.Visible;
        if (lib) SearchOverlay.Visibility = Visibility.Collapsed;
        if (lib) { LibraryTab.SetResourceReference(Border.BackgroundProperty, "Surface"); LibraryTab.SetResourceReference(Border.BorderBrushProperty, "TabBorder"); LibraryTabText.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); }
        else { LibraryTab.Background = System.Windows.Media.Brushes.Transparent; LibraryTab.BorderBrush = System.Windows.Media.Brushes.Transparent; LibraryTabText.SetResourceReference(TextBlock.ForegroundProperty, "InkSoft"); }
    }

    private void ShowNewPdfsSettings()
    {
        if (_library is null) return;
        if (new NewPdfsDialog(this, _services.Settings, _library.Folders).ShowDialog() != true) return;
        _library.SetNewestVisible(_services.Settings.ShowNewestButton);
        _watcher?.Watch(_library.Folders.Select(f => f.Path));
        _ = _library.RescanAsync();
    }

    private void DisposeLibrary()
    {
        _watcher?.Dispose();
        _images?.Dispose();
    }
}

public partial class MainWindow
{
    // ------------------------------------------------------------------ themes

    private MenuItem ThemeMenu()
    {
        var root = new MenuItem { Header = "Theme" };
        string chosen = _services.Settings.Theme;
        root.Items.Add(Choice(Themes.MatchWindows, chosen == Themes.MatchWindows, () => SetTheme(Themes.MatchWindows, null)));
        root.Items.Add(new Separator());
        foreach (var name in Themes.Names)
            root.Items.Add(Choice(name, chosen == name, () => SetTheme(name, null)));
        return root;
    }

    private MenuItem PagesMenu()
    {
        var root = new MenuItem { Header = "Pages" };
        string chosen = _services.Settings.Pages;
        string asTheme = PageTint.Label(Themes.DefaultPages(Themes.Current));
        root.Items.Add(Choice("As the theme (" + asTheme.ToLowerInvariant() + ")", chosen == "Theme", () => SetTheme(null, "Theme")));
        root.Items.Add(new Separator());
        foreach (var look in Enum.GetValues<PageLook>())
            root.Items.Add(Choice(PageTint.Label(look), chosen == look.ToString(), () => SetTheme(null, look.ToString())));
        return root;
    }

    private void SetTheme(string? theme, string? pages)
    {
        var s = _services.Settings;
        if (theme != null) s.Theme = theme;
        if (pages != null) s.Pages = pages;
        s.Save();
        Themes.Apply(s.Theme, s.Pages);
    }

    private void OnThemeChanged()
    {
        // Pages already drawn in the old look are drawn again.
        foreach (var session in Sessions) _services.Tiles.ForgetDocument(session.DocId);
        Viewport.ApplyTheme();
        _library?.ApplyTheme();
    }
}
