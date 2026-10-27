using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Pdf_Merger;

public sealed class WordToPdfConverter : IWordToPdfConverter
{
    private const int PdfFormat = 17;

    public string? Convert(string sourcePath)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType is null)
        {
            Logger.LogInfo($"Microsoft Word is not installed; skipped '{sourcePath}'.");
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "PdfMerger");
        Directory.CreateDirectory(tempDirectory);
        var outputPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.pdf");
        var marker = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.word.json");
        object? application = null;
        Process? process = null;

        try
        {
            process = WordConversionWorker.StartOwnedWord(marker);
            dynamic word = WordConversionWorker.GetOwnedApplication(process);
            application = word;
            word.Visible = false;
            word.ScreenUpdating = false;
            word.DisplayAlerts = 0;
            ConvertWithApplication(word, sourcePath, outputPath);

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new IOException("Word did not produce a PDF.");
            }

            return outputPath;
        }
        catch (Exception exception)
        {
            Logger.LogError($"Word conversion failed for '{sourcePath}'.", exception);
            TryDelete(outputPath);
            TryDelete(tempDirectory);
            return null;
        }
        finally
        {
            try
            {
                if (application is not null)
                {
                    ((dynamic)application).Quit(SaveChanges: false);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Closing Word failed.", exception);
            }

            ReleaseComObject(application);
            WordConversionWorker.StopOwnedWord(marker);
            TryDelete(marker);
            process?.Dispose();
        }
    }

    internal static void ConvertWithApplication(object application, string sourcePath, string outputPath)
    {
        dynamic word = application;
        word.AutomationSecurity = 3; // Fail closed before touching Documents or opening a file.
        object? documents = null;
        object? document = null;
        try
        {
            documents = word.Documents;
            document = ((dynamic)documents).Open(sourcePath, ReadOnly: true,
                AddToRecentFiles: false, Visible: false);
            ((dynamic)document).ExportAsFixedFormat(outputPath, PdfFormat,
                OpenAfterExport: false, OptimizeFor: 0, CreateBookmarks: true,
                DocStructureTags: true, BitmapMissingFonts: true, UseISO19005_1: false);
        }
        finally
        {
            try { if (document is not null) ((dynamic)document).Close(SaveChanges: false); }
            catch (Exception exception) { Logger.LogError($"Closing Word document '{sourcePath}' failed.", exception); }
            ReleaseComObject(document);
            ReleaseComObject(documents);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // Cleanup is best effort.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
            // Cleanup is best effort.
        }
    }
}
