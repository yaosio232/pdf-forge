namespace Pdf_Merger;

/// <summary>
/// Uses the Poppler renderer shipped beside the application for the split-page browser.
/// The split workflow remains usable without rendered previews.
/// </summary>
public static class PdfPageRenderer
{
    public static IReadOnlyList<string> RenderPages(
        string sourcePath,
        string outputDirectory,
        int pageCount,
        int targetSize = 640,
        int firstPage = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputDirectory);
        var renderer = FindRenderer();
        if (renderer is null || pageCount <= 0 || firstPage <= 0)
        {
            Logger.LogInfo($"Preview renderer unavailable. Candidate path search returned '{renderer ?? "none"}'.");
            return Array.Empty<string>();
        }

        var prefix = Path.Combine(outputDirectory, "page");
        var arguments = new[]
        {
            "-png", "-scale-to", targetSize.ToString(), "-f", firstPage.ToString(),
            "-l", (firstPage + pageCount - 1).ToString(), sourcePath, prefix
        };
        int exitCode;
        try
        {
            exitCode = RunConfined(renderer, arguments,
                Path.GetDirectoryName(renderer) ?? AppContext.BaseDirectory, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Logger.LogError("Preview renderer was stopped or could not be confined; preview is unavailable.", exception);
            return Array.Empty<string>();
        }
        if (exitCode != 0)
        {
            Logger.LogInfo($"Preview renderer failed (exit {exitCode}) using '{renderer}'. Standard output and error are not retained.");
            return Array.Empty<string>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var outputFiles = Directory.EnumerateFiles(outputDirectory, "page-*.png")
            .Select(path => new
            {
                Path = path,
                Number = ParsePageNumber(path)
            })
            .Where(item => item.Number >= firstPage)
            .OrderBy(item => item.Number)
            .Take(pageCount)
            .Select(item => item.Path)
            .ToArray();
        if (outputFiles.Length == 0)
        {
            Logger.LogInfo($"Preview renderer produced no PNG files using '{renderer}' in '{outputDirectory}'.");
        }

        return outputFiles;
    }

    internal static int RunConfined(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, CancellationToken cancellationToken,
        TimeSpan? wallTimeLimit = null, TimeSpan? cpuTimeLimit = null, long? memoryLimitBytes = null) =>
        RendererProcess.Run(executable, arguments, workingDirectory, cancellationToken,
            wallTimeLimit ?? TimeSpan.FromSeconds(120), cpuTimeLimit ?? TimeSpan.FromSeconds(90),
            memoryLimitBytes ?? 1024L * 1024 * 1024);

    private static int ParsePageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name[(separator + 1)..], out var page)
            ? page
            : 0;
    }

    private static string? FindRenderer()
    {
        foreach (var candidate in GetRendererCandidates(AppContext.BaseDirectory))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> GetRendererCandidates(string baseDirectory) =>
        new[]
        {
            Path.Combine(baseDirectory, "pdftoppm.exe"),
            Path.Combine(baseDirectory, "poppler", "Library", "bin", "pdftoppm.exe")
        };
}
