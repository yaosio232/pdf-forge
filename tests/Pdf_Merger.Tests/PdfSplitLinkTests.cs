using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Security.Cryptography;
using Xunit;

namespace Pdf_Merger.Tests;

// Service-output regression is authorized for this run by the 2026-09-30 exception.
[Collection("PDF services")]
public sealed class PdfSplitLinkTests
{
    private static string OutputDirectory()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("PDF_FORGE_LINK_RESULTS") ?? System.IO.Path.GetTempPath(),
            "link-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavePart_serialized_links_land_on_correct_output_pages(bool direct)
    {
        var directory = OutputDirectory();
        var source = System.IO.Path.Combine(directory, "source-40.pdf");
        using (var reader = CreateReader())
        {
            AddNamedDestination(reader, "target", Destination(reader, 20));
            var annotations = new PdfArray();
            annotations.Add(Link(new PdfString("target"), direct));
            annotations.Add(Link(Destination(reader, 2), direct));
            annotations.Add(Link(Destination(reader, 24), direct));
            annotations.Add(Link(Destination(reader, 1), direct));
            annotations.Add(Link(Destination(reader, 40), direct));
            annotations.Add(ActionLink(PdfName.URI, PdfName.URI, new PdfString("https://example.invalid/#target")));
            reader.GetPageN(2).Put(PdfName.ANNOTS, annotations);
            using var stamper = new PdfStamper(reader, File.Create(source));
        }
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        var output = System.IO.Path.Combine(directory, "range-2-24.pdf");
        var result = new PdfSplitService().SavePart(source, 2, 24, output);
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var saved = new PdfReader(output);
        Assert.Equal(23, saved.NumberOfPages);
        var links = saved.GetPageN(1).GetAsArray(PdfName.ANNOTS);
        Assert.Equal(4, links.Size);
        Assert.Equal(new[] { 19, 1, 23 }, Enumerable.Range(0, 3).Select(i => TargetPage(saved, links.GetAsDict(i))));
        Assert.Equal("https://example.invalid/#target", links.GetAsDict(3).GetAsDict(PdfName.A).GetAsString(PdfName.URI).ToUnicodeString());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
        for (var page = 1; page <= 23; page++)
            Assert.Contains($"LINK-REGRESSION PAGE {page + 1:00}", PdfTextExtractor.GetTextFromPage(saved, page));
    }

