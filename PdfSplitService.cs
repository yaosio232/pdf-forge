using iTextSharp.text;
using iTextSharp.text.pdf;

namespace Pdf_Merger;

public sealed record SplitPart(string OutputPath, int FirstPage, int LastPage, int PageCount);

public sealed class SplitResult
{
    public bool Succeeded { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public IReadOnlyList<int> SplitAfterPages { get; init; } = Array.Empty<int>();
    public IReadOnlyList<SplitPart> Parts { get; init; } = Array.Empty<SplitPart>();
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Splits one PDF into multiple page-range PDFs without modifying the source.
/// </summary>
public sealed class PdfSplitService
{
    public SplitResult Split(
        string sourcePath,
        IReadOnlyCollection<int> splitAfterPages,
        string outputBasePath,
        IReadOnlyList<string>? partNames = null)
    {
        var normalizedSource = Path.GetFullPath(sourcePath);
        var normalizedOutputBase = Path.GetFullPath(outputBasePath);
        var splitPoints = splitAfterPages.OrderBy(page => page).ToArray();
        var temporaryFiles = new List<string>();
        var outputFiles = new List<string>();

        try
        {
            ValidateSource(normalizedSource);
            if (PathsEqual(normalizedSource, normalizedOutputBase))
            {
                return Failure(normalizedSource, "The output base path cannot be the same as the source file.");
            }

            var outputDirectory = Path.GetDirectoryName(normalizedOutputBase);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return Failure(normalizedSource, "Choose a valid output folder.");
            }

            Directory.CreateDirectory(outputDirectory);
            Logger.SetOutputPath(normalizedOutputBase);
            Logger.LogInfo($"Split started for source '{normalizedSource}'.");

            using var reader = new PdfReader(normalizedSource);
            if (reader.IsEncrypted())
            {
                return Failure(normalizedSource, "Encrypted PDFs are not supported.");
            }

            var pageCount = reader.NumberOfPages;
            if (pageCount <= 0)
            {
                return Failure(normalizedSource, "The PDF does not contain any pages.");
            }

            if (splitPoints.Any(page => page <= 0 || page >= pageCount))
            {
                return Failure(normalizedSource, $"Split points must be between page 1 and page {pageCount - 1}.");
            }

            var ranges = BuildRanges(pageCount, splitPoints);
            var usedPartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < ranges.Count; index++)
            {
                var range = ranges[index];
                var partName = MakeSafePartName(partNames?.ElementAtOrDefault(index), index + 1);
                if (!usedPartNames.Add(partName))
                {
                    partName = $"{partName}_{index + 1:00}";
                    usedPartNames.Add(partName);
                }
                var outputPath = Path.Combine(outputDirectory, $"{partName}.pdf");
                var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";

                WriteRange(reader, range.FirstPage, range.LastPage, temporaryPath);
                MetadataSanitizer.Sanitize(temporaryPath);
                ValidatePdf(temporaryPath);
                temporaryFiles.Add(temporaryPath);
                outputFiles.Add(outputPath);
            }

            var parts = new List<SplitPart>(ranges.Count);
            for (var index = 0; index < ranges.Count; index++)
            {
                AtomicReplace(temporaryFiles[index], outputFiles[index]);
                temporaryFiles[index] = string.Empty;
                var range = ranges[index];
                parts.Add(new SplitPart(outputFiles[index], range.FirstPage, range.LastPage, range.PageCount));
            }

            Logger.LogInfo($"Split completed with {parts.Count} output part(s).");
            return new SplitResult
            {
                Succeeded = true,
                SourcePath = normalizedSource,
                SplitAfterPages = splitPoints,
                Parts = parts
            };
        }
        catch (Exception exception)
        {
            Logger.LogError($"Splitting '{normalizedSource}' failed.", exception);
            return new SplitResult
            {
                SourcePath = normalizedSource,
                SplitAfterPages = splitPoints,
                ErrorMessage = GetFriendlyMessage(exception)
            };
        }
        finally
        {
            foreach (var temporaryFile in temporaryFiles)
            {
                DeleteIfExists(temporaryFile);
            }
        }
    }

