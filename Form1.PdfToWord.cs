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
    private ComboBox conversionMode = null!;
    private Button conversionCancelButton = null!;
    private Label conversionRequirements = null!;
    private Label conversionBadge = null!;
    private Label conversionHint = null!;
    private ListBox markdownFileList = null!;
    private string[] markdownSources = Array.Empty<string>();
    private CancellationTokenSource? conversionCancellation;
    private bool closeAfterConversion;
    private bool IsMarkdownMode => conversionMode.SelectedIndex == 1;

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
        conversionBadge = new Label { Text = "PDF   →   WORD", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Primary, Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold) };
        pdfToWordDropTitle = new Label { Text = "Drop your PDF here", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold), AutoEllipsis = true };
        conversionHint = new Label { Text = "One PDF, one editable Word document.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 10) };
        pdfToWordChooseButton = new Button { Text = "Choose PDF…", Size = new Size(184, 44), Anchor = AnchorStyles.None, TabIndex = 0, AccessibleName = "Choose a PDF to convert to Word", FlatStyle = FlatStyle.Flat };
        ConfigureButton(pdfToWordChooseButton, Color.White, Primary, PrimaryHover);
        pdfToWordChooseButton.Click += PdfToWordChoose_Click;
        dropContent.Controls.Add(conversionBadge, 0, 1);
        dropContent.Controls.Add(pdfToWordDropTitle, 0, 2);
        dropContent.Controls.Add(conversionHint, 0, 3);
        dropContent.Controls.Add(pdfToWordChooseButton, 0, 4);
        dropContent.Controls.Add(new Label { Text = "Files are processed on this computer", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Subtle }, 0, 5);
        pdfToWordDropPanel.Controls.Add(dropContent);
        markdownFileList = new ListBox { Dock = DockStyle.Bottom, Height = 160, Visible = false, AccessibleName = "Selected Markdown files", HorizontalScrollbar = true, IntegralHeight = false };
        pdfToWordDropPanel.Controls.Add(markdownFileList);

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
        Add(Copy("Convert Files", 18, Ink, true));
        conversionMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "File conversion mode", Margin = new Padding(0, 0, 0, 20), TabIndex = 0 };
        conversionMode.Items.AddRange(new object[] { "PDF → Word", "Markdown → PDF (batch)" });
        conversionMode.SelectedIndex = 0;
        Add(conversionMode);
        Add(Copy("1   Choose or drop files\r\n2   Choose where to save\r\n3   Convert and review", 10, Muted));
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
        conversionCancelButton = new Button { Text = "Cancel conversion", Dock = DockStyle.Top, Height = 38, Enabled = false, AccessibleName = "Cancel file conversion", FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, 16), TabIndex = 2 };
        ConfigureButton(conversionCancelButton, Color.White, Danger, PrimarySoft);
        conversionCancelButton.Click += (_, _) => CancelFileConversion();
        Add(conversionCancelButton);
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
        var logButton = new Button { Text = "Open log folder", Dock = DockStyle.Top, Height = 38, AccessibleName = "Open conversion log folder", FlatStyle = FlatStyle.Flat, TabIndex = 4, Margin = new Padding(0, 0, 0, 16) };
        ConfigureButton(logButton, Color.White, Ink, PrimarySoft);
        logButton.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Logger.LogDirectory, UseShellExecute = true }); }
            catch (Exception exception) { pdfToWordStatusText.Text = $"Could not open log folder: {exception.Message}"; }
        };
        Add(logButton);
        conversionRequirements = Copy("Requires Microsoft Word desktop.\r\nLayout may change. Scanned pages may remain images.\r\nFive-minute timeout; cancel or close to stop.", 9, Subtle);
        Add(conversionRequirements);
        conversionMode.SelectedIndexChanged += (_, _) => ChangeConversionMode();
        modeTabs.Selecting += (_, e) => { if (isPdfToWordProcessing && e.TabPage != modeTabs.SelectedTab) e.Cancel = true; };
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
        var allowed = !isProcessing && !isUnlockProcessing && (IsMarkdownMode ? GetDroppedMarkdown(e.Data) is not null : GetDroppedPdf(e.Data) is not null) &&
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
        if (IsMarkdownMode)
        {
            var files = GetDroppedMarkdown(e.Data);
            if (files is not null) SelectMarkdownSources(files);
            else pdfToWordStatusText.Text = "Drop Markdown files (.md or .markdown).";
            return;
        }
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
        pdfToWordOutputText.Text = path ?? (IsMarkdownMode ? "Your PDF output folder will appear here." : "Your DOCX location will appear here.");
        pdfToWordOpenFolderButton.Enabled = path is not null && !isProcessing;
    }

    private void PdfToWordOpenFolder_Click(object? sender, EventArgs e)
    {
        if (isProcessing || pdfToWordOutput is null) return;
        try
        {
            var folder = Directory.Exists(pdfToWordOutput) ? pdfToWordOutput : Path.GetDirectoryName(pdfToWordOutput)!;
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
        if (IsMarkdownMode)
        {
            using var markdownDialog = new OpenFileDialog { Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown", Multiselect = true, Title = "Choose Markdown files to convert to PDF" };
            var selected = markdownDialog.ShowDialog(this);
            RestoreMaximizedWindow();
            if (selected == DialogResult.OK) SelectMarkdownSources(markdownDialog.FileNames);
            return;
        }
        using var dialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = false, Title = "Choose a PDF to convert to Word" };
        var result = dialog.ShowDialog(this);
        RestoreMaximizedWindow();
        if (result != DialogResult.OK) return;
        SelectPdfToWordSource(dialog.FileName);
    }

    private async void PdfToWordConvert_Click(object? sender, EventArgs e)
    {
        if (isProcessing || isUnlockProcessing) return;
        if (IsMarkdownMode) { await ConvertMarkdownBatch(); return; }
        if (pdfToWordSource is null) return;
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
        conversionCancellation = new CancellationTokenSource();
        pdfToWordStatusText.Text = "Converting with Microsoft Word… Cancel or close to stop.";
        try
        {
            var cancellation = conversionCancellation.Token;
            await Task.Run(() => new PdfToWordConverter().Convert(source, output, cancellation));
            SetPdfToWordOutput(output);
            pdfToWordStatusText.Text = "DOCX saved. Review the document layout before use.";
        }
        catch (OperationCanceledException) { pdfToWordStatusText.Text = "Conversion cancelled. Existing output was preserved."; }
        catch (Exception exception)
        {
            Logger.LogError("PDF to Word conversion failed.", exception);
            pdfToWordStatusText.Text = $"Conversion failed: {exception.Message}\r\nChoose another file or try again.";
            if (!closeAfterConversion) MessageBox.Show(this, exception.Message, "PDF to Word failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { FinishFileConversion(); }
    }

    private void SetPdfToWordProcessing(bool processing)
    {
        isPdfToWordProcessing = processing;
        isProcessing = processing;
        if (processing) SetPdfToWordOutput(null);
        // Keep this tab enabled so Cancel and log access remain available during work.
        foreach (TabPage tab in modeTabs.TabPages) if (tab.Text != "Convert Files") tab.Enabled = !processing;
        btnDisclaimer.Enabled = !processing;
        conversionMode.Enabled = !processing;
        conversionCancelButton.Enabled = processing;
        pdfToWordChooseButton.Enabled = !processing;
        pdfToWordConvertButton.Enabled = !processing && (IsMarkdownMode ? markdownSources.Length > 0 : pdfToWordSource is not null);
        pdfToWordConvertButton.Text = processing ? "Converting…" : IsMarkdownMode ? "Convert to PDF" : "Convert to Word";
        pdfToWordOpenFolderButton.Enabled = !processing && pdfToWordOutput is not null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isPdfToWordProcessing)
        {
            e.Cancel = true;
            closeAfterConversion = true;
            CancelFileConversion();
            pdfToWordStatusText.Text = "Stopping conversion before closing…";
        }
        base.OnFormClosing(e);
    }

    private void CancelFileConversion()
    {
        Logger.LogInfo("File conversion: user requested cancellation or window closure.");
        conversionCancellation?.Cancel();
        conversionCancelButton.Enabled = false;
        pdfToWordStatusText.Text = "Stopping conversion and cleaning temporary files…";
    }

    private void FinishFileConversion()
    {
        conversionCancellation?.Dispose(); conversionCancellation = null;
        SetPdfToWordProcessing(false);
        if (closeAfterConversion && IsHandleCreated) BeginInvoke(new Action(Close));
    }

    private void ChangeConversionMode()
    {
        pdfToWordSource = null; markdownSources = Array.Empty<string>(); markdownFileList.Items.Clear();
        markdownFileList.Visible = IsMarkdownMode;
        conversionBadge.Text = IsMarkdownMode ? "MARKDOWN   →   PDF" : "PDF   →   WORD";
        pdfToWordDropTitle.Text = IsMarkdownMode ? "Drop Markdown files here" : "Drop your PDF here";
        conversionHint.Text = IsMarkdownMode ? "Multiple Markdown files, one PDF for each." : "One PDF, one editable Word document.";
        pdfToWordChooseButton.Text = IsMarkdownMode ? "Choose Markdown…" : "Choose PDF…";
        pdfToWordChooseButton.AccessibleName = IsMarkdownMode ? "Choose Markdown files to convert to PDF" : "Choose a PDF to convert to Word";
        pdfToWordSourceText.Text = "No files selected";
        toolTip.SetToolTip(pdfToWordSourceText, null); toolTip.SetToolTip(pdfToWordDropTitle, null);
        pdfToWordConvertButton.AccessibleName = IsMarkdownMode ? "Convert Markdown files to PDF" : "Convert PDF to Word";
        pdfToWordStatusText.Text = IsMarkdownMode ? "Choose Markdown files to begin." : "Choose a PDF to begin.";
        conversionRequirements.Text = IsMarkdownMode ? "Requires Edge or Chrome; Word is not needed.\r\nLocal rendering; images and raw HTML are excluded.\r\nTwo-minute timeout per file." : "Requires Microsoft Word desktop.\r\nLayout may change. Scanned pages may remain images.\r\nFive-minute timeout; cancel or close to stop.";
        SetPdfToWordOutput(null); SetPdfToWordProcessing(false);
    }

    private static string[]? GetDroppedMarkdown(IDataObject? data)
    {
        var files = data?.GetData(DataFormats.FileDrop) as string[];
        return files is { Length: > 0 } && files.All(MarkdownToPdfConverter.IsMarkdown) ? files : null;
    }

    private void SelectMarkdownSources(string[] files)
    {
        if (isProcessing || isUnlockProcessing) return;
        if (files.Length == 0 || files.Any(file => !MarkdownToPdfConverter.IsMarkdown(file) || !File.Exists(file)))
        { pdfToWordStatusText.Text = "Choose existing Markdown files (.md or .markdown)."; return; }
        markdownSources = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        markdownFileList.Items.Clear(); markdownFileList.Items.AddRange(markdownSources);
        pdfToWordSourceText.Text = $"{markdownSources.Length} Markdown file(s) selected";
        pdfToWordDropTitle.Text = $"{markdownSources.Length} files ready to convert";
        SetPdfToWordOutput(null);
        pdfToWordStatusText.Text = "Ready. Each file will be saved as a separate PDF.";
        SetPdfToWordProcessing(false);
    }

    private async Task ConvertMarkdownBatch()
    {
        if (markdownSources.Length == 0) return;
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder for converted PDFs", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) { RestoreMaximizedWindow(); return; }
        RestoreMaximizedWindow();
        var sources = markdownSources.ToArray();
        string[] outputs;
        try
        {
            outputs = MarkdownToPdfConverter.GetOutputPaths(sources, dialog.SelectedPath);
            MarkdownToPdfConverter.FindBrowser();
        }
        catch (Exception exception) { pdfToWordStatusText.Text = exception.Message; return; }
        var existing = outputs.Where(File.Exists).ToArray();
        if (existing.Length > 0 && MessageBox.Show(this, $"Replace {existing.Length} existing PDF(s) in this folder?\r\n\r\n" + string.Join("\r\n", existing.Select(Path.GetFileName)), "Confirm PDF replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetPdfToWordProcessing(true);
        conversionCancellation = new CancellationTokenSource();
        var token = conversionCancellation.Token;
        var results = new List<string>(); var success = 0;
        try
        {
            for (var index = 0; index < sources.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var source = sources[index]; var output = outputs[index];
                pdfToWordStatusText.Text = $"Converting {index + 1}/{sources.Length}: {Path.GetFileName(source)}…";
                try
                {
                    await Task.Run(() => new MarkdownToPdfConverter().Convert(source, output, token));
                    success++; results.Add($"Saved: {Path.GetFileName(output)}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    results.Add($"Failed: {Path.GetFileName(source)} — {exception.Message}");
                    Logger.LogError("Markdown batch: a file failed; continuing the queue.", exception);
                }
            }
            pdfToWordStatusText.Text = $"Saved {success}/{sources.Length} PDF(s).\r\n" + string.Join("\r\n", results);
        }
        catch (OperationCanceledException) { pdfToWordStatusText.Text = $"Cancelled. {success} PDF(s) saved; remaining files were not converted.\r\n" + string.Join("\r\n", results); }
        catch (Exception exception) { Logger.LogError("Markdown batch failed.", exception); pdfToWordStatusText.Text = exception.Message; }
        finally
        {
            if (success > 0) SetPdfToWordOutput(dialog.SelectedPath);
            FinishFileConversion();
        }
    }
}
