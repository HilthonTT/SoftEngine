using SoftEngine.WinForms.Debugging;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        tlpSidebar = new TableLayoutPanel();
        lblTitle = new Label();
        lblModelHeader = new Label();
        btnLoadModel = new Button();
        lblCurrentModel = new Label();
        lblDisplayHeader = new Label();
        flpDisplay = new FlowLayoutPanel();
        chkShowTriangles = new CheckBox();
        chkShowBackFacesCulling = new CheckBox();
        chkShowXZGrid = new CheckBox();
        chkShowAxes = new CheckBox();
        lblShadingHeader = new Label();
        flpShading = new FlowLayoutPanel();
        rdbNoneShading = new RadioButton();
        rdbClassicShading = new RadioButton();
        rdbFlatShading = new RadioButton();
        rdbGouraudShading = new RadioButton();
        rdbPhongShading = new RadioButton();
        rdbTexturedShading = new RadioButton();
        pnlViewport = new Panel();
        panel3D1 = new Panel3D();
        toolTip1 = new ToolTip(components);

        menuStrip = new MenuStrip();
        mnuFile = new ToolStripMenuItem();
        mnuLoadModel = new ToolStripMenuItem();
        mnuOpenModel = new ToolStripMenuItem();
        mnuScreenshot = new ToolStripMenuItem();
        mnuExit = new ToolStripMenuItem();
        mnuView = new ToolStripMenuItem();
        mnuPixelHistory = new ToolStripMenuItem();
        mnuObjectTable = new ToolStripMenuItem();
        mnuEventList = new ToolStripMenuItem();
        mnuStatsOverlay = new ToolStripMenuItem();
        mnuRecordEvents = new ToolStripMenuItem();
        mnuZoomIn = new ToolStripMenuItem();
        mnuZoomOut = new ToolStripMenuItem();
        mnuZoomActual = new ToolStripMenuItem();
        mnuClearPixel = new ToolStripMenuItem();

        statusStrip = new StatusStrip();
        lblZoomStatus = new ToolStripStatusLabel();
        lblPixelStatus = new ToolStripStatusLabel();
        lblScreenshotHint = new ToolStripStatusLabel();
        lblCameraStatus = new ToolStripStatusLabel();
        lblFrameStatus = new ToolStripStatusLabel();

        splitMain = new SplitContainer();
        splitLeft = new SplitContainer();
        splitRight = new SplitContainer();
        splitCenter = new SplitContainer();

        pixelHistoryPanel = new PixelHistoryPanel();
        objectTablePanel = new GraphicsObjectTablePanel();
        eventListPanel = new GraphicsEventListPanel();

        tmrDebugRefresh = new System.Windows.Forms.Timer(components);

        tlpSidebar.SuspendLayout();
        flpDisplay.SuspendLayout();
        flpShading.SuspendLayout();
        pnlViewport.SuspendLayout();
        menuStrip.SuspendLayout();
        statusStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
        splitMain.Panel1.SuspendLayout();
        splitMain.Panel2.SuspendLayout();
        splitMain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitLeft).BeginInit();
        splitLeft.Panel1.SuspendLayout();
        splitLeft.Panel2.SuspendLayout();
        splitLeft.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitRight).BeginInit();
        splitRight.Panel1.SuspendLayout();
        splitRight.Panel2.SuspendLayout();
        splitRight.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitCenter).BeginInit();
        splitCenter.Panel1.SuspendLayout();
        splitCenter.Panel2.SuspendLayout();
        splitCenter.SuspendLayout();
        SuspendLayout();
        //
        // tlpSidebar
        //
        tlpSidebar.ColumnCount = 1;
        tlpSidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpSidebar.Controls.Add(lblTitle, 0, 0);
        tlpSidebar.Controls.Add(lblModelHeader, 0, 1);
        tlpSidebar.Controls.Add(btnLoadModel, 0, 2);
        tlpSidebar.Controls.Add(lblCurrentModel, 0, 3);
        tlpSidebar.Controls.Add(lblDisplayHeader, 0, 4);
        tlpSidebar.Controls.Add(flpDisplay, 0, 5);
        tlpSidebar.Controls.Add(lblShadingHeader, 0, 6);
        tlpSidebar.Controls.Add(flpShading, 0, 7);
        tlpSidebar.AutoScroll = true;
        tlpSidebar.Dock = DockStyle.Fill;
        tlpSidebar.Name = "tlpSidebar";
        tlpSidebar.Padding = new Padding(16, 12, 16, 12);
        tlpSidebar.RowCount = 9;
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpSidebar.TabIndex = 0;
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        lblTitle.Margin = new Padding(0, 0, 0, 10);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "SoftEngine";
        //
        // lblModelHeader
        //
        lblModelHeader.AutoSize = true;
        lblModelHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblModelHeader.Margin = new Padding(2, 8, 0, 6);
        lblModelHeader.Name = "lblModelHeader";
        lblModelHeader.Text = "MODEL";
        //
        // btnLoadModel
        //
        btnLoadModel.Dock = DockStyle.Fill;
        btnLoadModel.FlatStyle = FlatStyle.Flat;
        btnLoadModel.Height = 32;
        btnLoadModel.Margin = new Padding(0, 0, 0, 6);
        btnLoadModel.Name = "btnLoadModel";
        btnLoadModel.TabIndex = 1;
        btnLoadModel.Text = "Load model…";
        btnLoadModel.UseVisualStyleBackColor = false;
        toolTip1.SetToolTip(btnLoadModel, "Pick a bundled world or open an OBJ/Collada file");
        //
        // lblCurrentModel
        //
        lblCurrentModel.AutoSize = true;
        lblCurrentModel.Margin = new Padding(2, 0, 0, 6);
        lblCurrentModel.Name = "lblCurrentModel";
        lblCurrentModel.Text = "Skull";
        //
        // lblDisplayHeader
        //
        lblDisplayHeader.AutoSize = true;
        lblDisplayHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblDisplayHeader.Margin = new Padding(2, 10, 0, 6);
        lblDisplayHeader.Name = "lblDisplayHeader";
        lblDisplayHeader.Text = "DISPLAY";
        //
        // flpDisplay
        //
        flpDisplay.AutoSize = true;
        flpDisplay.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flpDisplay.Controls.Add(chkShowTriangles);
        flpDisplay.Controls.Add(chkShowBackFacesCulling);
        flpDisplay.Controls.Add(chkShowXZGrid);
        flpDisplay.Controls.Add(chkShowAxes);
        flpDisplay.FlowDirection = FlowDirection.TopDown;
        flpDisplay.Margin = new Padding(0);
        flpDisplay.Name = "flpDisplay";
        flpDisplay.WrapContents = false;
        //
        // chkShowTriangles
        //
        chkShowTriangles.AutoSize = true;
        chkShowTriangles.Margin = new Padding(2, 2, 0, 2);
        chkShowTriangles.Name = "chkShowTriangles";
        chkShowTriangles.Text = "Triangles";
        chkShowTriangles.UseVisualStyleBackColor = true;
        //
        // chkShowBackFacesCulling
        //
        chkShowBackFacesCulling.AutoSize = true;
        chkShowBackFacesCulling.Margin = new Padding(2, 2, 0, 2);
        chkShowBackFacesCulling.Name = "chkShowBackFacesCulling";
        chkShowBackFacesCulling.Text = "Back faces culling";
        chkShowBackFacesCulling.UseVisualStyleBackColor = true;
        //
        // chkShowXZGrid
        //
        chkShowXZGrid.AutoSize = true;
        chkShowXZGrid.Margin = new Padding(2, 2, 0, 2);
        chkShowXZGrid.Name = "chkShowXZGrid";
        chkShowXZGrid.Text = "XZ grid";
        chkShowXZGrid.UseVisualStyleBackColor = true;
        //
        // chkShowAxes
        //
        chkShowAxes.AutoSize = true;
        chkShowAxes.Margin = new Padding(2, 2, 0, 2);
        chkShowAxes.Name = "chkShowAxes";
        chkShowAxes.Text = "Axes";
        chkShowAxes.UseVisualStyleBackColor = true;
        //
        // lblShadingHeader
        //
        lblShadingHeader.AutoSize = true;
        lblShadingHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblShadingHeader.Margin = new Padding(2, 10, 0, 6);
        lblShadingHeader.Name = "lblShadingHeader";
        lblShadingHeader.Text = "SHADING";
        //
        // flpShading
        //
        flpShading.AutoSize = true;
        flpShading.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flpShading.Controls.Add(rdbNoneShading);
        flpShading.Controls.Add(rdbClassicShading);
        flpShading.Controls.Add(rdbFlatShading);
        flpShading.Controls.Add(rdbGouraudShading);
        flpShading.Controls.Add(rdbPhongShading);
        flpShading.Controls.Add(rdbTexturedShading);
        flpShading.FlowDirection = FlowDirection.TopDown;
        flpShading.Margin = new Padding(0);
        flpShading.Name = "flpShading";
        flpShading.WrapContents = false;
        //
        // rdbNoneShading
        //
        rdbNoneShading.AutoSize = true;
        rdbNoneShading.Margin = new Padding(2, 2, 0, 2);
        rdbNoneShading.Name = "rdbNoneShading";
        rdbNoneShading.TabStop = true;
        rdbNoneShading.Text = "None";
        rdbNoneShading.UseVisualStyleBackColor = true;
        //
        // rdbClassicShading
        //
        rdbClassicShading.AutoSize = true;
        rdbClassicShading.Margin = new Padding(2, 2, 0, 2);
        rdbClassicShading.Name = "rdbClassicShading";
        rdbClassicShading.TabStop = true;
        rdbClassicShading.Text = "Classic";
        rdbClassicShading.UseVisualStyleBackColor = true;
        //
        // rdbFlatShading
        //
        rdbFlatShading.AutoSize = true;
        rdbFlatShading.Margin = new Padding(2, 2, 0, 2);
        rdbFlatShading.Name = "rdbFlatShading";
        rdbFlatShading.TabStop = true;
        rdbFlatShading.Text = "Flat";
        rdbFlatShading.UseVisualStyleBackColor = true;
        //
        // rdbGouraudShading
        //
        rdbGouraudShading.AutoSize = true;
        rdbGouraudShading.Margin = new Padding(2, 2, 0, 2);
        rdbGouraudShading.Name = "rdbGouraudShading";
        rdbGouraudShading.TabStop = true;
        rdbGouraudShading.Text = "Gouraud";
        rdbGouraudShading.UseVisualStyleBackColor = true;
        //
        // rdbPhongShading
        //
        rdbPhongShading.AutoSize = true;
        rdbPhongShading.Margin = new Padding(2, 2, 0, 2);
        rdbPhongShading.Name = "rdbPhongShading";
        rdbPhongShading.TabStop = true;
        rdbPhongShading.Text = "Phong";
        rdbPhongShading.UseVisualStyleBackColor = true;
        //
        // rdbTexturedShading
        //
        rdbTexturedShading.AutoSize = true;
        rdbTexturedShading.Margin = new Padding(2, 2, 0, 2);
        rdbTexturedShading.Name = "rdbTexturedShading";
        rdbTexturedShading.TabStop = true;
        rdbTexturedShading.Text = "Textured";
        rdbTexturedShading.UseVisualStyleBackColor = true;
        //
        // pnlViewport
        //
        pnlViewport.Controls.Add(panel3D1);
        pnlViewport.Dock = DockStyle.Fill;
        pnlViewport.Name = "pnlViewport";
        pnlViewport.Padding = new Padding(10);
        pnlViewport.TabIndex = 1;
        //
        // panel3D1
        //
        panel3D1.Dock = DockStyle.Fill;
        panel3D1.Margin = new Padding(0);
        panel3D1.Name = "panel3D1";
        panel3D1.TabIndex = 0;
        //
        // menuStrip
        //
        menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuView });
        menuStrip.Name = "menuStrip";
        menuStrip.TabIndex = 10;
        //
        // mnuFile
        //
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuLoadModel, mnuOpenModel, new ToolStripSeparator(), mnuScreenshot, new ToolStripSeparator(), mnuExit });
        mnuFile.Name = "mnuFile";
        mnuFile.Text = "&File";
        //
        // mnuLoadModel
        //
        mnuLoadModel.Name = "mnuLoadModel";
        mnuLoadModel.ShortcutKeys = Keys.Control | Keys.M;
        mnuLoadModel.Text = "&Load model…";
        //
        // mnuOpenModel
        //
        mnuOpenModel.Name = "mnuOpenModel";
        mnuOpenModel.ShortcutKeys = Keys.Control | Keys.O;
        mnuOpenModel.Text = "&Open model file…";
        //
        // mnuScreenshot
        //
        mnuScreenshot.Name = "mnuScreenshot";
        mnuScreenshot.ShortcutKeys = Keys.F12;
        mnuScreenshot.Text = "Save &screenshot…";
        //
        // mnuExit
        //
        mnuExit.Name = "mnuExit";
        mnuExit.Text = "E&xit";
        //
        // mnuView
        //
        mnuView.DropDownItems.AddRange(new ToolStripItem[]
        {
            mnuPixelHistory, mnuObjectTable, mnuEventList, new ToolStripSeparator(),
            mnuStatsOverlay, mnuRecordEvents, new ToolStripSeparator(),
            mnuZoomIn, mnuZoomOut, mnuZoomActual, new ToolStripSeparator(),
            mnuClearPixel,
        });
        mnuView.Name = "mnuView";
        mnuView.Text = "&View";
        //
        // mnuPixelHistory
        //
        mnuPixelHistory.Checked = true;
        mnuPixelHistory.CheckOnClick = true;
        mnuPixelHistory.CheckState = CheckState.Checked;
        mnuPixelHistory.Name = "mnuPixelHistory";
        mnuPixelHistory.Text = "&Pixel History";
        //
        // mnuObjectTable
        //
        mnuObjectTable.Checked = true;
        mnuObjectTable.CheckOnClick = true;
        mnuObjectTable.CheckState = CheckState.Checked;
        mnuObjectTable.Name = "mnuObjectTable";
        mnuObjectTable.Text = "Graphics &Object Table";
        //
        // mnuEventList
        //
        mnuEventList.Checked = true;
        mnuEventList.CheckOnClick = true;
        mnuEventList.CheckState = CheckState.Checked;
        mnuEventList.Name = "mnuEventList";
        mnuEventList.Text = "Graphics &Event List";
        //
        // mnuStatsOverlay
        //
        mnuStatsOverlay.Checked = true;
        mnuStatsOverlay.CheckOnClick = true;
        mnuStatsOverlay.CheckState = CheckState.Checked;
        mnuStatsOverlay.Name = "mnuStatsOverlay";
        mnuStatsOverlay.Text = "&Stats overlay";
        //
        // mnuRecordEvents
        //
        mnuRecordEvents.Checked = true;
        mnuRecordEvents.CheckOnClick = true;
        mnuRecordEvents.CheckState = CheckState.Checked;
        mnuRecordEvents.Name = "mnuRecordEvents";
        mnuRecordEvents.Text = "&Record graphics events";
        //
        // mnuZoomIn
        //
        mnuZoomIn.Name = "mnuZoomIn";
        mnuZoomIn.ShortcutKeys = Keys.Control | Keys.Oemplus;
        mnuZoomIn.Text = "Zoom &In";
        //
        // mnuZoomOut
        //
        mnuZoomOut.Name = "mnuZoomOut";
        mnuZoomOut.ShortcutKeys = Keys.Control | Keys.OemMinus;
        mnuZoomOut.Text = "Zoom O&ut";
        //
        // mnuZoomActual
        //
        mnuZoomActual.Name = "mnuZoomActual";
        mnuZoomActual.ShortcutKeys = Keys.Control | Keys.D0;
        mnuZoomActual.Text = "&Reset zoom (100%)";
        //
        // mnuClearPixel
        //
        mnuClearPixel.Name = "mnuClearPixel";
        mnuClearPixel.Text = "&Clear pixel selection";
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblZoomStatus, lblPixelStatus, lblScreenshotHint, lblCameraStatus, lblFrameStatus });
        statusStrip.Name = "statusStrip";
        statusStrip.SizingGrip = false;
        statusStrip.TabIndex = 11;
        //
        // lblZoomStatus
        //
        lblZoomStatus.AutoSize = false;
        lblZoomStatus.Name = "lblZoomStatus";
        lblZoomStatus.Width = 210;
        lblZoomStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblZoomStatus.Text = "Zoom: 100%";
        //
        // lblPixelStatus
        //
        lblPixelStatus.AutoSize = false;
        lblPixelStatus.Name = "lblPixelStatus";
        lblPixelStatus.Spring = true;
        lblPixelStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblPixelStatus.Text = "Selected pixel: none";
        //
        // lblScreenshotHint
        //
        lblScreenshotHint.Name = "lblScreenshotHint";
        lblScreenshotHint.TextAlign = ContentAlignment.MiddleRight;
        lblScreenshotHint.Text = "F12: Screenshot";
        lblScreenshotHint.ToolTipText = "Save the current view as a PNG (File → Save screenshot…)";
        //
        // lblCameraStatus
        //
        lblCameraStatus.AutoSize = false;
        lblCameraStatus.Name = "lblCameraStatus";
        lblCameraStatus.Width = 240;
        lblCameraStatus.TextAlign = ContentAlignment.MiddleRight;
        lblCameraStatus.Text = "Camera:";
        //
        // lblFrameStatus
        //
        lblFrameStatus.AutoSize = false;
        lblFrameStatus.Name = "lblFrameStatus";
        lblFrameStatus.Width = 190;
        lblFrameStatus.TextAlign = ContentAlignment.MiddleRight;
        lblFrameStatus.Text = "Frame:";
        //
        // pixelHistoryPanel
        //
        pixelHistoryPanel.Dock = DockStyle.Fill;
        pixelHistoryPanel.Name = "pixelHistoryPanel";
        //
        // objectTablePanel
        //
        objectTablePanel.Dock = DockStyle.Fill;
        objectTablePanel.Name = "objectTablePanel";
        //
        // eventListPanel
        //
        eventListPanel.Dock = DockStyle.Fill;
        eventListPanel.Name = "eventListPanel";
        //
        // splitCenter — viewport over the object table
        //
        splitCenter.Dock = DockStyle.Fill;
        splitCenter.Name = "splitCenter";
        splitCenter.Orientation = Orientation.Horizontal;
        splitCenter.Panel1.Controls.Add(pnlViewport);
        splitCenter.Panel1.Controls.Add(statusStrip);
        splitCenter.Panel1MinSize = 160;
        splitCenter.Panel2.Controls.Add(objectTablePanel);
        splitCenter.Panel2MinSize = 80;
        splitCenter.Size = new Size(910, 812);
        splitCenter.SplitterDistance = 540;
        splitCenter.SplitterWidth = 6;
        splitCenter.FixedPanel = FixedPanel.Panel2;
        splitCenter.TabIndex = 0;
        //
        // splitRight — centre column beside the event list
        //
        splitRight.Dock = DockStyle.Fill;
        splitRight.Name = "splitRight";
        splitRight.Panel1.Controls.Add(splitCenter);
        splitRight.Panel1MinSize = 240;
        splitRight.Panel2.Controls.Add(eventListPanel);
        splitRight.Panel2MinSize = 120;
        splitRight.Size = new Size(1250, 812);
        splitRight.SplitterDistance = 870;
        splitRight.SplitterWidth = 6;
        splitRight.FixedPanel = FixedPanel.Panel2;
        splitRight.TabIndex = 1;
        //
        // splitLeft — controls over the pixel history
        //
        splitLeft.Dock = DockStyle.Fill;
        splitLeft.Name = "splitLeft";
        splitLeft.Orientation = Orientation.Horizontal;
        splitLeft.Panel1.Controls.Add(tlpSidebar);
        splitLeft.Panel1MinSize = 160;
        splitLeft.Panel2.Controls.Add(pixelHistoryPanel);
        splitLeft.Panel2MinSize = 120;
        splitLeft.Size = new Size(290, 812);
        splitLeft.SplitterDistance = 470;
        splitLeft.SplitterWidth = 6;
        splitLeft.TabIndex = 0;
        //
        // splitMain — left column beside everything else
        //
        splitMain.Dock = DockStyle.Fill;
        splitMain.Name = "splitMain";
        splitMain.Panel1.Controls.Add(splitLeft);
        splitMain.Panel1MinSize = 180;
        splitMain.Panel2.Controls.Add(splitRight);
        splitMain.Panel2MinSize = 320;
        splitMain.Size = new Size(1546, 812);
        splitMain.SplitterDistance = 290;
        splitMain.SplitterWidth = 6;
        splitMain.FixedPanel = FixedPanel.Panel1;
        splitMain.TabIndex = 0;
        //
        // tmrDebugRefresh
        //
        tmrDebugRefresh.Interval = 120;
        //
        // MainScreen
        //
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1546, 836);
        Controls.Add(splitMain);
        Controls.Add(menuStrip);
        Font = new Font("Segoe UI", 9.75F);
        MainMenuStrip = menuStrip;
        MinimumSize = new Size(900, 560);
        Name = "MainScreen";
        Text = "SoftEngine";
        tlpSidebar.ResumeLayout(false);
        tlpSidebar.PerformLayout();
        flpDisplay.ResumeLayout(false);
        flpDisplay.PerformLayout();
        flpShading.ResumeLayout(false);
        flpShading.PerformLayout();
        pnlViewport.ResumeLayout(false);
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        splitCenter.Panel1.ResumeLayout(false);
        splitCenter.Panel1.PerformLayout();
        splitCenter.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitCenter).EndInit();
        splitCenter.ResumeLayout(false);
        splitRight.Panel1.ResumeLayout(false);
        splitRight.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitRight).EndInit();
        splitRight.ResumeLayout(false);
        splitLeft.Panel1.ResumeLayout(false);
        splitLeft.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitLeft).EndInit();
        splitLeft.ResumeLayout(false);
        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel tlpSidebar;
    private Label lblTitle;
    private Label lblModelHeader;
    private Button btnLoadModel;
    private Label lblCurrentModel;
    private Label lblDisplayHeader;
    private FlowLayoutPanel flpDisplay;
    private CheckBox chkShowTriangles;
    private CheckBox chkShowBackFacesCulling;
    private CheckBox chkShowXZGrid;
    private CheckBox chkShowAxes;
    private Label lblShadingHeader;
    private FlowLayoutPanel flpShading;
    private RadioButton rdbNoneShading;
    private RadioButton rdbClassicShading;
    private RadioButton rdbFlatShading;
    private RadioButton rdbGouraudShading;
    private RadioButton rdbPhongShading;
    private RadioButton rdbTexturedShading;
    private Panel pnlViewport;
    private Panel3D panel3D1;
    private ToolTip toolTip1;

    private MenuStrip menuStrip;
    private ToolStripMenuItem mnuFile;
    private ToolStripMenuItem mnuLoadModel;
    private ToolStripMenuItem mnuOpenModel;
    private ToolStripMenuItem mnuScreenshot;
    private ToolStripMenuItem mnuExit;
    private ToolStripMenuItem mnuView;
    private ToolStripMenuItem mnuPixelHistory;
    private ToolStripMenuItem mnuObjectTable;
    private ToolStripMenuItem mnuEventList;
    private ToolStripMenuItem mnuStatsOverlay;
    private ToolStripMenuItem mnuRecordEvents;
    private ToolStripMenuItem mnuZoomIn;
    private ToolStripMenuItem mnuZoomOut;
    private ToolStripMenuItem mnuZoomActual;
    private ToolStripMenuItem mnuClearPixel;

    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblZoomStatus;
    private ToolStripStatusLabel lblScreenshotHint;
    private ToolStripStatusLabel lblPixelStatus;
    private ToolStripStatusLabel lblCameraStatus;
    private ToolStripStatusLabel lblFrameStatus;

    private SplitContainer splitMain;
    private SplitContainer splitLeft;
    private SplitContainer splitRight;
    private SplitContainer splitCenter;

    private PixelHistoryPanel pixelHistoryPanel;
    private GraphicsObjectTablePanel objectTablePanel;
    private GraphicsEventListPanel eventListPanel;

    private System.Windows.Forms.Timer tmrDebugRefresh;
}
