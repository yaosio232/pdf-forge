using System.Reflection;
using Xunit;

namespace Pdf_Merger.Tests;

public class PreviewLifecycleTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public PreviewLifecycleTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CleanupWaitsForCancelledWriterToExit()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            using var cancellation = new CancellationTokenSource();
            var directory = Path.Combine(Path.GetTempPath(), "PdfForgeLifecycleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var exited = new TaskCompletionSource<bool>();
            try
            {
                SetField(form, "splitPreviewDirectory", directory);
                SetField(form, "splitLargePreviewCancellation", cancellation);
                typeof(Form1).GetMethod("TrackSplitPreviewTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { exited.Task });
                var cleanup = (Task)typeof(Form1).GetMethod("ClearSplitPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, null)!;
                Assert.True(cancellation.IsCancellationRequested);
                Assert.False(cleanup.IsCompleted);
                Assert.True(Directory.Exists(directory));
                // The cancelled writer may still finish one last write before it exits.
                File.WriteAllText(Path.Combine(directory, "last-write.txt"), "writer is stopping");
                exited.SetResult(true);
                PumpUntil(() => cleanup.IsCompleted);
                cleanup.GetAwaiter().GetResult();
                Assert.False(Directory.Exists(directory));
            }
            finally
            {
                exited.TrySetResult(true);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void UnchangedPdfReloadKeepsPreviewAndCutPoints()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            var directory = Path.Combine(Path.GetTempPath(), "PdfForgeLifecycleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "forty-pages.pdf");
            try
            {
                using (var stream = File.Create(source))
                using (var document = new iTextSharp.text.Document())
                {
                    iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
                    document.Open();
                    for (var page = 1; page <= 40; page++)
                    {
                        document.NewPage();
                        document.Add(new iTextSharp.text.Paragraph($"RELOAD-{page:000} Preview lifecycle fixture"));
                        document.Add(new iTextSharp.text.Paragraph("This locally generated document verifies repeated loading preserves the source and the existing preview. Each page has a unique identity and substantive content."));
                    }
                }
                using var hash = System.Security.Cryptography.SHA256.Create();
                var before = Convert.ToHexString(hash.ComputeHash(File.ReadAllBytes(source)));
                SetField(form, "splitSourceFile", source);
                SetField(form, "splitSourceHash", before);
                SetField(form, "splitPreviewDirectory", directory);
                using var bitmap = new System.Drawing.Bitmap(10, 10);
                SetField(form, "splitLargePreviewImage", bitmap);
                var cuts = (HashSet<int>)typeof(Form1).GetField("splitAfterPages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                cuts.Add(7);
                typeof(Form1).GetMethod("LoadSplitPdfAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { source });
                PumpUntil(() => !(bool)typeof(Form1).GetField("isProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!);
                Assert.True(Directory.Exists(directory));
                Assert.Contains(7, cuts);
                Assert.Same(bitmap, typeof(Form1).GetField("splitLargePreviewImage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form));
                Assert.Equal(before, Convert.ToHexString(hash.ComputeHash(File.ReadAllBytes(source))));
                output.WriteLine($"Fresh 40-page fixture SHA-256 before/after: {before}");
                var evidenceDirectory = Environment.GetEnvironmentVariable("PDF_FORGE_TEST_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidenceDirectory))
                {
                    Directory.CreateDirectory(evidenceDirectory);
                    File.Copy(source, Path.Combine(evidenceDirectory, "reload-forty-pages.pdf"), overwrite: true);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(completed(), "Preview lifecycle operation timed out.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Lifecycle test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ReloadCancelsWriterBeforeDeletingItsDirectory()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "PdfForgeLifecycleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var form = new Form1();
                using var cancellation = new CancellationTokenSource();
                bool? directoryExistedAtCancellation = null;
                using var registration = cancellation.Token.Register(() =>
                    directoryExistedAtCancellation = Directory.Exists(directory));
                SetField(form, "splitPreviewDirectory", directory);
                SetField(form, "splitLargePreviewCancellation", cancellation);
                var result = typeof(Form1).GetMethod("ClearSplitPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, null);
                if (result is Task task) task.GetAwaiter().GetResult();
                Assert.True(directoryExistedAtCancellation);
                Assert.False(Directory.Exists(directory));
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Lifecycle test timed out.");
        if (failure is not null) throw failure;
    }

    private static void SetField(Form1 form, string name, object value) =>
        typeof(Form1).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);
}
