using System.Runtime.InteropServices;

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
        object? application = null;
        object? documents = null;
        object? document = null;

        try
        {
            dynamic word = Activator.CreateInstance(wordType)!;
            application = word;
            word.Visible = false;
            word.ScreenUpdating = false;
            word.DisplayAlerts = 0;

            documents = word.Documents;
            document = ((dynamic)documents).Open(
                sourcePath,
                ReadOnly: true,
                AddToRecentFiles: false,
                Visible: false);

            ((dynamic)document).ExportAsFixedFormat(
                outputPath,
                PdfFormat,
                OpenAfterExport: false,
                OptimizeFor: 0,
                CreateBookmarks: true,
                DocStructureTags: true,
                BitmapMissingFonts: true,
                UseISO19005_1: false);

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
                if (document is not null)
                {
                    ((dynamic)document).Close(SaveChanges: false);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError($"Closing Word document '{sourcePath}' failed.", exception);
            }

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

            ReleaseComObject(document);
            ReleaseComObject(documents);
            ReleaseComObject(application);
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
