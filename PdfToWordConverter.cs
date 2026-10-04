using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using iTextSharp.text.pdf;
using iTextSharp.text.exceptions;

namespace Pdf_Merger;

public sealed class PdfToWordConverter
{
    private readonly Action<string, string> convertDocument;

    public PdfToWordConverter() : this(ConvertWithWord) { }
    internal PdfToWordConverter(Action<string, string> convert) => convertDocument = convert;

    public void Convert(string sourcePath, string outputPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var output = Path.GetFullPath(outputPath);
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output must not overwrite the source PDF.");
        if (!string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(output), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PDF source and a DOCX output file.");
        if (!File.Exists(source)) throw new FileNotFoundException("The source PDF could not be found.", source);
        try
        {
            using var reader = new PdfReader(source);
            if (reader.IsEncrypted()) throw new InvalidDataException("Unlock the encrypted PDF before converting it to Word.");
            if (reader.NumberOfPages == 0) throw new InvalidDataException("The PDF contains no pages.");
        }
        catch (BadPasswordException exception)
        {
            throw new InvalidDataException("Unlock the encrypted PDF before converting it to Word.", exception);
        }

        var directory = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The output folder does not exist.");
        var temporary = Path.Combine(directory, $".pdfforge-{Guid.NewGuid():N}.docx");
        try
        {
            // Reserve the sibling file first, so unwritable folders fail before starting Word.
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) { }
            convertDocument(source, temporary);
            ValidateDocx(temporary);
            if (File.Exists(output)) File.Replace(temporary, output, null);
            else File.Move(temporary, output);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) { Logger.LogError("Removing the temporary DOCX failed.", exception); }
        }
    }

    private static void ValidateDocx(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidDataException("Word did not produce a DOCX file.");
        using var archive = ZipFile.OpenRead(path);
        XDocument ReadPart(string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Word produced an incomplete DOCX file.");
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        var document = ReadPart("word/document.xml");
        var contentTypes = ReadPart("[Content_Types].xml");
        var relationships = ReadPart("_rels/.rels");
        if (document.Root?.Name != word + "document" || document.Root.Element(word + "body") is null ||
            !contentTypes.Descendants(types + "Override").Any(part =>
                (string?)part.Attribute("PartName") == "/word/document.xml" &&
                (string?)part.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") ||
            !relationships.Descendants(rels + "Relationship").Any(part =>
                (string?)part.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" &&
                ((string?)part.Attribute("Target"))?.TrimStart('/') == "word/document.xml" &&
                (string?)part.Attribute("TargetMode") != "External"))
            throw new InvalidDataException("Word produced an invalid DOCX document.");
    }

    private static void ConvertWithWord(string source, string output)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Word conversion must run on an STA thread.");
        var wordType = Type.GetTypeFromProgID("Word.Application") ??
            throw new InvalidOperationException("Microsoft Word desktop is required. Install Word before converting PDFs.");
        object? application = null;
        object? documents = null;
        object? document = null;
        var saved = false;
        try
        {
            dynamic word = Activator.CreateInstance(wordType)!;
            application = word;
            word.Visible = false;
            word.ScreenUpdating = false;
            word.DisplayAlerts = 0;
            word.AutomationSecurity = 3; // Disable macros in this automation instance.
            documents = word.Documents;
            document = ((dynamic)documents).Open(source, ConfirmConversions: false,
                ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
            ((dynamic)document).SaveAs2(output, FileFormat: 16, AddToRecentFiles: false);
            saved = true;
        }
        catch (COMException exception)
        {
            throw new IOException("Microsoft Word could not convert this PDF. Check that Word is usable and the PDF is supported.", exception);
        }
        finally
        {
            Exception? cleanupFailure = null;
            try { if (document is not null) ((dynamic)document).Close(SaveChanges: false); }
            catch (Exception exception) { cleanupFailure = exception; Logger.LogError("Closing the PDF conversion document failed.", exception); }
            try { if (application is not null) ((dynamic)application).Quit(SaveChanges: false); }
            catch (Exception exception) { cleanupFailure = exception; Logger.LogError("Closing the PDF conversion Word instance failed.", exception); }
            foreach (var value in new[] { document, documents, application })
            {
                try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
                catch (Exception exception) { Logger.LogError("Releasing a Word conversion COM object failed.", exception); }
            }
            if (saved && cleanupFailure is not null)
                throw new IOException("Word saved a temporary document but could not close cleanly. The output was not replaced.", cleanupFailure);
        }
    }
}
