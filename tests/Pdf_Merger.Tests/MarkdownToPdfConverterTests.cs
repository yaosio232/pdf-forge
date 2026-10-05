using Xunit;

namespace Pdf_Merger.Tests;

public class MarkdownToPdfConverterTests
{
    [Fact]
    public void LocalBrowserConvertsRichTwentySectionMarkdownAndPreservesSource()
    {
        var evidence = Environment.GetEnvironmentVariable("PDF_FORGE_BROWSER_TEST_DIRECTORY");
        var folder = evidence ?? Path.Combine(Path.GetTempPath(), "PdfForgeBrowserTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "rich-markdown.md"); var output = Path.Combine(folder, "rich-markdown.pdf");
        var content = new System.Text.StringBuilder();
        for (var section = 1; section <= 20; section++)
        {
            content.AppendLine($"# MD-{section:000} — 繁體中文與 English conversion");
            for (var paragraph = 1; paragraph <= 12; paragraph++)
                content.AppendLine($"\nMD-{section:000}-P{paragraph:00} " + string.Concat(Enumerable.Repeat("這是繁體中文測試段落，確認內容順序與文字可讀性。 This substantial paragraph tests offline Markdown pagination, editable text extraction, headings, and consistent content order. ", 5)));
            content.AppendLine($"\n| Page ID | Value |\n|---|---|\n| MD-{section:000} | Table content |\n\n```text\nMD-{section:000} code block\n```\n");
        }
        File.WriteAllText(source, content.ToString());
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
        try
        {
            new MarkdownToPdfConverter().Convert(source, output, CancellationToken.None);
            using var reader = new iTextSharp.text.pdf.PdfReader(output);
            Assert.True(reader.NumberOfPages >= 20, $"Expected >=20 substantive pages, found {reader.NumberOfPages}.");
            var text = string.Join("\n", Enumerable.Range(1, reader.NumberOfPages).Select(page => iTextSharp.text.pdf.parser.PdfTextExtractor.GetTextFromPage(reader, page)));
            var previous = -1;
            for (var section = 1; section <= 20; section++)
            {
                var position = text.IndexOf($"MD-{section:000}", StringComparison.Ordinal);
                Assert.True(position > previous, $"Missing or misordered MD-{section:000}."); previous = position;
            }
            Assert.Contains("繁體中文", text); Assert.Contains("Table content", text); Assert.Contains("code block", text);
            Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
            Assert.Empty(Directory.GetFiles(folder, ".pdfforge-*"));
            if (evidence is not null) File.WriteAllText(Path.Combine(folder, "browser-result.json"), System.Text.Json.JsonSerializer.Serialize(new { pages = reader.NumberOfPages, sourceSha256 = Convert.ToHexString(before), outputSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(output))), ids = 20, traditionalChinese = true, tables = true, codeBlocks = true, mode = "service/browser integration, not WinForms UI" }));
        }
        finally { if (evidence is null) { File.Delete(source); File.Delete(output); Directory.Delete(folder); } }
    }
    [Fact]
    public void HtmlContainsMarkdownFormattingAndOfflinePolicy()
    {
        var html = MarkdownToPdfConverter.RenderHtml("# 繁中 Title\n\n|ID|Value|\n|-|-|\n|001|English|\n\n```cs\nvar x = 1;\n```\n\n<script>alert(1)</script>\n\n![remote](https://example.invalid/picture.png)");
        Assert.Contains("<h1>", html); Assert.Contains("繁中", html);
        Assert.Contains("<table>", html); Assert.Contains("<code", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains("img-src 'none'", html);
    }

    [Fact]
    public void BatchRejectsCollidingNamesBeforeCreatingOutputs()
    {
        Assert.Throws<ArgumentException>(() => MarkdownToPdfConverter.GetOutputPaths(new[] { @"C:\first\same.md", @"C:\second\same.markdown" }, @"C:\output"));
    }

    [Fact]
    public void FailureAndCancellationPreservePreviousPdfAndCleanTemporary()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PdfForgeMarkdownTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.md"); var output = Path.Combine(folder, "output.pdf");
        File.WriteAllText(source, "# Source\n\nA meaningful paragraph.");
        File.WriteAllText(output, "previous output");
        try
        {
            var converter = new MarkdownToPdfConverter((_, path, _) => File.WriteAllText(path, "invalid PDF"));
            Assert.ThrowsAny<Exception>(() => converter.Convert(source, output, CancellationToken.None));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => converter.Convert(source, output, cancellation.Token));
            Assert.Equal("previous output", File.ReadAllText(output));
            Assert.Equal("# Source\n\nA meaningful paragraph.", File.ReadAllText(source));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }
        finally { File.Delete(source); File.Delete(output); Directory.Delete(folder); }
    }
}