    [Fact]
    public void Split_six_serialized_parts_preserve_links_page_order_and_source_hash()
    {
        var directory = OutputDirectory();
        var source = System.IO.Path.Combine(directory, "source-40.pdf");
        using (var reader = CreateReader())
        {
            for (var page = 1; page <= 40; page++)
            {
                var annotations = new PdfArray(Link(Destination(reader, page)));
                if (page < 40) annotations.Add(Link(Destination(reader, page + 1)));
                reader.GetPageN(page).Put(PdfName.ANNOTS, annotations);
            }
            using var stamper = new PdfStamper(reader, File.Create(source));
        }
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        var result = new PdfSplitService().Split(source, new[] { 7, 14, 21, 28, 34 }, System.IO.Path.Combine(directory, "parts.pdf"));
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new[] { 7, 7, 7, 7, 6, 6 }, result.Parts.Select(p => p.PageCount));
        var sourcePage = 1;
        foreach (var part in result.Parts)
        {
            using var saved = new PdfReader(part.OutputPath);
            Assert.Equal(part.PageCount, saved.NumberOfPages);
            for (var page = 1; page <= saved.NumberOfPages; page++, sourcePage++)
            {
                Assert.Contains($"LINK-REGRESSION PAGE {sourcePage:00}", PdfTextExtractor.GetTextFromPage(saved, page));
                var links = saved.GetPageN(page).GetAsArray(PdfName.ANNOTS);
                Assert.Equal(page == saved.NumberOfPages ? 1 : 2, links.Size);
                Assert.Equal(page, TargetPage(saved, links.GetAsDict(0)));
                if (links.Size == 2) Assert.Equal(page + 1, TargetPage(saved, links.GetAsDict(1)));
            }
        }
        Assert.Equal(41, sourcePage);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [LocalPdfFact]
    public void Reported_document_range_retains_exactly_43_links_and_introduction_targets_page_3()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var source = Environment.GetEnvironmentVariable("PDF_FORGE_LINK_SOURCE")!;
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        var output = System.IO.Path.Combine(OutputDirectory(), "AAAA.fixed.pdf");
        var result = new PdfSplitService().SavePart(source, 2, 24, output);
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var original = new PdfReader(source);
        original.ConsolidateNamedDestinations();
        using var saved = new PdfReader(output);
        Assert.Equal(23, saved.NumberOfPages);
        var count = 0;
        for (var page = 1; page <= 23; page++)
        {
            Assert.Equal(PdfTextExtractor.GetTextFromPage(original, page + 1), PdfTextExtractor.GetTextFromPage(saved, page));
            var expected = LocalLinks(original, page + 1).Where(a => TargetPage(original, a) is >= 2 and <= 24).ToArray();
            var actual = LocalLinks(saved, page).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(TargetPage(original, expected[i]) - 1, TargetPage(saved, actual[i]));
                Assert.Equal(expected[i].GetAsArray(PdfName.RECT).ToString(), actual[i].GetAsArray(PdfName.RECT).ToString());
                Assert.Equal(DestinationOf(expected[i]).ArrayList.Skip(1).Select(x => x.ToString()),
                    DestinationOf(actual[i]).ArrayList.Skip(1).Select(x => x.ToString()));
                count++;
            }
        }
        Assert.Equal(43, count);
        Assert.Equal(3, TargetPage(saved, LocalLinks(saved, 1).First()));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
    }

    private static IEnumerable<PdfDictionary> LocalLinks(PdfReader reader, int page)
    {
        var annotations = reader.GetPageN(page).GetAsArray(PdfName.ANNOTS);
        if (annotations is null) yield break;
        for (var i = 0; i < annotations.Size; i++)
        {
            var a = annotations.GetAsDict(i);
            if (a?.GetAsName(PdfName.SUBTYPE) == PdfName.LINK &&
                (a.Get(PdfName.DEST) is not null || a.GetAsDict(PdfName.A)?.GetAsName(PdfName.S) == PdfName.GOTO)) yield return a;
        }
    }

    private static PdfArray DestinationOf(PdfDictionary link) =>
        link.GetAsArray(PdfName.DEST) ?? link.GetAsDict(PdfName.A).GetAsArray(PdfName.D);

    private static int TargetPage(PdfReader reader, PdfDictionary link)
    {
        var target = DestinationOf(link).GetAsIndirectObject(0);
        Assert.NotNull(target);
        for (var page = 1; page <= reader.NumberOfPages; page++)
            if (reader.GetPageOrigRef(page).Number == target.Number) return page;
        return -1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_links_resolve_to_retained_page_with_original_view(bool directDestination)
    {
        using var reader = CreateReader();
        var destination = Destination(reader, 20);
        AddNamedDestination(reader, "target", destination);
        var link = Link(new PdfString("target"), directDestination);
        reader.GetPageN(2).Put(PdfName.ANNOTS, new PdfArray(link));

        PdfSplitService.PrepareRangeLinks(reader, 2, 24);

        var actual = directDestination ? link.GetAsArray(PdfName.DEST) : link.GetAsDict(PdfName.A).GetAsArray(PdfName.D);
        Assert.NotNull(actual);
        Assert.Equal(reader.GetPageOrigRef(20).Number, actual.GetAsIndirectObject(0).Number);
        Assert.Equal(PdfName.XYZ, actual.GetAsName(1));
        Assert.Equal(36, actual.GetAsNumber(2).IntValue);
        Assert.Equal(750, actual.GetAsNumber(3).IntValue);
        Assert.Equal(0, actual.GetAsNumber(4).IntValue);
        Assert.Equal("[10, 20, 110, 40]", link.GetAsArray(PdfName.RECT).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Omitted_and_unresolved_targets_are_removed_but_other_annotations_survive(bool directDestination)
    {
        using var reader = CreateReader();
        AddNamedDestination(reader, "outside", Destination(reader, 40));
        var first = Link(Destination(reader, 2), directDestination);
        var last = Link(Destination(reader, 24), directDestination);
        var uri = ActionLink(PdfName.URI, PdfName.URI, new PdfString("https://example.invalid/#target"));
        var remote = ActionLink(PdfName.GOTOR, PdfName.D, new PdfString("outside"));
        remote.GetAsDict(PdfName.A).Put(PdfName.F, new PdfString("other.pdf"));
        var note = new PdfDictionary();
        note.Put(PdfName.SUBTYPE, PdfName.TEXT);
        var annotations = new PdfArray();
        foreach (var item in new[] { first, Link(Destination(reader, 1), directDestination),
            Link(new PdfString("outside"), directDestination), Link(new PdfString("missing"), directDestination),
            last, uri, remote, note })
            annotations.Add(item);
        reader.GetPageN(2).Put(PdfName.ANNOTS, annotations);

        PdfSplitService.PrepareRangeLinks(reader, 2, 24);

        Assert.Equal(5, annotations.Size);
        Assert.Same(first, annotations.GetAsDict(0));
        Assert.Same(last, annotations.GetAsDict(1));
        Assert.Same(uri, annotations.GetAsDict(2));
        Assert.Equal("https://example.invalid/#target", uri.GetAsDict(PdfName.A).GetAsString(PdfName.URI).ToUnicodeString());
        Assert.Same(remote, annotations.GetAsDict(3));
        Assert.Equal("outside", remote.GetAsDict(PdfName.A).GetAsString(PdfName.D).ToUnicodeString());
        Assert.Same(note, annotations.GetAsDict(4));
    }

    [Fact]
    public void Preparing_one_part_does_not_strip_links_on_later_parts()
    {
        using var reader = CreateReader();
        var earlier = Link(Destination(reader, 7));
        var later = Link(Destination(reader, 28));
        reader.GetPageN(1).Put(PdfName.ANNOTS, new PdfArray(earlier));
        reader.GetPageN(22).Put(PdfName.ANNOTS, new PdfArray(later));

        PdfSplitService.PrepareRangeLinks(reader, 1, 7);
        Assert.Single(reader.GetPageN(22).GetAsArray(PdfName.ANNOTS).ArrayList);
        PdfSplitService.PrepareRangeLinks(reader, 22, 28);
        Assert.Single(reader.GetPageN(1).GetAsArray(PdfName.ANNOTS).ArrayList);
        Assert.Single(reader.GetPageN(22).GetAsArray(PdfName.ANNOTS).ArrayList);
        Assert.Equal(reader.GetPageOrigRef(28).Number,
            later.GetAsDict(PdfName.A).GetAsArray(PdfName.D).GetAsIndirectObject(0).Number);
    }

    private static PdfReader CreateReader()
    {
        using var stream = new MemoryStream();
        using (var document = new Document(PageSize.A4))
        {
            PdfWriter.GetInstance(document, stream).CloseStream = false;
            document.Open();
            for (var page = 1; page <= 40; page++)
            {
                if (page > 1) document.NewPage();
                document.Add(new Paragraph($"LINK-REGRESSION PAGE {page:00} - Navigation and content retention"));
                document.Add(new Paragraph("This deterministic page contains a unique ID and readable content for checking navigation, source integrity, and retained page boundaries."));
                document.Add(new Paragraph("Internal destinations must remain on their original content. Links to omitted pages must not jump to a different retained page; external actions must remain unchanged."));
            }
        }
        return new PdfReader(stream.ToArray());
    }

    private static PdfArray Destination(PdfReader reader, int page) => new(new PdfObject[]
    {
        reader.GetPageOrigRef(page), PdfName.XYZ, new PdfNumber(36), new PdfNumber(750), new PdfNumber(0)
    });

    private static void AddNamedDestination(PdfReader reader, string name, PdfArray destination)
    {
        var entries = new PdfArray(new PdfObject[] { new PdfString(name), destination });
        var tree = new PdfDictionary();
        tree.Put(PdfName.NAMES, entries);
        var names = new PdfDictionary();
        names.Put(PdfName.DESTS, tree);
        reader.Catalog.Put(PdfName.NAMES, names);
    }

    private static PdfDictionary Link(PdfObject destination, bool direct = false)
    {
        var link = new PdfDictionary();
        link.Put(PdfName.SUBTYPE, PdfName.LINK);
        link.Put(PdfName.RECT, new PdfArray(new float[] { 10, 20, 110, 40 }));
        if (direct) link.Put(PdfName.DEST, destination);
        else
        {
            var action = new PdfDictionary();
            action.Put(PdfName.S, PdfName.GOTO);
            action.Put(PdfName.D, destination);
            link.Put(PdfName.A, action);
        }
        return link;
    }

    private static PdfDictionary ActionLink(PdfName kind, PdfName key, PdfObject value)
    {
        var link = Link(value);
        var action = new PdfDictionary();
        action.Put(PdfName.S, kind);
        action.Put(key, value);
        link.Put(PdfName.A, action);
        return link;
    }
}

public sealed class LocalPdfFactAttribute : FactAttribute
{
    public LocalPdfFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PDF_FORGE_LINK_SOURCE")))
            Skip = "Set PDF_FORGE_LINK_SOURCE to run the private local regression; the PDF is not committed.";
    }
}
