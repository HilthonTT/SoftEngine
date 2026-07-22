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
        lblWorldsHeader = new Label();
        lstDemos = new ListBox();
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
        tlpSidebar.SuspendLayout();
        flpDisplay.SuspendLayout();
        flpShading.SuspendLayout();
        pnlViewport.SuspendLayout();
        SuspendLayout();
        //
        // tlpSidebar
        //
        tlpSidebar.ColumnCount = 1;
        tlpSidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpSidebar.Controls.Add(lblTitle, 0, 0);
        tlpSidebar.Controls.Add(lblWorldsHeader, 0, 1);
        tlpSidebar.Controls.Add(lstDemos, 0, 2);
        tlpSidebar.Controls.Add(lblDisplayHeader, 0, 3);
        tlpSidebar.Controls.Add(flpDisplay, 0, 4);
        tlpSidebar.Controls.Add(lblShadingHeader, 0, 5);
        tlpSidebar.Controls.Add(flpShading, 0, 6);
        tlpSidebar.Dock = DockStyle.Left;
        tlpSidebar.Name = "tlpSidebar";
        tlpSidebar.Padding = new Padding(16, 18, 16, 14);
        tlpSidebar.RowCount = 7;
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.Size = new Size(236, 700);
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
        // lblWorldsHeader
        //
        lblWorldsHeader.AutoSize = true;
        lblWorldsHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblWorldsHeader.Margin = new Padding(2, 8, 0, 6);
        lblWorldsHeader.Name = "lblWorldsHeader";
        lblWorldsHeader.Text = "WORLDS";
        //
        // lstDemos
        //
        lstDemos.BorderStyle = BorderStyle.None;
        lstDemos.Dock = DockStyle.Fill;
        lstDemos.IntegralHeight = false;
        lstDemos.Margin = new Padding(0, 0, 0, 4);
        lstDemos.Name = "lstDemos";
        lstDemos.TabIndex = 1;
        toolTip1.SetToolTip(lstDemos, "Double-click to load a world");
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
        pnlViewport.Padding = new Padding(14);
        pnlViewport.TabIndex = 1;
        //
        // panel3D1
        //
        panel3D1.Dock = DockStyle.Fill;
        panel3D1.Margin = new Padding(0);
        panel3D1.Name = "panel3D1";
        panel3D1.TabIndex = 0;
        //
        // MainScreen
        //
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 700);
        Controls.Add(pnlViewport);
        Controls.Add(tlpSidebar);
        Font = new Font("Segoe UI", 9.75F);
        MinimumSize = new Size(760, 480);
        Name = "MainScreen";
        Text = "SoftEngine";
        tlpSidebar.ResumeLayout(false);
        tlpSidebar.PerformLayout();
        flpDisplay.ResumeLayout(false);
        flpDisplay.PerformLayout();
        flpShading.ResumeLayout(false);
        flpShading.PerformLayout();
        pnlViewport.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel tlpSidebar;
    private Label lblTitle;
    private Label lblWorldsHeader;
    private ListBox lstDemos;
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
}
