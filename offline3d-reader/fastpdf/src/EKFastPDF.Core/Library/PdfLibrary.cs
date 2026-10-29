using System.Globalization;

namespace EKFastPDF.Core.Library;

/// <summary>A PDF in one of the user's folders. <see cref="Arrived"/> is when it landed there:
/// the later of creation and last write (a download or copy is created now even when the file
/// keeps an older modified time; a file saved in place is written now).</summary>
public sealed record LibraryFile(string Path, string Name, long Size, DateTime Arrived)
{
    public static LibraryFile From(FileInfo f) =>
        new(f.FullName, System.IO.Path.GetFileNameWithoutExtension(f.Name), f.Length,
            f.CreationTime > f.LastWriteTime ? f.CreationTime : f.LastWriteTime);
}

/// <summary>PDFs that arrived together (no more than <see cref="PdfLibrary.BatchGap"/> apart).
/// Files are newest first.</summary>
public sealed record LibraryBatch(DateTime Newest, IReadOnlyList<LibraryFile> Files);

/// <summary>A folder shown in My PDFs. <see cref="AutoOpen"/>: PDFs that land there open at once.</summary>
public sealed class LibraryFolder
{
    public string Path { get; set; } = "";
    public bool AutoOpen { get; set; }
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } n ? n : Path;
}

/// <summary>Folder scanning, batching and "recent" rules for My PDFs. No UI, no PDFium.</summary>
public static class PdfLibrary
{
    /// <summary>Files further apart than this start a new batch.</summary>
    public static readonly TimeSpan BatchGap = TimeSpan.FromMinutes(2);

    public static string DownloadsFolder
    {
        get
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(profile, "Downloads");
        }
    }

    /// <summary>PDFs directly in <paramref name="folder"/> (not subfolders), newest first.
    /// A missing or unreadable folder gives an empty list.</summary>
    public static List<LibraryFile> Scan(string folder)
    {
        var list = new List<LibraryFile>();
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return list;
            foreach (var f in dir.EnumerateFiles("*.pdf", new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }))
            {
                try { list.Add(LibraryFile.From(f)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        list.Sort((a, b) => b.Arrived.CompareTo(a.Arrived));
        return list;
    }

    /// <summary>Groups files into batches, newest batch first; inside a batch newest first.</summary>
    public static List<LibraryBatch> Batches(IEnumerable<LibraryFile> files, TimeSpan? gap = null)
    {
        var g = gap ?? BatchGap;
        var sorted = files.OrderByDescending(f => f.Arrived).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var batches = new List<LibraryBatch>();
        var current = new List<LibraryFile>();
        foreach (var f in sorted)
        {
            if (current.Count > 0 && current[^1].Arrived - f.Arrived > g)
            {
                batches.Add(new LibraryBatch(current[0].Arrived, current));
                current = new List<LibraryFile>();
            }
            current.Add(f);
        }
        if (current.Count > 0) batches.Add(new LibraryBatch(current[0].Arrived, current));
        return batches;
    }

    /// <summary>The newest batch over all files, or empty.</summary>
    public static IReadOnlyList<LibraryFile> NewestBatch(IEnumerable<LibraryFile> files) =>
        Batches(files) is { Count: > 0 } b ? b[0].Files : Array.Empty<LibraryFile>();

    /// <summary>Files that arrived within <paramref name="window"/> before <paramref name="now"/>
    /// and are not in <paramref name="opened"/>, oldest first (the newest ends up as the active tab).</summary>
    public static List<LibraryFile> Recent(IEnumerable<LibraryFile> files, DateTime now, TimeSpan window, ISet<string>? opened = null) =>
        files.Where(f => f.Arrived <= now.AddSeconds(5) && now - f.Arrived <= window && (opened == null || !opened.Contains(f.Path)))
             .OrderBy(f => f.Arrived).ToList();

    /// <summary>"Today 10:42", "Yesterday 16:05", "Monday 09:12" (this week), "12 Oct 09:12", "12 Oct 2025".</summary>
    public static string BatchLabel(DateTime when, DateTime now, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        string time = when.ToString("HH:mm", c);
        var days = (now.Date - when.Date).Days;
        if (days <= 0) return "Today " + time;
        if (days == 1) return "Yesterday " + time;
        if (days < 7) return c.DateTimeFormat.GetDayName(when.DayOfWeek) + " " + time;
        if (when.Year == now.Year) return when.ToString("d MMM", c) + " " + time;
        return when.ToString("d MMM yyyy", c);
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.CurrentCulture) + " GB" :
        bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.CurrentCulture) + " MB" :
        Math.Max(1, (long)Math.Round(bytes / 1024.0)).ToString(CultureInfo.CurrentCulture) + " KB";

    /// <summary>True when the file can be read and nothing is writing it any more (a download
    /// that is still being written, or a PDF still being saved, is not ready).</summary>
    public static bool IsSettled(string path)
    {
        try
        {
            // Read sharing only: fails while anything writes the file. Delete sharing: never blocks a delete.
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return s.Length > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Moves a folder from one position to another (drag to reorder).</summary>
    public static void Move<T>(IList<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count) return;
        to = Math.Clamp(to, 0, list.Count - 1);
        if (from == to) return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
    }

    /// <summary>Same folder? (case-insensitive on Windows, trailing separators ignored).</summary>
    public static bool SamePath(string a, string b)
    {
        static string N(string p) { try { return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(p)); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return p; } }
        return string.Equals(N(a), N(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
