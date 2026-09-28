using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Pdf_Merger;
using Xunit;
using IoPath = System.IO.Path;

namespace Pdf_Merger.Tests;

public sealed class PdfMergeServiceTests
{
    [Fact]
    public void Merge_preserves_source_order_and_page_count()
    {
        using var fixture = new TempFixture();
        var first = fixture.CreatePdf("FIRST", 20);
        var second = fixture.CreatePdf("SECOND", 21);
        var output = fixture.PathFor("merged.pdf");

        var result = new PdfMergeService().Merge(new[] { first, second }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(41, result.PageCount);
        Assert.True(File.Exists(output));
        using var reader = new PdfReader(output);
        Assert.Equal(41, reader.NumberOfPages);
        Assert.Contains("FIRST", PdfTextExtractor.GetTextFromPage(reader, 1));
        Assert.Contains("SECOND", PdfTextExtractor.GetTextFromPage(reader, 21));
        Assert.Contains("SECOND", PdfTextExtractor.GetTextFromPage(reader, 41));
    }

    [Fact]
    public void Merge_writes_log_next_to_output_pdf()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("LOG_SOURCE", 20);
        var output = fixture.PathFor("nested-output", "merged.pdf");

        var result = new PdfMergeService().Merge(new[] { source }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var logPath = fixture.PathFor("nested-output", "PDF_Forge_Error_Log.txt");
        Assert.True(File.Exists(logPath));
        Assert.Contains("Merge completed successfully", File.ReadAllText(logPath));
    }

    [Fact]
    public void Merge_rejects_output_equal_to_source_without_modifying_source()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("KEEP", 20);
        var before = File.ReadAllBytes(source);

        var result = new PdfMergeService().Merge(new[] { source }, source);

        Assert.False(result.Succeeded);
        Assert.Contains("same as a source", result.ErrorMessage);
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public void Merge_skips_missing_word_when_word_is_unavailable()
    {
        using var fixture = new TempFixture();
        var pdf = fixture.CreatePdf("PDF", 20);
        var docx = fixture.PathFor("missing.docx");
        File.WriteAllText(docx, "not converted");
        var output = fixture.PathFor("partial.pdf");

        var result = new PdfMergeService(new FakeWordConverter()).Merge(
            new[] { pdf, docx }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Single(result.Failures);
        Assert.Equal(20, result.PageCount);
    }

    [Fact]
    public void Merge_removes_standard_metadata_without_removing_page_content()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("VISIBLE_CONTENT", 20, addMetadata: true);
        var output = fixture.PathFor("metadata-clean.pdf");

        var result = new PdfMergeService().Merge(new[] { source }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var reader = new PdfReader(output);
        Assert.Contains("VISIBLE_CONTENT", PdfTextExtractor.GetTextFromPage(reader, 1));
        Assert.DoesNotContain("PRIVATE_TITLE", File.ReadAllText(output, System.Text.Encoding.Latin1));
        Assert.False(reader.Info.TryGetValue("Title", out var title) &&
                     !string.IsNullOrWhiteSpace(title));
    }

    [Fact]
    public void Merge_failure_does_not_replace_an_existing_output()
    {
        using var fixture = new TempFixture();
        var valid = fixture.CreatePdf("VALID", 20);
        var invalid = fixture.PathFor("broken.pdf");
        File.WriteAllText(invalid, "%PDF-1.7 but this is not a PDF");
        var output = fixture.PathFor("existing.pdf");
        File.WriteAllText(output, "existing output must survive");
        var before = File.ReadAllBytes(output);

        var result = new PdfMergeService().Merge(new[] { valid, invalid }, output);

        Assert.False(result.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(output));
    }

    [Fact]
    public void Merge_preserves_external_uri_link_annotations()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("LINK_SOURCE", 20, addLink: true);
        var second = fixture.CreatePdf("SECOND_SOURCE", 20);
        var output = fixture.PathFor("links.pdf");
        var expectedUri = "https://example.com/pdf-merger?fixture=link";

        var sourceUris = ReadUriAnnotations(source);
        var result = new PdfMergeService().Merge(new[] { source, second }, output);
        var outputUris = ReadUriAnnotations(output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(expectedUri, sourceUris);
        Assert.Contains(expectedUri, outputUris);
    }

    [Fact]
    public void Merge_shifts_internal_goto_destination_by_page_offset()
    {
        using var fixture = new TempFixture();
        var first = fixture.CreatePdf("FIRST_SOURCE", 20);
        var second = fixture.CreateInternalLinkPdf("SECOND_SOURCE");
        var output = fixture.PathFor("internal-link.pdf");

        var sourceTargets = ReadGoToPageNumbers(second);
        var result = new PdfMergeService().Merge(new[] { first, second }, output);
        var outputTargets = ReadGoToPageNumbers(output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(20, sourceTargets);
        Assert.Contains(40, outputTargets);
    }

    [Fact]
    public void Merge_shifts_bookmark_page_offset()
    {
        using var fixture = new TempFixture();
        var first = fixture.CreatePdf("FIRST_BOOKMARK_SOURCE", 20);
        var second = fixture.CreateBookmarkedPdf("SECOND_BOOKMARK_SOURCE", 20);
        var output = fixture.PathFor("bookmarks.pdf");

        var result = new PdfMergeService().Merge(new[] { first, second }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var reader = new PdfReader(output);
        var bookmarks = SimpleBookmark.GetBookmark(reader);
        Assert.NotNull(bookmarks);
        Assert.Contains(bookmarks!, bookmark =>
            bookmark.TryGetValue("Title", out var title) &&
            string.Equals(title?.ToString(), "SECOND TARGET", StringComparison.Ordinal) &&
            bookmark.TryGetValue("Page", out var page) &&
            string.Equals(page?.ToString()?.Split(' ')[0], "40", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_preserves_page_rotation_and_size()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreateRotatedLetterPdf("ROTATED_SOURCE", 20);
        var output = fixture.PathFor("geometry.pdf");

        var result = new PdfMergeService().Merge(new[] { source }, output);

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var reader = new PdfReader(output);
        Assert.Equal(20, reader.NumberOfPages);
        Assert.Equal(90, reader.GetPageRotation(1));
        var pageSize = reader.GetPageSize(1);
        Assert.Equal(PageSize.LETTER.Width, pageSize.Width);
        Assert.Equal(PageSize.LETTER.Height, pageSize.Height);
    }

    [Fact]
    public void Split_creates_multiple_parts_with_expected_ranges_and_content()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("SPLIT_SOURCE", 40);
        var outputBase = fixture.PathFor("split-output", "guide.pdf");
        var before = File.ReadAllBytes(source);

        var result = new PdfSplitService().Split(source, new[] { 7, 14, 21, 28, 34 }, outputBase);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(6, result.Parts.Count);
        Assert.Equal(new[] { 7, 14, 21, 28, 34 }, result.SplitAfterPages);
        Assert.Equal(new[] { 7, 7, 7, 7, 6, 6 }, result.Parts.Select(part => part.PageCount));
        Assert.All(result.Parts, part => Assert.True(File.Exists(part.OutputPath)));
        Assert.Equal(before, File.ReadAllBytes(source));

        foreach (var part in result.Parts)
        {
            using var reader = new PdfReader(part.OutputPath);
            Assert.Equal(part.PageCount, reader.NumberOfPages);
            var firstPageText = PdfTextExtractor.GetTextFromPage(reader, 1);
            Assert.Contains("SPLIT_SOURCE", firstPageText);
            Assert.Contains("deterministic multi-page fixture", firstPageText);
        }

        using var finalPartReader = new PdfReader(result.Parts[^1].OutputPath);
        Assert.Contains("SPLIT_SOURCE PAGE 35", PdfTextExtractor.GetTextFromPage(finalPartReader, 1));
        Assert.Contains("SPLIT_SOURCE PAGE 40", PdfTextExtractor.GetTextFromPage(finalPartReader, 6));
    }

    [Fact]
    public void Split_without_split_points_writes_the_complete_pdf_as_one_part()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("WHOLE_SOURCE", 20);
        var outputBase = fixture.PathFor("whole.pdf");

        var result = new PdfSplitService().Split(source, Array.Empty<int>(), outputBase);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var part = Assert.Single(result.Parts);
        Assert.Equal(1, part.FirstPage);
        Assert.Equal(20, part.LastPage);
        using var reader = new PdfReader(part.OutputPath);
        Assert.Equal(20, reader.NumberOfPages);
        Assert.Contains("WHOLE_SOURCE PAGE 20", PdfTextExtractor.GetTextFromPage(reader, 20));
    }

    [Fact]
    public void Split_uses_edited_part_names_as_exact_output_file_names()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("NAMED_SPLIT", 20);
        var outputBase = fixture.PathFor("ignored-base-name.pdf");

        var result = new PdfSplitService().Split(source, new[] { 6 }, outputBase, new[] { "AAA", "BBB" });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new[] { "AAA.pdf", "BBB.pdf" },
            result.Parts.Select(part => System.IO.Path.GetFileName(part.OutputPath)));
    }

    [Fact]
    public void Split_rejects_invalid_split_point_without_creating_outputs()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("INVALID_SPLIT", 20);
        var outputBase = fixture.PathFor("invalid.pdf");

        var result = new PdfSplitService().Split(source, new[] { 0, 3 }, outputBase);

        Assert.False(result.Succeeded);
        Assert.Contains("between page 1 and page 19", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "invalid_part_*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public void Split_rejects_output_base_equal_to_source()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreatePdf("KEEP_SPLIT_SOURCE", 20);
        var before = File.ReadAllBytes(source);

        var result = new PdfSplitService().Split(source, new[] { 2 }, source);

        Assert.False(result.Succeeded);
        Assert.Contains("same as the source", result.ErrorMessage);
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public void Unlock_removes_known_password_from_multiple_pdfs_without_modifying_sources()
    {
        using var fixture = new TempFixture();
        var first = fixture.CreateEncryptedPdf("LOCKED_FIRST", 20, "known-password");
        var second = fixture.CreateEncryptedPdf("LOCKED_SECOND", 21, "known-password");
        var firstBefore = File.ReadAllBytes(first);
        var secondBefore = File.ReadAllBytes(second);
        var outputDirectory = fixture.PathFor("unlocked");
        using (var passwordReader = new PdfReader(
                   first, System.Text.Encoding.UTF8.GetBytes("known-password")))
        {
            Assert.True(passwordReader.IsEncrypted());
        }

        var result = new PdfUnlockService().Unlock(
            new[] { first, second }, "known-password", outputDirectory);

        Assert.True(result.Succeeded,
            $"{result.ErrorMessage} {string.Join("; ", result.Failures.Select(failure => failure.Message))}");
        Assert.Equal(2, result.Outputs.Count);
        Assert.Empty(result.Failures);
        Assert.Equal(firstBefore, File.ReadAllBytes(first));
        Assert.Equal(secondBefore, File.ReadAllBytes(second));
        Assert.Equal(new[] { 20, 21 }, result.Outputs.Select(output => output.PageCount));
        foreach (var output in result.Outputs)
        {
            using var reader = new PdfReader(output.OutputPath);
            Assert.False(reader.IsEncrypted());
            Assert.Equal(output.PageCount, reader.NumberOfPages);
            Assert.Contains("LOCKED_", PdfTextExtractor.GetTextFromPage(reader, 1));
        }
    }

    [Fact]
    public void Unlock_wrong_password_creates_no_output_and_preserves_source()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreateEncryptedPdf("LOCKED_KEEP", 20, "correct-password");
        var before = File.ReadAllBytes(source);
        var outputDirectory = fixture.PathFor("wrong-password");

        var result = new PdfUnlockService().Unlock(
            new[] { source }, "wrong-password", outputDirectory);

        Assert.False(result.Succeeded);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("incorrect", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Empty(Directory.GetFiles(outputDirectory, "*.pdf", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Unlock_does_not_overwrite_an_existing_output()
    {
        using var fixture = new TempFixture();
        var source = fixture.CreateEncryptedPdf("LOCKED_EXISTING", 20, "known-password");
        var outputDirectory = fixture.PathFor("existing-output");
        Directory.CreateDirectory(outputDirectory);
        var existing = IoPath.Combine(outputDirectory, "LOCKED_EXISTING_unlocked.pdf");
        File.WriteAllText(existing, "keep existing output");

        var result = new PdfUnlockService().Unlock(
            new[] { source }, "known-password", outputDirectory);

        Assert.True(result.Succeeded,
            $"{result.ErrorMessage} {string.Join("; ", result.Failures.Select(failure => failure.Message))}");
        Assert.Equal("keep existing output", File.ReadAllText(existing));
        Assert.Equal("LOCKED_EXISTING_unlocked_2.pdf", IoPath.GetFileName(result.Outputs[0].OutputPath));
    }

    private static IReadOnlyList<string> ReadUriAnnotations(string path)
    {
        using var reader = new PdfReader(path);
        var uris = new List<string>();
        for (var pageNumber = 1; pageNumber <= reader.NumberOfPages; pageNumber++)
        {
            var page = reader.GetPageN(pageNumber);
            var annotations = page.GetAsArray(PdfName.ANNOTS);
            if (annotations is null)
            {
                continue;
            }

            foreach (var reference in annotations.ArrayList)
            {
                var annotation = PdfReader.GetPdfObject(reference) as PdfDictionary;
                var action = annotation is null
                    ? null
                    : PdfReader.GetPdfObject(annotation.Get(PdfName.A)) as PdfDictionary;
                if (action?.GetAsName(PdfName.S) == PdfName.URI)
                {
                    var uri = action.GetAsString(PdfName.URI)?.ToUnicodeString();
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        uris.Add(uri);
                    }
                }
            }
        }

        return uris;
    }

    private static IReadOnlyList<int> ReadGoToPageNumbers(string path)
    {
        using var reader = new PdfReader(path);
        reader.ConsolidateNamedDestinations();
        var targetPages = new List<int>();

        for (var pageNumber = 1; pageNumber <= reader.NumberOfPages; pageNumber++)
        {
            var annotations = reader.GetPageN(pageNumber).GetAsArray(PdfName.ANNOTS);
            if (annotations is null)
            {
                continue;
            }

            foreach (var reference in annotations.ArrayList)
            {
                var annotation = PdfReader.GetPdfObject(reference) as PdfDictionary;
                var action = annotation is null
                    ? null
                    : PdfReader.GetPdfObject(annotation.Get(PdfName.A)) as PdfDictionary;
                if (action is not null && action.GetAsName(PdfName.S) != PdfName.GOTO)
                {
                    continue;
                }

                var destinationObject = action is not null
                    ? action.Get(PdfName.D)
                    : annotation?.Get(PdfName.D);
                var destination = PdfReader.GetPdfObject(destinationObject) as PdfArray;
                var targetReference = destination?.GetAsIndirectObject(0);
                if (targetReference is null)
                {
                    continue;
                }

                for (var targetPage = 1; targetPage <= reader.NumberOfPages; targetPage++)
                {
                    var pageReference = reader.GetPageOrigRef(targetPage);
                    if (pageReference?.Number == targetReference.Number)
                    {
                        targetPages.Add(targetPage);
                        break;
                    }
                }
            }
        }

        return targetPages;
    }

    private sealed class FakeWordConverter : IWordToPdfConverter
    {
        public string? Convert(string sourcePath) => null;
    }

    private sealed class TempFixture : IDisposable
    {
        private readonly string directory = IoPath.Combine(
            IoPath.GetTempPath(), "PdfMergerTests", Guid.NewGuid().ToString("N"));

        public TempFixture() => Directory.CreateDirectory(directory);

        public string DirectoryPath => directory;

        public string PathFor(params string[] parts) => IoPath.Combine(new[] { directory }.Concat(parts).ToArray());

        public string CreatePdf(
            string text,
            int pages,
            bool addMetadata = false,
            bool addLink = false)
        {
            var path = PathFor($"{text}.pdf");
            using var stream = File.Create(path);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            if (addMetadata)
            {
                document.AddTitle("PRIVATE_TITLE");
                document.AddAuthor("PRIVATE_AUTHOR");
                document.AddSubject("PRIVATE_SUBJECT");
            }
            for (var page = 0; page < pages; page++)
            {
                document.Add(new Paragraph($"{text} PAGE {page + 1}"));
                document.Add(new Paragraph(
                    "This is a deterministic multi-page fixture with enough visible content " +
                    "to exercise page browsing, rendering, ordering, and split boundaries."));
                document.Add(new Paragraph(
                    "Section summary: scope, acceptance criteria, validation notes, and a stable " +
                    "page marker for parser and visual checks."));
                var table = new PdfPTable(3) { WidthPercentage = 92 };
                table.AddCell("Item");
                table.AddCell("Status");
                table.AddCell("Page");
                table.AddCell("Content retention");
                table.AddCell("Expected");
                table.AddCell((page + 1).ToString());
                table.AddCell("Source integrity");
                table.AddCell("Unchanged");
                table.AddCell(text);
                document.Add(table);
                if (addLink && page == 0)
                {
                    var link = new Chunk("OPEN_LINK");
                    link.SetAction(new PdfAction("https://example.com/pdf-merger?fixture=link"));
                    document.Add(new Paragraph(link));
                }
                if (page < pages - 1)
                {
                    document.NewPage();
                }
            }

            document.Close();
            writer.Close();
            return path;
        }

        public string CreateEncryptedPdf(string text, int pages, string password)
        {
            var path = PathFor($"{text}.pdf");
            using var stream = File.Create(path);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            writer.SetEncryption(
                System.Text.Encoding.UTF8.GetBytes(password),
                System.Text.Encoding.UTF8.GetBytes($"owner-{password}"),
                PdfWriter.ALLOW_PRINTING,
                PdfWriter.ENCRYPTION_AES_128);
            document.Open();
            for (var page = 1; page <= pages; page++)
            {
                document.Add(new Paragraph($"{text} PAGE {page}"));
                document.Add(new Paragraph(
                    "Encrypted deterministic fixture with visible content for password-removal verification."));
                if (page < pages)
                {
                    document.NewPage();
                }
            }
            document.Close();
            writer.Close();
            return path;
        }

        public string CreateInternalLinkPdf(string text)
        {
            var path = PathFor($"{text}.pdf");
            using var stream = File.Create(path);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            document.Add(new Paragraph($"{text} LINK_PAGE 1"));
            var link = new Chunk("GO_TO_TARGET");
            link.SetLocalGoto("TARGET");
            document.Add(new Paragraph(link));
            for (var page = 2; page <= 20; page++)
            {
                document.NewPage();
                if (page == 20)
                {
                    writer.DirectContent.LocalDestination("TARGET", new PdfDestination(PdfDestination.FIT));
                }
                document.Add(new Paragraph($"{text} PAGE {page}"));
                document.Add(new Paragraph("This page provides realistic multi-page content for navigation and split verification."));
            }
            document.Close();
            writer.Close();
            return path;
        }

        public string CreateBookmarkedPdf(string text, int pages)
        {
            var path = PathFor($"{text}.pdf");
            using var stream = File.Create(path);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            for (var page = 0; page < pages; page++)
            {
                document.Add(new Paragraph($"{text} PAGE {page + 1}"));
                if (page < pages - 1)
                {
                    document.NewPage();
                }
            }

            var outline = new PdfOutline(
                writer.RootOutline,
                new PdfDestination(PdfDestination.FIT),
                "SECOND TARGET");
            outline.SetDestinationPage(writer.GetPageReference(pages));
            document.Close();
            writer.Close();
            return path;
        }

        public string CreateRotatedLetterPdf(string text, int pages)
        {
            var path = PathFor($"{text}.pdf");
            using var stream = File.Create(path);
            var document = new Document(PageSize.LETTER.Rotate());
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            for (var page = 1; page <= pages; page++)
            {
                document.Add(new Paragraph($"{text} PAGE {page}"));
                document.Add(new Paragraph("Rotated geometry fixture with readable content across twenty pages."));
                if (page < pages)
                {
                    document.NewPage();
                }
            }
            document.Close();
            writer.Close();
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }
}
