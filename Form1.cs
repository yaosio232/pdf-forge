using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Pdf_Merger;

public partial class Form1 : Form
{
    private static readonly Color Canvas = Color.FromArgb(246, 248, 251);
    private static readonly Color Surface = Color.White;
    private static readonly Color ViewerSurface = Color.FromArgb(237, 243, 249);
    private static readonly Color Ink = Color.FromArgb(20, 31, 51);
    private static readonly Color Muted = Color.FromArgb(71, 85, 105);
    private static readonly Color Subtle = Color.FromArgb(100, 116, 139);
    private static readonly Color Border = Color.FromArgb(220, 226, 235);
    private static readonly Color Primary = Color.FromArgb(29, 78, 216);
    private static readonly Color PrimarySoft = Color.FromArgb(239, 246, 255);
    private static readonly Color PrimaryHover = Color.FromArgb(37, 99, 235);
    private static readonly Color PrimaryPressed = Color.FromArgb(30, 64, 175);
    private static readonly Color Teal = Color.FromArgb(13, 148, 136);
    private static readonly Color Danger = Color.FromArgb(185, 28, 28);
    private static readonly Bitmap AppIconBitmap = LoadAppIconBitmap();
    // Keep one dynamic page preview for every PDF. Rendering a full page stack
    // makes medium-sized files expensive to paint and can leave blank scroll gaps.
    private const int LargePdfPageThreshold = 0;
    private const int LargePreviewRenderSize = 2400;

    private readonly List<string> sourceFiles = new();
    private readonly List<string> unlockSourceFiles = new();
    private readonly PdfMergeService mergeService;
    private readonly PdfSplitService splitService = new();
    private readonly PdfUnlockService unlockService = new();
    private TabControl modeTabs = null!;
    private Button splitChooseButton = null!;
    private Button splitSaveButton = null!;
    private Label splitFileLabel = null!;
    private Label splitPageStatusLabel = null!;
    private Label splitStatusLabel = null!;
    private Label splitSummaryNumber = null!;
    private Label splitSummaryCaption = null!;
    private Label splitPartsSummaryHeader = null!;
    private FlowLayoutPanel splitPartsSummaryCards = null!;
    private Label? splitPreviewEmptyState;
    private Label? splitThumbnailTitle;
    private Label? splitThumbnailCountLabel;
    private ScrollAwarePanel splitBrowserPanel = null!;
    private FlowLayoutPanel splitPageStack = null!;
    private Panel? splitThumbnailPanel;
    private FlowLayoutPanel? splitThumbnailStack;
    private readonly Dictionary<int, Panel> splitThumbnailCards = new();
    private readonly Dictionary<int, PictureBox> splitThumbnailPictures = new();
    private readonly Dictionary<int, Panel> splitThumbnailFrames = new();
    private readonly Dictionary<int, Button> splitThumbnailScissors = new();
    private readonly Dictionary<int, Panel> splitThumbnailDividers = new();
    private readonly Dictionary<int, Label> splitThumbnailPageLabels = new();
    private NumericUpDown? splitPageJumpInput;
    private Label? splitPageTotalLabel;
    private bool splitPageJumpUpdating;
    private Panel? splitLargePageIndex;
    private Button splitZoomOutButton = null!;
    private Button splitZoomInButton = null!;
    private Button splitFitWidthButton = null!;
    private Label splitZoomLabel = null!;
    private int splitZoomPercent = 100;
    private string? splitSourceFile;
    private string? splitPreviewDirectory;
    private int splitCurrentPage;
    private int splitPageCount;
    private IReadOnlyList<string> splitRenderedPages = Array.Empty<string>();
    private readonly HashSet<int> splitAfterPages = new();
    private readonly Dictionary<string, string> splitPartNames = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> splitLargePreviewPaths = new();
    private readonly SemaphoreSlim splitLargePreviewGate = new(1, 1);
    private CancellationTokenSource? splitLargePreviewCancellation;
    private CancellationTokenSource? splitLargePreviewWarmupCancellation;
    private CancellationTokenSource? splitLargeThumbnailWarmupCancellation;
    private System.Windows.Forms.Timer? splitLargePreviewTimer;
    private Bitmap? splitLargePreviewImage;
    private int splitLargePreviewImagePage;
    private bool splitScrollToBottomAfterPreviewLoad;
    private bool isProcessing;
    private bool isDragOver;
    private bool isSplitDragOver;
    private int splitLargePreviewRequestId;
    private int splitLargePreviewRequestedPage;
    private ListBox unlockFileList = null!;
    private TextBox unlockPasswordTextBox = null!;
    private TextBox unlockOutputFolderTextBox = null!;
    private Button unlockChooseFilesButton = null!;
    private Button unlockChooseFolderButton = null!;
    private Button unlockRemoveButton = null!;
    private Button unlockStartButton = null!;
    private Label unlockCountLabel = null!;
    private Label unlockStatusLabel = null!;
    private bool isUnlockProcessing;

    public Form1()
    {
        InitializeComponent();
        var workArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(
            Math.Min(1200, Math.Max(760, workArea.Width - 32)),
            Math.Min(720, Math.Max(520, workArea.Height - 32)));
        KeyPreview = true;
        KeyDown += Form1_KeyDown;
        WindowState = FormWindowState.Maximized;
        Load += (_, _) => WindowState = FormWindowState.Maximized;
        Shown += (_, _) => BeginInvoke(new Action(() =>
        {
            WindowState = FormWindowState.Maximized;
            Activate();
        }));
        mergeService = new PdfMergeService();
        Icon = LoadAppIcon();
        BuildModeTabs();
        ConfigureInteractionStates();
        Logger.LogInfo("Main window initialized.");
        UpdateStatus();
        UpdateSplitStatus();
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control && modeTabs?.SelectedIndex == 0 && splitPageCount > 0 &&
            splitLargePageIndex is not null && ActiveControl != splitPageJumpInput &&
            e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
        {
            var delta = e.KeyCode is Keys.Up or Keys.PageUp ? -1 : 1;
            SelectSplitPage(Math.Clamp(splitCurrentPage + delta, 1, splitPageCount));
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control || modeTabs is null)
        {
            return;
        }

        if (e.KeyCode == Keys.M)
        {
            modeTabs.SelectedIndex = 1;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.S)
        {
            modeTabs.SelectedIndex = 0;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.U)
        {
            modeTabs.SelectedIndex = 2;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ConfigureInteractionStates()
    {
        ConfigureButton(btnChooseFiles, Color.White, Primary, PrimaryHover);
        ConfigureButton(btnMoveUp, Color.White, Ink, Color.FromArgb(235, 240, 247));
        ConfigureButton(btnMoveDown, Color.White, Ink, Color.FromArgb(235, 240, 247));
        ConfigureButton(btnDelete, Color.White, Danger, Color.FromArgb(254, 242, 242));
        ConfigureButton(btnDisclaimer, Color.White, Ink, Color.FromArgb(235, 240, 247));
        ConfigureButton(btnMerge, Primary, Color.White, PrimaryHover, PrimaryPressed);
        ConfigureButton(splitChooseButton, Color.White, Primary, Color.FromArgb(239, 246, 255));
        ConfigureButton(splitSaveButton, Primary, Color.White, PrimaryHover, PrimaryPressed);
        ConfigureButton(unlockChooseFilesButton, Color.White, Primary, PrimarySoft);
        ConfigureButton(unlockChooseFolderButton, Color.White, Ink, Color.FromArgb(235, 240, 247));
        ConfigureButton(unlockRemoveButton, Color.White, Danger, Color.FromArgb(254, 242, 242));
        ConfigureButton(unlockStartButton, Primary, Color.White, PrimaryHover, PrimaryPressed);
    }

    private void ConfigureButton(Button button, Color normal, Color foreground, Color hover, Color? pressed = null)
    {
        button.BackColor = normal;
        button.ForeColor = foreground;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed ?? hover;
        button.EnabledChanged += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = normal;
                button.ForeColor = foreground;
            }
            else if (button == btnMerge || button == splitSaveButton || button == unlockStartButton)
            {
                button.BackColor = Color.FromArgb(226, 232, 240);
                button.ForeColor = Color.FromArgb(100, 116, 139);
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.BorderSize = 1;
            }
        };
        button.Enter += (_, _) =>
        {
            if (button.Enabled)
            {
                button.FlatAppearance.BorderColor = foreground;
                button.FlatAppearance.BorderSize = 2;
            }
        };
        button.Leave += (_, _) =>
        {
            button.FlatAppearance.BorderSize = button == btnMerge ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
        };

        if (button == btnMerge)
        {
            button.MouseEnter += (_, _) =>
            {
                if (button.Enabled)
                {
                    button.BackColor = hover;
                    button.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
                    button.FlatAppearance.BorderSize = 2;
                }
            };
            button.MouseLeave += (_, _) =>
            {
                button.BackColor = button.Enabled ? normal : Color.FromArgb(191, 219, 254);
                button.FlatAppearance.BorderSize = 0;
            };
            button.MouseDown += (_, _) =>
            {
                if (button.Enabled)
                {
                    button.BackColor = pressed ?? hover;
                    button.FlatAppearance.BorderColor = Color.White;
                    button.FlatAppearance.BorderSize = 2;
                }
            };
            button.MouseUp += (_, _) =>
            {
                if (button.Enabled)
                {
                    button.BackColor = hover;
                    button.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
                    button.FlatAppearance.BorderSize = 2;
                }
            };
        }
    }

    private void BuildModeTabs()
    {
        mainLayout.Controls.Remove(bodyLayout);
        mainLayout.Controls.Remove(footerPanel);
        mainLayout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0F);

        modeTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(148, 38),
            Padding = new Point(18, 8),
            TabIndex = 0,
            AccessibleName = "PDF operation mode"
        };
        modeTabs.DrawItem += ModeTabs_DrawItem;

        var splitTab = new TabPage("Split PDF")
        {
            BackColor = Canvas,
            Padding = new Padding(0, 12, 0, 0),
            UseVisualStyleBackColor = false,
            AccessibleName = "Split PDF"
        };
        var mergeTab = new TabPage("Merge PDFs")
        {
            BackColor = Canvas,
            Padding = new Padding(0),
            UseVisualStyleBackColor = false,
            AccessibleName = "Merge PDFs"
        };
        var unlockTab = new TabPage("Unlock PDFs")
        {
            BackColor = Canvas,
            Padding = new Padding(0, 12, 0, 0),
            UseVisualStyleBackColor = false,
            AccessibleName = "Unlock password-protected PDFs"
        };

        bodyLayout.Dock = DockStyle.Fill;
        var mergeLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        mergeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mergeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        mergeLayout.Controls.Add(bodyLayout, 0, 0);
        mergeLayout.Controls.Add(footerPanel, 0, 1);
        mergeTab.Controls.Add(mergeLayout);
        splitTab.Controls.Add(BuildSplitWorkspace());
        unlockTab.Controls.Add(BuildUnlockWorkspace());
        modeTabs.TabPages.Add(splitTab);
        modeTabs.TabPages.Add(mergeTab);
        modeTabs.TabPages.Add(unlockTab);
        modeTabs.SelectedIndex = 0;
        modeTabs.SelectedIndexChanged += (_, _) =>
        {
            if (modeTabs.SelectedIndex == 0)
            {
                splitChooseButton.Focus();
            }
            else if (modeTabs.SelectedIndex == 2)
            {
                unlockChooseFilesButton.Focus();
            }
        };

        headerPanel.Visible = false;
        mainLayout.Padding = new Padding(12, 0, 12, 10);
        mainLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, 0F);
        var modeHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Canvas
        };
        btnDisclaimer.AutoSize = false;
        btnDisclaimer.Size = new Size(76, 30);
        btnDisclaimer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnDisclaimer.Text = "About";
        btnDisclaimer.Font = new Font("Segoe UI", 9F);
        btnDisclaimer.Margin = new Padding(0);
        modeHost.Controls.Add(modeTabs);
        modeHost.Controls.Add(btnDisclaimer);
        void PositionAboutButton() => btnDisclaimer.Location =
            new Point(Math.Max(0, modeHost.ClientSize.Width - btnDisclaimer.Width - 8), 4);
        modeHost.Resize += (_, _) => PositionAboutButton();
        PositionAboutButton();
        btnDisclaimer.BringToFront();
        mainLayout.Controls.Add(modeHost, 0, 1);
    }

    private void ModeTabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var selected = e.Index == tabs.SelectedIndex;
        var bounds = Rectangle.Inflate(e.Bounds, -3, -3);
        using var background = new SolidBrush(selected ? PrimarySoft : Surface);
        using var border = new Pen(selected ? Color.FromArgb(96, 143, 255) : Border,
            selected ? 1.5F : 1F);
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);
        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            tabs.Font,
            bounds,
            selected ? Primary : Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private Control BuildUnlockWorkspace()
    {
        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            BackColor = Canvas
        };
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 6,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(24),
            BackColor = Surface
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        card.Paint += SurfacePanel_Paint;

        var heading = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
            Text = "Batch unlock PDFs",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Remove one known password from multiple PDFs. Files stay on this computer.",
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft
        };
        heading.Controls.Add(subtitle);
        heading.Controls.Add(title);

        var fileActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 4)
        };
        unlockChooseFilesButton = new Button
        {
            Text = "Choose PDFs",
            Size = new Size(132, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Primary,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            TabIndex = 0,
            AccessibleName = "Choose password-protected PDF files"
        };
        unlockChooseFilesButton.FlatAppearance.BorderColor = Border;
        unlockChooseFilesButton.Click += UnlockChooseFiles_Click;
        unlockRemoveButton = new Button
        {
            Text = "Remove selected",
            Size = new Size(144, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Danger,
            Margin = new Padding(8, 0, 0, 0),
            TabIndex = 1,
            AccessibleName = "Remove selected unlock files"
        };
        unlockRemoveButton.FlatAppearance.BorderColor = Border;
        unlockRemoveButton.Click += (_, _) => RemoveSelectedUnlockFiles();
        unlockCountLabel = new Label
        {
            AutoSize = false,
            Size = new Size(160, 38),
            Margin = new Padding(16, 0, 0, 0),
            Text = "0 PDFs selected",
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        fileActions.Controls.Add(unlockChooseFilesButton);
        fileActions.Controls.Add(unlockRemoveButton);
        fileActions.Controls.Add(unlockCountLabel);

        unlockFileList = new ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 12),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10F),
            SelectionMode = SelectionMode.MultiExtended,
            HorizontalScrollbar = true,
            IntegralHeight = false,
            AllowDrop = true,
            TabIndex = 2,
            AccessibleName = "PDF files to unlock"
        };
        unlockFileList.SelectedIndexChanged += (_, _) => UpdateUnlockStatus();
        unlockFileList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedUnlockFiles();
                e.Handled = true;
            }
        };
        unlockFileList.DragEnter += (_, e) =>
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            e.Effect = files?.Any(IsPdfFile) == true ? DragDropEffects.Copy : DragDropEffects.None;
        };
        unlockFileList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                AddUnlockFiles(files);
            }
        };

        var passwordArea = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 0, 0)
        };
        passwordArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        passwordArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        passwordArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134F));
        passwordArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        passwordArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var passwordLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "PDF password",
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        unlockPasswordTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 4),
            UseSystemPasswordChar = true,
            TabIndex = 3,
            AccessibleName = "Known PDF password"
        };
        unlockPasswordTextBox.TextChanged += (_, _) => UpdateUnlockStatus();
        var showPassword = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Show password",
            ForeColor = Ink,
            TabIndex = 4,
            AccessibleName = "Show PDF password"
        };
        showPassword.CheckedChanged += (_, _) =>
            unlockPasswordTextBox.UseSystemPasswordChar = !showPassword.Checked;
        var passwordHelp = new Label
        {
            Dock = DockStyle.Fill,
            Text = "The same known password is applied to every selected file. PDF Forge does not guess passwords.",
            ForeColor = Subtle,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.TopLeft
        };
        passwordArea.Controls.Add(passwordLabel, 0, 0);
        passwordArea.Controls.Add(unlockPasswordTextBox, 1, 0);
        passwordArea.Controls.Add(showPassword, 2, 0);
        passwordArea.Controls.Add(passwordHelp, 1, 1);
        passwordArea.SetColumnSpan(passwordHelp, 2);

        var outputArea = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 7, 0, 7)
        };
        outputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        outputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134F));
        outputArea.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Output folder",
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        unlockOutputFolderTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            ReadOnly = true,
            BackColor = Color.White,
            TabIndex = 5,
            AccessibleName = "Unlock output folder"
        };
        unlockOutputFolderTextBox.TextChanged += (_, _) => UpdateUnlockStatus();
        unlockChooseFolderButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Choose folder",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Ink,
            Margin = new Padding(0),
            TabIndex = 6,
            AccessibleName = "Choose unlock output folder"
        };
        unlockChooseFolderButton.FlatAppearance.BorderColor = Border;
        unlockChooseFolderButton.Click += UnlockChooseFolder_Click;
        outputArea.Controls.Add(unlockOutputFolderTextBox, 1, 0);
        outputArea.Controls.Add(unlockChooseFolderButton, 2, 0);

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        unlockStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Choose encrypted PDFs to begin.",
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            AccessibleName = "Unlock status"
        };
        unlockStartButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Unlock PDFs",
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            Margin = new Padding(8, 0, 0, 0),
            TabIndex = 7,
            AccessibleName = "Unlock selected PDF files"
        };
        unlockStartButton.FlatAppearance.BorderSize = 0;
        unlockStartButton.Click += UnlockStart_Click;
        footer.Controls.Add(unlockStatusLabel, 0, 0);
        footer.Controls.Add(unlockStartButton, 1, 0);

        card.Controls.Add(heading, 0, 0);
        card.Controls.Add(fileActions, 0, 1);
        card.Controls.Add(unlockFileList, 0, 2);
        card.Controls.Add(passwordArea, 0, 3);
        card.Controls.Add(outputArea, 0, 4);
        card.Controls.Add(footer, 0, 5);
        workspace.Controls.Add(card);
        UpdateUnlockStatus();
        return workspace;
    }

    private void UnlockChooseFiles_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Choose password-protected PDFs"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RestoreMaximizedWindow();
            AddUnlockFiles(dialog.FileNames);
        }
    }

    private void UnlockChooseFolder_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for unlocked PDF copies",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(unlockOutputFolderTextBox.Text))
        {
            dialog.SelectedPath = unlockOutputFolderTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RestoreMaximizedWindow();
            unlockOutputFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void AddUnlockFiles(IEnumerable<string> files)
    {
        foreach (var file in files.Where(IsPdfFile))
        {
            var fullPath = Path.GetFullPath(file);
            if (unlockSourceFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            unlockSourceFiles.Add(fullPath);
            unlockFileList.Items.Add(fullPath);
        }

        if (unlockSourceFiles.Count > 0 && string.IsNullOrWhiteSpace(unlockOutputFolderTextBox.Text))
        {
            unlockOutputFolderTextBox.Text = Path.Combine(
                Path.GetDirectoryName(unlockSourceFiles[0])!, "Unlocked");
        }

        UpdateUnlockStatus();
    }

    private static bool IsPdfFile(string path) =>
        File.Exists(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    private void RemoveSelectedUnlockFiles()
    {
        foreach (var index in unlockFileList.SelectedIndices.Cast<int>().OrderByDescending(index => index))
        {
            unlockSourceFiles.RemoveAt(index);
            unlockFileList.Items.RemoveAt(index);
        }

        UpdateUnlockStatus();
    }

    private async void UnlockStart_Click(object? sender, EventArgs e)
    {
        if (isUnlockProcessing || unlockSourceFiles.Count == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(unlockPasswordTextBox.Text))
        {
            unlockStatusLabel.Text = "Enter the known PDF password.";
            unlockPasswordTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(unlockOutputFolderTextBox.Text))
        {
            unlockStatusLabel.Text = "Choose an output folder.";
            unlockChooseFolderButton.Focus();
            return;
        }

        var files = unlockSourceFiles.ToList();
        var password = unlockPasswordTextBox.Text;
        var outputFolder = unlockOutputFolderTextBox.Text;
        SetUnlockProcessing(true);
        try
        {
            var result = await Task.Run(() => unlockService.Unlock(files, password, outputFolder));
            if (result.Succeeded && result.Failures.Count == 0)
            {
                unlockPasswordTextBox.Clear();
            }

            ShowUnlockResult(result);
        }
        catch (Exception exception)
        {
            Logger.LogError("Unexpected UI unlock error.", exception);
            unlockStatusLabel.Text = "Unlock failed. Check the password and try again.";
            MessageBox.Show(exception.Message, "Unlock failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            password = string.Empty;
            SetUnlockProcessing(false);
        }
    }

    private void ShowUnlockResult(UnlockResult result)
    {
        var failureDetails = string.Join(Environment.NewLine,
            result.Failures.Take(8).Select(failure =>
                $"{Path.GetFileName(failure.SourcePath)}: {failure.Message}"));
        if (!result.Succeeded)
        {
            unlockStatusLabel.Text = "No PDF was unlocked. Check the password and selected files.";
            MessageBox.Show(
                string.IsNullOrWhiteSpace(failureDetails)
                    ? result.ErrorMessage ?? "No PDF could be unlocked."
                    : failureDetails,
                "Unlock failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        unlockStatusLabel.Text = result.Failures.Count == 0
            ? $"Unlocked {result.Outputs.Count} PDF(s) to {Path.GetDirectoryName(result.Outputs[0].OutputPath)}."
            : $"Unlocked {result.Outputs.Count} PDF(s); {result.Failures.Count} failed.";
        var message = $"{result.Outputs.Count} unlocked PDF(s) created.";
        if (result.Failures.Count > 0)
        {
            message += $"\r\n\r\n{failureDetails}";
        }
        MessageBox.Show(message,
            result.Failures.Count == 0 ? "Unlock complete" : "Unlock completed with warnings",
            MessageBoxButtons.OK,
            result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void SetUnlockProcessing(bool processing)
    {
        isUnlockProcessing = processing;
        modeTabs.Enabled = !processing;
        btnDisclaimer.Enabled = !processing;
        unlockChooseFilesButton.Enabled = !processing;
        unlockChooseFolderButton.Enabled = !processing;
        unlockFileList.Enabled = !processing;
        unlockPasswordTextBox.Enabled = !processing;
        unlockStartButton.Text = processing ? "Unlocking…" : "Unlock PDFs";
        UpdateUnlockStatus();
    }

    private void UpdateUnlockStatus()
    {
        if (unlockFileList is null || unlockCountLabel is null || unlockStartButton is null)
        {
            return;
        }

        unlockCountLabel.Text = $"{unlockSourceFiles.Count} PDF{(unlockSourceFiles.Count == 1 ? "" : "s")} selected";
        unlockRemoveButton.Enabled = !isUnlockProcessing && unlockFileList.SelectedIndices.Count > 0;
        unlockStartButton.Enabled = !isUnlockProcessing && unlockSourceFiles.Count > 0 &&
                                    !string.IsNullOrEmpty(unlockPasswordTextBox.Text) &&
                                    !string.IsNullOrWhiteSpace(unlockOutputFolderTextBox.Text);
        if (isUnlockProcessing)
        {
            unlockStatusLabel.Text = $"Unlocking {unlockSourceFiles.Count} PDF(s)… keep this window open.";
        }
        else if (unlockSourceFiles.Count == 0)
        {
            unlockStatusLabel.Text = "Choose encrypted PDFs to begin.";
        }
    }

    private Control BuildSplitWorkspace()
    {
        var splitLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Canvas
        };
        splitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
        splitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        splitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350F));
        splitLayout.SizeChanged += (_, _) =>
        {
            var compact = splitLayout.ClientSize.Width < 1100;
            splitLayout.ColumnStyles[0].Width = compact ? 176F : 210F;
            splitLayout.ColumnStyles[2].Width = compact ? 292F : 350F;
        };

        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            BackColor = Canvas
        };
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = Surface
        };
        toolbar.Paint += SurfacePanel_Paint;

        var toolbarLayout = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196F));
        toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        splitChooseButton = new Button
        {
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Text = "Open PDF",
            AccessibleName = "Choose PDF to split",
            TabIndex = 0,
            UseVisualStyleBackColor = false
        };
        splitChooseButton.Click += SplitChooseButton_Click;

        splitFileLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Ink,
            Padding = new Padding(10, 0, 8, 0),
            Text = "Drop PDF here or click Open PDF",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        splitPageStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Muted,
            Padding = new Padding(10, 0, 0, 0),
            Text = "0 pages · 0 split points",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            UseCompatibleTextRendering = true
        };
        splitStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Muted,
            Text = "Drop a PDF here or click Open PDF to start.",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseCompatibleTextRendering = false
        };

        toolbarLayout.Controls.Add(splitChooseButton, 0, 0);
        toolbarLayout.SetRowSpan(splitChooseButton, 2);
        toolbarLayout.Controls.Add(splitFileLabel, 1, 0);
        toolbarLayout.Controls.Add(splitStatusLabel, 1, 1);

        var pageNavigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        var pageLabel = new Label
        {
            AutoSize = false,
            Width = 56,
            Height = 30,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Muted,
            Text = "Page",
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 7, 4, 0)
        };
        splitPageJumpInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1,
            Value = 1,
            Width = 68,
            Height = 30,
            Font = new Font("Segoe UI", 9F),
            TextAlign = HorizontalAlignment.Center,
            AccessibleName = "Jump to PDF page",
            AccessibleDescription = "Enter a page number to jump to that page",
            TabIndex = 2
        };
        splitPageJumpInput.ValueChanged += (_, _) =>
        {
            if (!splitPageJumpUpdating && splitPageCount > 0)
            {
                SelectSplitPage((int)splitPageJumpInput.Value);
            }
        };
        splitPageTotalLabel = new Label
        {
            AutoSize = false,
            Width = 58,
            Height = 30,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Ink,
            Text = "/ 0",
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(3, 7, 0, 0)
        };
        pageNavigation.Controls.Add(pageLabel);
        pageNavigation.Controls.Add(splitPageJumpInput);
        pageNavigation.Controls.Add(splitPageTotalLabel);
        toolbarLayout.Controls.Add(pageNavigation, 2, 0);
        toolbarLayout.SetRowSpan(pageNavigation, 2);

        var zoomControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        splitZoomOutButton = CreateViewerButton("−", "Zoom out");
        splitZoomInButton = CreateViewerButton("+", "Zoom in");
        splitFitWidthButton = CreateViewerButton("Fit", "Fit page");
        splitZoomLabel = new Label
        {
            AutoSize = false,
            Width = 56,
            Height = 30,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Ink,
            Text = "100%",
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(2, 0, 2, 0),
            AccessibleName = "PDF zoom level"
        };
        splitZoomOutButton.Click += (_, _) => SetSplitZoom(splitZoomPercent - 10);
        splitZoomInButton.Click += (_, _) => SetSplitZoom(splitZoomPercent + 10);
        splitFitWidthButton.Click += (_, _) => SetSplitZoom(100);
        zoomControls.Controls.Add(splitZoomOutButton);
        zoomControls.Controls.Add(splitZoomLabel);
        zoomControls.Controls.Add(splitZoomInButton);
        zoomControls.Controls.Add(splitFitWidthButton);
        toolbarLayout.Controls.Add(zoomControls, 3, 0);
        toolbarLayout.SetRowSpan(zoomControls, 2);
        toolbar.Controls.Add(toolbarLayout);

        void ApplyToolbarLayout()
        {
            var compact = toolbar.ClientSize.Width < 620;
            toolbar.Height = compact ? 116 : 78;
            toolbarLayout.RowCount = compact ? 3 : 2;
            toolbarLayout.ColumnStyles.Clear();
            toolbarLayout.RowStyles.Clear();

            if (compact)
            {
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0F));
                toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

                splitPageJumpInput.Width = 48;
                pageLabel.AutoSize = false;
                pageLabel.Width = 36;
                pageLabel.Font = new Font("Segoe UI", 7.5F);
                pageLabel.Margin = new Padding(0, 7, 1, 0);
                splitPageTotalLabel.Width = 32;
                splitPageTotalLabel.Margin = new Padding(1, 7, 0, 0);
                splitZoomOutButton.Width = 20;
                splitZoomInButton.Width = 20;
                splitFitWidthButton.Width = 40;
                splitFitWidthButton.Font = new Font("Segoe UI Semibold", 7F, FontStyle.Bold);
                splitFitWidthButton.Padding = new Padding(0);
                splitZoomLabel.Width = 30;
                splitZoomOutButton.Margin = new Padding(0);
                splitZoomInButton.Margin = new Padding(0);
                splitFitWidthButton.Margin = new Padding(0);
                splitZoomLabel.Margin = new Padding(0);

                toolbarLayout.SetCellPosition(splitChooseButton, new TableLayoutPanelCellPosition(0, 0));
                toolbarLayout.SetRowSpan(splitChooseButton, 2);
                toolbarLayout.SetCellPosition(splitFileLabel, new TableLayoutPanelCellPosition(1, 0));
                toolbarLayout.SetColumnSpan(splitFileLabel, 2);
                toolbarLayout.SetCellPosition(splitStatusLabel, new TableLayoutPanelCellPosition(1, 1));
                toolbarLayout.SetColumnSpan(splitStatusLabel, 2);
                toolbarLayout.SetCellPosition(pageNavigation, new TableLayoutPanelCellPosition(1, 2));
                toolbarLayout.SetRowSpan(pageNavigation, 1);
                toolbarLayout.SetColumnSpan(pageNavigation, 1);
                toolbarLayout.SetCellPosition(zoomControls, new TableLayoutPanelCellPosition(2, 2));
                toolbarLayout.SetRowSpan(zoomControls, 1);
                toolbarLayout.SetColumnSpan(zoomControls, 1);
            }
            else
            {
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194F));
                toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196F));
                toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

                splitPageJumpInput.Width = 68;
                pageLabel.AutoSize = false;
                pageLabel.Width = 56;
                pageLabel.Font = new Font("Segoe UI", 8.5F);
                pageLabel.Margin = new Padding(0, 7, 4, 0);
                splitPageTotalLabel.Width = 58;
                splitPageTotalLabel.Margin = new Padding(3, 7, 0, 0);
                splitZoomOutButton.Width = 28;
                splitZoomInButton.Width = 28;
                splitFitWidthButton.Width = 52;
                splitFitWidthButton.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
                splitFitWidthButton.Padding = new Padding(0);
                splitZoomLabel.Width = 56;
                splitZoomOutButton.Margin = new Padding(2, 0, 2, 0);
                splitZoomInButton.Margin = new Padding(2, 0, 2, 0);
                splitFitWidthButton.Margin = new Padding(2, 0, 2, 0);
                splitZoomLabel.Margin = new Padding(2, 0, 2, 0);

                toolbarLayout.SetCellPosition(splitChooseButton, new TableLayoutPanelCellPosition(0, 0));
                toolbarLayout.SetRowSpan(splitChooseButton, 2);
                toolbarLayout.SetCellPosition(splitFileLabel, new TableLayoutPanelCellPosition(1, 0));
                toolbarLayout.SetColumnSpan(splitFileLabel, 1);
                toolbarLayout.SetCellPosition(splitStatusLabel, new TableLayoutPanelCellPosition(1, 1));
                toolbarLayout.SetColumnSpan(splitStatusLabel, 1);
                toolbarLayout.SetCellPosition(pageNavigation, new TableLayoutPanelCellPosition(2, 0));
                toolbarLayout.SetRowSpan(pageNavigation, 2);
                toolbarLayout.SetColumnSpan(pageNavigation, 1);
                toolbarLayout.SetCellPosition(zoomControls, new TableLayoutPanelCellPosition(3, 0));
                toolbarLayout.SetRowSpan(zoomControls, 2);
                toolbarLayout.SetColumnSpan(zoomControls, 1);
            }
        }

        toolbar.SizeChanged += (_, _) => ApplyToolbarLayout();
        ApplyToolbarLayout();

        splitBrowserPanel = new ScrollAwarePanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = ViewerSurface,
            Padding = new Padding(0, 18, 0, 18),
            AllowDrop = true,
            TabIndex = 1,
            AccessibleName = "PDF page browser"
        };
        splitBrowserPanel.DragEnter += SplitDropTarget_DragEnter;
        splitBrowserPanel.DragOver += SplitDropTarget_DragOver;
        splitBrowserPanel.DragLeave += SplitDropTarget_DragLeave;
        splitBrowserPanel.DragDrop += SplitDropTarget_DragDrop;
        splitBrowserPanel.Paint += SplitBrowserPanel_Paint;
        splitBrowserPanel.Resize += (_, _) => ResizeSplitPageStack();
        splitBrowserPanel.Scroll += (_, _) => UpdateCurrentPageFromScroll();
        splitBrowserPanel.MouseWheel += SplitPreview_MouseWheel;
        splitBrowserPanel.ScrollChanged += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdateCurrentPageFromScroll));
            }
        };
        splitPageStack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AllowDrop = true,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0),
            TabStop = false
        };
        splitPageStack.DragEnter += SplitDropTarget_DragEnter;
        splitPageStack.DragOver += SplitDropTarget_DragOver;
        splitPageStack.DragLeave += SplitDropTarget_DragLeave;
        splitPageStack.DragDrop += SplitDropTarget_DragDrop;
        splitBrowserPanel.Controls.Add(splitPageStack);
        splitPreviewEmptyState = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = Muted,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            Text = "Open a PDF to preview pages\r\n\r\nYou can also drag and drop a PDF here.",
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = "Split preview empty state",
            TabStop = false,
            AllowDrop = true
        };
        splitPreviewEmptyState.DragEnter += SplitDropTarget_DragEnter;
        splitPreviewEmptyState.DragOver += SplitDropTarget_DragOver;
        splitPreviewEmptyState.DragLeave += SplitDropTarget_DragLeave;
        splitPreviewEmptyState.DragDrop += SplitDropTarget_DragDrop;
        splitBrowserPanel.Controls.Add(splitPreviewEmptyState);
        splitPreviewEmptyState.BringToFront();
        workspace.Controls.Add(splitBrowserPanel);
        workspace.Controls.Add(toolbar);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true,
            Padding = new Padding(12, 0, 0, 0) };

        var summary = new Panel
        {
            Dock = DockStyle.Top,
            Height = 116,
            Margin = new Padding(0, 14, 0, 0),
            BackColor = Color.FromArgb(239, 246, 255),
            Padding = new Padding(20)
        };
        summary.Paint += SurfacePanel_Paint;
        var summaryTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
            ForeColor = Subtle,
            Location = new Point(20, 8),
            Text = "SPLIT SUMMARY",
            TextAlign = ContentAlignment.MiddleLeft
        };
        splitSummaryNumber = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 30F, FontStyle.Bold),
            ForeColor = Primary,
            Location = new Point(20, 24),
            Text = "0"
        };
        splitSummaryCaption = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Primary,
            Location = new Point(20, 78),
            Size = new Size(210, 30),
            Text = "PDF pages loaded",
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        summary.Controls.Add(summaryTitle);
        summary.Controls.Add(splitSummaryNumber);
        summary.Controls.Add(splitSummaryCaption);

        var actions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 106,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = Surface
        };
        splitSaveButton = new Button
        {
            Dock = DockStyle.Top,
            Height = 50,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            Text = "Save 1 split PDF",
            AccessibleName = "Save split PDF parts",
            TabIndex = 2,
            UseVisualStyleBackColor = false
        };
        splitSaveButton.Click += SplitSaveButton_Click;
        var saveHint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Muted,
            Padding = new Padding(4, 8, 4, 0),
            Text = "✓  Original file stays unchanged.",
            TextAlign = ContentAlignment.TopLeft,
            UseCompatibleTextRendering = true
        };
        actions.Controls.Add(saveHint);
        actions.Controls.Add(splitSaveButton);

        var partsSummary = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 12),
            BackColor = Surface
        };
        splitPartsSummaryHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Ink,
            Text = "Split results",
            TextAlign = ContentAlignment.MiddleLeft
        };
        splitPartsSummaryCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 4, 6, 4),
            Margin = new Padding(0),
            BackColor = Surface
        };
        splitPartsSummaryCards.Resize += (_, _) => ResizeSplitResultCards();
        splitPartsSummaryCards.Controls.Add(CreateSplitResultEmptyState());
        partsSummary.Controls.Add(splitPartsSummaryCards);
        partsSummary.Controls.Add(splitPartsSummaryHeader);

        sidebar.Controls.Add(actions);
        sidebar.Controls.Add(partsSummary);
        var thumbnailSidebar = BuildSplitThumbnailSidebar();
        splitLayout.Controls.Add(thumbnailSidebar, 0, 0);
        splitLayout.Controls.Add(workspace, 1, 0);
        splitLayout.Controls.Add(sidebar, 2, 0);

        foreach (var control in new Control[] { workspace, toolbar, toolbarLayout, splitFileLabel, splitStatusLabel })
        {
            control.AllowDrop = true;
            control.DragEnter += SplitDropTarget_DragEnter;
            control.DragOver += SplitDropTarget_DragOver;
            control.DragLeave += SplitDropTarget_DragLeave;
            control.DragDrop += SplitDropTarget_DragDrop;
        }

        splitLayout.PerformLayout();
        return splitLayout;
    }

    private Control BuildSplitThumbnailSidebar()
    {
        splitThumbnailPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(10, 0, 8, 0),
            AutoScroll = false,
            AccessibleName = "PDF page thumbnails"
        };
        splitThumbnailPanel.Paint += SurfacePanel_Paint;
        var thumbnailHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(248, 250, 252)
        };
        splitThumbnailTitle = new Label
        {
            Dock = DockStyle.Left,
            Width = 64,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Ink,
            Text = "Pages",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            AccessibleName = "PDF page count"
        };
        splitThumbnailCountLabel = new Label
        {
            AutoSize = false,
            Size = new Size(42, 24),
            Location = new Point(66, 9),
            BackColor = Color.FromArgb(226, 232, 240),
            ForeColor = Muted,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            Text = "0",
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = "PDF total pages"
        };
        thumbnailHeader.Controls.Add(splitThumbnailCountLabel);
        thumbnailHeader.Controls.Add(splitThumbnailTitle);
        splitThumbnailStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 12),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(248, 250, 252)
        };
        splitThumbnailStack.Resize += (_, _) => ResizeSplitThumbnails();
        splitThumbnailPanel.Controls.Add(splitThumbnailStack);
        splitThumbnailPanel.Controls.Add(thumbnailHeader);
        return splitThumbnailPanel;
    }

    private void BuildSplitThumbnails()
    {
        if (splitThumbnailStack is null)
        {
            return;
        }

        ClearSplitThumbnails();
        for (var page = 1; page <= splitPageCount; page++)
        {
            var thumbnailPage = page;
            var card = new TableLayoutPanel
            {
                Height = 142,
                Width = Math.Max(1, splitThumbnailStack.ClientSize.Width - 2),
                BackColor = Surface,
                Margin = new Padding(0),
                Tag = page,
                AccessibleName = $"Thumbnail for PDF page {page}",
                TabStop = true,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            var previewFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 6, 8, 4),
                Padding = new Padding(2),
                BackColor = Border,
                Tag = page,
                TabStop = false
            };
            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom,
                Tag = page,
                AccessibleName = $"Preview of PDF page {page}"
            };
            previewFrame.Controls.Add(picture);
            var scissors = new Button
            {
                Size = new Size(44, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 10F),
                Text = "✂",
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = page,
                AccessibleName = $"Split after page {page}",
                AccessibleDescription = "Add or remove a split point after this page",
                Enabled = true,
                UseVisualStyleBackColor = false,
                BackColor = Color.White,
                ForeColor = Primary,
                TabStop = true
            };
            scissors.FlatAppearance.BorderSize = 0;
            var pageNumber = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Muted,
                Text = page.ToString(),
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = $"Page number {page}",
                TabStop = false,
                Margin = new Padding(0)
            };
            var thumbnailDivider = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Surface,
                Tag = page,
                TabStop = false
            };
            thumbnailDivider.Controls.Add(scissors);
            thumbnailDivider.Resize += (_, _) => scissors.Location = new Point(
                Math.Max(0, (thumbnailDivider.ClientSize.Width - scissors.Width) / 2), 2);
            thumbnailDivider.Paint += (_, e) =>
            {
                if (!splitAfterPages.Contains(thumbnailPage))
                {
                    return;
                }

                using var pen = new Pen(Color.FromArgb(37, 99, 235), 1.5F)
                {
                    DashStyle = DashStyle.Dash
                };
                var y = thumbnailDivider.ClientSize.Height / 2;
                e.Graphics.DrawLine(pen, 4, y, Math.Max(4, scissors.Left - 6), y);
                e.Graphics.DrawLine(pen, scissors.Right + 6, y,
                    Math.Max(scissors.Right + 6, thumbnailDivider.ClientSize.Width - 4), y);
            };
            var lastThumbnailActivation = DateTime.MinValue;
            void ActivateThumbnailSplit()
            {
                if (DateTime.UtcNow - lastThumbnailActivation < TimeSpan.FromMilliseconds(120))
                {
                    return;
                }

                lastThumbnailActivation = DateTime.UtcNow;
                ToggleSplitPoint(thumbnailPage);
            }

            scissors.Click += (_, _) => ActivateThumbnailSplit();
            thumbnailDivider.Click += (_, _) => ActivateThumbnailSplit();
            card.MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (e.Y >= card.ClientSize.Height - 42)
                {
                    ToggleSplitPoint(thumbnailPage);
                }
                else
                {
                    SelectSplitPage(thumbnailPage);
                }
            };
            picture.Click += (_, _) => SelectSplitPage(thumbnailPage);
            previewFrame.Click += (_, _) => SelectSplitPage(thumbnailPage);
            pageNumber.Click += (_, _) => SelectSplitPage(thumbnailPage);
            card.Controls.Add(pageNumber, 0, 0);
            card.Controls.Add(previewFrame, 1, 0);
            card.Controls.Add(thumbnailDivider, 0, 1);
            card.SetColumnSpan(thumbnailDivider, 2);
            splitThumbnailCards[page] = card;
            splitThumbnailPictures[page] = picture;
            splitThumbnailFrames[page] = previewFrame;
            splitThumbnailScissors[page] = scissors;
            splitThumbnailDividers[page] = thumbnailDivider;
            splitThumbnailPageLabels[page] = pageNumber;
            splitThumbnailStack.Controls.Add(card);
        }

        ResizeSplitThumbnails();
        UpdateSplitThumbnailSelection();
    }

    private void ResizeSplitThumbnails()
    {
        if (splitThumbnailStack is null)
        {
            return;
        }

        var width = Math.Max(1, splitThumbnailStack.ClientSize.Width - 2);
        foreach (var card in splitThumbnailCards.Values)
        {
            card.Width = width;
        }
    }

    private void UpdateSplitThumbnailSelection()
    {
        foreach (var pair in splitThumbnailCards)
        {
            var selected = pair.Key == splitCurrentPage;
            pair.Value.BackColor = selected ? Color.FromArgb(232, 242, 255) : Surface;
            if (splitThumbnailPageLabels.TryGetValue(pair.Key, out var pageLabel))
            {
                pageLabel.ForeColor = selected ? Primary : Muted;
                pageLabel.BackColor = selected ? Color.FromArgb(232, 242, 255) : Surface;
            }
            if (splitThumbnailFrames.TryGetValue(pair.Key, out var frame))
            {
                frame.BackColor = selected ? Color.FromArgb(37, 99, 235) : Border;
            }
            if (splitThumbnailDividers.TryGetValue(pair.Key, out var divider))
            {
                divider.BackColor = selected ? Color.FromArgb(232, 242, 255) : Surface;
            }
            if (splitThumbnailScissors.TryGetValue(pair.Key, out var scissors) &&
                !splitAfterPages.Contains(pair.Key))
            {
                scissors.BackColor = selected ? Color.FromArgb(232, 242, 255) : Surface;
            }
            if (selected && splitThumbnailStack is not null)
            {
                splitThumbnailStack.ScrollControlIntoView(pair.Value);
            }
        }
    }

    private void RefreshSplitThumbnail(int page, string path)
    {
        if (!splitThumbnailPictures.TryGetValue(page, out var picture) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sourceImage = new Bitmap(stream);
            picture.Image?.Dispose();
            picture.Image = new Bitmap(sourceImage);
        }
        catch (Exception exception)
        {
            Logger.LogError($"Loading thumbnail {page} failed.", exception);
        }
    }

    private void ClearSplitThumbnails()
    {
        foreach (var picture in splitThumbnailPictures.Values)
        {
            picture.Image?.Dispose();
            picture.Image = null;
        }

        splitThumbnailPictures.Clear();
        splitThumbnailFrames.Clear();
        splitThumbnailScissors.Clear();
        splitThumbnailDividers.Clear();
        splitThumbnailPageLabels.Clear();
        splitThumbnailCards.Clear();
        if (splitThumbnailStack is null)
        {
            return;
        }

        while (splitThumbnailStack.Controls.Count > 0)
        {
            var control = splitThumbnailStack.Controls[0];
            splitThumbnailStack.Controls.RemoveAt(0);
            control.Dispose();
        }
    }

    private static string? GetDroppedPdf(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return null;
        }

        return Path.GetExtension(files[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;
    }

    private void SplitDropTarget_DragEnter(object? sender, DragEventArgs e)
    {
        var valid = !isProcessing && GetDroppedPdf(e.Data) is not null;
        e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
        if (valid && !isSplitDragOver)
        {
            isSplitDragOver = true;
            splitStatusLabel.Text = "Release to open this PDF.";
            splitBrowserPanel.Invalidate();
        }
    }

    private void SplitDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        SplitDropTarget_DragEnter(sender, e);
    }

    private void SplitDropTarget_DragLeave(object? sender, EventArgs e)
    {
        isSplitDragOver = false;
        splitBrowserPanel.Invalidate();
        if (!isProcessing)
        {
            if (splitPageCount == 0)
            {
                splitStatusLabel.Text = "Drop a PDF here or click Open PDF to start.";
            }
            else
            {
                UpdateSplitStatus();
            }
        }
    }

    private void SplitDropTarget_DragDrop(object? sender, DragEventArgs e)
    {
        isSplitDragOver = false;
        splitBrowserPanel.Invalidate();
        var path = GetDroppedPdf(e.Data);
        if (path is not null)
        {
            LoadSplitPdfAsync(path);
            return;
        }

        splitStatusLabel.Text = "Drop one PDF file to open it.";
    }

    private Button CreateViewerButton(string text, string accessibleName)
    {
        var button = new Button
        {
            AutoSize = false,
            Width = text == "Fit" ? 44 : 28,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Text = text,
            AccessibleName = accessibleName,
            Margin = new Padding(2, 0, 2, 0),
            TabStop = true,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = PrimarySoft;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(219, 234, 254);
        return button;
    }

    private void SplitChooseButton_Click(object? sender, EventArgs e)
    {
        if (isProcessing)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = false,
            Title = "Choose a PDF to split"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RestoreMaximizedWindow();
            LoadSplitPdfAsync(dialog.FileName);
        }
    }

    private void RestoreMaximizedWindow()
    {
        WindowState = FormWindowState.Maximized;
        BeginInvoke(new Action(() =>
        {
            WindowState = FormWindowState.Maximized;
            Activate();
        }));
    }

    private async void LoadSplitPdfAsync(string path)
    {
        SetSplitProcessing(true);
        ClearSplitPreview();
        splitSourceFile = null;
        splitPageCount = 0;
        splitCurrentPage = 0;
        splitAfterPages.Clear();
        splitFileLabel.Text = Path.GetFileName(path);
        splitStatusLabel.Text = "Reading PDF pages…";
        try
        {
            var fullPath = Path.GetFullPath(path);
            var pageCount = await Task.Run(() => PdfSplitService.GetPageCount(fullPath));
            var previewDirectory = Path.Combine(Path.GetTempPath(), "PdfMergerPreview", Guid.NewGuid().ToString("N"));
            splitSourceFile = fullPath;
            splitPageCount = pageCount;
            splitCurrentPage = pageCount > 0 ? 1 : 0;
            splitPreviewDirectory = previewDirectory;
            BuildSplitThumbnails();
            if (splitPageJumpInput is not null)
            {
                splitPageJumpUpdating = true;
                splitPageJumpInput.Maximum = Math.Max(1, pageCount);
                splitPageJumpInput.Value = pageCount > 0 ? 1 : 1;
                splitPageJumpUpdating = false;
            }
            if (splitPageTotalLabel is not null)
            {
                splitPageTotalLabel.Text = $"/ {pageCount}";
            }
            if (pageCount > LargePdfPageThreshold)
            {
                BuildLargeSplitIndex();
                splitStatusLabel.Text = "Pages loaded. Loading the first page…";
                UpdateSplitStatus();
                RequestLargePagePreview(splitCurrentPage, immediate: true);
                StartLargeThumbnailWarmup(fullPath, previewDirectory, 7,
                    Math.Max(0, pageCount - 6));
            }
            else
            {
                BuildSplitPages(Array.Empty<string>());
                splitStatusLabel.Text = "Pages loaded. Rendering previews…";
                UpdateSplitStatus();
            }

            if (pageCount > LargePdfPageThreshold)
            {
                splitStatusLabel.Text = "Preview loads one page at a time as you scroll. Click a scissors icon to add a cut.";
            }
            else
            {
                var renderedPages = await Task.Run(() => PdfPageRenderer.RenderPages(
                    fullPath, previewDirectory, pageCount, 1200));
                BuildSplitPages(renderedPages);
                splitStatusLabel.Text = renderedPages.Count == pageCount
                    ? "Preview ready. Click a scissors icon to add a cut."
                    : "Preview unavailable. Cut points still work.";
            }
            SelectSplitPage(splitCurrentPage);
            Logger.LogInfo($"Loaded '{fullPath}' for splitting ({pageCount} page(s)).");
        }
        catch (Exception exception)
        {
            Logger.LogError($"Loading split source '{path}' failed.", exception);
            splitFileLabel.Text = "Drop PDF here or click Open PDF";
            splitStatusLabel.Text = "Could not read this PDF.";
            MessageBox.Show(exception.Message, "PDF could not be opened",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetSplitProcessing(false);
            UpdateSplitStatus();
            RestoreMaximizedWindow();
        }
    }

    private void BuildSplitPages(IReadOnlyList<string> renderedPages)
    {
        splitRenderedPages = renderedPages;
        SetSplitPreviewEmptyStateVisible(false);
        RemoveLargeSplitIndex();
        splitPageStack.Visible = true;
        ClearSplitPageControls();
        splitPageStack.SuspendLayout();
        try
        {
            for (var page = 1; page <= splitPageCount; page++)
            {
                var imagePath = page <= splitRenderedPages.Count ? splitRenderedPages[page - 1] : null;
                splitPageStack.Controls.Add(CreateSplitPageCard(page, imagePath));
                if (page < splitPageCount)
                {
                    splitPageStack.Controls.Add(CreateSplitDivider(page));
                }
            }
        }
        finally
        {
            splitPageStack.ResumeLayout(true);
        }

        ResizeSplitPageStack();
    }

    private void BuildLargeSplitIndex()
    {
        RemoveLargeSplitIndex();
        SetSplitPreviewEmptyStateVisible(false);
        ClearSplitPageControls();
        splitPageStack.Visible = false;

        splitLargePageIndex = new ScrollAwarePanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = ViewerSurface,
            TabIndex = 1,
            TabStop = true,
            AccessibleName = "Large PDF page index"
        };
        splitLargePageIndex.Paint += LargeSplitPageIndex_Paint;
        splitLargePageIndex.MouseClick += LargeSplitPageIndex_MouseClick;
        splitLargePageIndex.MouseWheel += LargeSplitPageIndex_MouseWheel;
        splitLargePageIndex.KeyDown += PageNavigation_KeyDown;
        splitLargePageIndex.Resize += (_, _) => ResizeLargeSplitPageIndex();
        splitBrowserPanel.Controls.Add(splitLargePageIndex);
        splitLargePageIndex.BringToFront();
        splitLargePreviewTimer ??= new System.Windows.Forms.Timer { Interval = 220 };
        splitLargePreviewTimer.Tick -= LargePreviewTimer_Tick;
        splitLargePreviewTimer.Tick += LargePreviewTimer_Tick;
        ResizeLargeSplitPageIndex();
    }

    private void LargePreviewTimer_Tick(object? sender, EventArgs e)
    {
        splitLargePreviewTimer?.Stop();
        if (splitLargePreviewRequestedPage <= 0 || splitLargePageIndex is null ||
            string.IsNullOrWhiteSpace(splitSourceFile) || string.IsNullOrWhiteSpace(splitPreviewDirectory))
        {
            return;
        }

        splitLargePreviewCancellation?.Dispose();
        splitLargePreviewCancellation = new CancellationTokenSource();
        var requestId = ++splitLargePreviewRequestId;
        Logger.LogInfo($"Preview request started for page {splitLargePreviewRequestedPage} (request {requestId}).");
        _ = LoadLargePagePreviewAsync(splitSourceFile, splitPreviewDirectory,
            splitLargePreviewRequestedPage, requestId, splitLargePreviewCancellation.Token);
    }

    private void RemoveLargeSplitIndex()
    {
        if (splitLargePageIndex is null)
        {
            return;
        }

        splitBrowserPanel.Controls.Remove(splitLargePageIndex);
        splitLargePageIndex.Dispose();
        splitLargePageIndex = null;
    }

    private void ResizeLargeSplitPageIndex()
    {
        if (splitLargePageIndex is null)
        {
            return;
        }

        var pageSize = GetLargePageSize(splitLargePageIndex);
        splitLargePageIndex.AutoScrollMinSize = splitZoomPercent > 100
            ? new Size(pageSize.Width + 64, pageSize.Height + 86)
            : Size.Empty;
        splitLargePageIndex.Invalidate();
    }

    private const int LargeSplitRowHeight = 860;

    private Size GetLargePageSize(Panel panel)
    {
        var availableWidth = Math.Max(320,
            panel.ClientSize.Width - 28);
        var availableHeight = Math.Max(240, panel.ClientSize.Height - 86);
        var aspectRatio = splitLargePreviewImage is { Width: > 0, Height: > 0 }
            ? (double)splitLargePreviewImage.Height / splitLargePreviewImage.Width
            : 1.414D;
        var fitWidth = Math.Min(availableWidth - 32, availableHeight / aspectRatio);
        var pageWidth = Math.Max(180,
            (int)Math.Round(fitWidth * splitZoomPercent / 100D));
        var pageHeight = Math.Max(180, (int)Math.Round(pageWidth * aspectRatio));
        return new Size(pageWidth, pageHeight);
    }

    private Rectangle GetLargePageBounds(Panel panel)
    {
        var pageSize = GetLargePageSize(panel);
        var contentWidth = Math.Max(panel.ClientSize.Width, panel.AutoScrollMinSize.Width);
        return new Rectangle(
            Math.Max(14, (contentWidth - pageSize.Width) / 2) + panel.AutoScrollPosition.X,
            42 + panel.AutoScrollPosition.Y,
            pageSize.Width,
            pageSize.Height);
    }

    private void LargeSplitPageIndex_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel || splitPageCount <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var pageRect = GetLargePageBounds(panel);
        using var pageBrush = new SolidBrush(Color.White);
        using var outline = new Pen(Border);
        using var shadow = new SolidBrush(Color.FromArgb(28, 30, 41, 59));
        using var textBrush = new SolidBrush(Ink);
        using var pageFont = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

        var page = splitCurrentPage;
        var viewerHint = splitZoomPercent > 100
            ? "Use the scroll bars or hold the middle mouse button and drag to pan"
            : "Scroll to browse pages · use the sidebar scissors to add a split";
        TextRenderer.DrawText(e.Graphics, viewerHint,
            Font, new Rectangle(0, 10, panel.ClientSize.Width, 24), Subtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Graphics.FillRectangle(shadow,
            new Rectangle(pageRect.Left + 6, pageRect.Top + 8, pageRect.Width, pageRect.Height));
        e.Graphics.FillRectangle(pageBrush, pageRect);
        e.Graphics.DrawRectangle(outline, pageRect);
        e.Graphics.DrawString($"PAGE {page:000}", pageFont, textBrush, pageRect.Left + 16, pageRect.Top + 10);
        DrawLargePagePreview(e.Graphics, page, pageRect);

    }

    private void LargeSplitPageIndex_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (splitPageCount <= 0 || e.Delta == 0)
        {
            return;
        }

        if (HandleSplitZoomWheel(e))
        {
            return;
        }

        if (splitZoomPercent > 100)
        {
            var verticalScroll = splitLargePageIndex?.VerticalScroll;
            if (verticalScroll is null)
            {
                return;
            }

            var maximum = Math.Max(verticalScroll.Minimum,
                verticalScroll.Maximum - verticalScroll.LargeChange + 1);
            var atBoundary = e.Delta < 0
                ? verticalScroll.Value >= maximum
                : verticalScroll.Value <= verticalScroll.Minimum;
            if (!atBoundary)
            {
                return;
            }
        }

        var pageDelta = Math.Max(1, Math.Abs(e.Delta) / SystemInformation.MouseWheelScrollDelta);
        var nextPage = Math.Clamp(
            splitCurrentPage + (e.Delta < 0 ? pageDelta : -pageDelta),
            1,
            splitPageCount);
        if (nextPage == splitCurrentPage)
        {
            return;
        }

        var showBottom = e.Delta > 0;
        splitScrollToBottomAfterPreviewLoad = showBottom;
        SelectSplitPage(nextPage);
        if (splitLargePageIndex is not null)
        {
            var x = Math.Abs(splitLargePageIndex.AutoScrollPosition.X);
            var y = showBottom
                ? Math.Max(splitLargePageIndex.VerticalScroll.Minimum,
                    splitLargePageIndex.VerticalScroll.Maximum -
                    splitLargePageIndex.VerticalScroll.LargeChange + 1)
                : 0;
            splitLargePageIndex.AutoScrollPosition = new Point(x, y);
        }
    }

    private void LargeSplitPageIndex_MouseClick(object? sender, MouseEventArgs e)
    {
        if (splitLargePageIndex is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (splitCurrentPage <= 0 || splitCurrentPage > splitPageCount)
        {
            return;
        }

        splitLargePageIndex.Focus();
        SelectSplitPage(splitCurrentPage);
    }

    private void PageNavigation_KeyDown(object? sender, KeyEventArgs e)
    {
        if (splitPageCount <= 0 || e.KeyCode is not (Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown))
        {
            return;
        }

        var delta = e.KeyCode is Keys.Up or Keys.PageUp ? -1 : 1;
        SelectSplitPage(Math.Clamp(splitCurrentPage + delta, 1, splitPageCount));
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void SetSplitZoom(int percent)
    {
        splitZoomPercent = Math.Clamp(percent, 50, 200);
        if (splitZoomLabel is not null)
        {
            splitZoomLabel.Text = $"{splitZoomPercent}%";
        }

        ResizeSplitPageStack();
        if (splitCurrentPage > 0)
        {
            SelectSplitPage(splitCurrentPage);
        }
    }

    private Panel CreateSplitPageCard(int page, string? imagePath)
    {
        var hasPreview = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);
        var card = new Panel
        {
            Height = hasPreview ? 720 : 210,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 0),
            Tag = page,
            AccessibleDescription = hasPreview ? "preview" : "placeholder",
            TabIndex = page + 2,
            AccessibleName = $"PDF page {page}",
            TabStop = true
        };
        card.Paint += SplitPageCard_Paint;
        card.Click += (_, _) => SelectSplitPage(page);
        card.MouseWheel += SplitPreview_MouseWheel;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Ink,
            Padding = new Padding(18, 0, 18, 0),
            Text = $"PAGE {page:00}",
            TextAlign = ContentAlignment.MiddleLeft,
            TabStop = false
        };
        header.Click += (_, _) => SelectSplitPage(page);
        header.MouseWheel += SplitPreview_MouseWheel;

        Control preview;
        if (hasPreview)
        {
            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                SizeMode = PictureBoxSizeMode.Zoom,
                TabStop = false,
                AccessibleName = $"Preview of PDF page {page}"
            };
            using var stream = File.OpenRead(imagePath!);
            using var sourceImage = new Bitmap(stream);
            picture.Image = new Bitmap(sourceImage);
            preview = picture;
            picture.Click += (_, _) => SelectSplitPage(page);
        }
        else
        {
            preview = new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Muted,
                Font = new Font("Segoe UI", 10F),
                Text = $"Page {page}\r\n\r\nPreview unavailable\r\nUse the scissors below to cut.",
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false
            };
            preview.Click += (_, _) => SelectSplitPage(page);
        }

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Subtle,
            Padding = new Padding(18, 0, 18, 0),
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            TabStop = false
        };
        footer.Click += (_, _) => SelectSplitPage(page);
        preview.MouseWheel += SplitPreview_MouseWheel;
        footer.MouseWheel += SplitPreview_MouseWheel;
        card.Controls.Add(preview);
        card.Controls.Add(footer);
        card.Controls.Add(header);
        return card;
    }

    private void SplitPreview_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (HandleSplitZoomWheel(e))
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(new Action(UpdateCurrentPageFromScroll));
        }
    }

    private bool HandleSplitZoomWheel(MouseEventArgs e)
    {
        if (e.Delta == 0 || (ModifierKeys & Keys.Control) != Keys.Control)
        {
            return false;
        }

        SetSplitZoom(splitZoomPercent + (e.Delta > 0 ? 10 : -10));
        return true;
    }

    private Panel CreateSplitDivider(int page)
    {
        var divider = new Panel
        {
            Height = 58,
            Tag = page,
            TabStop = false,
            BackColor = Color.Transparent
        };
        var splitButton = new Button
        {
            Width = 42,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Symbol", 14F, FontStyle.Regular),
            Text = "✂",
            AccessibleName = $"Insert split after page {page}",
            AccessibleDescription = "Add or remove a cut at this page boundary",
            Tag = page,
            UseVisualStyleBackColor = false
        };
        splitButton.Click += (_, _) => ToggleSplitPoint(page);
        divider.MouseWheel += SplitPreview_MouseWheel;
        divider.Controls.Add(splitButton);
        divider.Resize += (_, _) => splitButton.Location = new Point(
            Math.Max(0, (divider.ClientSize.Width - splitButton.Width) / 2), 11);
        divider.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(203, 213, 225), 1) { DashStyle = DashStyle.Dash };
            var y = divider.ClientSize.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, Math.Max(0, splitButton.Left - 12), y);
            e.Graphics.DrawLine(pen, splitButton.Right + 12, y, divider.ClientSize.Width, y);
        };
        divider.PerformLayout();
        return divider;
    }

    private void SplitPageCard_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel card)
        {
            return;
        }

        var page = card.Tag is int value ? value : 0;
        var selected = page == splitCurrentPage;
        using var pen = new Pen(selected ? Primary : Border, selected ? 2 : 1);
        e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        if (selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(239, 246, 255));
            e.Graphics.FillRectangle(brush, 0, 0, 4, card.Height);
        }
    }

    private void ToggleSplitPoint(int page)
    {
        if (page <= 0 || page >= splitPageCount)
        {
            return;
        }

        if (!splitAfterPages.Add(page))
        {
            splitAfterPages.Remove(page);
        }

        foreach (Control control in splitPageStack.Controls)
        {
            if (control is not Panel divider || divider.Tag is not int dividerPage || dividerPage != page)
            {
                continue;
            }

            if (divider.Controls.Count > 0 && divider.Controls[0] is Button button)
            {
                button.Text = "✂";
                button.BackColor = splitAfterPages.Contains(page) ? Color.FromArgb(220, 252, 231) : Color.White;
                button.ForeColor = splitAfterPages.Contains(page) ? Color.FromArgb(22, 101, 52) : Primary;
                button.AccessibleName = splitAfterPages.Contains(page)
                    ? $"Remove split after page {page}"
                    : $"Insert split after page {page}";
            }
        }

        if (splitThumbnailScissors.TryGetValue(page, out var thumbnailScissors))
        {
            var selected = splitAfterPages.Contains(page);
            thumbnailScissors.Size = selected ? new Size(94, 32) : new Size(44, 32);
            thumbnailScissors.Text = selected ? "✂  Split here" : "✂";
            thumbnailScissors.BackColor = selected
                ? Color.FromArgb(219, 234, 254)
                : page == splitCurrentPage ? Color.FromArgb(232, 242, 255) : Surface;
            thumbnailScissors.ForeColor = Primary;
            thumbnailScissors.AccessibleName = selected
                ? $"Remove split after page {page}"
                : $"Split after page {page}";
            if (splitThumbnailDividers.TryGetValue(page, out var thumbnailDivider))
            {
                thumbnailScissors.Location = new Point(
                    Math.Max(0, (thumbnailDivider.ClientSize.Width - thumbnailScissors.Width) / 2), 2);
                thumbnailDivider.Invalidate();
            }
        }

        splitLargePageIndex?.Invalidate();
        UpdateSplitStatus();
    }

    private void SelectSplitPage(int page)
    {
        if (page <= 0 || page > splitPageCount)
        {
            return;
        }

        splitCurrentPage = page;
        if (splitPageJumpInput is not null)
        {
            splitPageJumpUpdating = true;
            splitPageJumpInput.Value = Math.Clamp(page, 1, (int)splitPageJumpInput.Maximum);
            splitPageJumpUpdating = false;
        }
        if (splitLargePageIndex is not null)
        {
            splitLargePageIndex.Invalidate();
            RequestLargePagePreview(page);
        }
        foreach (Control control in splitPageStack.Controls)
        {
            if (control is Panel card && card.Tag is int cardPage && cardPage == page)
            {
                splitBrowserPanel.ScrollControlIntoView(card);
                break;
            }
        }

        splitPageStatusLabel.Text = $"Page {page}/{splitPageCount} · {splitAfterPages.Count} split(s)";
        splitStatusLabel.Text = $"{splitPageStatusLabel.Text} · Click a scissors icon to add or remove a cut.";
        splitPageStack.Invalidate(true);
        splitLargePageIndex?.Invalidate();
        UpdateSplitThumbnailSelection();
    }

    private void NavigateToSplitPart(int firstPage)
    {
        splitScrollToBottomAfterPreviewLoad = false;
        if (splitLargePageIndex is not null)
        {
            splitLargePageIndex.AutoScrollPosition = Point.Empty;
        }

        SelectSplitPage(firstPage);
        if (splitLargePageIndex is not null)
        {
            splitLargePageIndex.AutoScrollPosition = Point.Empty;
            splitLargePageIndex.Focus();
        }
    }

    private void DrawLargePagePreview(Graphics graphics, int page, Rectangle pageRect)
    {
        var previewRect = new Rectangle(pageRect.Left + 16, pageRect.Top + 32,
            pageRect.Width - 32, pageRect.Height - 42);
        if (splitLargePreviewImagePage != page || splitLargePreviewImage is null)
        {
            TextRenderer.DrawText(graphics, "Loading preview…", Font, previewRect, Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        try
        {
            var image = splitLargePreviewImage;
            var scale = Math.Min((double)previewRect.Width / image.Width,
                (double)previewRect.Height / image.Height);
            var size = new Size(Math.Max(1, (int)(image.Width * scale)),
                Math.Max(1, (int)(image.Height * scale)));
            var imageRect = new Rectangle(
                previewRect.Left + (previewRect.Width - size.Width) / 2,
                previewRect.Top + (previewRect.Height - size.Height) / 2,
                size.Width,
                size.Height);
            graphics.DrawImage(image, imageRect);
        }
        catch (Exception exception)
        {
            Logger.LogError($"Drawing page {page} preview failed.", exception);
            TextRenderer.DrawText(graphics, "Preview unavailable", Font, previewRect, Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void RequestLargePagePreview(int page, bool immediate = false)
    {
        if (page <= 0 || page > splitPageCount || string.IsNullOrWhiteSpace(splitSourceFile) ||
            string.IsNullOrWhiteSpace(splitPreviewDirectory) || splitLargePageIndex is null)
        {
            return;
        }

        if (splitLargePreviewImagePage == page && splitLargePreviewImage is not null)
        {
            splitLargePageIndex.Invalidate();
            return;
        }

        if (splitLargePreviewPaths.TryGetValue(page, out var cachedPath) && File.Exists(cachedPath))
        {
            LoadLargePreviewBitmap(page, cachedPath);
            splitLargePageIndex.Invalidate();
            return;
        }

        if (page == splitLargePreviewRequestedPage &&
            (splitLargePreviewTimer?.Enabled == true || splitLargePreviewRequestId > 0))
        {
            return;
        }

        splitLargePreviewCancellation?.Cancel();
        splitLargePreviewRequestedPage = page;
        splitLargePreviewRequestId++;
        Logger.LogInfo($"Preview request queued for page {page} (request {splitLargePreviewRequestId}).");
        splitLargePreviewTimer?.Stop();
        if (immediate)
        {
            LargePreviewTimer_Tick(this, EventArgs.Empty);
        }
        else
        {
            splitLargePreviewTimer?.Start();
        }
    }

    private void StartLargePreviewWarmup(string source, string rootDirectory, int firstPage, int pageCount)
    {
        if (firstPage <= 0 || pageCount <= 0)
        {
            return;
        }

        splitLargePreviewWarmupCancellation?.Cancel();
        splitLargePreviewWarmupCancellation?.Dispose();
        splitLargePreviewWarmupCancellation = new CancellationTokenSource();
        var cancellationToken = splitLargePreviewWarmupCancellation.Token;
        _ = WarmLargePreviewPagesAsync(source, rootDirectory, firstPage, pageCount, cancellationToken);
    }

    private void StartLargeThumbnailWarmup(string source, string rootDirectory, int firstPage, int pageCount)
    {
        if (firstPage <= 0 || pageCount <= 0)
        {
            return;
        }

        splitLargeThumbnailWarmupCancellation?.Cancel();
        splitLargeThumbnailWarmupCancellation?.Dispose();
        splitLargeThumbnailWarmupCancellation = new CancellationTokenSource();
        _ = WarmLargeThumbnailPagesAsync(source, rootDirectory, firstPage, pageCount,
            splitLargeThumbnailWarmupCancellation.Token);
    }

    private async Task WarmLargeThumbnailPagesAsync(string source, string rootDirectory,
        int firstPage, int pageCount, CancellationToken cancellationToken)
    {
        const int batchSize = 12;
        var lastPage = firstPage + pageCount - 1;

        try
        {
            for (var batchFirst = firstPage; batchFirst <= lastPage; batchFirst += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchCount = Math.Min(batchSize, lastPage - batchFirst + 1);
                var batchDirectory = Path.Combine(rootDirectory, $"thumbnail-pages-{batchFirst:0000}");
                var rendered = await Task.Run(() => PdfPageRenderer.RenderPages(
                        source, batchDirectory, batchCount, 220, batchFirst, cancellationToken),
                    cancellationToken);
                if (cancellationToken.IsCancellationRequested || splitLargePageIndex is null ||
                    splitLargePageIndex.IsDisposed || !string.Equals(splitSourceFile, source,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var previewPanel = splitLargePageIndex;
                if (!previewPanel.IsHandleCreated)
                {
                    return;
                }

                previewPanel.BeginInvoke(new Action(() =>
                {
                    if (cancellationToken.IsCancellationRequested || previewPanel.IsDisposed)
                    {
                        return;
                    }

                    foreach (var path in rendered)
                    {
                        var page = ParsePreviewPageNumber(path);
                        if (page > 0 && File.Exists(path))
                        {
                            RefreshSplitThumbnail(page, path);
                        }
                    }
                }));
            }

            Logger.LogInfo($"Sidebar thumbnail warmup finished for pages {firstPage}-{lastPage}.");
        }
        catch (OperationCanceledException)
        {
            // A new PDF replaced this warmup.
        }
        catch (Exception exception)
        {
            Logger.LogError("Sidebar thumbnail warmup failed.", exception);
        }
    }

    private static int ParsePreviewPageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name[(separator + 1)..], out var page) ? page : 0;
    }

    private async Task WarmLargePreviewPagesAsync(string source, string rootDirectory, int firstPage,
        int pageCount, CancellationToken cancellationToken)
    {
        if (pageCount <= 0)
        {
            return;
        }

        try
        {
            var warmupDirectory = Path.Combine(rootDirectory, $"initial-pages-{firstPage:0000}");
            var rendered = await Task.Run(() => PdfPageRenderer.RenderPages(
                    source, warmupDirectory, pageCount, LargePreviewRenderSize, firstPage, cancellationToken),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || splitLargePageIndex is null ||
                splitLargePageIndex.IsDisposed || !string.Equals(splitSourceFile, source,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (var index = 0; index < rendered.Count; index++)
            {
                var page = firstPage + index;
                var path = rendered[index];
                if (!File.Exists(path))
                {
                    continue;
                }

                splitLargePreviewPaths[page] = path;
                RefreshSplitThumbnail(page, path);
                if (page == splitCurrentPage)
                {
                    LoadLargePreviewBitmap(page, path);
                }
            }

            splitStatusLabel.Text = "Preview ready. Click a scissors icon to add or remove a cut.";
            splitLargePageIndex.Refresh();
            Logger.LogInfo($"Initial preview warmup finished for pages {firstPage}-{firstPage + rendered.Count - 1}.");
        }
        catch (OperationCanceledException)
        {
            // A new PDF replaced this warmup.
        }
        catch (Exception exception)
        {
            Logger.LogError("Initial preview warmup failed.", exception);
            if (splitCurrentPage > 0)
            {
                RequestLargePagePreview(splitCurrentPage);
            }
        }
    }

    private void LoadLargePreviewBitmap(int page, string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sourceImage = new Bitmap(stream);
            splitLargePreviewImage?.Dispose();
            splitLargePreviewImage = new Bitmap(sourceImage);
            splitLargePreviewImagePage = page;
            RefreshSplitThumbnail(page, path);
            ResizeLargeSplitPageIndex();
            if (splitScrollToBottomAfterPreviewLoad && splitLargePageIndex is not null &&
                page == splitCurrentPage)
            {
                var x = Math.Abs(splitLargePageIndex.AutoScrollPosition.X);
                var y = Math.Max(splitLargePageIndex.VerticalScroll.Minimum,
                    splitLargePageIndex.VerticalScroll.Maximum -
                    splitLargePageIndex.VerticalScroll.LargeChange + 1);
                splitLargePageIndex.AutoScrollPosition = new Point(x, y);
                splitScrollToBottomAfterPreviewLoad = false;
            }
        }
        catch (Exception exception)
        {
            Logger.LogError($"Loading page {page} preview failed.", exception);
            splitLargePreviewImage?.Dispose();
            splitLargePreviewImage = null;
            splitLargePreviewImagePage = 0;
        }
    }

    private async Task LoadLargePagePreviewAsync(string source, string rootDirectory, int page,
        int requestId, CancellationToken cancellationToken)
    {
        try
        {
            await splitLargePreviewGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (requestId != splitLargePreviewRequestId || page != splitLargePreviewRequestedPage ||
                splitLargePageIndex is null)
            {
                return;
            }

            var pageDirectory = Path.Combine(rootDirectory, $"page-{page:0000}");
            var rendered = await Task.Run(() => PdfPageRenderer.RenderPages(
                    source, pageDirectory, 1, LargePreviewRenderSize, page, cancellationToken),
                cancellationToken);
            var path = rendered.FirstOrDefault();
            Logger.LogInfo($"Preview render finished for page {page}: {(path is null ? "empty" : path)}.");
            if (requestId != splitLargePreviewRequestId || path is null || !File.Exists(path))
            {
                return;
            }

            var previewPanel = splitLargePageIndex;
            if (previewPanel.IsDisposed || !previewPanel.IsHandleCreated)
            {
                return;
            }

            previewPanel.BeginInvoke(new Action(() =>
            {
                if (requestId != splitLargePreviewRequestId || page != splitLargePreviewRequestedPage ||
                    previewPanel.IsDisposed)
                {
                    return;
                }

                splitLargePreviewPaths[page] = path;
                LoadLargePreviewBitmap(page, path);
                Logger.LogInfo($"Preview bitmap ready for page {page}.");
                if (page == 1 && splitPageCount > 1 && splitSourceFile is not null &&
                    splitPreviewDirectory is not null)
                {
                    StartLargePreviewWarmup(splitSourceFile, splitPreviewDirectory, 2,
                        Math.Min(5, splitPageCount - 1));
                }
                previewPanel.Refresh();
            }));
        }
        catch (OperationCanceledException)
        {
            // A newer scroll position has replaced this preview request.
        }
        catch (Exception exception)
        {
            Logger.LogError($"Loading page {page} preview failed.", exception);
        }
        finally
        {
            splitLargePreviewGate.Release();
        }
    }

    private void UpdateCurrentPageFromScroll()
    {
        if (splitPageCount == 0)
        {
            return;
        }

        if (splitLargePageIndex is not null)
        {
            RequestLargePagePreview(splitCurrentPage);
            return;
        }

        if (splitPageStack.Controls.Count == 0)
        {
            return;
        }

        var scrollOffset = Math.Max(splitBrowserPanel.VerticalScroll.Value,
            Math.Abs(splitBrowserPanel.AutoScrollPosition.Y));
        var targetOffset = scrollOffset + Math.Max(80, splitBrowserPanel.ClientSize.Height / 6);
        var contentOffset = 0;
        var visiblePage = 1;
        foreach (Control control in splitPageStack.Controls)
        {
            if (control.Tag is int page && control.Height > 100)
            {
                if (contentOffset + control.Height >= targetOffset)
                {
                    visiblePage = page;
                    break;
                }
            }

            contentOffset += control.Height + control.Margin.Vertical;
        }

        if (visiblePage != splitCurrentPage)
        {
            splitCurrentPage = visiblePage;
            UpdateSplitStatus();
            splitPageStack.Invalidate(true);
        }
    }

    private void SplitSaveButton_Click(object? sender, EventArgs e)
    {
        if (isProcessing || string.IsNullOrWhiteSpace(splitSourceFile))
        {
            if (string.IsNullOrWhiteSpace(splitSourceFile))
            {
                MessageBox.Show("Choose a PDF before saving split parts.", "Nothing to split",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }

        var sourceName = Path.GetFileNameWithoutExtension(splitSourceFile);
        using var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
            FileName = $"{sourceName}_split.pdf",
            Title = "Choose the base name for split PDFs"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            RestoreMaximizedWindow();
            return;
        }
        RestoreMaximizedWindow();

        var source = splitSourceFile;
        var splitPoints = splitAfterPages.OrderBy(page => page).ToArray();
        var partNames = GetSplitPartNamesForSave();
        SetSplitProcessing(true);
        splitStatusLabel.Text = "Saving split PDFs…";
        _ = SaveSplitAsync(source, splitPoints, partNames, dialog.FileName);
    }

    private async Task DownloadSplitPartAsync(int firstPage, int lastPage, string requestedName,
        Button downloadButton)
    {
        if (isProcessing || string.IsNullOrWhiteSpace(splitSourceFile))
        {
            return;
        }

        var fileName = string.IsNullOrWhiteSpace(requestedName)
            ? $"part_{firstPage:00}-{lastPage:00}"
            : Path.GetFileNameWithoutExtension(requestedName.Trim());
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter, '_');
        }
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            fileName = $"part_{firstPage:00}-{lastPage:00}";
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
            FileName = $"{fileName}.pdf",
            Title = $"Download pages {firstPage}-{lastPage}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            RestoreMaximizedWindow();
            return;
        }
        RestoreMaximizedWindow();

        var source = splitSourceFile;
        SetSplitProcessing(true);
        downloadButton.Text = "Saving…";
        splitStatusLabel.Text = $"Saving pages {firstPage}-{lastPage}…";
        try
        {
            var result = await Task.Run(() =>
                splitService.SavePart(source, firstPage, lastPage, dialog.FileName));
            if (!result.Succeeded)
            {
                MessageBox.Show(result.ErrorMessage ?? "This part could not be saved.", "Download failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                splitStatusLabel.Text = "Download failed. Original PDF unchanged.";
                return;
            }

            splitStatusLabel.Text = $"Saved pages {firstPage}-{lastPage}. Original PDF unchanged.";
            MessageBox.Show($"Created {Path.GetFileName(dialog.FileName)}", "Download complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            Logger.LogError("Unexpected single-part download error.", exception);
            MessageBox.Show(exception.Message, "Download failed", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            splitStatusLabel.Text = "Download failed. Please try again.";
        }
        finally
        {
            downloadButton.Text = "Download";
            SetSplitProcessing(false);
            UpdateSplitStatus();
            RestoreMaximizedWindow();
        }
    }

    private async Task SaveSplitAsync(string source, IReadOnlyCollection<int> splitPoints,
        IReadOnlyList<string> partNames, string outputBasePath)
    {
        try
        {
            var result = await Task.Run(() => splitService.Split(source, splitPoints, outputBasePath, partNames));
            if (!result.Succeeded)
            {
                MessageBox.Show(result.ErrorMessage ?? "The PDF could not be split.", "Split failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                splitStatusLabel.Text = "Split failed. Original PDF unchanged.";
                return;
            }

            var names = string.Join(Environment.NewLine, result.Parts.Select(part => Path.GetFileName(part.OutputPath)));
            MessageBox.Show(
                $"Created {result.Parts.Count} PDF part(s):\r\n\r\n{names}",
                "Split complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            splitStatusLabel.Text = $"Saved {result.Parts.Count} part(s). Original PDF unchanged.";
        }
        catch (Exception exception)
        {
            Logger.LogError("Unexpected UI split error.", exception);
            MessageBox.Show(exception.Message, "Split failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            splitStatusLabel.Text = "Split failed. Please try again.";
        }
        finally
        {
            SetSplitProcessing(false);
            UpdateSplitStatus();
            RestoreMaximizedWindow();
        }
    }

    private void SetSplitProcessing(bool processing)
    {
        isProcessing = processing;
        modeTabs.Enabled = !processing;
        splitChooseButton.Enabled = !processing;
        splitSaveButton.Enabled = !processing && splitPageCount > 0;
        foreach (var button in splitPartsSummaryCards.Controls
                     .Cast<Control>()
                     .SelectMany(card => card.Controls.OfType<Button>()))
        {
            button.Enabled = !processing;
        }
    }

    private void UpdateSplitStatus()
    {
        splitSummaryNumber.Text = splitPageCount.ToString();
        splitSummaryCaption.Text = splitPageCount == 1 ? "PDF page loaded" : "PDF pages loaded";
        if (splitThumbnailCountLabel is not null)
        {
            splitThumbnailCountLabel.Text = splitPageCount.ToString();
        }
        splitSaveButton.Enabled = !isProcessing && splitPageCount > 0 && !string.IsNullOrWhiteSpace(splitSourceFile);
        UpdateSplitResultSummary();
        if (splitPageCount > 0)
        {
            splitPageStatusLabel.Text = $"Page {splitCurrentPage}/{splitPageCount} · {splitAfterPages.Count} split(s)";
            var statusIsOutcome = splitStatusLabel.Text.StartsWith("Saved", StringComparison.Ordinal) ||
                                  splitStatusLabel.Text.StartsWith("Split failed", StringComparison.Ordinal) ||
                                  splitStatusLabel.Text.StartsWith("Download failed", StringComparison.Ordinal) ||
                                  splitStatusLabel.Text.StartsWith("Could not", StringComparison.Ordinal);
            if (!statusIsOutcome)
            {
                splitStatusLabel.Text = $"{splitPageStatusLabel.Text} · Click a scissors icon to add or remove a cut.";
            }
            UpdateSplitThumbnailSelection();
        }
    }

    private void UpdateSplitResultSummary()
    {
        if (splitPartsSummaryCards is null || splitPartsSummaryHeader is null || splitSaveButton is null)
        {
            return;
        }

        if (splitPageCount <= 0)
        {
            splitPartsSummaryHeader.Text = "Split results";
            while (splitPartsSummaryCards.Controls.Count > 0)
            {
                var control = splitPartsSummaryCards.Controls[0];
                splitPartsSummaryCards.Controls.RemoveAt(0);
                control.Dispose();
            }

            splitPartsSummaryCards.Controls.Add(CreateSplitResultEmptyState());
            return;
        }

        var previousScrollY = Math.Max(splitPartsSummaryCards.VerticalScroll.Value,
            Math.Abs(splitPartsSummaryCards.AutoScrollPosition.Y));
        var partRanges = new List<(int Start, int End)>();
        var startPage = 1;
        foreach (var cutPage in splitAfterPages.OrderBy(page => page))
        {
            if (cutPage < startPage || cutPage >= splitPageCount)
            {
                continue;
            }

            partRanges.Add((startPage, cutPage));
            startPage = cutPage + 1;
        }

        partRanges.Add((startPage, splitPageCount));
        var partLines = partRanges.Select((range, index) =>
        {
            var pages = range.End - range.Start + 1;
            return (Index: index + 1, range.Start, range.End, Pages: pages);
        });
        splitPartsSummaryHeader.Text = $"Split results · {partRanges.Count} part{(partRanges.Count == 1 ? "" : "s")}";
        splitPartsSummaryCards.SuspendLayout();
        while (splitPartsSummaryCards.Controls.Count > 0)
        {
            var control = splitPartsSummaryCards.Controls[0];
            splitPartsSummaryCards.Controls.RemoveAt(0);
            control.Dispose();
        }

        foreach (var part in partLines)
        {
            var rangeKey = GetSplitRangeKey(part.Start, part.End);
            var defaultName = $"part_{part.Index:00}";
            if (!splitPartNames.TryGetValue(rangeKey, out var partName) ||
                string.IsNullOrWhiteSpace(partName) ||
                IsStaleDefaultPartName(partName, defaultName))
            {
                partName = defaultName;
                splitPartNames[rangeKey] = partName;
            }
            var card = new Panel
            {
                Height = 112,
                Width = Math.Max(1, splitPartsSummaryCards.ClientSize.Width - 8),
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(12, 8, 12, 8),
                AccessibleName = $"Split part {part.Index}, pages {part.Start} to {part.End}"
            };
            var cardHovered = false;
            card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                cardHovered ? Primary : Border, ButtonBorderStyle.Solid);
            var partLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = Primary,
                Padding = new Padding(0, 0, 86, 0),
                Text = $"PART {part.Index}",
                TextAlign = ContentAlignment.MiddleLeft
            };
            var nameInput = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 27,
                Font = new Font("Segoe UI", 9F),
                Text = partName,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Ink,
                TabIndex = 0,
                AccessibleName = $"Name for split part {part.Index}",
                AccessibleDescription = "Edit the output file name for this split part"
            };
            nameInput.Enter += (_, _) => nameInput.BackColor = Color.FromArgb(239, 246, 255);
            nameInput.Leave += (_, _) => nameInput.BackColor = Color.White;
            nameInput.TextChanged += (_, _) => splitPartNames[rangeKey] = nameInput.Text;
            var downloadButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(card.Width - 92, 4),
                Size = new Size(80, 24),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderColor = Border },
                BackColor = PrimarySoft,
                ForeColor = Primary,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                Text = "Download",
                AccessibleName = $"Download split part {part.Index}, pages {part.Start} to {part.End}",
                UseVisualStyleBackColor = false,
                TabIndex = 1
            };
            downloadButton.Click += async (_, _) =>
                await DownloadSplitPartAsync(part.Start, part.End, nameInput.Text, downloadButton);
            var countLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Muted,
                Text = $"Pages {part.Start}–{part.End}   ·   {part.Pages} page{(part.Pages == 1 ? "" : "s")}",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false
            };
            void NavigateToPart() => NavigateToSplitPart(part.Start);
            void SetCardHover(bool hovered)
            {
                cardHovered = hovered;
                card.BackColor = hovered ? PrimarySoft : Color.White;
                card.Invalidate();
            }
            void RefreshCardHover()
            {
                SetCardHover(card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)));
            }

            card.Cursor = Cursors.Hand;
            partLabel.Cursor = Cursors.Hand;
            countLabel.Cursor = Cursors.Hand;
            card.Click += (_, _) => NavigateToPart();
            partLabel.Click += (_, _) => NavigateToPart();
            countLabel.Click += (_, _) => NavigateToPart();
            card.MouseEnter += (_, _) => SetCardHover(true);
            card.MouseLeave += (_, _) => RefreshCardHover();
            card.Controls.Add(countLabel);
            card.Controls.Add(nameInput);
            card.Controls.Add(partLabel);
            card.Controls.Add(downloadButton);
            foreach (Control child in card.Controls)
            {
                child.MouseEnter += (_, _) => SetCardHover(true);
                child.MouseLeave += (_, _) => RefreshCardHover();
            }
            downloadButton.BringToFront();
            splitPartsSummaryCards.Controls.Add(card);
        }

        ResizeSplitResultCards();
        splitPartsSummaryCards.ResumeLayout(true);
        if (previousScrollY > 0 && splitPartsSummaryCards.IsHandleCreated)
        {
            splitPartsSummaryCards.BeginInvoke(new Action(() =>
            {
                if (!splitPartsSummaryCards.IsDisposed)
                {
                    splitPartsSummaryCards.AutoScrollPosition = new Point(0, previousScrollY);
                }
            }));
        }
        splitSaveButton.Text = partRanges.Count == 1
            ? "Save 1 split PDF"
            : $"Save {partRanges.Count} split PDFs";
        splitSaveButton.AccessibleName = splitSaveButton.Text;
    }

    private IReadOnlyList<string> GetSplitPartNamesForSave()
    {
        var ranges = BuildSplitPartRanges();
        return ranges.Select((range, index) =>
        {
            var key = GetSplitRangeKey(range.Start, range.End);
            return splitPartNames.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : $"part_{index + 1:00}";
        }).ToArray();
    }

    private static bool IsStaleDefaultPartName(string partName, string expectedDefaultName)
    {
        return partName.StartsWith("part_", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(partName.AsSpan(5), out _) &&
               !string.Equals(partName, expectedDefaultName, StringComparison.OrdinalIgnoreCase);
    }

    private List<(int Start, int End)> BuildSplitPartRanges()
    {
        var ranges = new List<(int Start, int End)>();
        var startPage = 1;
        foreach (var cutPage in splitAfterPages.OrderBy(page => page))
        {
            if (cutPage < startPage || cutPage >= splitPageCount)
            {
                continue;
            }

            ranges.Add((startPage, cutPage));
            startPage = cutPage + 1;
        }

        if (splitPageCount > 0)
        {
            ranges.Add((startPage, splitPageCount));
        }

        return ranges;
    }

    private static string GetSplitRangeKey(int start, int end) => $"{start}-{end}";

    private void ResizeSplitResultCards()
    {
        if (splitPartsSummaryCards is null)
        {
            return;
        }

        var width = Math.Max(1, splitPartsSummaryCards.ClientSize.Width -
            SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control control in splitPartsSummaryCards.Controls)
        {
            control.Width = width;
        }
    }

    private Label CreateSplitResultEmptyState()
    {
        return new Label
        {
            AutoSize = false,
            Width = Math.Max(1, splitPartsSummaryCards?.ClientSize.Width - 8 ?? 260),
            Height = 64,
            Margin = new Padding(0, 4, 0, 8),
            Padding = new Padding(4, 0, 4, 0),
            Font = new Font("Segoe UI", 9F),
            ForeColor = Muted,
            Text = "Open a PDF to see its split results here.",
            TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = "Split results empty state"
        };
    }

    private void ResizeSplitPageStack()
    {
        if (splitPageStack is null || splitBrowserPanel is null)
        {
            return;
        }

        if (splitLargePageIndex is not null)
        {
            ResizeLargeSplitPageIndex();
            return;
        }

        var viewportWidth = Math.Max(320, splitBrowserPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 28);
        var basePageWidth = Math.Min(1080, Math.Max(320, viewportWidth - splitPageStack.Padding.Horizontal));
        var pageWidth = Math.Max(320, (int)Math.Round(basePageWidth * splitZoomPercent / 100D));
        var contentWidth = Math.Max(viewportWidth, pageWidth + splitPageStack.Padding.Horizontal);
        splitPageStack.Width = contentWidth;
        foreach (Control control in splitPageStack.Controls)
        {
            if (control is Panel card && card.Tag is int)
            {
                // Chrome's PDF viewer keeps a white page centered on a dark canvas and
                // lets the page itself be taller than the viewport for readable scrolling.
                card.Width = pageWidth;
                card.Height = card.AccessibleDescription == "preview"
                    ? (int)Math.Round(pageWidth * 1.4142D) + 74
                    : 210;
                var left = Math.Max(0, (contentWidth - pageWidth) / 2);
                card.Margin = new Padding(left, 0, Math.Max(0, contentWidth - pageWidth - left), 0);
            }
            else
            {
                control.Width = contentWidth - splitPageStack.Padding.Horizontal;
                control.Margin = new Padding(0);
            }
        }

        splitPageStack.PerformLayout();
    }

    private void ClearSplitPreview()
    {
        RemoveLargeSplitIndex();
        SetSplitPreviewEmptyStateVisible(true);
        splitPageStack.Visible = true;
        ClearSplitPageControls();
        if (!string.IsNullOrWhiteSpace(splitPreviewDirectory))
        {
            try
            {
                if (Directory.Exists(splitPreviewDirectory))
                {
                    Directory.Delete(splitPreviewDirectory, recursive: true);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to clean up split preview files.", exception);
            }
        }

        splitPreviewDirectory = null;
        splitRenderedPages = Array.Empty<string>();
        splitLargePreviewPaths.Clear();
        splitLargePreviewCancellation?.Cancel();
        splitLargePreviewCancellation?.Dispose();
        splitLargePreviewCancellation = null;
        splitLargePreviewWarmupCancellation?.Cancel();
        splitLargePreviewWarmupCancellation?.Dispose();
        splitLargePreviewWarmupCancellation = null;
        splitLargeThumbnailWarmupCancellation?.Cancel();
        splitLargeThumbnailWarmupCancellation?.Dispose();
        splitLargeThumbnailWarmupCancellation = null;
        splitLargePreviewTimer?.Stop();
        splitLargePreviewImage?.Dispose();
        splitLargePreviewImage = null;
        splitLargePreviewImagePage = 0;
        splitLargePreviewRequestedPage = 0;
        splitLargePreviewRequestId++;
        splitZoomPercent = 100;
        if (splitZoomLabel is not null)
        {
            splitZoomLabel.Text = "100%";
        }
        splitSummaryNumber.Text = "0";
        splitSummaryCaption.Text = "PDF pages loaded";
        splitPartNames.Clear();
        if (splitThumbnailCountLabel is not null)
        {
            splitThumbnailCountLabel.Text = "0";
        }
        splitPageStatusLabel.Text = "0 pages · 0 split points";
        splitStatusLabel.Text = "Drop a PDF here or click Open PDF to start.";
        UpdateSplitResultSummary();
        ClearSplitThumbnails();
        if (splitPageJumpInput is not null)
        {
            splitPageJumpUpdating = true;
            splitPageJumpInput.Maximum = 1;
            splitPageJumpInput.Value = 1;
            splitPageJumpUpdating = false;
        }
        if (splitPageTotalLabel is not null)
        {
            splitPageTotalLabel.Text = "/ 0";
        }
    }

    private void SetSplitPreviewEmptyStateVisible(bool visible)
    {
        if (splitPreviewEmptyState is null)
        {
            return;
        }

        splitPreviewEmptyState.Visible = visible;
        if (visible)
        {
            splitPreviewEmptyState.BringToFront();
        }
    }

    private void ClearSplitPageControls()
    {
        while (splitPageStack.Controls.Count > 0)
        {
            var control = splitPageStack.Controls[0];
            if (control is Panel card)
            {
                foreach (Control child in card.Controls)
                {
                    if (child is PictureBox picture)
                    {
                        picture.Image?.Dispose();
                        picture.Image = null;
                    }
                }
            }

            splitPageStack.Controls.RemoveAt(0);
            control.Dispose();
        }
    }

    private void ChooseFiles_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Documents (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx|PDF files (*.pdf)|*.pdf|Word files (*.doc;*.docx)|*.doc;*.docx",
            Multiselect = true,
            Title = "Choose files to merge"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RestoreMaximizedWindow();
            AddFiles(dialog.FileNames);
        }
    }

    private void DropTarget_DragEnter(object? sender, DragEventArgs e)
    {
        var valid = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
        if (valid && !isDragOver)
        {
            isDragOver = true;
            dropZonePanel.Invalidate();
        }
    }

    private void DropTarget_DragLeave(object? sender, EventArgs e)
    {
        isDragOver = false;
        dropZonePanel.Invalidate();
    }

    private void DropTarget_DragDrop(object? sender, DragEventArgs e)
    {
        isDragOver = false;
        dropZonePanel.Invalidate();
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }
    }

    private void AddFiles(IEnumerable<string> files)
    {
        var invalid = new List<string>();
        var added = 0;

        foreach (var file in files)
        {
            try
            {
                var extension = Path.GetExtension(file);
                if (!File.Exists(file) ||
                    !new[] { ".pdf", ".doc", ".docx" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    invalid.Add(file);
                    continue;
                }

                var fullPath = Path.GetFullPath(file);
                if (sourceFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceFiles.Add(fullPath);
                listBox.Items.Add(CreateListViewItem(fullPath));
                added++;
            }
            catch (Exception exception)
            {
                Logger.LogError($"Adding '{file}' failed.", exception);
                invalid.Add(file);
            }
        }

        listBox.Invalidate();
        UpdateStatus();

        if (invalid.Count > 0)
        {
            MessageBox.Show(
                $"{invalid.Count} file(s) were skipped. Supported types are PDF, DOC, and DOCX.",
                "Unsupported file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (added > 0)
        {
            listBox.Items[listBox.Items.Count - added].Selected = true;
            listBox.Items[listBox.Items.Count - added].Focused = true;
            listBox.EnsureVisible(listBox.Items.Count - added);
            Logger.LogInfo($"Added {added} source file(s).");
        }
    }

    private static ListViewItem CreateListViewItem(string path)
    {
        var file = new FileInfo(path);
        var item = new ListViewItem(Path.GetFileNameWithoutExtension(file.Name));
        item.SubItems.Add(file.Extension.TrimStart('.').ToUpperInvariant());
        item.SubItems.Add(FormatFileSize(file.Length));
        item.ToolTipText = $"{file.FullName}\r\n{FormatFileSize(file.Length)} · Modified {file.LastWriteTime:yyyy-MM-dd HH:mm}";
        return item;
    }

    private void ResizeQueueColumns()
    {
        if (listBox.Columns.Count < 3)
        {
            return;
        }

        var fixedWidth = listBox.Columns[1].Width + listBox.Columns[2].Width;
        var available = listBox.ClientSize.Width - fixedWidth - SystemInformation.VerticalScrollBarWidth - 6;
        listBox.Columns[0].Width = Math.Max(220, available);
    }

    private void ListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= listBox.Items.Count)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var background = new SolidBrush(selected ? Color.FromArgb(239, 246, 255) : Color.White);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (selected)
        {
            using var selectionBar = new SolidBrush(Primary);
            e.Graphics.FillRectangle(selectionBar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
        }

        var sourcePath = e.Index < sourceFiles.Count ? sourceFiles[e.Index] : string.Empty;
        var fileName = Path.GetFileName(sourcePath);
        var fileDetails = GetFileDetails(sourcePath);
        var extension = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
        var badgeColor = extension == "PDF" ? Color.FromArgb(220, 38, 38) : Teal;
        var badge = new Rectangle(e.Bounds.X + 18, e.Bounds.Y + 17, 46, 30);

        using (var badgeBrush = new SolidBrush(Color.FromArgb(20, badgeColor.R, badgeColor.G, badgeColor.B)))
        using (var badgeFont = new Font("Segoe UI", 8F, FontStyle.Bold))
        using (var badgeTextBrush = new SolidBrush(badgeColor))
        {
            e.Graphics.FillRectangle(badgeBrush, badge);
            using var badgeFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(extension, badgeFont, badgeTextBrush, badge, badgeFormat);
        }

        using var nameFont = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        using var pathFont = new Font("Segoe UI", 8.75F, FontStyle.Regular);
        using var nameBrush = new SolidBrush(Ink);
        using var pathBrush = new SolidBrush(Subtle);
        var textLeft = badge.Right + 14;
        var textWidth = Math.Max(60, e.Bounds.Right - textLeft - 48);
        var nameFormat = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var displayName = string.IsNullOrWhiteSpace(fileName)
            ? listBox.Items[e.Index]?.ToString() ?? string.Empty
            : fileName;
        e.Graphics.DrawString(
            displayName,
            nameFont,
            nameBrush,
            new RectangleF(textLeft, e.Bounds.Y + 7, textWidth, 24),
            nameFormat);
        e.Graphics.DrawString(
            fileDetails,
            pathFont,
            pathBrush,
            new RectangleF(textLeft, e.Bounds.Y + 31, textWidth, 18),
            nameFormat);

        using var orderFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        using var orderBrush = new SolidBrush(Subtle);
        using var orderFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        e.Graphics.DrawString($"{e.Index + 1:00}", orderFont, orderBrush,
            new RectangleF(e.Bounds.Right - 42, e.Bounds.Y, 28, e.Bounds.Height), orderFormat);
        e.DrawFocusRectangle();
    }

    private static string GetFileDetails(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return $"{FormatFileSize(file.Length)} · {file.LastWriteTime:yyyy-MM-dd HH:mm} · {file.DirectoryName}";
        }
        catch
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes / (1024d * 1024d):0.#} MB";
    }

    private void ListBox_Paint(object? sender, PaintEventArgs e)
    {
        if (listBox.Items.Count == 0)
        {
            using var emptyFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            TextRenderer.DrawText(
                e.Graphics,
                "Your files will appear here",
                emptyFont,
                new Rectangle(0, listBox.ClientSize.Height / 2 - 12, listBox.ClientSize.Width, 24),
                Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void ListBox_SelectedIndexChanged(object? sender, EventArgs e) => UpdateStatus();

    private void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelectedFiles();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && !isProcessing)
        {
            BtnMerge_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void BtnMoveUp_Click(object? sender, EventArgs e) => MoveItem(-1);

    private void BtnMoveDown_Click(object? sender, EventArgs e) => MoveItem(1);

    private void MoveItem(int direction)
    {
        if (listBox.SelectedIndices.Count != 1)
        {
            return;
        }

        var index = listBox.SelectedIndices[0];
        var target = index + direction;
        if (target < 0 || target >= sourceFiles.Count)
        {
            return;
        }

        (sourceFiles[index], sourceFiles[target]) = (sourceFiles[target], sourceFiles[index]);
        var item = listBox.Items[index];
        listBox.Items.RemoveAt(index);
        listBox.Items.Insert(target, item);
        item.Selected = true;
        item.Focused = true;
        listBox.EnsureVisible(target);
        listBox.Invalidate();
        UpdateStatus();
    }

    private void BtnDelete_Click(object? sender, EventArgs e) => DeleteSelectedFiles();

    private void BtnDelete_Paint(object? sender, PaintEventArgs e)
    {
        var color = btnDelete.Enabled ? Danger : Subtle;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.7F);
        using var brush = new SolidBrush(Color.FromArgb(24, color.R, color.G, color.B));

        var centerX = 19;
        var body = new Rectangle(centerX - 6, 15, 12, 15);
        e.Graphics.FillRectangle(brush, body);
        e.Graphics.DrawRectangle(pen, body);
        e.Graphics.DrawLine(pen, centerX - 9, 12, centerX + 9, 12);
        e.Graphics.DrawLine(pen, centerX - 3, 9, centerX + 3, 9);
        e.Graphics.DrawLine(pen, centerX - 2, 18, centerX - 2, 27);
        e.Graphics.DrawLine(pen, centerX + 2, 18, centerX + 2, 27);

        TextRenderer.DrawText(
            e.Graphics,
            "Remove",
            btnDelete.Font,
            new Rectangle(38, 0, Math.Max(1, btnDelete.ClientSize.Width - 46), btnDelete.ClientSize.Height),
            color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static Icon LoadAppIcon()
    {
        var handle = AppIconBitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(handle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Bitmap LoadAppIconBitmap()
    {
        const string resourceName = "Pdf_Merger.Assets.PdfMergerIcon.png";
        using var stream = typeof(Form1).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded app icon not found: {resourceName}");
        return new Bitmap(stream);
    }

    private static void DrawAppIcon(Graphics graphics, RectangleF bounds)
    {
        graphics.DrawImage(AppIconBitmap, bounds);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void DeleteSelectedFiles()
    {
        var selected = listBox.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        foreach (var index in selected)
        {
            sourceFiles.RemoveAt(index);
            listBox.Items.RemoveAt(index);
        }

        if (selected.Count > 0)
        {
            Logger.LogInfo($"Removed {selected.Count} source file(s).");
            listBox.Invalidate();
            UpdateStatus();
        }
    }

    private async void BtnMerge_Click(object? sender, EventArgs e)
    {
        if (isProcessing)
        {
            return;
        }

        if (sourceFiles.Count == 0)
        {
            MessageBox.Show("Add at least one PDF, DOC, or DOCX file to begin.",
                "Nothing to merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
            FileName = $"Merged_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            Title = "Save merged PDF"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetProcessing(true);
        try
        {
            var result = await RunMergeOnStaThreadAsync(sourceFiles.ToList(), dialog.FileName);
            ShowMergeResult(result);
        }
        catch (Exception exception)
        {
            Logger.LogError("Unexpected UI merge error.", exception);
            MessageBox.Show(exception.Message, "Merge failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetProcessing(false);
        }
    }

    private async Task<MergeResult> RunMergeOnStaThreadAsync(
        IReadOnlyList<string> sources, string outputPath)
    {
        var completion = new TaskCompletionSource<MergeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(mergeService.Merge(sources, outputPath));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "PDF Forge Worker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task;
    }

    private void ShowMergeResult(MergeResult result)
    {
        if (!result.Succeeded)
        {
            var message = result.ErrorMessage ?? "The merge did not produce an output file.";
            if (result.Failures.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine +
                           string.Join(Environment.NewLine,
                               result.Failures.Select(f =>
                                   $"{Path.GetFileName(f.SourcePath)}: {f.Message}"));
            }

            MessageBox.Show(message, "Merge failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var title = result.Failures.Count == 0
            ? "Merge complete"
            : "Merge completed with warnings";
        var messageText = result.Failures.Count == 0
            ? $"Your merged PDF is ready.\r\n\r\n{result.PageCount} page(s) written."
            : $"The PDF was created with {result.Failures.Count} skipped file(s).\r\n\r\n" +
              $"{result.PageCount} page(s) written.";

        MessageBox.Show(messageText, title, MessageBoxButtons.OK,
            result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void SetProcessing(bool processing)
    {
        isProcessing = processing;
        modeTabs.Enabled = !processing;
        btnMerge.Text = processing ? "Merging…" : "Merge PDF";
        btnDisclaimer.Enabled = !processing;
        btnChooseFiles.Enabled = !processing;
        listBox.Enabled = !processing;
        btnMoveUp.Enabled = !processing;
        btnMoveDown.Enabled = !processing;
        btnDelete.Enabled = !processing;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var count = sourceFiles.Count;
        var selectedCount = listBox.SelectedIndices.Count;
        labelEmptyState.Visible = count == 0;
        labelCount.Text = count == 0
            ? "0 files · scrollable"
            : $"{count} ready · scrollable";
        labelStatus.Text = isProcessing
            ? "Merging files… keep this window open"
            : count == 0
                ? "Drop files above or choose them to begin."
                : selectedCount > 0
                    ? $"{selectedCount} selected · Use arrows to set order; Remove deletes selected files."
                    : "Drop files above to add them, or select several to remove them.";
        summaryNumber.Text = count.ToString();
        summaryCaption.Text = count == 1 ? "Queued file" : "Queued files";
        var selectedIndex = selectedCount == 1 ? listBox.SelectedIndices[0] : -1;
        btnMoveUp.Enabled = !isProcessing && selectedCount == 1 && selectedIndex > 0;
        btnMoveDown.Enabled = !isProcessing && selectedCount == 1 && selectedIndex >= 0 && selectedIndex < count - 1;
        btnDelete.Enabled = !isProcessing && selectedCount > 0;
        btnMerge.Enabled = !isProcessing && count > 0;
        listBox.Invalidate();
    }

    private void SurfacePanel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
    }

    private void DropZonePanel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(isDragOver ? Primary : Color.FromArgb(180, 194, 216), 2)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 5F, 4F }
        };
        var bounds = new Rectangle(1, 1, panel.Width - 3, panel.Height - 3);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    private void SplitBrowserPanel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel || !isSplitDragOver)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(147, 197, 253), 3);
        e.Graphics.DrawRectangle(pen, 2, 2, panel.Width - 5, panel.Height - 5);
    }

    private void BtnDisclaimer_Click(object? sender, EventArgs e) => ShowCustomAboutBox();

    private void ShowCustomAboutBox()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var buildDate = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildDate")?.Value ?? "unknown";
        var workArea = Screen.FromControl(this).WorkingArea;
        var aboutWidth = Math.Min(760, Math.Max(520, workArea.Width - 48));
        var aboutHeight = Math.Min(620, Math.Max(420, workArea.Height - 48));
        using var about = new Form
        {
            Text = "About / 關於 PDF Forge",
            ClientSize = new Size(aboutWidth, aboutHeight),
            MinimumSize = new Size(Math.Min(680, aboutWidth), Math.Min(540, aboutHeight)),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = true,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Canvas,
            AutoScaleMode = AutoScaleMode.None,
            Font = new Font("Segoe UI", 10F)
        };

        static TextBox CreateAboutText(string text)
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(14),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 10F),
                Text = text,
                TabIndex = 0
            };
        }

        var languageTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 8),
            AccessibleName = "About language",
            TabIndex = 0
        };
        var chineseTab = new TabPage("中文") { BackColor = Canvas, Padding = new Padding(0, 8, 0, 0) };
        var englishTab = new TabPage("English") { BackColor = Canvas, Padding = new Padding(0, 8, 0, 0) };
        chineseTab.Controls.Add(CreateAboutText(
            "PDF Forge\r\n\r\n" +
            "這是一個以本機處理為優先的 PDF 分割、合併、預覽與批次解鎖工具。\r\n\r\n" +
            "分割 PDF\r\n" +
            "開啟 PDF 後，在中央預覽區瀏覽頁面；點擊剪刀圖示即可新增或移除分割點。\r\n" +
            "每張結果卡都可以修改輸出名稱，並顯示完整頁碼範圍與頁數。\r\n\r\n" +
            "合併 PDF\r\n" +
            "將 PDF、DOC 或 DOCX 拖曳到檔案區，調整順序後選擇 Merge PDFs 儲存結果。\r\n\r\n" +
            "批次解鎖\r\n" +
            "選取多個 PDF、輸入共同的已知密碼並指定輸出資料夾。PDF Forge 不會猜測或破解密碼，來源檔案不會被覆寫。\r\n\r\n" +
            "瀏覽操作\r\n" +
            "可使用滑鼠滾輪、頁碼欄位或上下鍵翻頁；縮圖會在背景載入。\r\n\r\n" +
            "注意事項\r\n" +
            "所有處理都在本機完成。DOC/DOCX 轉換需要 Microsoft Word 桌面版自動化；合併後不保證保留簽章、表單、附件、圖層、標記結構或 PDF/A、PDF/UA 相容性。發布前請務必檢查輸出檔案。\r\n\r\n" +
            "開源授權\r\n" +
            "Copyright © 2026 yaosio232。PDF Forge 依 GNU AGPL v3 授權提供，可依條款使用、修改與再散布；本程式不提供任何保固。\r\n" +
            "原始碼：https://github.com/yaosio232/pdf-forge\r\n" +
            "授權全文：https://github.com/yaosio232/pdf-forge/blob/main/LICENSE.md\r\n" +
            "第三方聲明：https://github.com/yaosio232/pdf-forge/blob/main/NOTICE.md\r\n" +
            "PDF 引擎 iTextSharp 5.5.13.4 採 AGPLv3／商業授權雙軌；BouncyCastle.Cryptography 採 MIT 授權。\r\n\r\n" +
            "記錄\r\n" +
            "每次操作會在選定輸出 PDF 的同一資料夾留下 PDF_Forge_Error_Log.txt。"));
        englishTab.Controls.Add(CreateAboutText(
            "PDF Forge\r\n\r\n" +
            "A local-first utility for splitting, combining, reviewing, and batch-unlocking PDF files.\r\n\r\n" +
            "Split PDF\r\n" +
            "Open a PDF, browse pages in the center viewer, and click a scissors icon to add or remove a split point. Download one part from its card or save every part together.\r\n" +
            "Each result card shows an editable output name, the exact page range, and the page count. Click the card to jump to its first page.\r\n\r\n" +
            "Merge PDFs\r\n" +
            "Drop PDF, DOC, or DOCX files into the drop zone, arrange their order, then choose Merge PDFs to save the result.\r\n\r\n" +
            "Unlock PDFs\r\n" +
            "Choose multiple PDFs, enter their shared known password, and select an output folder. PDF Forge does not guess or crack passwords, and it never overwrites source files.\r\n\r\n" +
            "Navigation\r\n" +
            "Use the mouse wheel, the page field, or the Up/Down keys to move through pages. When zoomed in, use the scroll bars or hold the middle mouse button and drag. Thumbnails are loaded in the background.\r\n\r\n" +
            "Notes\r\n" +
            "All processing stays on this computer. DOC/DOCX conversion requires Microsoft Word desktop automation. Existing signatures, forms, attachments, layers, tagged structure, and PDF/A or PDF/UA conformance are not guaranteed after merging. Always review generated files before distribution.\r\n\r\n" +
            "Open-source license\r\n" +
            "Copyright © 2026 yaosio232. PDF Forge is licensed under GNU AGPL v3. You may use, modify, and redistribute it under that license. This software comes with no warranty.\r\n" +
            "Source code: https://github.com/yaosio232/pdf-forge\r\n" +
            "Full license: https://github.com/yaosio232/pdf-forge/blob/main/LICENSE.md\r\n" +
            "Third-party notices: https://github.com/yaosio232/pdf-forge/blob/main/NOTICE.md\r\n" +
            "The PDF engine iTextSharp 5.5.13.4 is dual-licensed under AGPLv3/commercial terms; BouncyCastle.Cryptography is MIT-licensed.\r\n\r\n" +
            "Log\r\n" +
            "Each operation writes PDF_Forge_Error_Log.txt next to the selected output PDF."));
        languageTabs.TabPages.Add(chineseTab);
        languageTabs.TabPages.Add(englishTab);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
            WrapContents = false
        };
        var close = new Button
        {
            Text = "Close / 關閉",
            DialogResult = DialogResult.OK,
            Size = new Size(124, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            Margin = new Padding(8, 0, 0, 0),
            TabIndex = 0,
            AccessibleName = "Close About"
        };
        close.FlatAppearance.BorderSize = 0;
        var openLog = new Button
        {
            Text = "Open log / 開啟記錄",
            Size = new Size(230, 34),
            MinimumSize = new Size(230, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Ink,
            Margin = new Padding(0),
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleCenter,
            TabIndex = 1,
            AccessibleName = "Open log folder"
        };
        openLog.FlatAppearance.BorderColor = Border;
        openLog.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{Logger.LogFilePath}\"",
                UseShellExecute = true
            });
        };
        var versionLabel = new Label
        {
            Text = $"Version {version}\r\nBuild {buildDate}",
            Size = new Size(170, 40),
            Margin = new Padding(8, 0, 0, 0),
            ForeColor = Subtle,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.BottomRight,
            AccessibleName = $"PDF Forge version {version}, build date {buildDate}"
        };
        about.AcceptButton = close;
        about.CancelButton = close;
        actions.Controls.Add(versionLabel);
        actions.Controls.Add(close);
        actions.Controls.Add(openLog);
        about.Controls.Add(languageTabs);
        about.Controls.Add(actions);
        about.ShowDialog(this);
    }
}
