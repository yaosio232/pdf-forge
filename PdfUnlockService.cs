using System.Text;
using iTextSharp.text;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;

namespace Pdf_Merger;

public sealed record UnlockOutput(string SourcePath, string OutputPath, int PageCount);

public sealed record UnlockFailure(string SourcePath, string Message);

public sealed class UnlockResult
{
    public bool Succeeded => Outputs.Count > 0;
    public IReadOnlyList<UnlockOutput> Outputs { get; init; } = Array.Empty<UnlockOutput>();
    public IReadOnlyList<UnlockFailure> Failures { get; init; } = Array.Empty<UnlockFailure>();
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Removes encryption from PDFs when the caller supplies the known password.
/// Source files are never modified and no password guessing is performed.
/// </summary>
public sealed class PdfUnlockService
{
    private static readonly object PdfPermissionSync = new();

    public UnlockResult Unlock(
        IReadOnlyList<string> sourcePaths,
        string password,
        string outputDirectory)
    {
        if (sourcePaths.Count == 0)
        {
            return new UnlockResult { ErrorMessage = "Add at least one encrypted PDF." };
        }

        if (string.IsNullOrEmpty(password))
        {
            return new UnlockResult { ErrorMessage = "Enter the known PDF password." };
        }

        var outputs = new List<UnlockOutput>();
        var failures = new List<UnlockFailure>();

        try
        {
            var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(normalizedOutputDirectory);
            Logger.SetOutputPath(Path.Combine(normalizedOutputDirectory, "PDF_Forge_Output.pdf"));
            Logger.LogInfo($"Batch unlock started for {sourcePaths.Count} PDF file(s).");

            var reservedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourcePath in sourcePaths)
            {
                var normalizedSource = Path.GetFullPath(sourcePath);
                try
                {
                    ValidateSource(normalizedSource);
                    var outputPath = GetAvailableOutputPath(
                        normalizedOutputDirectory,
                        Path.GetFileNameWithoutExtension(normalizedSource),
                        reservedOutputs);
                    var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";

                    try
                    {
                        var pageCount = WriteUnlockedCopy(normalizedSource, password, temporaryPath);
                        ValidateUnlockedPdf(temporaryPath, pageCount);
                        File.Move(temporaryPath, outputPath);
                        outputs.Add(new UnlockOutput(normalizedSource, outputPath, pageCount));
                        Logger.LogInfo($"Unlocked PDF '{normalizedSource}' to '{outputPath}'.");
                    }
                    finally
                    {
                        DeleteIfExists(temporaryPath);
                    }
                }
                catch (Exception exception)
                {
                    Logger.LogError($"Unlocking '{normalizedSource}' failed.", exception);
                    failures.Add(new UnlockFailure(normalizedSource, GetFriendlyMessage(exception)));
                }
            }

            Logger.LogInfo($"Batch unlock finished with {outputs.Count} output(s) and {failures.Count} failure(s).");
            return new UnlockResult
            {
                Outputs = outputs,
                Failures = failures,
                ErrorMessage = outputs.Count == 0 ? "No PDF could be unlocked." : null
            };
        }
        catch (Exception exception)
        {
            Logger.LogError("Starting batch unlock failed.", exception);
            return new UnlockResult
            {
                Outputs = outputs,
                Failures = failures,
                ErrorMessage = GetFriendlyMessage(exception)
            };
        }
    }

    private static int WriteUnlockedCopy(string sourcePath, string password, string temporaryPath)
    {
        lock (PdfPermissionSync)
        {
            var previousPermissionMode = PdfReader.unethicalreading;
            try
            {
                // A known user password opens the document but iText still blocks rewriting
                // permission-restricted files. This flag affects permissions only; it does not
                // guess or bypass the encryption password supplied above.
                PdfReader.unethicalreading = true;
                using var reader = new PdfReader(sourcePath, Encoding.UTF8.GetBytes(password));
                if (!reader.IsEncrypted())
                {
                    throw new InvalidDataException("This PDF is not password protected.");
                }

                var pageCount = reader.NumberOfPages;
                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var document = new Document();
                var copy = new PdfCopy(document, output);
                try
                {
                    document.Open();
                    copy.AddDocument(reader);
                }
                finally
                {
                    copy.Close();
                    document.Close();
                }
                return pageCount;
            }
            finally
            {
                PdfReader.unethicalreading = previousPermissionMode;
            }
        }
    }

    private static void ValidateSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source PDF does not exist.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only PDF files can be unlocked.");
        }

        using var stream = File.OpenRead(sourcePath);
        Span<byte> header = stackalloc byte[5];
        if (stream.Read(header) != header.Length ||
            header[0] != (byte)'%' || header[1] != (byte)'P' ||
            header[2] != (byte)'D' || header[3] != (byte)'F' || header[4] != (byte)'-')
        {
            throw new InvalidDataException("The file does not contain a valid PDF header.");
        }
    }

    private static void ValidateUnlockedPdf(string path, int expectedPageCount)
    {
        try
        {
            using var reader = new PdfReader(path);
            if (reader.IsEncrypted() || reader.NumberOfPages != expectedPageCount)
            {
                throw new InvalidDataException("The unlocked PDF failed validation.");
            }
        }
        catch (BadPasswordException exception)
        {
            throw new InvalidDataException("The generated PDF is still encrypted.", exception);
        }
    }

    private static string GetAvailableOutputPath(
        string outputDirectory,
        string sourceName,
        ISet<string> reservedOutputs)
    {
        var suffix = 1;
        while (true)
        {
            var name = suffix == 1
                ? $"{sourceName}_unlocked.pdf"
                : $"{sourceName}_unlocked_{suffix}.pdf";
            var candidate = Path.Combine(outputDirectory, name);
            if (!File.Exists(candidate) && reservedOutputs.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string GetFriendlyMessage(Exception exception) =>
        exception switch
        {
            BadPasswordException => "The password is incorrect.",
            UnauthorizedAccessException => "The file or output folder is not accessible.",
            IOException ioException when ioException.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) =>
                "The file is locked by another application.",
            _ => exception.Message
        };

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Logger.LogError($"Failed to delete unlock temporary file '{path}'.", exception);
        }
    }
}
