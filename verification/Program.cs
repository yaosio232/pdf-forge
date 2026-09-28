using System.Security.Cryptography;
using System.Text.Json;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Pdf_Merger;

if (args.Length >= 2 && string.Equals(args[0], "--fixtures-only", StringComparison.OrdinalIgnoreCase))
{
    var fixtureDirectory = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(fixtureDirectory);
    var splitFixture = CreatePdf(
        Path.Combine(fixtureDirectory, "pdf-forge-split-40-pages.pdf"),
        "PDF_FORGE_SPLIT", 40);
    var encryptedFirst = CreateEncryptedPdf(
        Path.Combine(fixtureDirectory, "pdf-forge-locked-a.pdf"),
        "PDF_FORGE_LOCKED_A", 20, "PDF-Forge-Test-2026");
    var encryptedSecond = CreateEncryptedPdf(
        Path.Combine(fixtureDirectory, "pdf-forge-locked-b.pdf"),
        "PDF_FORGE_LOCKED_B", 21, "PDF-Forge-Test-2026");
    var manifest = new
    {
        generatedUtc = DateTime.UtcNow,
        password = "PDF-Forge-Test-2026",
        files = new[] { splitFixture, encryptedFirst, encryptedSecond }
            .Select(path => new { path, bytes = new FileInfo(path).Length, sha256 = Sha256(path) })
    };
    File.WriteAllText(
        Path.Combine(fixtureDirectory, "fixture-manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Environment.CurrentDirectory, "verification-artifacts", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
Directory.CreateDirectory(outputDirectory);

var sourceDirectory = Path.Combine(outputDirectory, "sources");
Directory.CreateDirectory(sourceDirectory);
var first = CreatePdf(Path.Combine(sourceDirectory, "first.pdf"), "VERIFY_FIRST", 20);
var second = CreatePdf(Path.Combine(sourceDirectory, "second.pdf"), "VERIFY_SECOND", 21);
var splitSource = CreatePdf(Path.Combine(sourceDirectory, "split-source-40-pages.pdf"), "VERIFY_SPLIT", 40);
var sourceHashesBefore = new Dictionary<string, string>
{
    [first] = Sha256(first),
    [second] = Sha256(second)
};
var splitSourceHashBefore = Sha256(splitSource);
var output = Path.Combine(outputDirectory, "merged.pdf");

var result = new PdfMergeService().Merge(new[] { first, second }, output);
if (!result.Succeeded)
{
    throw new InvalidOperationException(result.ErrorMessage ?? "Verification merge failed.");
}

using var reader = new PdfReader(output);
var splitBase = Path.Combine(outputDirectory, "split-output.pdf");
var splitResult = new PdfSplitService().Split(splitSource, new[] { 7, 14, 21, 28, 34 }, splitBase);
if (!splitResult.Succeeded)
{
    throw new InvalidOperationException(splitResult.ErrorMessage ?? "Verification split failed.");
}
var splitSourceHashAfter = Sha256(splitSource);
var previewDirectory = Path.Combine(outputDirectory, "preview-pages");
var renderedPreviewPaths = PdfPageRenderer.RenderPages(splitSource, previewDirectory, 40, 1400);
if (renderedPreviewPaths.Count > 0 && renderedPreviewPaths.Count != 40)
{
    throw new InvalidOperationException("Verification preview renderer did not produce all 40 pages.");
}
if (splitResult.Parts.Count != 6 ||
    !new[] { 7, 7, 7, 7, 6, 6 }.SequenceEqual(splitResult.Parts.Select(part => part.PageCount)) ||
    splitSourceHashBefore != splitSourceHashAfter)
{
    throw new InvalidOperationException("Verification split page ranges or source integrity check failed.");
}
var sourceHashesAfter = new Dictionary<string, string>
{
    [first] = Sha256(first),
    [second] = Sha256(second)
};
var report = new
{
    runId = Path.GetFileName(outputDirectory),
    generatedUtc = DateTime.UtcNow,
    outputPath = output,
    outputSha256 = Sha256(output),
    outputBytes = new FileInfo(output).Length,
    pageCount = reader.NumberOfPages,
    splitSourcePath = splitSource,
    splitOutputCount = splitResult.Parts.Count,
    splitOutputPageCounts = splitResult.Parts.Select(part => part.PageCount).ToArray(),
    splitSourceUnchanged = splitSourceHashBefore == splitSourceHashAfter,
    renderedPreviewCount = renderedPreviewPaths.Count,
    sourceHashesBefore,
    sourceHashesAfter,
    sourcesUnchanged = sourceHashesBefore.All(pair =>
        sourceHashesAfter.TryGetValue(pair.Key, out var after) && after == pair.Value)
};
File.WriteAllText(
    Path.Combine(outputDirectory, "verification.json"),
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static string CreatePdf(string path, string label, int pages)
{
    using var stream = File.Create(path);
    using var document = new Document(PageSize.A4);
    var writer = PdfWriter.GetInstance(document, stream);
    document.AddTitle("VERIFICATION_PRIVATE_TITLE");
    document.Open();
    for (var page = 1; page <= pages; page++)
    {
        document.Add(new Paragraph($"{label} PAGE {page}"));
        document.Add(new Paragraph(
            "This is a deterministic multi-page verification fixture with complete visible " +
            "content for rendering, browsing, ordering, and split-boundary checks."));
        document.Add(new Paragraph(
            "Validation notes: page identity, source integrity, output ranges, and parser readability."));
        var table = new PdfPTable(3) { WidthPercentage = 92 };
        table.AddCell("Check");
        table.AddCell("Expected");
        table.AddCell("Page");
        table.AddCell("Content retained");
        table.AddCell("Yes");
        table.AddCell(page.ToString());
        table.AddCell("Source unchanged");
        table.AddCell("Yes");
        table.AddCell(label);
        document.Add(table);
        if (page < pages)
        {
            document.NewPage();
        }
    }
    document.Close();
    writer.Close();
    return path;
}

static string CreateEncryptedPdf(string path, string label, int pages, string password)
{
    using var stream = File.Create(path);
    using var document = new Document(PageSize.A4);
    var writer = PdfWriter.GetInstance(document, stream);
    writer.SetEncryption(
        System.Text.Encoding.UTF8.GetBytes(password),
        System.Text.Encoding.UTF8.GetBytes($"owner-{password}"),
        PdfWriter.ALLOW_PRINTING,
        PdfWriter.ENCRYPTION_AES_128);
    document.Open();
    for (var page = 1; page <= pages; page++)
    {
        document.Add(new Paragraph($"{label} PAGE {page}"));
        document.Add(new Paragraph(
            "This encrypted fixture has deterministic multi-page content for batch password removal."));
        document.Add(new Paragraph(
            "Validation notes: the source hash must remain unchanged and the output must open without a password."));
        var table = new PdfPTable(3) { WidthPercentage = 92 };
        table.AddCell("Fixture");
        table.AddCell("Expected");
        table.AddCell("Page");
        table.AddCell(label);
        table.AddCell("Unlocked copy");
        table.AddCell(page.ToString());
        document.Add(table);
        if (page < pages)
        {
            document.NewPage();
        }
    }
    document.Close();
    writer.Close();
    return path;
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    using var algorithm = SHA256.Create();
    return Convert.ToHexString(algorithm.ComputeHash(stream));
}
