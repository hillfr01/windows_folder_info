using System.Globalization;
using System.Text;

namespace FolderInfo;

class Program
{
    static int Main(string[] args)
    {
        string? root = null;
        double minMb = 0;
        string? csvPath = null;
        bool csvRequested = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--min-mb" or "-m")
            {
                if (i + 1 >= args.Length || !double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out minMb))
                {
                    Console.Error.WriteLine("--min-mb requires a numeric value");
                    return 1;
                }
                i++;
            }
            else if (args[i] == "--csv")
            {
                csvRequested = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    csvPath = args[i + 1];
                    i++;
                }
            }
            else
            {
                root = args[i];
            }
        }

        root ??= Directory.GetCurrentDirectory();

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Directory not found: {root}");
            return 1;
        }

        root = Path.GetFullPath(root);
        long minBytes = (long)(minMb * 1024 * 1024);

        var results = new List<FolderStats>();
        CollectStats(root, results);

        var filtered = results.Where(s => s.TotalBytes >= minBytes).OrderByDescending(s => s.TotalBytes).ToList();

        foreach (var stats in filtered)
        {
            Console.WriteLine($"{stats.FileCount,8}  {FormatSize(stats.TotalBytes),12}  {stats.Path}");
        }

        if (csvRequested)
        {
            string folderName = Path.GetFileName(root);
            if (string.IsNullOrEmpty(folderName))
            {
                folderName = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(":", "");
            }

            string csvDir = Path.Combine(Directory.GetCurrentDirectory(), "csv");
            Directory.CreateDirectory(csvDir);

            string fileName = Path.GetFileName(csvPath) is { Length: > 0 } name ? name : $"{folderName}.csv";
            string fullCsvPath = Path.Combine(csvDir, fileName);

            WriteCsv(fullCsvPath, filtered);
            Console.WriteLine();
            Console.WriteLine($"Wrote {filtered.Count} rows to {fullCsvPath}");
        }

        return 0;
    }

    static void WriteCsv(string path, IEnumerable<FolderStats> stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Folder,FileCount,SizeBytes,Size");

        foreach (var s in stats)
        {
            sb.AppendLine($"{CsvEscape(s.Path)},{s.FileCount},{s.TotalBytes},{CsvEscape(FormatSize(s.TotalBytes))}");
        }

        File.WriteAllText(path, sb.ToString());
    }

    static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    static void CollectStats(string root, List<FolderStats> results)
    {
        foreach (var dir in EnumerateDirectoriesSafe(root, includeRoot: true))
        {
            long fileCount = 0;
            long totalBytes = 0;

            foreach (var file in EnumerateFilesSafe(dir))
            {
                fileCount++;
                totalBytes += file.Length;
            }

            results.Add(new FolderStats(dir, fileCount, totalBytes));
        }
    }

    static IEnumerable<string> EnumerateDirectoriesSafe(string root, bool includeRoot)
    {
        if (includeRoot)
        {
            yield return root;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] subDirs;

            try
            {
                subDirs = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subDir in subDirs)
            {
                yield return subDir;
                pending.Push(subDir);
            }
        }
    }

    static IEnumerable<FileInfo> EnumerateFilesSafe(string dir)
    {
        FileInfo[] files;

        try
        {
            files = new DirectoryInfo(dir).GetFiles();
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }
    }

    static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size.ToString("0.##", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    record FolderStats(string Path, long FileCount, long TotalBytes);
}
