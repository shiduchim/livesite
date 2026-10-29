using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Services;

namespace EKFastPDF;

public partial class App : Application
{
    private AppServices? _services;
    private bool _errorShown;

    public AppSettings Settings { get; init; } = new();
    public IReadOnlyList<string> StartupFiles { get; init; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Log.Info($"WPF render tier {RenderCapability.Tier >> 16}, process render mode {RenderOptions.ProcessRenderMode}, startup {Program.Uptime.ElapsedMilliseconds} ms");

        Themes.Apply(Settings.Theme, Settings.Pages);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (Settings.Theme == Themes.MatchWindows) Dispatcher.BeginInvoke(() => { if (Themes.Resolve(Settings.Theme) != Themes.Current) Themes.Apply(Settings.Theme, Settings.Pages); });
        };
        _services = new AppServices(Settings);
        _services.Pool.Warmup(); // worker processes start while the window appears

        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
        Log.Info($"Main window shown at {Program.Uptime.ElapsedMilliseconds} ms");

        Program.Instance?.Listen(files => Dispatcher.BeginInvoke(() => window.OpenFromShell(files)));
        if (Program.Arguments.Contains("--benchmark"))
        {
            var runner = new BenchmarkRunner(window, _services, Program.Arguments);
            _ = runner.RunAsync();
        }
        else
        {
            // Started for a PDF (double-click, or a PDF landing in a watched folder): come to the front.
            if (StartupFiles.Count > 0) { window.OpenFiles(StartupFiles); window.BringToFront(); }
            _ = window.StartLibraryAsync(StartupFiles.Count > 0);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true; // keep the viewer alive; the error is logged and shown once
        if (_errorShown) return;
        _errorShown = true;
        MessageBox.Show(MainWindow!, "An unexpected error occurred and was written to the EK FastPDF log:\n\n" + e.Exception.Message,
            "EK FastPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _services?.Dispose(); }
        catch (Exception ex) { Log.Error("Shutdown error", ex); }
        base.OnExit(e);
    }
}
