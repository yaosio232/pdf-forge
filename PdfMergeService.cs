using iTextSharp.text;
using iTextSharp.text.pdf;

namespace Pdf_Merger;

public sealed record MergeFailure(string SourcePath, string Message);

public sealed class MergeResult
{
    public bool Succeeded { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public IReadOnlyList<string> MergedSources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MergeFailure> Failures { get; init; } = Array.Empty<MergeFailure>();
    public string? ErrorMessage { get; init; }
}

public interface IWordToPdfConverter
{
    string? Convert(string sourcePath);
}

public sealed class PdfMergeService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx" };

    private readonly IWordToPdfConverter _wordConverter;

    public PdfMergeService(IWordToPdfConverter? wordConverter = null)
    {
        _wordConverter = wordConverter ?? new WordToPdfConverter();
    }

    public MergeResult Merge(IReadOnlyList<string> sourcePaths, string outputPath)
    {
        var failures = new List<MergeFailure>();
        var mergedSources = new List<string>();
        var temporaryFiles = new List<string>();
        string? transactionPath = null;

        try
        {
            if (sourcePaths.Count == 0)
            {
                return Failure(outputPath, "Please add at least one PDF, DOC, or DOCX file.");
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            Logger.SetOutputPath(fullOutputPath);
            Logger.LogInfo($"Merge started for output '{fullOutputPath}'.");
            var normalizedSources = sourcePaths.Select(Path.GetFullPath).ToList();

            if (normalizedSources.Any(path => PathsEqual(path, fullOutputPath)))
            {
                return Failure(fullOutputPath, "The output path cannot be the same as a source file.");
            }

            var preparedPdfPaths = new List<string>();
            foreach (var sourcePath in normalizedSources)
            {
                try
                {
                    ValidateSource(sourcePath);
                    var extension = Path.GetExtension(sourcePath);
                    if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        preparedPdfPaths.Add(sourcePath);
                        mergedSources.Add(sourcePath);
                        continue;
                    }

                    var convertedPath = _wordConverter.Convert(sourcePath);
                    if (string.IsNullOrWhiteSpace(convertedPath) || !File.Exists(convertedPath))
                    {
                        failures.Add(new MergeFailure(sourcePath,
                            "Microsoft Word conversion was not available or failed."));
                        continue;
                    }

                    preparedPdfPaths.Add(convertedPath);
                    temporaryFiles.Add(convertedPath);
                    mergedSources.Add(sourcePath);
                }
                catch (Exception exception)
                {
                    Logger.LogError($"Preparing source '{sourcePath}' failed.", exception);
                    failures.Add(new MergeFailure(sourcePath, GetFriendlyMessage(exception)));
                }
            }

            if (preparedPdfPaths.Count == 0)
            {
                return new MergeResult
                {
                    OutputPath = fullOutputPath,
                    Failures = failures,
                    ErrorMessage = "No source files could be prepared for merging."
                };
            }

            transactionPath = CreateSiblingTempPath(fullOutputPath);
            MergePdfFiles(preparedPdfPaths, transactionPath);
            MetadataSanitizer.Sanitize(transactionPath);
            ValidatePdf(transactionPath);
            AtomicReplace(transactionPath, fullOutputPath);
            transactionPath = null;
            Logger.LogInfo($"Merge completed successfully with {mergedSources.Count} source file(s) and {CountPages(fullOutputPath)} page(s).");

            return new MergeResult
            {
                Succeeded = true,
                OutputPath = fullOutputPath,
                PageCount = CountPages(fullOutputPath),
                MergedSources = mergedSources,
                Failures = failures
            };
        }
        catch (Exception exception)
        {
            Logger.LogError($"Merging to '{outputPath}' failed.", exception);
            return new MergeResult
            {
                OutputPath = outputPath,
                MergedSources = mergedSources,
                Failures = failures,
                ErrorMessage = GetFriendlyMessage(exception)
            };
        }
        finally
        {
            DeleteIfExists(transactionPath);
            foreach (var temporaryFile in temporaryFiles)
            {
                DeleteIfExists(temporaryFile);
            }
        }
    }

