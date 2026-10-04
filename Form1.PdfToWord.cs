namespace Pdf_Merger;

public partial class Form1
{
    private string? pdfToWordSource;
    private bool isPdfToWordProcessing;
    private Button pdfToWordChooseButton = null!;
    private Button pdfToWordConvertButton = null!;
    private Label pdfToWordSourceText = null!;
    private Label pdfToWordStatusText = null!;
    private Label pdfToWordDropTitle = null!;
    private Label pdfToWordOutputText = null!;
    private Button pdfToWordOpenFolderButton = null!;
    private Panel pdfToWordDropPanel = null!;
    private string? pdfToWordOutput;
    private bool isPdfToWordDragOver;

    private Control BuildPdfToWordWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Canvas,
            Padding = new Padding(12, 4, 12, 12), AccessibleName = "PDF to Word workspace"
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.SizeChanged += (_, _) => workspace.ColumnStyles[1].Width = workspace.ClientSize.Width < 1000 ? 300 : 340;
        pdfToWordDropPanel = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(28),
            Margin = new Padding(0, 0, 16, 0), AccessibleName = "Drop one PDF to convert to Word"
        };
        pdfToWordDropPanel.Paint += (_, e) =>
        {
            using var pen = new System.Drawing.Pen(isPdfToWordDragOver ? Primary : Border, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 10, 10, Math.Max(1, pdfToWordDropPanel.Width - 21), Math.Max(1, pdfToWordDropPanel.Height - 21));
        };
        var dropContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Color.Transparent };
        dropContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        dropContent.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        var badge = new Label { Text = "PDF   →   WORD", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Primary, Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold) };
        pdfToWordDropTitle = new Label { Text = "Drop your PDF here", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold), AutoEllipsis = true };
        var dropHint = new Label { Text = "One PDF, one editable Word document.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 10) };
        pdfToWordChooseButton = new Button { Text = "Choose PDF…", Size = new Size(184, 44), Anchor = AnchorStyles.None, TabIndex = 0, AccessibleName = "Choose a PDF to convert to Word", FlatStyle = FlatStyle.Flat };
        ConfigureButton(pdfToWordChooseButton, Color.White, Primary, PrimaryHover);
        pdfToWordChooseButton.Click += PdfToWordChoose_Click;
        dropContent.Controls.Add(badge, 0, 1);
        dropContent.Controls.Add(pdfToWordDropTitle, 0, 2);
        dropContent.Controls.Add(dropHint, 0, 3);
        dropContent.Controls.Add(pdfToWordChooseButton, 0, 4);
        dropContent.Controls.Add(new Label { Text = "PDF only · processed on this computer", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Subtle }, 0, 5);
        pdfToWordDropPanel.Controls.Add(dropContent);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(20) };
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Surface, Margin = new Padding(0) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label Copy(string text, float size, Color color, bool bold = false) => new Label
        {
            Text = text, AutoSize = true, ForeColor = color, Dock = DockStyle.Top,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 16)
        };
        void Add(Control control) { card.RowStyles.Add(new RowStyle(SizeType.AutoSize)); card.Controls.Add(control, 0, card.RowCount++); }
        card.RowCount = 0;
        Add(Copy("PDF to Word", 18, Ink, true));
        Add(Copy("Make your PDF editable.", 10, Muted));
        Add(Copy("1   Choose or drop a PDF\r\n2   Choose where to save\r\n3   Convert and review", 10, Muted));
        Add(Copy("SOURCE FILE", 8.5F, Subtle, true));
        pdfToWordSourceText = Copy("No PDF selected", 10, Ink, true);
        pdfToWordSourceText.AccessibleName = "PDF to Word source file";
        Add(pdfToWordSourceText);
        pdfToWordConvertButton = new Button
        {
            Text = "Convert to Word", Dock = DockStyle.Top, Height = 46, Enabled = false, TabIndex = 1,
            AccessibleName = "Convert PDF to Word", FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 8, 0, 24)
        };
        ConfigureButton(pdfToWordConvertButton, Primary, Color.White, PrimaryHover, PrimaryPressed);
        pdfToWordConvertButton.Click += PdfToWordConvert_Click;
        Add(pdfToWordConvertButton);
        Add(Copy("EXPORT", 8.5F, Subtle, true));
        pdfToWordStatusText = Copy("Choose a PDF to begin.", 10, Muted);
        pdfToWordStatusText.AccessibleName = "PDF to Word conversion status";
        Add(pdfToWordStatusText);
        pdfToWordOutputText = Copy("Your DOCX location will appear here.", 9, Subtle);
        pdfToWordOutputText.AccessibleName = "Converted Word output path";
        Add(pdfToWordOutputText);
        pdfToWordOpenFolderButton = new Button { Text = "Open output folder", Dock = DockStyle.Top, Height = 42, Enabled = false, TabIndex = 2, FlatStyle = FlatStyle.Flat, AccessibleName = "Open converted Word output folder", Margin = new Padding(0, 0, 0, 24) };
        ConfigureButton(pdfToWordOpenFolderButton, Color.White, Ink, PrimarySoft);
        pdfToWordOpenFolderButton.Click += PdfToWordOpenFolder_Click;
        Add(pdfToWordOpenFolderButton);
        Add(Copy("Requires Microsoft Word desktop.\r\nLayout may change. Scanned pages may remain images.", 9, Subtle));
        card.SizeChanged += (_, _) =>
        {
            foreach (var label in card.Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(120, card.ClientSize.Width), 0);
        };
        sidebar.Controls.Add(card);
        workspace.Controls.Add(pdfToWordDropPanel, 0, 0);
        workspace.Controls.Add(sidebar, 1, 0);
        EnablePdfToWordDrop(workspace);
        return workspace;
    }

    private void EnablePdfToWordDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += PdfToWordDragEnter;
        control.DragOver += PdfToWordDragEnter;
        control.DragLeave += (_, _) => { isPdfToWordDragOver = false; pdfToWordDropPanel.Invalidate(); };
        control.DragDrop += PdfToWordDragDrop;
        foreach (Control child in control.Controls) EnablePdfToWordDrop(child);
    }

    private void PdfToWordDragEnter(object? sender, DragEventArgs e)
    {
        var allowed = !isProcessing && !isUnlockProcessing && GetDroppedPdf(e.Data) is not null &&
            (e.AllowedEffect & DragDropEffects.Copy) != 0;
        e.Effect = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        isPdfToWordDragOver = allowed;
        pdfToWordDropPanel.Invalidate();
    }

    private void PdfToWordDragDrop(object? sender, DragEventArgs e)
    {
        isPdfToWordDragOver = false;
        pdfToWordDropPanel.Invalidate();
        if (isProcessing || isUnlockProcessing) return;
        var path = GetDroppedPdf(e.Data);
        if (path is not null) SelectPdfToWordSource(path);
        else pdfToWordStatusText.Text = "Drop one PDF file at a time.";
    }

    private void SelectPdfToWordSource(string path)
    {
        if (isProcessing || isUnlockProcessing) return;
        if (!File.Exists(path)) { pdfToWordStatusText.Text = "The selected PDF could not be found."; return; }
        pdfToWordSource = Path.GetFullPath(path);
        pdfToWordSourceText.Text = Path.GetFileName(path);
        pdfToWordDropTitle.Text = Path.GetFileName(path);
        toolTip.SetToolTip(pdfToWordSourceText, pdfToWordSource);
        toolTip.SetToolTip(pdfToWordDropTitle, pdfToWordSource);
        pdfToWordChooseButton.Text = "Choose another PDF…";
        SetPdfToWordOutput(null);
        pdfToWordStatusText.Text = "Ready to convert. Choose where to save your DOCX.";
        pdfToWordConvertButton.Enabled = true;
    }

    private void SetPdfToWordOutput(string? path)
    {
        pdfToWordOutput = path;
        pdfToWordOutputText.Text = path ?? "Your DOCX location will appear here.";
        pdfToWordOpenFolderButton.Enabled = path is not null && !isProcessing;
    }

    private void PdfToWordOpenFolder_Click(object? sender, EventArgs e)
    {
        if (isProcessing || pdfToWordOutput is null) return;
        try
        {
            var folder = Path.GetDirectoryName(pdfToWordOutput)!;
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The output folder is no longer available.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            pdfToWordStatusText.Text = $"Could not open the output folder: {exception.Message}";
        }
    }

    private void PdfToWordChoose_Click(object? sender, EventArgs e)
    {
        if (isProcessing || isUnlockProcessing) return;
        using var dialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = false, Title = "Choose a PDF to convert to Word" };
        var result = dialog.ShowDialog(this);
        RestoreMaximizedWindow();
        if (result != DialogResult.OK) return;
        SelectPdfToWordSource(dialog.FileName);
    }

    private async void PdfToWordConvert_Click(object? sender, EventArgs e)
    {
        if (isProcessing || isUnlockProcessing || pdfToWordSource is null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "Word documents (*.docx)|*.docx", DefaultExt = "docx", AddExtension = true,
            OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(pdfToWordSource) + ".docx",
            Title = "Save converted Word document"
        };
        var result = dialog.ShowDialog(this);
        RestoreMaximizedWindow();
        if (result != DialogResult.OK) return;
        var source = pdfToWordSource;
        var output = dialog.FileName;
        SetPdfToWordProcessing(true);
        pdfToWordStatusText.Text = "Converting with Microsoft Word… Keep this window open.";
        try
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { new PdfToWordConverter().Convert(source, output); completion.SetResult(true); }
                catch (Exception exception) { completion.SetException(exception); }
            }) { IsBackground = true, Name = "PDF Forge Word Conversion" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task;
            SetPdfToWordOutput(output);
            pdfToWordStatusText.Text = "DOCX saved. Review the document layout before use.";
        }
        catch (Exception exception)
        {
            Logger.LogError("PDF to Word conversion failed.", exception);
            pdfToWordStatusText.Text = $"Conversion failed: {exception.Message}\r\nChoose another file or try again.";
            MessageBox.Show(this, exception.Message, "PDF to Word failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetPdfToWordProcessing(false); }
    }

    private void SetPdfToWordProcessing(bool processing)
    {
        isPdfToWordProcessing = processing;
        isProcessing = processing;
        if (processing) SetPdfToWordOutput(null);
        modeTabs.Enabled = !processing;
        btnDisclaimer.Enabled = !processing;
        pdfToWordChooseButton.Enabled = !processing;
        pdfToWordConvertButton.Enabled = !processing && pdfToWordSource is not null;
        pdfToWordConvertButton.Text = processing ? "Converting…" : "Convert to Word";
        pdfToWordOpenFolderButton.Enabled = !processing && pdfToWordOutput is not null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isPdfToWordProcessing)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Please wait for PDF to Word conversion to finish before closing.",
                "Conversion in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        base.OnFormClosing(e);
    }
}
