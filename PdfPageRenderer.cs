using System.Diagnostics;

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
        var startInfo = new ProcessStartInfo
        {
            FileName = renderer,
            WorkingDirectory = Path.GetDirectoryName(renderer) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-png");
        startInfo.ArgumentList.Add("-scale-to");
        startInfo.ArgumentList.Add(targetSize.ToString());
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(firstPage.ToString());
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add((firstPage + pageCount - 1).ToString());
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(prefix);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return Array.Empty<string>();
        }

        while (!process.WaitForExit(200))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TryStop(process);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var errorOutput = process.StandardError.ReadToEnd();
        var standardOutput = process.StandardOutput.ReadToEnd();
        if (process.ExitCode != 0)
        {
            Logger.LogInfo($"Preview renderer failed (exit {process.ExitCode}) using '{renderer}': {errorOutput.Trim()} {standardOutput.Trim()}".Trim());
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

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
            // Preview rendering is optional; the split controls still work.
        }
    }

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
