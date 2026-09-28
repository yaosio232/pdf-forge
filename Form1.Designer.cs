#nullable enable

namespace Pdf_Merger;

partial class Form1
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel mainLayout = null!;
    private Panel headerPanel = null!;
    private Panel brandMark = null!;
    private Label labelTitle = null!;
    private Label labelSubtitle = null!;
    private Button btnDisclaimer = null!;
    private TableLayoutPanel bodyLayout = null!;
    private Panel workspacePanel = null!;
    private Panel dropZonePanel = null!;
    private Label dropTitle = null!;
    private Label dropSubtitle = null!;
    private Button btnChooseFiles = null!;
    private Panel listCard = null!;
    private Panel listContentPanel = null!;
    private Panel listHeaderPanel = null!;
    private Label labelSection = null!;
    private Label labelCount = null!;
    private Label labelEmptyState = null!;
    private ListView listBox = null!;
    private Panel sidebarPanel = null!;
    private Panel stepsCard = null!;
    private Panel summaryCard = null!;
    private Label summaryNumber = null!;
    private Label summaryCaption = null!;
    private Panel footerPanel = null!;
    private Label labelStatus = null!;
    private TableLayoutPanel footerActions = null!;
    private Button btnMoveUp = null!;
    private Button btnMoveDown = null!;
    private Button btnDelete = null!;
    private Button btnMerge = null!;
    private ToolTip toolTip = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        mainLayout = new TableLayoutPanel();
        headerPanel = new Panel();
        brandMark = new Panel();
        labelTitle = new Label();
        labelSubtitle = new Label();
        btnDisclaimer = new Button();
        bodyLayout = new TableLayoutPanel();
        workspacePanel = new Panel();
        dropZonePanel = new Panel();
        dropTitle = new Label();
        dropSubtitle = new Label();
        btnChooseFiles = new Button();
        listCard = new Panel();
        listContentPanel = new Panel();
        listHeaderPanel = new Panel();
        labelSection = new Label();
        labelCount = new Label();
        labelEmptyState = new Label();
        listBox = new ListView();
        sidebarPanel = new Panel();
        stepsCard = new Panel();
        summaryCard = new Panel();
        summaryNumber = new Label();
        summaryCaption = new Label();
        footerPanel = new Panel();
        labelStatus = new Label();
        footerActions = new TableLayoutPanel();
        btnMoveUp = new Button();
        btnMoveDown = new Button();
        btnDelete = new Button();
        btnMerge = new Button();
        toolTip = new ToolTip(components);

        SuspendLayout();
        mainLayout.SuspendLayout();
        headerPanel.SuspendLayout();
        bodyLayout.SuspendLayout();
        workspacePanel.SuspendLayout();
        dropZonePanel.SuspendLayout();
        listCard.SuspendLayout();
        listHeaderPanel.SuspendLayout();
        sidebarPanel.SuspendLayout();
        stepsCard.SuspendLayout();
        summaryCard.SuspendLayout();
        footerPanel.SuspendLayout();
        footerActions.SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(246, 248, 251);
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(1200, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PDF Forge";
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowCount = 3;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(24, 14, 24, 14);
        mainLayout.BackColor = Color.FromArgb(246, 248, 251);

        headerPanel.BackColor = Color.FromArgb(246, 248, 251);
        headerPanel.Dock = DockStyle.Fill;
        headerPanel.Padding = new Padding(0, 0, 0, 8);

        var headerLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var headerCopy = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 8, 0) };

        brandMark.BackColor = Color.Transparent;
        brandMark.Dock = DockStyle.Fill;
        brandMark.Margin = new Padding(0, 10, 8, 10);
        brandMark.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            DrawAppIcon(e.Graphics, new RectangleF(0, 0, brandMark.ClientSize.Width, brandMark.ClientSize.Height));
        };

        labelTitle.AutoSize = false;
        labelTitle.AutoEllipsis = true;
        labelTitle.Dock = DockStyle.Fill;
        labelTitle.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point);
        labelTitle.ForeColor = Color.FromArgb(20, 31, 51);
        labelTitle.Text = "PDF Forge";
        labelTitle.TextAlign = ContentAlignment.MiddleLeft;

        labelSubtitle.AutoSize = false;
        labelSubtitle.AutoEllipsis = true;
        labelSubtitle.Dock = DockStyle.Fill;
        labelSubtitle.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        labelSubtitle.ForeColor = Color.FromArgb(71, 85, 105);
        labelSubtitle.Padding = new Padding(0, 1, 0, 0);
        labelSubtitle.Visible = false;
        labelSubtitle.TextAlign = ContentAlignment.MiddleLeft;

        btnDisclaimer.Dock = DockStyle.Fill;
        btnDisclaimer.Margin = new Padding(8, 12, 0, 12);
        btnDisclaimer.BackColor = Color.White;
        btnDisclaimer.FlatAppearance.BorderColor = Color.FromArgb(220, 226, 235);
        btnDisclaimer.FlatAppearance.BorderSize = 1;
        btnDisclaimer.FlatStyle = FlatStyle.Flat;
        btnDisclaimer.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        btnDisclaimer.ForeColor = Color.FromArgb(20, 31, 51);
        btnDisclaimer.Size = new Size(120, 36);
        btnDisclaimer.TabIndex = 1;
        btnDisclaimer.Text = "About";
        btnDisclaimer.AccessibleName = "About PDF Forge";
        btnDisclaimer.UseVisualStyleBackColor = false;
        btnDisclaimer.Click += BtnDisclaimer_Click;

        headerCopy.Controls.Add(labelTitle);
        headerLayout.Controls.Add(brandMark, 0, 0);
        headerLayout.Controls.Add(headerCopy, 1, 0);
        headerLayout.Controls.Add(btnDisclaimer, 2, 0);
        headerPanel.Controls.Add(headerLayout);

        bodyLayout.ColumnCount = 2;
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        bodyLayout.RowCount = 1;
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bodyLayout.Dock = DockStyle.Fill;
        bodyLayout.Padding = new Padding(0, 0, 0, 10);

        workspacePanel.Dock = DockStyle.Fill;
        workspacePanel.Padding = new Padding(0, 0, 14, 0);

        dropZonePanel.AllowDrop = true;
        dropZonePanel.BackColor = Color.White;
        dropZonePanel.Dock = DockStyle.Top;
        dropZonePanel.Height = 132;
        dropZonePanel.Padding = new Padding(16);
        dropZonePanel.Paint += DropZonePanel_Paint;
        dropZonePanel.DragEnter += DropTarget_DragEnter;
        dropZonePanel.DragLeave += DropTarget_DragLeave;
        dropZonePanel.DragDrop += DropTarget_DragDrop;

        var dropLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(16, 8, 16, 8)
        };
        dropLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        dropLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        dropLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        dropLayout.AllowDrop = true;
        dropLayout.DragEnter += DropTarget_DragEnter;
        dropLayout.DragLeave += DropTarget_DragLeave;
        dropLayout.DragDrop += DropTarget_DragDrop;

        var dropCopy = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0), AllowDrop = true };
        dropCopy.DragEnter += DropTarget_DragEnter;
        dropCopy.DragLeave += DropTarget_DragLeave;
        dropCopy.DragDrop += DropTarget_DragDrop;
        var dropActions = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), AllowDrop = true };
        dropActions.DragEnter += DropTarget_DragEnter;
        dropActions.DragLeave += DropTarget_DragLeave;
        dropActions.DragDrop += DropTarget_DragDrop;

        dropTitle.AutoSize = false;
        dropTitle.Dock = DockStyle.Top;
        dropTitle.Height = 30;
        dropTitle.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
        dropTitle.ForeColor = Color.FromArgb(20, 31, 51);
        dropTitle.Text = "Add files to your merge queue";
        dropTitle.TextAlign = ContentAlignment.MiddleLeft;
        dropTitle.DragEnter += DropTarget_DragEnter;
        dropTitle.DragDrop += DropTarget_DragDrop;

        dropSubtitle.AutoSize = false;
        dropSubtitle.Dock = DockStyle.Fill;
        dropSubtitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        dropSubtitle.ForeColor = Color.FromArgb(71, 85, 105);
        dropSubtitle.Text = "PDF, DOC, DOCX · files stay local.";
        dropSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        dropSubtitle.UseCompatibleTextRendering = true;
        dropSubtitle.DragEnter += DropTarget_DragEnter;
        dropSubtitle.DragDrop += DropTarget_DragDrop;

        btnChooseFiles.Dock = DockStyle.Top;
        btnChooseFiles.BackColor = Color.White;
        btnChooseFiles.FlatAppearance.BorderColor = Color.FromArgb(29, 78, 216);
        btnChooseFiles.FlatAppearance.BorderSize = 1;
        btnChooseFiles.FlatStyle = FlatStyle.Flat;
        btnChooseFiles.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        btnChooseFiles.ForeColor = Color.FromArgb(29, 78, 216);
        btnChooseFiles.Size = new Size(126, 36);
        btnChooseFiles.TabIndex = 2;
        btnChooseFiles.Text = "Choose files";
        btnChooseFiles.UseVisualStyleBackColor = false;
        btnChooseFiles.Click += ChooseFiles_Click;

        dropCopy.Controls.Add(dropSubtitle);
        dropCopy.Controls.Add(dropTitle);
        dropActions.Controls.Add(btnChooseFiles);
        dropLayout.Controls.Add(dropCopy, 0, 0);
        dropLayout.Controls.Add(dropActions, 1, 0);
        dropZonePanel.Controls.Add(dropLayout);

        listCard.BackColor = Color.White;
        listCard.Dock = DockStyle.Fill;
        listCard.Margin = new Padding(0, 14, 0, 0);
        listCard.Padding = new Padding(1);
        listCard.Paint += SurfacePanel_Paint;

        listHeaderPanel.BackColor = Color.White;
        listHeaderPanel.Dock = DockStyle.Top;
        listHeaderPanel.Height = 56;
        listHeaderPanel.Padding = new Padding(20, 12, 20, 8);

        var queueHeaderLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        queueHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        queueHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queueHeaderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        labelSection.AutoSize = false;
        labelSection.Dock = DockStyle.Fill;
        labelSection.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        labelSection.ForeColor = Color.FromArgb(20, 31, 51);
        labelSection.Text = "MERGE QUEUE";
        labelSection.TextAlign = ContentAlignment.MiddleLeft;

        labelCount.AutoSize = false;
        labelCount.Dock = DockStyle.Fill;
        labelCount.MinimumSize = new Size(120, 0);
        labelCount.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        labelCount.ForeColor = Color.FromArgb(71, 85, 105);
        labelCount.Text = "0 files · scrollable";
        labelCount.TextAlign = ContentAlignment.MiddleRight;

        queueHeaderLayout.Controls.Add(labelSection, 0, 0);
        queueHeaderLayout.Controls.Add(labelCount, 1, 0);
        listHeaderPanel.Controls.Add(queueHeaderLayout);

        listBox.AllowDrop = true;
        listBox.BackColor = Color.White;
        listBox.BorderStyle = BorderStyle.None;
        listBox.Dock = DockStyle.Fill;
        listBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        listBox.ForeColor = Color.FromArgb(20, 31, 51);
        listBox.FullRowSelect = true;
        listBox.GridLines = false;
        listBox.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        listBox.HideSelection = false;
        listBox.LabelEdit = false;
        listBox.MultiSelect = true;
        listBox.Name = "listBox";
        listBox.ShowGroups = false;
        listBox.ShowItemToolTips = true;
        listBox.UseCompatibleStateImageBehavior = false;
        listBox.View = View.Details;
        listBox.TabIndex = 3;
        listBox.AccessibleName = "Merge queue files";
        listBox.Columns.Add("File name", 360);
        listBox.Columns.Add("Type", 62);
        listBox.Columns.Add("Size", 82);
        listBox.Resize += (_, _) => ResizeQueueColumns();
        listBox.SelectedIndexChanged += ListBox_SelectedIndexChanged;
        listBox.KeyDown += ListBox_KeyDown;
        listBox.DragEnter += DropTarget_DragEnter;
        listBox.DragLeave += DropTarget_DragLeave;
        listBox.DragDrop += DropTarget_DragDrop;
        ResizeQueueColumns();

        labelEmptyState.AllowDrop = true;
        labelEmptyState.BackColor = Color.White;
        labelEmptyState.Dock = DockStyle.Fill;
        labelEmptyState.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        labelEmptyState.ForeColor = Color.FromArgb(100, 116, 139);
        labelEmptyState.Text = "No files yet — add documents above.";
        labelEmptyState.TextAlign = ContentAlignment.MiddleCenter;
        labelEmptyState.Padding = new Padding(0);
        labelEmptyState.DragEnter += DropTarget_DragEnter;
        labelEmptyState.DragLeave += DropTarget_DragLeave;
        labelEmptyState.DragDrop += DropTarget_DragDrop;

        listContentPanel.BackColor = Color.White;
        listContentPanel.Dock = DockStyle.Fill;
        listContentPanel.Margin = new Padding(0);
        listContentPanel.Padding = new Padding(0, listHeaderPanel.Height, 0, 0);
        listContentPanel.Controls.Add(listBox);
        listContentPanel.Controls.Add(labelEmptyState);
        labelEmptyState.BringToFront();

        listCard.Controls.Add(listContentPanel);
        listCard.Controls.Add(listHeaderPanel);
        listHeaderPanel.BringToFront();
        workspacePanel.Controls.Add(listCard);
        workspacePanel.Controls.Add(dropZonePanel);

        sidebarPanel.Dock = DockStyle.Fill;

        stepsCard.BackColor = Color.White;
        stepsCard.Dock = DockStyle.Top;
        stepsCard.Height = 168;
        stepsCard.Padding = new Padding(20);
        stepsCard.Paint += SurfacePanel_Paint;

        var stepsTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 31, 51),
            Location = new Point(20, 18),
            Text = "Simple three-step flow"
        };
        var stepOne = CreateStepLabel("01", "Add files", 58);
        var stepTwo = CreateStepLabel("02", "Set order", 94);
        var stepThree = CreateStepLabel("03", "Save PDF", 130);
        stepsCard.Controls.AddRange(new Control[] { stepsTitle, stepOne, stepTwo, stepThree });

        summaryCard.BackColor = Color.FromArgb(239, 246, 255);
        summaryCard.Dock = DockStyle.Top;
        summaryCard.Height = 126;
        summaryCard.Margin = new Padding(0, 14, 0, 0);
        summaryCard.Padding = new Padding(20);

        summaryNumber.AutoSize = true;
        summaryNumber.Font = new Font("Segoe UI Semibold", 30F, FontStyle.Bold, GraphicsUnit.Point);
        summaryNumber.ForeColor = Color.FromArgb(29, 78, 216);
        summaryNumber.Location = new Point(20, 12);
        summaryNumber.Text = "0";
        summaryNumber.TextAlign = ContentAlignment.MiddleLeft;

        summaryCaption.AutoSize = false;
        summaryCaption.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        summaryCaption.ForeColor = Color.FromArgb(29, 78, 216);
        summaryCaption.Location = new Point(20, 78);
        summaryCaption.Size = new Size(190, 24);
        summaryCaption.Text = "Queued files";
        summaryCaption.TextAlign = ContentAlignment.MiddleLeft;
        summaryCaption.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        var summaryHint = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(51, 78, 121),
            Location = new Point(20, 102),
            Size = new Size(190, 18),
            Text = "Local only · no upload.",
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        summaryCard.Controls.AddRange(new Control[] { summaryNumber, summaryCaption, summaryHint });

        sidebarPanel.Controls.Add(summaryCard);
        sidebarPanel.Controls.Add(stepsCard);

        bodyLayout.Controls.Add(workspacePanel, 0, 0);
        bodyLayout.Controls.Add(sidebarPanel, 1, 0);

        footerPanel.BackColor = Color.White;
        footerPanel.Dock = DockStyle.Fill;
        footerPanel.Padding = new Padding(14, 8, 14, 8);
        footerPanel.Paint += SurfacePanel_Paint;

        labelStatus.AutoSize = false;
        labelStatus.Dock = DockStyle.Fill;
        labelStatus.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        labelStatus.ForeColor = Color.FromArgb(71, 85, 105);
        labelStatus.Padding = new Padding(0, 0, 14, 0);
        labelStatus.Text = "Drop files above or choose them to begin.";
        labelStatus.TextAlign = ContentAlignment.MiddleLeft;

        footerActions.AutoSize = false;
        footerActions.Dock = DockStyle.Right;
        footerActions.Margin = new Padding(0);
        footerActions.Padding = new Padding(0);
        footerActions.Width = 430;
        footerActions.ColumnCount = 4;
        footerActions.RowCount = 1;
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
        footerActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        ConfigureFooterButton(btnMoveUp, "↑ Up", "Move selected file up");
        ConfigureFooterButton(btnMoveDown, "↓ Down", "Move selected file down");
        ConfigureFooterButton(btnDelete, "Remove", "Remove selected files");
        btnDelete.Text = string.Empty;
        btnDelete.Padding = new Padding(0);
        btnDelete.TextAlign = ContentAlignment.MiddleCenter;
        btnDelete.Paint += BtnDelete_Paint;

        btnMoveUp.Click += BtnMoveUp_Click;
        btnMoveDown.Click += BtnMoveDown_Click;
        btnDelete.Click += BtnDelete_Click;

        btnMerge.BackColor = Color.FromArgb(29, 78, 216);
        btnMerge.FlatAppearance.BorderSize = 0;
        btnMerge.FlatStyle = FlatStyle.Flat;
        btnMerge.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        btnMerge.ForeColor = Color.White;
        btnMerge.Dock = DockStyle.Fill;
        btnMerge.Margin = new Padding(8, 0, 0, 0);
        btnMerge.Padding = new Padding(0, 2, 0, 0);
        btnMerge.Size = new Size(132, 40);
        btnMerge.TabIndex = 7;
        btnMerge.Text = "Merge PDF";
        btnMerge.TextAlign = ContentAlignment.MiddleCenter;
        btnMerge.AccessibleName = "Merge selected files to PDF";
        btnMerge.UseVisualStyleBackColor = false;
        btnMerge.Click += BtnMerge_Click;

        toolTip.SetToolTip(btnMoveUp, "Move selected file up");
        toolTip.SetToolTip(btnMoveDown, "Move selected file down");
        toolTip.SetToolTip(btnDelete, "Remove selected files");

        footerActions.Controls.Add(btnMoveUp, 0, 0);
        footerActions.Controls.Add(btnMoveDown, 1, 0);
        footerActions.Controls.Add(btnDelete, 2, 0);
        footerActions.Controls.Add(btnMerge, 3, 0);
        footerPanel.Controls.Add(labelStatus);
        footerPanel.Controls.Add(footerActions);

        mainLayout.Controls.Add(headerPanel, 0, 0);
        mainLayout.Controls.Add(bodyLayout, 0, 1);
        mainLayout.Controls.Add(footerPanel, 0, 2);
        Controls.Add(mainLayout);

        ResumeLayout(false);
        mainLayout.ResumeLayout(false);
        headerPanel.ResumeLayout(false);
        headerPanel.PerformLayout();
        bodyLayout.ResumeLayout(false);
        workspacePanel.ResumeLayout(false);
        dropZonePanel.ResumeLayout(false);
        dropZonePanel.PerformLayout();
        listCard.ResumeLayout(false);
        listHeaderPanel.ResumeLayout(false);
        listHeaderPanel.PerformLayout();
        sidebarPanel.ResumeLayout(false);
        stepsCard.ResumeLayout(false);
        stepsCard.PerformLayout();
        summaryCard.ResumeLayout(false);
        summaryCard.PerformLayout();
        footerPanel.ResumeLayout(false);
        footerActions.ResumeLayout(false);
        footerActions.PerformLayout();
    }

    private static Label CreateStepLabel(string number, string text, int top)
    {
        var label = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(91, 105, 126),
            Location = new Point(20, top),
            Size = new Size(220, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Text = $"{number}   {text}",
            TextAlign = ContentAlignment.MiddleLeft
        };
        return label;
    }

    private static void ConfigureFooterButton(Button button, string text, string accessibleName)
    {
        button.AutoSize = false;
        button.BackColor = Color.White;
        button.FlatAppearance.BorderColor = Color.FromArgb(220, 226, 235);
        button.FlatAppearance.BorderSize = 1;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.ForeColor = Color.FromArgb(20, 31, 51);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 8, 0);
        button.Size = new Size(82, 40);
        button.Text = text;
        button.AccessibleName = accessibleName;
        button.UseVisualStyleBackColor = false;
    }
}
