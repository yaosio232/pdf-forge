using Xunit;

namespace Pdf_Merger.Tests;

public sealed class WordToPdfSecurityTests
{
    [Fact]
    public void Security_is_set_before_opening_document()
    {
        var word = new FakeWord();
        WordToPdfConverter.ConvertWithApplication(word, "input.docx", "output.pdf");
        Assert.Equal(3, word.AutomationSecurity);
        Assert.Equal(1, word.Documents.OpenCount);
    }

    [Fact]
    public void Security_failure_does_not_open_document()
    {
        var word = new FakeWord { RejectSecurity = true };
        Assert.Throws<InvalidOperationException>(() =>
            WordToPdfConverter.ConvertWithApplication(word, "input.docx", "output.pdf"));
        Assert.Equal(0, word.Documents.OpenCount);
    }

    public sealed class FakeWord
    {
        public bool RejectSecurity { get; init; }
        private int security;
        public int AutomationSecurity
        {
            get => security;
            set { if (RejectSecurity) throw new InvalidOperationException("Blocked by policy"); security = value; }
        }
        public FakeDocuments Documents { get; }
        public FakeWord() => Documents = new FakeDocuments(this);
        public sealed class FakeDocuments
        {
            private readonly FakeWord owner;
            public int OpenCount { get; private set; }
            public FakeDocuments(FakeWord owner) => this.owner = owner;
            public FakeDocument Open(string sourcePath, bool ReadOnly, bool AddToRecentFiles, bool Visible)
            {
                Assert.Equal(3, owner.AutomationSecurity);
                OpenCount++;
                return new FakeDocument();
            }
        }
        public sealed class FakeDocument
        {
            public void ExportAsFixedFormat(string outputPath, int PdfFormat, bool OpenAfterExport,
                int OptimizeFor, bool CreateBookmarks, bool DocStructureTags, bool BitmapMissingFonts,
                bool UseISO19005_1) { }
            public void Close(bool SaveChanges) { }
        }
    }
}
