using System.Diagnostics;
using Markdig;
using iTextSharp.text.pdf;

namespace Pdf_Merger;

internal sealed class MarkdownToPdfConverter
{
    private readonly Action<string, string, CancellationToken> print;
    internal MarkdownToPdfConverter() => print = PrintWithBrowser;
    internal MarkdownToPdfConverter(Action<string, string, CancellationToken> print) => this.print = print;

    internal static string[] GetOutputPaths(string[] sources, string directory)
    {
        var outputs = sources.Select(source => Path.Combine(Path.GetFullPath(directory), Path.GetFileNameWithoutExtension(source) + ".pdf")).ToArray();
        if (outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length)
            throw new ArgumentException("Two selected Markdown files have the same output name. Rename one before converting.");
        return outputs;
    }

    internal void Convert(string sourcePath, string outputPath, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourcePath); var output = Path.GetFullPath(outputPath);
        if (!IsMarkdown(source) || !string.Equals(Path.GetExtension(output), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a Markdown source and a separate PDF output.");
        if (!File.Exists(source)) throw new FileNotFoundException("The Markdown source could not be found.", source);
        var directory = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The output folder does not exist.");
        var temporary = Path.Combine(directory, $".pdfforge-{Guid.NewGuid():N}.pdf");
        var html = temporary + ".html";
        try
        {
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) { }
            File.WriteAllText(html, RenderHtml(File.ReadAllText(source)), System.Text.Encoding.UTF8);
            Logger.LogInfo("Markdown to PDF: rendering with a local browser.");
            print(html, temporary, cancellation);
            using (var reader = new PdfReader(temporary))
                if (reader.NumberOfPages < 1) throw new InvalidDataException("The browser produced an empty PDF.");
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(output)) File.Replace(temporary, output, null); else File.Move(temporary, output);
            Logger.LogInfo("Markdown to PDF: output committed.");
        }
        finally
        {
            foreach (var path in new[] { temporary, html })
                try { File.Delete(path); } catch (Exception exception) { Logger.LogError("Cleaning Markdown conversion temporary files failed.", exception); }
        }
    }

    internal static bool IsMarkdown(string path) => new[] { ".md", ".markdown" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static string RenderHtml(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().DisableHtml().Build();
        // Images are deliberately blocked: conversion does not fetch remote or local resources.
        return "<!doctype html><html><head><meta charset='utf-8'>" +
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; font-src 'none'; connect-src 'none'; base-uri 'none'\">" +
            "<style>@page{size:A4;margin:18mm}body{font-family:'Segoe UI','Microsoft JhengHei',sans-serif;font-size:11pt;line-height:1.55;color:#142033}" +
            "h1,h2,h3{break-after:avoid}table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #bdc7d5;padding:6px;text-align:left}" +
            "thead{display:table-header-group}tr{break-inside:avoid}pre{background:#f2f5fa;padding:10px;white-space:pre-wrap;overflow-wrap:anywhere}" +
            "code{font-family:Consolas,monospace}blockquote{border-left:3px solid #91a4be;padding-left:12px}a{color:#1d4ed8}img{display:none}</style></head><body>" +
            Markdown.ToHtml(markdown, pipeline) + "</body></html>";
    }

    internal static string FindBrowser()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            foreach (var relative in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" })
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path)) return path;
            }
        throw new InvalidOperationException("Markdown to PDF requires Microsoft Edge or Google Chrome installed on this computer. Microsoft Word is not required.");
    }

    private static void PrintWithBrowser(string html, string output, CancellationToken cancellation)
    {
        var browser = FindBrowser();
        var profile = Path.Combine(Path.GetTempPath(), "PDFForgeBrowser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-breakpad", "--no-pdf-header-footer", "--proxy-server=127.0.0.1:9", "--proxy-bypass-list=<-loopback>", "--user-data-dir=" + profile, "--print-to-pdf=" + output, new Uri(html).AbsoluteUri })
            start.ArgumentList.Add(argument);
        try { ConversionProcess.Run(start, cancellation, TimeSpan.FromMinutes(2)); }
        finally
        {
            try
            {
                // The profile path is always a newly created direct child of the OS temp directory.
                var parent = Path.GetDirectoryName(Path.GetFullPath(profile));
                if (string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(profile, recursive: true);
            }
            catch (Exception exception) { Logger.LogError("Cleaning the isolated browser profile failed.", exception); }
        }
    }
}
