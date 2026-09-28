using System.Text;

namespace Pdf_Merger;

internal static class Logger
{
    private static readonly object Sync = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            lock (Sync)
            {
                return _logFilePath ??= ResolveLogFilePath();
            }
        }
    }

    public static string LogDirectory => Path.GetDirectoryName(LogFilePath)!;

    public static void SetOutputPath(string outputPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var candidate = Path.Combine(directory, "PDF_Forge_Error_Log.txt");
            Directory.CreateDirectory(directory);
            using (new FileStream(candidate, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                lock (Sync)
                {
                    _logFilePath = candidate;
                }
            }
        }
        catch
        {
            // Keep the fallback log when the selected output directory is unavailable.
        }
    }

    public static void LogInfo(string message) => Append("INFO", message, null);

    public static void LogError(string context, Exception exception) =>
        Append("ERROR", context, exception);

    private static string ResolveLogFilePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDF Forge", "PDF_Forge_Error_Log.txt")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
                using (new FileStream(candidate, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                {
                    return candidate;
                }
            }
            catch
            {
                // Try the next writable location.
            }
        }

        return candidates[^1];
    }

    private static void Append(string level, string message, Exception? exception)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(" [").Append(level).Append("] ").AppendLine(message);
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never stop the application.
        }
    }
}