    public static void MergePdfFiles(IReadOnlyList<string> pdfPaths, string outputPath)
    {
        if (pdfPaths.Count == 0)
        {
            throw new ArgumentException("At least one PDF is required.", nameof(pdfPaths));
        }

        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var document = new Document();
        var copy = new PdfCopy(document, output);
        var outlines = new List<Dictionary<string, object>>();
        var pageOffset = 0;

        try
        {
            document.Open();
            foreach (var pdfPath in pdfPaths)
            {
                using var reader = new PdfReader(pdfPath);
                if (reader.IsEncrypted())
                {
                    throw new InvalidDataException($"Encrypted PDFs are not supported: {pdfPath}");
                }

                reader.ConsolidateNamedDestinations();
                var bookmarks = SimpleBookmark.GetBookmark(reader);
                if (bookmarks is not null)
                {
                    SimpleBookmark.ShiftPageNumbers(bookmarks, pageOffset, null);
                    outlines.AddRange(bookmarks);
                }

                copy.AddDocument(reader);
                pageOffset += reader.NumberOfPages;
            }

            if (outlines.Count > 0)
            {
                copy.Outlines = outlines;
            }
        }
        finally
        {
            copy.Close();
            document.Close();
        }
    }

    private static void ValidateSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source file does not exist.", sourcePath);
        }

        if ((File.GetAttributes(sourcePath) & FileAttributes.Directory) != 0)
        {
            throw new IOException("The source path is a directory.");
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidDataException("The file type is not supported.");
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(sourcePath);
            Span<byte> header = stackalloc byte[5];
            if (stream.Read(header) != header.Length ||
                header[0] != (byte)'%' || header[1] != (byte)'P' ||
                header[2] != (byte)'D' || header[3] != (byte)'F' ||
                header[4] != (byte)'-')
            {
                throw new InvalidDataException("The file does not contain a valid PDF header.");
            }
        }
    }

    private static string CreateSiblingTempPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        return Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void AtomicReplace(string temporaryPath, string outputPath)
    {
        if (File.Exists(outputPath))
        {
            File.Replace(temporaryPath, outputPath, null);
        }
        else
        {
            File.Move(temporaryPath, outputPath);
        }
    }

    private static int CountPages(string pdfPath)
    {
        using var reader = new PdfReader(pdfPath);
        return reader.NumberOfPages;
    }

    private static void ValidatePdf(string pdfPath)
    {
        using var reader = new PdfReader(pdfPath);
        if (reader.NumberOfPages <= 0)
        {
            throw new InvalidDataException("The generated PDF has no pages.");
        }
    }

    private static MergeResult Failure(string outputPath, string message) =>
        new() { OutputPath = outputPath, ErrorMessage = message };

    private static string GetFriendlyMessage(Exception exception) =>
        exception switch
        {
            iTextSharp.text.exceptions.InvalidPdfException => "The PDF is invalid or damaged.",
            UnauthorizedAccessException => "The file or output folder is not accessible.",
            IOException ioException when ioException.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) =>
                "The file is locked by another application.",
            _ => exception.Message
        };

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            second.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void DeleteIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Logger.LogError($"Failed to delete temporary file '{path}'.", exception);
        }
    }
}

public static class MetadataSanitizer
{
    public static void Sanitize(string inputPath)
    {
        var sanitizedPath = $"{inputPath}.{Guid.NewGuid():N}.sanitized";
        try
        {
            using (var reader = new PdfReader(inputPath))
            {
                reader.Trailer.Remove(PdfName.INFO);
                reader.Catalog.Remove(PdfName.METADATA);

                using var output = new FileStream(sanitizedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var stamper = new PdfStamper(reader, output);
                stamper.MoreInfo = new Dictionary<string, string>();
                stamper.XmpMetadata = Array.Empty<byte>();
            }

            File.Replace(sanitizedPath, inputPath, null);
        }
        finally
        {
            if (File.Exists(sanitizedPath))
            {
                File.Delete(sanitizedPath);
            }
        }
    }
}
