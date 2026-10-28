using System.Printing;
using System.Printing.Interop;
using System.Windows;
using System.Windows.Controls;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Viewer;

namespace EKFastPDF.Services;

/// <summary>
/// Standard Windows print dialog (printer, paper, copies, page range) followed by vector printing
/// in a separate worker process (PDFium draws straight into the printer's device context).
/// </summary>
public static class PrintService
{
    public static async Task PrintAsync(Window owner, DocumentSession session, AppServices services, Action<string> status)
    {
        int count = session.PageCount;
        var dialog = new PrintDialog
        {
            UserPageRangeEnabled = true,
            CurrentPageEnabled = true,
            MinPage = 1,
            MaxPage = (uint)count,
            PageRange = new PageRange(1, count),
        };
        if (dialog.ShowDialog() != true) return;

        List<int> pages = dialog.PageRangeSelection switch
        {
            PageRangeSelection.CurrentPage => new List<int> { session.CurrentPage },
            PageRangeSelection.UserPages => Enumerable.Range(Math.Max(1, dialog.PageRange.PageFrom), Math.Max(0, Math.Min(count, dialog.PageRange.PageTo) - Math.Max(1, dialog.PageRange.PageFrom) + 1)).Select(p => p - 1).ToList(),
            _ => Enumerable.Range(0, count).ToList(),
        };
        if (pages.Count == 0) return;

        PrintQueue queue = dialog.PrintQueue;
        byte[]? devMode = null;
        try
        {
            using var converter = new PrintTicketConverter(queue.FullName, queue.ClientPrintSchemaVersion);
            devMode = converter.ConvertPrintTicketToDevMode(dialog.PrintTicket, BaseDevModeType.UserDefault);
        }
        catch (Exception e) when (e is PrintQueueException or PrintingNotSupportedException or ArgumentException)
        {
            Log.Warn("Print ticket conversion failed, using printer defaults: " + e.Message);
        }

        status($"Printing {pages.Count} page(s) to {queue.FullName}…");
        try
        {
            int printed = await services.Pool.PrintAsync(session.FilePath, session.Password, queue.FullName, devMode,
                session.Title, null, pages, CancellationToken.None);
            status($"Sent {printed} page(s) to {queue.FullName}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("Printing failed", e);
            status("Printing failed: " + e.Message);
            MessageBox.Show(owner, "Printing failed:\n\n" + e.Message, "EK FastPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
