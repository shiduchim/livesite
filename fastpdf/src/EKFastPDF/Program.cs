using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Native;
using EKFastPDF.Services;

namespace EKFastPDF;

/// <summary>
/// Process entry point. The same executable is the viewer and its render workers, so worker
/// start-up never depends on a second file or on the working directory.
/// Startup is logged from the first line, before any WPF code runs, so a failure that happens
/// before a window appears is still recorded in %LOCALAPPDATA%\EK FastPDF\logs\fastpdf.log.
/// </summary>
public static class Program
{
    public static string[] Arguments { get; private set; } = Array.Empty<string>();
    public static readonly Stopwatch Uptime = Stopwatch.StartNew();
    internal static SingleInstance? Instance;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EK FastPDF", "logs");

    /// <summary>Unattended print (used by the installer tests; also handy for scripting):
    /// EKFastPDF.exe --print-file "&lt;printer&gt;" &lt;output file or -&gt; &lt;pdf&gt;</summary>
    private static int PrintFile(string printer, string? output, string pdf)
    {
        using var pool = new Core.Rendering.RenderPool(new Core.Rendering.RenderPoolOptions
        {
            WorkerExecutable = Environment.ProcessPath!,
            WorkerCount = 1,
        });
        int doc = pool.RegisterDocument(pdf);
        var info = pool.OpenDocumentAsync(doc).GetAwaiter().GetResult();
        int printed = pool.PrintAsync(pdf, null, printer, null, Path.GetFileName(pdf), output,
            Enumerable.Range(0, info.PageCount).ToList(), CancellationToken.None).GetAwaiter().GetResult();
        Log.Info($"--print-file: {printed} page(s) to {printer}");
        Console.WriteLine($"Printed {printed} page(s)");
        return printed == info.PageCount ? 0 : 3;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (WorkerHost.IsWorkerInvocation(args)) return WorkerHost.Run(args);

        Arguments = args;
        Log.Initialize(LogDirectory, "fastpdf.log");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception (terminating: " + e.IsTerminating + ")", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            Log.Info($"Start v{typeof(Program).Assembly.GetName().Version} pid {Environment.ProcessId}; exe \"{Environment.ProcessPath}\"; " +
                     $"cwd \"{Environment.CurrentDirectory}\"; args [{string.Join(" | ", args)}]; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}");

            if (args.Contains("--diagnose"))
            {
                string report = Path.Combine(LogDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(report, DiagnosticsReport.Build(null));
                Log.Info("Diagnostics written to " + report);
                Console.WriteLine(report);
                return 0;
            }

            if (args.Length >= 4 && args[0] == "--print-file")
                return PrintFile(printer: args[1], output: args[2] == "-" ? null : Path.GetFullPath(args[2]), pdf: Path.GetFullPath(args[3]));

            bool benchmark = args.Contains("--benchmark");
            var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a)).Select(Path.GetFullPath).ToList();
            if (!benchmark)
            {
                Instance = SingleInstance.Acquire(files);
                if (Instance is null) return 0; // handed over to the running window
            }

            var settings = AppSettings.Load();
            if (args.Contains("--software-rendering") || settings.RenderMode.Equals("Software", StringComparison.OrdinalIgnoreCase))
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                Log.Info("Software rendering requested");
            }

            var app = new App { Settings = settings, StartupFiles = files };
            app.InitializeComponent();
            int code = app.Run();
            Log.Info($"Exit code {code} after {Uptime.Elapsed.TotalSeconds:0.0} s");
            return code;
        }
        catch (Exception e)
        {
            Log.Error("Fatal startup error", e);
            try
            {
                MessageBox.Show(
                    "EK FastPDF could not start.\n\n" + e.Message + "\n\nDetails were written to:\n" + Path.Combine(LogDirectory, "fastpdf.log"),
                    "EK FastPDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception) { /* no UI available; the log has the details */ }
            return 1;
        }
        finally
        {
            Instance?.Dispose();
        }
    }
}
