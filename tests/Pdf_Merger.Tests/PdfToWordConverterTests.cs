using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace Pdf_Merger.Tests;

public sealed class PdfToWordConverterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PdfForgeDocxTests", Guid.NewGuid().ToString("N"));
    private readonly string source;
    private readonly string output;

    public PdfToWordConverterTests()
    {
        Directory.CreateDirectory(directory);
        source = Path.Combine(directory, "source.pdf");
        output = Path.Combine(directory, "output.docx");
        using var stream = File.Create(source);
        using var document = new iTextSharp.text.Document();
        iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
        document.Open();
        for (var page = 1; page <= 20; page++)
        {
            document.NewPage();
            document.Add(new iTextSharp.text.Paragraph($"DOCX-{page:000} Transaction validation"));
            document.Add(new iTextSharp.text.Paragraph("This generated PDF tests source protection during conversion. Each page contains a unique identifier and a paragraph describing the intended output transaction."));
            var table = new iTextSharp.text.pdf.PdfPTable(2);
            table.AddCell("Page identity"); table.AddCell($"DOCX-{page:000}");
            table.AddCell("Expected behavior"); table.AddCell("The source remains unchanged.");
            document.Add(table);
        }
    }

    [Fact]
    public void ValidDocxReplacesOutputAndPreservesSource()
    {
        var before = SHA256.HashData(File.ReadAllBytes(source));
        File.WriteAllText(output, "previous output");
        new PdfToWordConverter((_, temporary) => WriteDocx(temporary)).Convert(source, output);
        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("word/document.xml"));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationNeverCommitsAndRemovesTemporary(bool beforeStart)
    {
        using var cancellation = new CancellationTokenSource();
        File.WriteAllText(output, "previous output");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var called = false;
        if (beforeStart) cancellation.Cancel();
        var converter = new PdfToWordConverter((_, temporary) =>
        {
            called = true;
            WriteDocx(temporary);
            cancellation.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() => converter.Convert(source, output, cancellation.Token));
        Assert.Equal(!beforeStart, called);
        Assert.Equal("previous output", File.ReadAllText(output));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("invalid-zip")]
    [InlineData("missing-document")]
    [InlineData("invalid-xml")]
    [InlineData("wrong-root")]
    [InlineData("wrong-content-type")]
    public void FailedConversionPreservesExistingOutputAndCleansTemporaryFile(string failure)
    {
        File.WriteAllText(output, "previous output");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var converter = new PdfToWordConverter((_, temporary) =>
        {
            if (failure == "missing") return;
            if (failure == "exception") { File.WriteAllText(temporary, "partial"); throw new IOException("Word failed"); }
            if (failure == "empty") { File.WriteAllBytes(temporary, Array.Empty<byte>()); return; }
            if (failure == "invalid-zip") { File.WriteAllText(temporary, "not a DOCX"); return; }
            WriteDocx(temporary, failure);
        });
        Assert.ThrowsAny<Exception>(() => converter.Convert(source, output));
        Assert.Equal("previous output", File.ReadAllText(output));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData("same-source")]
    [InlineData("wrong-extension")]
    [InlineData("missing-source")]
    [InlineData("corrupt-source")]
    public void InvalidPathsOrPdfAreRejectedBeforeWordRuns(string failure)
    {
        var destination = failure == "same-source" ? source : failure == "wrong-extension" ? Path.Combine(directory, "output.pdf") : output;
        var input = failure == "missing-source" ? Path.Combine(directory, "missing.pdf") : source;
        if (failure == "corrupt-source") File.WriteAllText(source, "not a PDF");
        var before = File.ReadAllBytes(source);
        var called = false;
        var converter = new PdfToWordConverter((_, _) => called = true);
        Assert.ThrowsAny<Exception>(() => converter.Convert(input, destination));
        Assert.False(called);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedConversionDoesNotCreateFinalOutput()
    {
        var converter = new PdfToWordConverter((_, temporary) => File.WriteAllText(temporary, "partial"));
        Assert.ThrowsAny<Exception>(() => converter.Convert(source, output));
        Assert.False(File.Exists(output));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void EncryptedPdfIsRejectedWithoutChangingExistingOutput()
    {
        var encrypted = Path.Combine(directory, "encrypted.pdf");
        using (var reader = new iTextSharp.text.pdf.PdfReader(source))
        using (var stream = File.Create(encrypted))
            iTextSharp.text.pdf.PdfEncryptor.Encrypt(reader, stream, true, "test-user", "test-owner", iTextSharp.text.pdf.PdfWriter.ALLOW_PRINTING);
        File.WriteAllText(output, "previous output");
        var called = false;
        var converter = new PdfToWordConverter((_, _) => called = true);
        Assert.Throws<InvalidDataException>(() => converter.Convert(encrypted, output));
        Assert.False(called);
        Assert.Equal("previous output", File.ReadAllText(output));
        Assert.Equal(3, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void LockedExistingOutputIsPreservedAndTemporaryFileRemoved()
    {
        File.WriteAllText(output, "previous output");
        using (var locked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => new PdfToWordConverter((_, temporary) => WriteDocx(temporary)).Convert(source, output));
        Assert.Equal("previous output", File.ReadAllText(output));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    private static void WriteDocx(string path, string failure = "")
    {
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
        Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"" +
            (failure == "wrong-content-type" ? "text/plain" : "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") + "\"/></Types>");
        Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        if (failure != "missing-document") Add("word/document.xml", failure == "invalid-xml" ? "<broken" : failure == "wrong-root" ? "<wrong/>" :
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Editable text</w:t></w:r></w:p></w:body></w:document>");
    }

    public void Dispose() => Directory.Delete(directory, true);
}