    public SplitResult SavePart(string sourcePath, int firstPage, int lastPage, string outputPath)
    {
        var normalizedSource = Path.GetFullPath(sourcePath);
        var normalizedOutput = Path.GetFullPath(outputPath);
        var temporaryPath = $"{normalizedOutput}.{Guid.NewGuid():N}.tmp";

        try
        {
            ValidateSource(normalizedSource);
            if (PathsEqual(normalizedSource, normalizedOutput))
            {
                return Failure(normalizedSource, "The output path cannot be the same as the source file.");
            }

            var outputDirectory = Path.GetDirectoryName(normalizedOutput);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return Failure(normalizedSource, "Choose a valid output folder.");
            }

            Directory.CreateDirectory(outputDirectory);
            using var reader = new PdfReader(normalizedSource);
            if (reader.IsEncrypted())
            {
                return Failure(normalizedSource, "Encrypted PDFs are not supported.");
            }

            if (firstPage <= 0 || lastPage < firstPage || lastPage > reader.NumberOfPages)
            {
                return Failure(normalizedSource,
                    $"Choose a page range between 1 and {reader.NumberOfPages}.");
            }

            WriteRange(reader, firstPage, lastPage, temporaryPath);
            MetadataSanitizer.Sanitize(temporaryPath);
            ValidatePdf(temporaryPath);
            AtomicReplace(temporaryPath, normalizedOutput);
            temporaryPath = string.Empty;

            var part = new SplitPart(normalizedOutput, firstPage, lastPage, lastPage - firstPage + 1);
            Logger.SetOutputPath(normalizedOutput);
            Logger.LogInfo($"Saved split part for pages {firstPage}-{lastPage}.");
            return new SplitResult
            {
                Succeeded = true,
                SourcePath = normalizedSource,
                Parts = new[] { part }
            };
        }
        catch (Exception exception)
        {
            Logger.LogError($"Saving pages {firstPage}-{lastPage} from '{normalizedSource}' failed.", exception);
            return new SplitResult
            {
                SourcePath = normalizedSource,
                ErrorMessage = GetFriendlyMessage(exception)
            };
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    public static int GetPageCount(string sourcePath)
    {
        var normalizedSource = Path.GetFullPath(sourcePath);
        ValidateSource(normalizedSource);
        using var reader = new PdfReader(normalizedSource);
        if (reader.IsEncrypted())
        {
            throw new InvalidDataException("Encrypted PDFs are not supported.");
        }

        return reader.NumberOfPages;
    }

    private static IReadOnlyList<PageRange> BuildRanges(int pageCount, IReadOnlyList<int> splitPoints)
    {
        var ranges = new List<PageRange>();
        var firstPage = 1;
        foreach (var splitPoint in splitPoints)
        {
            ranges.Add(new PageRange(firstPage, splitPoint));
            firstPage = splitPoint + 1;
        }

        ranges.Add(new PageRange(firstPage, pageCount));
        return ranges;
    }

    private static string MakeSafePartName(string? requestedName, int index)
    {
        var fallback = $"part_{index:00}";
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? fallback
            : Path.GetFileNameWithoutExtension(requestedName.Trim());
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? fallback : name;
    }

    private static void WriteRange(PdfReader reader, int firstPage, int lastPage, string outputPath)
    {
        PrepareRangeLinks(reader, firstPage, lastPage);
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var document = new Document();
        var copy = new PdfCopy(document, output);
        try
        {
            document.Open();
            for (var page = firstPage; page <= lastPage; page++)
            {
                copy.AddPage(copy.GetImportedPage(reader, page));
            }
        }
        finally
        {
            copy.Close();
            document.Close();
        }
    }

    internal static void PrepareRangeLinks(PdfReader reader, int firstPage, int lastPage)
    {
        // PdfCopy imports page annotations, but not the catalog's destination name tree.
        // Resolve names before copying so PdfCopy can remap the retained page references.
        reader.ConsolidateNamedDestinations();
        var retainedPages = new HashSet<(int Number, int Generation)>();
        for (var page = firstPage; page <= lastPage; page++)
        {
            var reference = reader.GetPageOrigRef(page);
            retainedPages.Add((reference.Number, reference.Generation));
        }

        // Only filter this part's annotations: later parts share the same reader.
        for (var page = firstPage; page <= lastPage; page++)
        {
            var annotations = reader.GetPageN(page).GetAsArray(PdfName.ANNOTS);
            if (annotations is null) continue;

            for (var index = annotations.Size - 1; index >= 0; index--)
            {
                var annotation = annotations.GetAsDict(index);
                if (annotation?.GetAsName(PdfName.SUBTYPE) != PdfName.LINK) continue;

                var destination = annotation.Get(PdfName.DEST);
                if (destination is null)
                {
                    var action = annotation.GetAsDict(PdfName.A);
                    if (action?.GetAsName(PdfName.S) != PdfName.GOTO) continue;
                    destination = action.Get(PdfName.D);
                }

                var explicitDestination = PdfReader.GetPdfObject(destination) as PdfArray;
                var target = explicitDestination is { Size: > 0 }
                    ? explicitDestination.GetAsIndirectObject(0)
                    : null;
                if (target is null || !retainedPages.Contains((target.Number, target.Generation)))
                {
                    // An omitted or unresolved local target must not become a wrong-page jump.
                    annotations.Remove(index);
                }
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
            throw new InvalidDataException("Only PDF files can be split.");
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

    private static void ValidatePdf(string path)
    {
        using var reader = new PdfReader(path);
        if (reader.NumberOfPages <= 0)
        {
            throw new InvalidDataException("The generated PDF has no pages.");
        }
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

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            second.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string GetFriendlyMessage(Exception exception) =>
        exception switch
        {
            iTextSharp.text.exceptions.InvalidPdfException => "The PDF is invalid or damaged.",
            UnauthorizedAccessException => "The file or output folder is not accessible.",
            IOException ioException when ioException.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) =>
                "The file is locked by another application.",
            _ => exception.Message
        };

    private static SplitResult Failure(string sourcePath, string message) =>
        new() { SourcePath = sourcePath, ErrorMessage = message };

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
            Logger.LogError($"Failed to delete split temporary file '{path}'.", exception);
        }
    }

    private sealed record PageRange(int FirstPage, int LastPage)
    {
        public int PageCount => LastPage - FirstPage + 1;
    }
}
