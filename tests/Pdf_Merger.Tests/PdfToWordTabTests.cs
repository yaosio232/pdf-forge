using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace Pdf_Merger.Tests;

[CollectionDefinition("Word conversion UI", DisableParallelization = true)]
public sealed class WordConversionUiCollection { }

[Collection("Word conversion UI")]
public class PdfToWordTabTests
{
    [Fact]
    public void BusyConversionStillOffersCancelAndLogFolder()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            typeof(Form1).GetMethod("SetPdfToWordProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
            var cancel = Assert.Single(Descendants(form).OfType<Button>().Where(button => button.AccessibleName == "Cancel file conversion"));
            var log = Assert.Single(Descendants(form).OfType<Button>().Where(button => button.AccessibleName == "Open conversion log folder"));
            Assert.True(cancel.Enabled);
            Assert.True(log.Enabled);
        });
    }

    [Fact]
    public void ConversionTabOffersWordAndWordFreeMarkdownModes()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            var tab = Assert.Single(Field<TabControl>(form, "modeTabs").TabPages.Cast<TabPage>().Where(page => page.Text == "Convert Files"));
            var modes = Assert.Single(Descendants(tab).OfType<ComboBox>());
            Assert.Equal(new[] { "PDF → Word", "Markdown → PDF (batch)" }, modes.Items.Cast<string>());
            modes.SelectedIndex = 1;
            var handler = typeof(Form1).GetMethod("PdfToWordDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var data = new DataObject(DataFormats.FileDrop, new[] { "first.md", "second.markdown" });
            var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            handler.Invoke(form, new object?[] { form, args });
            Assert.Equal(DragDropEffects.Copy, args.Effect);
        });
    }
    [Fact]
    public void NewTabOffersConversionWithoutChangingStartupMode()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            var tabs = Field<TabControl>(form, "modeTabs");
            Assert.Equal("Split PDF", tabs.SelectedTab!.Text);
            var tab = Assert.Single(tabs.TabPages.Cast<TabPage>().Where(page => page.Text == "Convert Files"));
            Assert.Contains(Descendants(tab), control => control is Button && control.AccessibleName == "Choose a PDF to convert to Word");
            var convert = Assert.Single(Descendants(tab).Where(control => control is Button && control.AccessibleName == "Convert PDF to Word"));
            Assert.False(convert.Enabled);
        });
    }

    [Fact]
    public void ProcessingLocksTabsAndRestoresConversionAfterFailure()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            typeof(Form1).GetField("pdfToWordSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, "source.pdf");
            var setter = typeof(Form1).GetMethod("SetPdfToWordProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            setter.Invoke(form, new object[] { true });
            var tabs = Field<TabControl>(form, "modeTabs");
            Assert.True(tabs.Enabled); // Cancel/log buttons inside the tab must remain usable.
            Assert.All(tabs.TabPages.Cast<TabPage>().Where(tab => tab.Text != "Convert Files"), tab => Assert.False(tab.Enabled));
            Assert.True(Field<bool>(form, "isProcessing"));
            Assert.False(Field<Button>(form, "pdfToWordChooseButton").Enabled);
            setter.Invoke(form, new object[] { false });
            Assert.True(Field<TabControl>(form, "modeTabs").Enabled);
            Assert.False(Field<bool>(form, "isProcessing"));
            Assert.True(Field<Button>(form, "pdfToWordChooseButton").Enabled);
            Assert.True(Field<Button>(form, "pdfToWordConvertButton").Enabled);
        });
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    [Fact]
    public void DropAcceptsOnePdfAndRejectsMultipleOrBusyInput()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            var handler = typeof(Form1).GetMethod("PdfToWordDragEnter", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(handler);
            DragEventArgs Drag(params string[] files)
            {
                var data = new DataObject(DataFormats.FileDrop, files);
                var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
                handler!.Invoke(form, new object?[] { form, args });
                return args;
            }
            Assert.Equal(DragDropEffects.Copy, Drag("source.PDF").Effect);
            Assert.Equal(DragDropEffects.None, Drag("source.docx").Effect);
            Assert.Equal(DragDropEffects.None, Drag("one.pdf", "two.pdf").Effect);
            typeof(Form1).GetMethod("SetPdfToWordProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
            Assert.Equal(DragDropEffects.None, Drag("source.pdf").Effect);
        });
    }

    [Fact]
    public void OutputFolderActionIsEnabledOnlyForSuccessfulOutputAndResetForNewWork()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            var button = Descendants(form).OfType<Button>().SingleOrDefault(control => control.AccessibleName == "Open converted Word output folder");
            Assert.NotNull(button);
            Assert.False(button!.Enabled);
            var outputSetter = typeof(Form1).GetMethod("SetPdfToWordOutput", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(outputSetter);
            outputSetter!.Invoke(form, new object[] { "C:\\output\\converted.docx" });
            Assert.True(button.Enabled);
            typeof(Form1).GetMethod("SetPdfToWordProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
            Assert.False(button.Enabled);
            typeof(Form1).GetMethod("SetPdfToWordProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { false });
            Assert.False(button.Enabled);
        });
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI state test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
