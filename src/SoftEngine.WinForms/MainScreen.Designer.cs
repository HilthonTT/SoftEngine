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
        pnlSidebar = new Panel();
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
        chkShowSkeleton = new CheckBox();
        chkAnimate = new CheckBox();
        chkFog = new CheckBox();
        chkShadows = new CheckBox();
        chkSky = new CheckBox();
        chkHdrSky = new CheckBox();
        chkPanorama = new CheckBox();
        btnPanorama = new Button();
        chkBakedLight = new CheckBox();
        btnBake = new Button();
        chkGammaCorrect = new CheckBox();
        chkHighDynamicRange = new CheckBox();
        chkTextureFiltering = new CheckBox();
        chkTrilinear = new CheckBox();
        chkSuperSampling = new CheckBox();
        chkTemporalAntiAliasing = new CheckBox();
        chkMotionBlur = new CheckBox();
        lblShadingHeader = new Label();
        flpShading = new FlowLayoutPanel();
        rdbNoneShading = new RadioButton();
        rdbClassicShading = new RadioButton();
        rdbFlatShading = new RadioButton();
        rdbGouraudShading = new RadioButton();
        rdbPhongShading = new RadioButton();
        rdbTexturedShading = new RadioButton();
        rdbMaterialShading = new RadioButton();
        rdbPbrShading = new RadioButton();
        lblPostHeader = new Label();
        flpPost = new FlowLayoutPanel();
        chkSsao = new CheckBox();
        chkBloom = new CheckBox();
        chkToneMap = new CheckBox();
        chkFxaa = new CheckBox();
        chkVignette = new CheckBox();
        lblBufferHeader = new Label();
        cboBufferView = new ComboBox();
        lblCascadeHeader = new Label();
        cboCascades = new ComboBox();
        lblGizmoHeader = new Label();
        cboGizmo = new ComboBox();
        chkSnap = new CheckBox();
        pnlViewport = new Panel();
        panel3D1 = new Panel3D();
        toolTip1 = new ToolTip(components);
        menuStrip = new MenuStrip();
        mnuFile = new ToolStripMenuItem();
        mnuLoadModel = new ToolStripMenuItem();
        mnuOpenModel = new ToolStripMenuItem();
        mnuOpenScene = new ToolStripMenuItem();
        mnuSaveScene = new ToolStripMenuItem();
        mnuScreenshot = new ToolStripMenuItem();
        mnuExit = new ToolStripMenuItem();
        mnuEdit = new ToolStripMenuItem();
        mnuUndo = new ToolStripMenuItem();
        mnuRedo = new ToolStripMenuItem();
        mnuSnap = new ToolStripMenuItem();
        mnuView = new ToolStripMenuItem();
        mnuRenderedBy = new ToolStripMenuItem();
        mnuRenderCpu = new ToolStripMenuItem();
        mnuRenderGpu = new ToolStripMenuItem();
        mnuRenderTrace = new ToolStripMenuItem();
        mnuPixelHistory = new ToolStripMenuItem();
        mnuObjectTable = new ToolStripMenuItem();
        mnuEventList = new ToolStripMenuItem();
        mnuStatsOverlay = new ToolStripMenuItem();
        mnuRecordEvents = new ToolStripMenuItem();
        mnuFrameHistory = new ToolStripMenuItem();
        mnuKeepFrames = new ToolStripMenuItem();
        mnuPreviousFrame = new ToolStripMenuItem();
        mnuNextFrame = new ToolStripMenuItem();
        mnuLatestFrame = new ToolStripMenuItem();
        mnuAxisViews = new ToolStripMenuItem();
        mnuViewFront = new ToolStripMenuItem();
        mnuViewBack = new ToolStripMenuItem();
        mnuViewRight = new ToolStripMenuItem();
        mnuViewLeft = new ToolStripMenuItem();
        mnuViewTop = new ToolStripMenuItem();
        mnuViewBottom = new ToolStripMenuItem();
        mnuViewOpposite = new ToolStripMenuItem();
        mnuTurnX = new ToolStripMenuItem();
        mnuTurnY = new ToolStripMenuItem();
        mnuTurnZ = new ToolStripMenuItem();
        mnuZoomIn = new ToolStripMenuItem();
        mnuZoomOut = new ToolStripMenuItem();
        mnuZoomActual = new ToolStripMenuItem();
        mnuClearPixel = new ToolStripMenuItem();
        statusStrip = new StatusStrip();
        lblZoomStatus = new ToolStripStatusLabel();
        lblPixelStatus = new ToolStripStatusLabel();
        lblScreenshotHint = new ToolStripStatusLabel();
        lblBackendStatus = new ToolStripStatusLabel();
        lblCameraStatus = new ToolStripStatusLabel();
        lblFrameStatus = new ToolStripStatusLabel();
        splitMain = new SplitContainer();
        splitLeft = new SplitContainer();
        pixelHistoryPanel = new PixelHistoryPanel();
        splitRight = new SplitContainer();
        splitCenter = new SplitContainer();
        objectTablePanel = new GraphicsObjectTablePanel();
        eventListPanel = new GraphicsEventListPanel();
        tmrDebugRefresh = new System.Windows.Forms.Timer(components);
        pnlSidebar.SuspendLayout();
        tlpSidebar.SuspendLayout();
        flpDisplay.SuspendLayout();
        flpShading.SuspendLayout();
        flpPost.SuspendLayout();
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
        // pnlSidebar
        // 
        pnlSidebar.AutoScroll = true;
        pnlSidebar.Controls.Add(tlpSidebar);
        pnlSidebar.Dock = DockStyle.Fill;
        pnlSidebar.Location = new Point(0, 0);
        pnlSidebar.Name = "pnlSidebar";
        pnlSidebar.Size = new Size(290, 469);
        pnlSidebar.TabIndex = 0;
        // 
        // tlpSidebar
        // 
        tlpSidebar.AutoSize = true;
        tlpSidebar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
        tlpSidebar.Controls.Add(lblPostHeader, 0, 8);
        tlpSidebar.Controls.Add(flpPost, 0, 9);
        tlpSidebar.Controls.Add(lblBufferHeader, 0, 10);
        tlpSidebar.Controls.Add(cboBufferView, 0, 11);
        tlpSidebar.Controls.Add(lblCascadeHeader, 0, 12);
        tlpSidebar.Controls.Add(cboCascades, 0, 13);
        tlpSidebar.Controls.Add(lblGizmoHeader, 0, 14);
        tlpSidebar.Controls.Add(cboGizmo, 0, 15);
        tlpSidebar.Controls.Add(chkSnap, 0, 16);
        tlpSidebar.Dock = DockStyle.Top;
        tlpSidebar.Location = new Point(0, 0);
        tlpSidebar.Name = "tlpSidebar";
        tlpSidebar.Padding = new Padding(16, 12, 16, 12);
        tlpSidebar.RowCount = 17;
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.RowStyles.Add(new RowStyle());
        tlpSidebar.Size = new Size(273, 1233);
        tlpSidebar.TabIndex = 0;
        // 
        // lblTitle
        // 
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        lblTitle.Location = new Point(16, 12);
        lblTitle.Margin = new Padding(0, 0, 0, 10);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(105, 25);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "SoftEngine";
        // 
        // lblModelHeader
        // 
        lblModelHeader.AutoSize = true;
        lblModelHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblModelHeader.Location = new Point(18, 55);
        lblModelHeader.Margin = new Padding(2, 8, 0, 6);
        lblModelHeader.Name = "lblModelHeader";
        lblModelHeader.Size = new Size(46, 13);
        lblModelHeader.TabIndex = 1;
        lblModelHeader.Text = "MODEL";
        // 
        // btnLoadModel
        // 
        btnLoadModel.Dock = DockStyle.Fill;
        btnLoadModel.FlatStyle = FlatStyle.Flat;
        btnLoadModel.Location = new Point(16, 74);
        btnLoadModel.Margin = new Padding(0, 0, 0, 6);
        btnLoadModel.MinimumSize = new Size(0, 38);
        btnLoadModel.Name = "btnLoadModel";
        btnLoadModel.Size = new Size(241, 38);
        btnLoadModel.TabIndex = 1;
        btnLoadModel.Text = "Load model…";
        toolTip1.SetToolTip(btnLoadModel, "Pick a bundled world or open an OBJ/Collada file");
        btnLoadModel.UseVisualStyleBackColor = false;
        // 
        // lblCurrentModel
        // 
        lblCurrentModel.AutoSize = true;
        lblCurrentModel.Location = new Point(18, 118);
        lblCurrentModel.Margin = new Padding(2, 0, 0, 6);
        lblCurrentModel.Name = "lblCurrentModel";
        lblCurrentModel.Size = new Size(34, 17);
        lblCurrentModel.TabIndex = 2;
        lblCurrentModel.Text = "Skull";
        // 
        // lblDisplayHeader
        // 
        lblDisplayHeader.AutoSize = true;
        lblDisplayHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblDisplayHeader.Location = new Point(18, 151);
        lblDisplayHeader.Margin = new Padding(2, 10, 0, 6);
        lblDisplayHeader.Name = "lblDisplayHeader";
        lblDisplayHeader.Size = new Size(51, 13);
        lblDisplayHeader.TabIndex = 3;
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
        flpDisplay.Controls.Add(chkShowSkeleton);
        flpDisplay.Controls.Add(chkAnimate);
        flpDisplay.Controls.Add(chkFog);
        flpDisplay.Controls.Add(chkShadows);
        flpDisplay.Controls.Add(chkSky);
        flpDisplay.Controls.Add(chkHdrSky);
        flpDisplay.Controls.Add(chkPanorama);
        flpDisplay.Controls.Add(btnPanorama);
        flpDisplay.Controls.Add(chkBakedLight);
        flpDisplay.Controls.Add(btnBake);
        flpDisplay.Controls.Add(chkGammaCorrect);
        flpDisplay.Controls.Add(chkHighDynamicRange);
        flpDisplay.Controls.Add(chkTextureFiltering);
        flpDisplay.Controls.Add(chkTrilinear);
        flpDisplay.Controls.Add(chkSuperSampling);
        flpDisplay.Controls.Add(chkTemporalAntiAliasing);
        flpDisplay.Controls.Add(chkMotionBlur);
        flpDisplay.FlowDirection = FlowDirection.TopDown;
        flpDisplay.Location = new Point(16, 170);
        flpDisplay.Margin = new Padding(0);
        flpDisplay.Name = "flpDisplay";
        flpDisplay.Size = new Size(178, 465);
        flpDisplay.TabIndex = 4;
        flpDisplay.WrapContents = false;
        // 
        // chkShowTriangles
        // 
        chkShowTriangles.AutoSize = true;
        chkShowTriangles.Location = new Point(2, 2);
        chkShowTriangles.Margin = new Padding(2, 2, 0, 2);
        chkShowTriangles.Name = "chkShowTriangles";
        chkShowTriangles.Size = new Size(79, 21);
        chkShowTriangles.TabIndex = 0;
        chkShowTriangles.Text = "Triangles";
        chkShowTriangles.UseVisualStyleBackColor = true;
        // 
        // chkShowBackFacesCulling
        // 
        chkShowBackFacesCulling.AutoSize = true;
        chkShowBackFacesCulling.Location = new Point(2, 27);
        chkShowBackFacesCulling.Margin = new Padding(2, 2, 0, 2);
        chkShowBackFacesCulling.Name = "chkShowBackFacesCulling";
        chkShowBackFacesCulling.Size = new Size(128, 21);
        chkShowBackFacesCulling.TabIndex = 1;
        chkShowBackFacesCulling.Text = "Back faces culling";
        chkShowBackFacesCulling.UseVisualStyleBackColor = true;
        // 
        // chkShowXZGrid
        // 
        chkShowXZGrid.AutoSize = true;
        chkShowXZGrid.Location = new Point(2, 52);
        chkShowXZGrid.Margin = new Padding(2, 2, 0, 2);
        chkShowXZGrid.Name = "chkShowXZGrid";
        chkShowXZGrid.Size = new Size(70, 21);
        chkShowXZGrid.TabIndex = 2;
        chkShowXZGrid.Text = "XZ grid";
        chkShowXZGrid.UseVisualStyleBackColor = true;
        // 
        // chkShowAxes
        // 
        chkShowAxes.AutoSize = true;
        chkShowAxes.Location = new Point(2, 77);
        chkShowAxes.Margin = new Padding(2, 2, 0, 2);
        chkShowAxes.Name = "chkShowAxes";
        chkShowAxes.Size = new Size(54, 21);
        chkShowAxes.TabIndex = 3;
        chkShowAxes.Text = "Axes";
        chkShowAxes.UseVisualStyleBackColor = true;
        // 
        // chkShowSkeleton
        // 
        chkShowSkeleton.AutoSize = true;
        chkShowSkeleton.Location = new Point(2, 102);
        chkShowSkeleton.Margin = new Padding(2, 2, 0, 2);
        chkShowSkeleton.Name = "chkShowSkeleton";
        chkShowSkeleton.Size = new Size(76, 21);
        chkShowSkeleton.TabIndex = 4;
        chkShowSkeleton.Text = "Skeleton";
        toolTip1.SetToolTip(chkShowSkeleton, "Draw the scene's node hierarchy as bones over the frame");
        chkShowSkeleton.UseVisualStyleBackColor = true;
        // 
        // chkAnimate
        // 
        chkAnimate.AutoSize = true;
        chkAnimate.Checked = true;
        chkAnimate.CheckState = CheckState.Checked;
        chkAnimate.Location = new Point(2, 127);
        chkAnimate.Margin = new Padding(2, 2, 0, 2);
        chkAnimate.Name = "chkAnimate";
        chkAnimate.Size = new Size(74, 21);
        chkAnimate.TabIndex = 5;
        chkAnimate.Text = "Animate";
        toolTip1.SetToolTip(chkAnimate, "Play the loaded world's animation; unchecking holds the current pose");
        chkAnimate.UseVisualStyleBackColor = true;
        // 
        // chkFog
        // 
        chkFog.AutoSize = true;
        chkFog.Location = new Point(2, 152);
        chkFog.Margin = new Padding(2, 2, 0, 2);
        chkFog.Name = "chkFog";
        chkFog.Size = new Size(49, 21);
        chkFog.TabIndex = 6;
        chkFog.Text = "Fog";
        toolTip1.SetToolTip(chkFog, "Fade distant geometry into the background");
        chkFog.UseVisualStyleBackColor = true;
        // 
        // chkShadows
        // 
        chkShadows.AutoSize = true;
        chkShadows.Location = new Point(2, 177);
        chkShadows.Margin = new Padding(2, 2, 0, 2);
        chkShadows.Name = "chkShadows";
        chkShadows.Size = new Size(79, 21);
        chkShadows.TabIndex = 7;
        chkShadows.Text = "Shadows";
        toolTip1.SetToolTip(chkShadows, "Shadow-map the world from the first light (lit shading modes)");
        chkShadows.UseVisualStyleBackColor = true;
        // 
        // chkSky
        // 
        chkSky.AutoSize = true;
        chkSky.Location = new Point(2, 202);
        chkSky.Margin = new Padding(2, 2, 0, 2);
        chkSky.Name = "chkSky";
        chkSky.Size = new Size(46, 21);
        chkSky.TabIndex = 8;
        chkSky.Text = "Sky";
        toolTip1.SetToolTip(chkSky, "Draw a procedural sky behind the scene, and take the ambient light from it");
        chkSky.UseVisualStyleBackColor = true;
        // 
        // chkHdrSky
        // 
        chkHdrSky.AutoSize = true;
        chkHdrSky.Location = new Point(18, 227);
        chkHdrSky.Margin = new Padding(18, 2, 0, 2);
        chkHdrSky.Name = "chkHdrSky";
        chkHdrSky.Size = new Size(77, 21);
        chkHdrSky.TabIndex = 9;
        chkHdrSky.Text = "HDR sun";
        toolTip1.SetToolTip(chkHdrSky, "Build the procedural sky in linear light, with a sun hundreds of times brighter than white");
        chkHdrSky.UseVisualStyleBackColor = true;
        // 
        // chkPanorama
        // 
        chkPanorama.AutoSize = true;
        chkPanorama.Enabled = false;
        chkPanorama.Location = new Point(18, 252);
        chkPanorama.Margin = new Padding(18, 2, 0, 2);
        chkPanorama.Name = "chkPanorama";
        chkPanorama.Size = new Size(109, 21);
        chkPanorama.TabIndex = 10;
        chkPanorama.Text = "No panorama";
        toolTip1.SetToolTip(chkPanorama, "Use the loaded panorama instead of the procedural sky");
        chkPanorama.UseVisualStyleBackColor = true;
        // 
        // btnPanorama
        // 
        btnPanorama.AutoSize = true;
        btnPanorama.FlatStyle = FlatStyle.Flat;
        btnPanorama.Location = new Point(18, 277);
        btnPanorama.Margin = new Padding(18, 2, 0, 6);
        btnPanorama.MinimumSize = new Size(0, 32);
        btnPanorama.Name = "btnPanorama";
        btnPanorama.Size = new Size(160, 32);
        btnPanorama.TabIndex = 11;
        btnPanorama.Text = "Load panorama…";
        toolTip1.SetToolTip(btnPanorama, "Open a Radiance .hdr or an image to surround and light the scene with");
        btnPanorama.UseVisualStyleBackColor = false;
        //
        // chkBakedLight
        //
        chkBakedLight.AutoSize = true;
        chkBakedLight.Enabled = false;
        chkBakedLight.Margin = new Padding(18, 2, 0, 2);
        chkBakedLight.Name = "chkBakedLight";
        chkBakedLight.Size = new Size(109, 21);
        chkBakedLight.TabIndex = 11;
        chkBakedLight.Text = "No baked light";
        toolTip1.SetToolTip(chkBakedLight, "Light the scene with the baked probes instead of the environment's ambient");
        chkBakedLight.UseVisualStyleBackColor = true;
        //
        // btnBake
        //
        btnBake.AutoSize = true;
        btnBake.FlatStyle = FlatStyle.Flat;
        btnBake.Margin = new Padding(18, 2, 0, 6);
        btnBake.MinimumSize = new Size(0, 32);
        btnBake.Name = "btnBake";
        btnBake.Size = new Size(160, 32);
        btnBake.TabIndex = 11;
        btnBake.Text = "Bake indirect light";
        toolTip1.SetToolTip(btnBake, "Trace the scene's bounce light into a grid of probes (software rasterizer only)");
        btnBake.UseVisualStyleBackColor = false;
        //
        // chkGammaCorrect
        // 
        chkGammaCorrect.AutoSize = true;
        chkGammaCorrect.Location = new Point(2, 317);
        chkGammaCorrect.Margin = new Padding(2, 2, 0, 2);
        chkGammaCorrect.Name = "chkGammaCorrect";
        chkGammaCorrect.Size = new Size(147, 21);
        chkGammaCorrect.TabIndex = 12;
        chkGammaCorrect.Text = "Gamma-correct light";
        toolTip1.SetToolTip(chkGammaCorrect, "Shade in linear light and encode to sRGB on output");
        chkGammaCorrect.UseVisualStyleBackColor = true;
        // 
        // chkHighDynamicRange
        // 
        chkHighDynamicRange.AutoSize = true;
        chkHighDynamicRange.Location = new Point(2, 342);
        chkHighDynamicRange.Margin = new Padding(2, 2, 0, 2);
        chkHighDynamicRange.Name = "chkHighDynamicRange";
        chkHighDynamicRange.Size = new Size(92, 21);
        chkHighDynamicRange.TabIndex = 13;
        chkHighDynamicRange.Text = "HDR target";
        toolTip1.SetToolTip(chkHighDynamicRange, "Keep highlights brighter than white in a linear float buffer, for bloom and tone mapping to work with");
        chkHighDynamicRange.UseVisualStyleBackColor = true;
        // 
        // chkTextureFiltering
        // 
        chkTextureFiltering.AutoSize = true;
        chkTextureFiltering.Location = new Point(2, 367);
        chkTextureFiltering.Margin = new Padding(2, 2, 0, 2);
        chkTextureFiltering.Name = "chkTextureFiltering";
        chkTextureFiltering.Size = new Size(117, 21);
        chkTextureFiltering.TabIndex = 14;
        chkTextureFiltering.Text = "Texture filtering";
        toolTip1.SetToolTip(chkTextureFiltering, "Bilinear filtering with mip-mapping (Textured shading)");
        chkTextureFiltering.UseVisualStyleBackColor = true;
        //
        // chkTrilinear
        //
        chkTrilinear.AutoSize = true;
        chkTrilinear.Location = new Point(2, 392);
        chkTrilinear.Margin = new Padding(14, 2, 0, 2);
        chkTrilinear.Name = "chkTrilinear";
        chkTrilinear.Size = new Size(117, 21);
        chkTrilinear.TabIndex = 15;
        chkTrilinear.Text = "Trilinear";
        toolTip1.SetToolTip(chkTrilinear, "Blend the two mip levels a surface falls between, instead of stepping from one to the next");
        chkTrilinear.UseVisualStyleBackColor = true;
        //
        // chkSuperSampling
        // 
        chkSuperSampling.AutoSize = true;
        chkSuperSampling.Location = new Point(2, 392);
        chkSuperSampling.Margin = new Padding(2, 2, 0, 2);
        chkSuperSampling.Name = "chkSuperSampling";
        chkSuperSampling.Size = new Size(123, 21);
        chkSuperSampling.TabIndex = 16;
        chkSuperSampling.Text = "Supersample 2×";
        toolTip1.SetToolTip(chkSuperSampling, "Render at twice the resolution and average down — anti-aliases everything, fills four times the pixels");
        chkSuperSampling.UseVisualStyleBackColor = true;
        // 
        // chkTemporalAntiAliasing
        // 
        chkTemporalAntiAliasing.AutoSize = true;
        chkTemporalAntiAliasing.Location = new Point(2, 417);
        chkTemporalAntiAliasing.Margin = new Padding(2, 2, 0, 2);
        chkTemporalAntiAliasing.Name = "chkTemporalAntiAliasing";
        chkTemporalAntiAliasing.Size = new Size(102, 21);
        chkTemporalAntiAliasing.TabIndex = 17;
        chkTemporalAntiAliasing.Text = "Temporal AA";
        toolTip1.SetToolTip(chkTemporalAntiAliasing, "Jitter the frame by a fraction of a pixel and average it with the previous ones — supersampling spread over time. Costs a velocity pass.");
        chkTemporalAntiAliasing.UseVisualStyleBackColor = true;
        // 
        // chkMotionBlur
        // 
        chkMotionBlur.AutoSize = true;
        chkMotionBlur.Location = new Point(2, 442);
        chkMotionBlur.Margin = new Padding(2, 2, 0, 2);
        chkMotionBlur.Name = "chkMotionBlur";
        chkMotionBlur.Size = new Size(96, 21);
        chkMotionBlur.TabIndex = 18;
        chkMotionBlur.Text = "Motion blur";
        toolTip1.SetToolTip(chkMotionBlur, "Smear each pixel along the direction its surface is travelling. Costs a velocity pass.");
        chkMotionBlur.UseVisualStyleBackColor = true;
        // 
        // lblShadingHeader
        // 
        lblShadingHeader.AutoSize = true;
        lblShadingHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblShadingHeader.Location = new Point(18, 645);
        lblShadingHeader.Margin = new Padding(2, 10, 0, 6);
        lblShadingHeader.Name = "lblShadingHeader";
        lblShadingHeader.Size = new Size(57, 13);
        lblShadingHeader.TabIndex = 5;
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
        flpShading.Controls.Add(rdbMaterialShading);
        flpShading.Controls.Add(rdbPbrShading);
        flpShading.FlowDirection = FlowDirection.TopDown;
        flpShading.Location = new Point(16, 664);
        flpShading.Margin = new Padding(0);
        flpShading.Name = "flpShading";
        flpShading.Size = new Size(122, 200);
        flpShading.TabIndex = 6;
        flpShading.WrapContents = false;
        // 
        // rdbNoneShading
        // 
        rdbNoneShading.AutoSize = true;
        rdbNoneShading.Location = new Point(2, 2);
        rdbNoneShading.Margin = new Padding(2, 2, 0, 2);
        rdbNoneShading.Name = "rdbNoneShading";
        rdbNoneShading.Size = new Size(58, 21);
        rdbNoneShading.TabIndex = 0;
        rdbNoneShading.TabStop = true;
        rdbNoneShading.Text = "None";
        rdbNoneShading.UseVisualStyleBackColor = true;
        // 
        // rdbClassicShading
        // 
        rdbClassicShading.AutoSize = true;
        rdbClassicShading.Location = new Point(2, 27);
        rdbClassicShading.Margin = new Padding(2, 2, 0, 2);
        rdbClassicShading.Name = "rdbClassicShading";
        rdbClassicShading.Size = new Size(65, 21);
        rdbClassicShading.TabIndex = 1;
        rdbClassicShading.TabStop = true;
        rdbClassicShading.Text = "Classic";
        rdbClassicShading.UseVisualStyleBackColor = true;
        // 
        // rdbFlatShading
        // 
        rdbFlatShading.AutoSize = true;
        rdbFlatShading.Location = new Point(2, 52);
        rdbFlatShading.Margin = new Padding(2, 2, 0, 2);
        rdbFlatShading.Name = "rdbFlatShading";
        rdbFlatShading.Size = new Size(46, 21);
        rdbFlatShading.TabIndex = 2;
        rdbFlatShading.TabStop = true;
        rdbFlatShading.Text = "Flat";
        rdbFlatShading.UseVisualStyleBackColor = true;
        // 
        // rdbGouraudShading
        // 
        rdbGouraudShading.AutoSize = true;
        rdbGouraudShading.Location = new Point(2, 77);
        rdbGouraudShading.Margin = new Padding(2, 2, 0, 2);
        rdbGouraudShading.Name = "rdbGouraudShading";
        rdbGouraudShading.Size = new Size(77, 21);
        rdbGouraudShading.TabIndex = 3;
        rdbGouraudShading.TabStop = true;
        rdbGouraudShading.Text = "Gouraud";
        rdbGouraudShading.UseVisualStyleBackColor = true;
        // 
        // rdbPhongShading
        // 
        rdbPhongShading.AutoSize = true;
        rdbPhongShading.Location = new Point(2, 102);
        rdbPhongShading.Margin = new Padding(2, 2, 0, 2);
        rdbPhongShading.Name = "rdbPhongShading";
        rdbPhongShading.Size = new Size(63, 21);
        rdbPhongShading.TabIndex = 4;
        rdbPhongShading.TabStop = true;
        rdbPhongShading.Text = "Phong";
        rdbPhongShading.UseVisualStyleBackColor = true;
        // 
        // rdbTexturedShading
        // 
        rdbTexturedShading.AutoSize = true;
        rdbTexturedShading.Location = new Point(2, 127);
        rdbTexturedShading.Margin = new Padding(2, 2, 0, 2);
        rdbTexturedShading.Name = "rdbTexturedShading";
        rdbTexturedShading.Size = new Size(76, 21);
        rdbTexturedShading.TabIndex = 5;
        rdbTexturedShading.TabStop = true;
        rdbTexturedShading.Text = "Textured";
        rdbTexturedShading.UseVisualStyleBackColor = true;
        // 
        // rdbMaterialShading
        // 
        rdbMaterialShading.AutoSize = true;
        rdbMaterialShading.Location = new Point(2, 152);
        rdbMaterialShading.Margin = new Padding(2, 2, 0, 2);
        rdbMaterialShading.Name = "rdbMaterialShading";
        rdbMaterialShading.Size = new Size(74, 21);
        rdbMaterialShading.TabIndex = 6;
        rdbMaterialShading.TabStop = true;
        rdbMaterialShading.Text = "Material";
        toolTip1.SetToolTip(rdbMaterialShading, "Per-pixel albedo, normal and specular maps");
        rdbMaterialShading.UseVisualStyleBackColor = true;
        // 
        // rdbPbrShading
        // 
        rdbPbrShading.AutoSize = true;
        rdbPbrShading.Location = new Point(2, 177);
        rdbPbrShading.Margin = new Padding(2, 2, 0, 2);
        rdbPbrShading.Name = "rdbPbrShading";
        rdbPbrShading.Size = new Size(120, 21);
        rdbPbrShading.TabIndex = 7;
        rdbPbrShading.TabStop = true;
        rdbPbrShading.Text = "Physically based";
        toolTip1.SetToolTip(rdbPbrShading, "Metallic-roughness materials lit by the scene and by the environment");
        rdbPbrShading.UseVisualStyleBackColor = true;
        // 
        // lblPostHeader
        // 
        lblPostHeader.AutoSize = true;
        lblPostHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblPostHeader.Location = new Point(18, 874);
        lblPostHeader.Margin = new Padding(2, 10, 0, 6);
        lblPostHeader.Name = "lblPostHeader";
        lblPostHeader.Size = new Size(105, 13);
        lblPostHeader.TabIndex = 7;
        lblPostHeader.Text = "POST-PROCESSING";
        // 
        // flpPost
        // 
        flpPost.AutoSize = true;
        flpPost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flpPost.Controls.Add(chkSsao);
        flpPost.Controls.Add(chkBloom);
        flpPost.Controls.Add(chkToneMap);
        flpPost.Controls.Add(chkFxaa);
        flpPost.Controls.Add(chkVignette);
        flpPost.FlowDirection = FlowDirection.TopDown;
        flpPost.Location = new Point(16, 893);
        flpPost.Margin = new Padding(0);
        flpPost.Name = "flpPost";
        flpPost.Size = new Size(135, 125);
        flpPost.TabIndex = 8;
        flpPost.WrapContents = false;
        // 
        // chkSsao
        // 
        chkSsao.AutoSize = true;
        chkSsao.Location = new Point(2, 2);
        chkSsao.Margin = new Padding(2, 2, 0, 2);
        chkSsao.Name = "chkSsao";
        chkSsao.Size = new Size(133, 21);
        chkSsao.TabIndex = 0;
        chkSsao.Text = "Ambient occlusion";
        toolTip1.SetToolTip(chkSsao, "Darken creases and contact points, from the depth buffer");
        chkSsao.UseVisualStyleBackColor = true;
        // 
        // chkBloom
        // 
        chkBloom.AutoSize = true;
        chkBloom.Location = new Point(2, 27);
        chkBloom.Margin = new Padding(2, 2, 0, 2);
        chkBloom.Name = "chkBloom";
        chkBloom.Size = new Size(64, 21);
        chkBloom.TabIndex = 1;
        chkBloom.Text = "Bloom";
        toolTip1.SetToolTip(chkBloom, "Bleed light out of the brightest parts of the image");
        chkBloom.UseVisualStyleBackColor = true;
        // 
        // chkToneMap
        // 
        chkToneMap.AutoSize = true;
        chkToneMap.Location = new Point(2, 52);
        chkToneMap.Margin = new Padding(2, 2, 0, 2);
        chkToneMap.Name = "chkToneMap";
        chkToneMap.Size = new Size(85, 21);
        chkToneMap.TabIndex = 2;
        chkToneMap.Text = "Tone map";
        toolTip1.SetToolTip(chkToneMap, "Exposure and an ACES filmic curve instead of a hard clip");
        chkToneMap.UseVisualStyleBackColor = true;
        // 
        // chkFxaa
        // 
        chkFxaa.AutoSize = true;
        chkFxaa.Location = new Point(2, 77);
        chkFxaa.Margin = new Padding(2, 2, 0, 2);
        chkFxaa.Name = "chkFxaa";
        chkFxaa.Size = new Size(57, 21);
        chkFxaa.TabIndex = 3;
        chkFxaa.Text = "FXAA";
        toolTip1.SetToolTip(chkFxaa, "Smooth stair-stepped edges after rasterization");
        chkFxaa.UseVisualStyleBackColor = true;
        // 
        // chkVignette
        // 
        chkVignette.AutoSize = true;
        chkVignette.Location = new Point(2, 102);
        chkVignette.Margin = new Padding(2, 2, 0, 2);
        chkVignette.Name = "chkVignette";
        chkVignette.Size = new Size(75, 21);
        chkVignette.TabIndex = 4;
        chkVignette.Text = "Vignette";
        toolTip1.SetToolTip(chkVignette, "Darken the frame toward its corners");
        chkVignette.UseVisualStyleBackColor = true;
        // 
        // lblBufferHeader
        // 
        lblBufferHeader.AutoSize = true;
        lblBufferHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblBufferHeader.Location = new Point(18, 1028);
        lblBufferHeader.Margin = new Padding(2, 10, 0, 6);
        lblBufferHeader.Name = "lblBufferHeader";
        lblBufferHeader.Size = new Size(77, 13);
        lblBufferHeader.TabIndex = 9;
        lblBufferHeader.Text = "BUFFER VIEW";
        // 
        // cboBufferView
        // 
        cboBufferView.Dock = DockStyle.Fill;
        cboBufferView.DropDownStyle = ComboBoxStyle.DropDownList;
        cboBufferView.FlatStyle = FlatStyle.Flat;
        cboBufferView.Location = new Point(16, 1047);
        cboBufferView.Margin = new Padding(0, 0, 0, 6);
        cboBufferView.Name = "cboBufferView";
        cboBufferView.Size = new Size(241, 25);
        cboBufferView.TabIndex = 3;
        toolTip1.SetToolTip(cboBufferView, "Present one of the frame's own buffers instead of the shaded image");
        // 
        // lblCascadeHeader
        // 
        lblCascadeHeader.AutoSize = true;
        lblCascadeHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblCascadeHeader.Location = new Point(18, 1086);
        lblCascadeHeader.Margin = new Padding(2, 10, 0, 6);
        lblCascadeHeader.Name = "lblCascadeHeader";
        lblCascadeHeader.Size = new Size(115, 13);
        lblCascadeHeader.TabIndex = 10;
        lblCascadeHeader.Text = "SHADOW CASCADES";
        // 
        // cboCascades
        // 
        cboCascades.Dock = DockStyle.Fill;
        cboCascades.DropDownStyle = ComboBoxStyle.DropDownList;
        cboCascades.FlatStyle = FlatStyle.Flat;
        cboCascades.Location = new Point(16, 1105);
        cboCascades.Margin = new Padding(0, 0, 0, 6);
        cboCascades.Name = "cboCascades";
        cboCascades.Size = new Size(241, 25);
        cboCascades.TabIndex = 4;
        toolTip1.SetToolTip(cboCascades, "Split the shadow pass across several depth buffers, each fitted to a slice of the view");
        // 
        // lblGizmoHeader
        // 
        lblGizmoHeader.AutoSize = true;
        lblGizmoHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblGizmoHeader.Location = new Point(18, 1144);
        lblGizmoHeader.Margin = new Padding(2, 10, 0, 6);
        lblGizmoHeader.Name = "lblGizmoHeader";
        lblGizmoHeader.Size = new Size(115, 13);
        lblGizmoHeader.TabIndex = 11;
        lblGizmoHeader.Text = "TRANSFORM GIZMO";
        // 
        // cboGizmo
        // 
        cboGizmo.Dock = DockStyle.Fill;
        cboGizmo.DropDownStyle = ComboBoxStyle.DropDownList;
        cboGizmo.FlatStyle = FlatStyle.Flat;
        cboGizmo.Location = new Point(16, 1163);
        cboGizmo.Margin = new Padding(0, 0, 0, 6);
        cboGizmo.Name = "cboGizmo";
        cboGizmo.Size = new Size(241, 25);
        cboGizmo.TabIndex = 5;
        toolTip1.SetToolTip(cboGizmo, "Drag the handles on the picked mesh to move, turn or stretch it");
        // 
        // chkSnap
        // 
        chkSnap.AutoSize = true;
        chkSnap.Location = new Point(18, 1192);
        chkSnap.Margin = new Padding(2, 0, 0, 8);
        chkSnap.Name = "chkSnap";
        chkSnap.Size = new Size(100, 21);
        chkSnap.TabIndex = 12;
        chkSnap.Text = "Snap to grid";
        chkSnap.UseVisualStyleBackColor = true;
        // 
        // pnlViewport
        // 
        pnlViewport.Controls.Add(panel3D1);
        pnlViewport.Dock = DockStyle.Fill;
        pnlViewport.Location = new Point(0, 0);
        pnlViewport.Name = "pnlViewport";
        pnlViewport.Padding = new Padding(10);
        pnlViewport.Size = new Size(864, 502);
        pnlViewport.TabIndex = 1;
        // 
        // panel3D1
        // 
        panel3D1.BackgroundImageLayout = ImageLayout.None;
        panel3D1.Dock = DockStyle.Fill;
        panel3D1.Location = new Point(10, 10);
        panel3D1.Margin = new Padding(0);
        panel3D1.Name = "panel3D1";
        panel3D1.Size = new Size(844, 482);
        panel3D1.TabIndex = 0;
        // 
        // menuStrip
        // 
        menuStrip.ImageScalingSize = new Size(24, 24);
        menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuEdit, mnuView });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Size = new Size(1546, 24);
        menuStrip.TabIndex = 10;
        // 
        // mnuFile
        // 
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuLoadModel, mnuOpenModel, mnuOpenScene, mnuSaveScene, mnuScreenshot, mnuExit });
        mnuFile.Name = "mnuFile";
        mnuFile.Size = new Size(37, 20);
        mnuFile.Text = "&File";
        // 
        // mnuLoadModel
        // 
        mnuLoadModel.Name = "mnuLoadModel";
        mnuLoadModel.ShortcutKeys = Keys.Control | Keys.M;
        mnuLoadModel.Size = new Size(211, 22);
        mnuLoadModel.Text = "&Load model…";
        // 
        // mnuOpenModel
        // 
        mnuOpenModel.Name = "mnuOpenModel";
        mnuOpenModel.ShortcutKeys = Keys.Control | Keys.O;
        mnuOpenModel.Size = new Size(211, 22);
        mnuOpenModel.Text = "&Open model file…";
        // 
        // mnuOpenScene
        // 
        mnuOpenScene.Name = "mnuOpenScene";
        mnuOpenScene.Size = new Size(211, 22);
        mnuOpenScene.Text = "Open sc&ene…";
        // 
        // mnuSaveScene
        // 
        mnuSaveScene.Name = "mnuSaveScene";
        mnuSaveScene.ShortcutKeys = Keys.Control | Keys.S;
        mnuSaveScene.Size = new Size(211, 22);
        mnuSaveScene.Text = "Sa&ve scene as…";
        // 
        // mnuScreenshot
        // 
        mnuScreenshot.Name = "mnuScreenshot";
        mnuScreenshot.ShortcutKeys = Keys.F12;
        mnuScreenshot.Size = new Size(211, 22);
        mnuScreenshot.Text = "Save &screenshot…";
        // 
        // mnuExit
        // 
        mnuExit.Name = "mnuExit";
        mnuExit.Size = new Size(211, 22);
        mnuExit.Text = "E&xit";
        // 
        // mnuEdit
        // 
        mnuEdit.DropDownItems.AddRange(new ToolStripItem[] { mnuUndo, mnuRedo, mnuSnap });
        mnuEdit.Name = "mnuEdit";
        mnuEdit.Size = new Size(39, 20);
        mnuEdit.Text = "&Edit";
        // 
        // mnuUndo
        // 
        mnuUndo.Name = "mnuUndo";
        mnuUndo.ShortcutKeys = Keys.Control | Keys.Z;
        mnuUndo.Size = new Size(257, 22);
        mnuUndo.Text = "&Undo";
        // 
        // mnuRedo
        // 
        mnuRedo.Name = "mnuRedo";
        mnuRedo.ShortcutKeys = Keys.Control | Keys.Y;
        mnuRedo.Size = new Size(257, 22);
        mnuRedo.Text = "&Redo";
        // 
        // mnuSnap
        // 
        mnuSnap.CheckOnClick = true;
        mnuSnap.Name = "mnuSnap";
        mnuSnap.ShortcutKeys = Keys.Control | Keys.G;
        mnuSnap.Size = new Size(257, 22);
        mnuSnap.Text = "&Snap gizmo drags to a grid";
        // 
        // mnuView
        // 
        mnuView.DropDownItems.AddRange(new ToolStripItem[] { mnuRenderedBy, mnuPixelHistory, mnuObjectTable, mnuEventList, mnuStatsOverlay, mnuRecordEvents, mnuFrameHistory, mnuAxisViews, mnuZoomIn, mnuZoomOut, mnuZoomActual, mnuClearPixel });
        mnuView.Name = "mnuView";
        mnuView.Size = new Size(44, 20);
        mnuView.Text = "&View";
        // 
        // mnuRenderedBy
        // 
        mnuRenderedBy.DropDownItems.AddRange(new ToolStripItem[] { mnuRenderCpu, mnuRenderGpu, mnuRenderTrace });
        mnuRenderedBy.Name = "mnuRenderedBy";
        mnuRenderedBy.Size = new Size(222, 22);
        mnuRenderedBy.Text = "Rendered &by";
        // 
        // mnuRenderCpu
        // 
        mnuRenderCpu.Checked = true;
        mnuRenderCpu.CheckState = CheckState.Checked;
        mnuRenderCpu.Name = "mnuRenderCpu";
        mnuRenderCpu.Size = new Size(210, 22);
        mnuRenderCpu.Text = "&CPU — software rasterizer";
        mnuRenderCpu.ToolTipText = "Rasterize every triangle on the CPU, as this engine was written to.";
        // 
        // mnuRenderGpu
        // 
        mnuRenderGpu.Name = "mnuRenderGpu";
        mnuRenderGpu.Size = new Size(210, 22);
        mnuRenderGpu.Text = "&GPU — graphics adapter";
        mnuRenderGpu.ToolTipText = "Fill the frame on the graphics card through OpenGL.";
        // 
        // mnuRenderTrace
        // 
        mnuRenderTrace.Name = "mnuRenderTrace";
        mnuRenderTrace.Size = new Size(210, 22);
        mnuRenderTrace.Text = "&Path tracer — reference";
        mnuRenderTrace.ToolTipText = "Trace light through the scene instead of filling triangles: real interreflection and occlusion, refined for as long as nothing moves.";
        // 
        // mnuPixelHistory
        // 
        mnuPixelHistory.Checked = true;
        mnuPixelHistory.CheckOnClick = true;
        mnuPixelHistory.CheckState = CheckState.Checked;
        mnuPixelHistory.Name = "mnuPixelHistory";
        mnuPixelHistory.Size = new Size(222, 22);
        mnuPixelHistory.Text = "&Pixel History";
        // 
        // mnuObjectTable
        // 
        mnuObjectTable.Checked = true;
        mnuObjectTable.CheckOnClick = true;
        mnuObjectTable.CheckState = CheckState.Checked;
        mnuObjectTable.Name = "mnuObjectTable";
        mnuObjectTable.Size = new Size(222, 22);
        mnuObjectTable.Text = "Graphics &Object Table";
        // 
        // mnuEventList
        // 
        mnuEventList.Checked = true;
        mnuEventList.CheckOnClick = true;
        mnuEventList.CheckState = CheckState.Checked;
        mnuEventList.Name = "mnuEventList";
        mnuEventList.Size = new Size(222, 22);
        mnuEventList.Text = "Graphics &Event List";
        // 
        // mnuStatsOverlay
        // 
        mnuStatsOverlay.Checked = true;
        mnuStatsOverlay.CheckOnClick = true;
        mnuStatsOverlay.CheckState = CheckState.Checked;
        mnuStatsOverlay.Name = "mnuStatsOverlay";
        mnuStatsOverlay.Size = new Size(222, 22);
        mnuStatsOverlay.Text = "&Stats overlay";
        // 
        // mnuRecordEvents
        // 
        mnuRecordEvents.Checked = true;
        mnuRecordEvents.CheckOnClick = true;
        mnuRecordEvents.CheckState = CheckState.Checked;
        mnuRecordEvents.Name = "mnuRecordEvents";
        mnuRecordEvents.Size = new Size(222, 22);
        mnuRecordEvents.Text = "&Record graphics events";
        // 
        // mnuFrameHistory
        // 
        mnuFrameHistory.DropDownItems.AddRange(new ToolStripItem[] { mnuKeepFrames, mnuPreviousFrame, mnuNextFrame, mnuLatestFrame });
        mnuFrameHistory.Name = "mnuFrameHistory";
        mnuFrameHistory.Size = new Size(222, 22);
        mnuFrameHistory.Text = "&Frame history";
        // 
        // mnuKeepFrames
        // 
        mnuKeepFrames.CheckOnClick = true;
        mnuKeepFrames.Name = "mnuKeepFrames";
        mnuKeepFrames.Size = new Size(287, 22);
        mnuKeepFrames.Text = "&Keep recent frames";
        // 
        // mnuPreviousFrame
        // 
        mnuPreviousFrame.Name = "mnuPreviousFrame";
        mnuPreviousFrame.ShortcutKeys = Keys.Control | Keys.Left;
        mnuPreviousFrame.Size = new Size(287, 22);
        mnuPreviousFrame.Text = "&Previous frame";
        // 
        // mnuNextFrame
        // 
        mnuNextFrame.Name = "mnuNextFrame";
        mnuNextFrame.ShortcutKeys = Keys.Control | Keys.Right;
        mnuNextFrame.Size = new Size(287, 22);
        mnuNextFrame.Text = "&Next frame";
        // 
        // mnuLatestFrame
        // 
        mnuLatestFrame.Name = "mnuLatestFrame";
        mnuLatestFrame.ShortcutKeys = Keys.Control | Keys.End;
        mnuLatestFrame.Size = new Size(287, 22);
        mnuLatestFrame.Text = "&Live (follow the newest frame)";
        // 
        // mnuAxisViews
        // 
        mnuAxisViews.DropDownItems.AddRange(new ToolStripItem[] { mnuViewFront, mnuViewBack, mnuViewRight, mnuViewLeft, mnuViewTop, mnuViewBottom, mnuViewOpposite, mnuTurnX, mnuTurnY, mnuTurnZ });
        mnuAxisViews.Name = "mnuAxisViews";
        mnuAxisViews.Size = new Size(222, 22);
        mnuAxisViews.Text = "&Axis view";
        // 
        // mnuViewFront
        // 
        mnuViewFront.Name = "mnuViewFront";
        mnuViewFront.ShortcutKeyDisplayString = "Numpad 1";
        mnuViewFront.Size = new Size(233, 22);
        mnuViewFront.Text = "&Front  (+Z)";
        // 
        // mnuViewBack
        // 
        mnuViewBack.Name = "mnuViewBack";
        mnuViewBack.ShortcutKeyDisplayString = "Ctrl+Numpad 1";
        mnuViewBack.Size = new Size(233, 22);
        mnuViewBack.Text = "&Back  (−Z)";
        // 
        // mnuViewRight
        // 
        mnuViewRight.Name = "mnuViewRight";
        mnuViewRight.ShortcutKeyDisplayString = "Numpad 3";
        mnuViewRight.Size = new Size(233, 22);
        mnuViewRight.Text = "&Right  (+X)";
        // 
        // mnuViewLeft
        // 
        mnuViewLeft.Name = "mnuViewLeft";
        mnuViewLeft.ShortcutKeyDisplayString = "Ctrl+Numpad 3";
        mnuViewLeft.Size = new Size(233, 22);
        mnuViewLeft.Text = "&Left  (−X)";
        // 
        // mnuViewTop
        // 
        mnuViewTop.Name = "mnuViewTop";
        mnuViewTop.ShortcutKeyDisplayString = "Numpad 7";
        mnuViewTop.Size = new Size(233, 22);
        mnuViewTop.Text = "&Top  (+Y)";
        // 
        // mnuViewBottom
        // 
        mnuViewBottom.Name = "mnuViewBottom";
        mnuViewBottom.ShortcutKeyDisplayString = "Ctrl+Numpad 7";
        mnuViewBottom.Size = new Size(233, 22);
        mnuViewBottom.Text = "B&ottom  (−Y)";
        // 
        // mnuViewOpposite
        // 
        mnuViewOpposite.Name = "mnuViewOpposite";
        mnuViewOpposite.ShortcutKeyDisplayString = "Numpad 9";
        mnuViewOpposite.Size = new Size(233, 22);
        mnuViewOpposite.Text = "&Opposite side";
        // 
        // mnuTurnX
        // 
        mnuTurnX.Name = "mnuTurnX";
        mnuTurnX.ShortcutKeyDisplayString = "X · Shift+X";
        mnuTurnX.Size = new Size(233, 22);
        mnuTurnX.Text = "Turn 15° about &X";
        // 
        // mnuTurnY
        // 
        mnuTurnY.Name = "mnuTurnY";
        mnuTurnY.ShortcutKeyDisplayString = "Y · Shift+Y";
        mnuTurnY.Size = new Size(233, 22);
        mnuTurnY.Text = "Turn 15° about &Y";
        // 
        // mnuTurnZ
        // 
        mnuTurnZ.Name = "mnuTurnZ";
        mnuTurnZ.ShortcutKeyDisplayString = "Z · Shift+Z";
        mnuTurnZ.Size = new Size(233, 22);
        mnuTurnZ.Text = "Turn 15° about &Z";
        // 
        // mnuZoomIn
        // 
        mnuZoomIn.Name = "mnuZoomIn";
        mnuZoomIn.ShortcutKeys = Keys.Control | Keys.Oemplus;
        mnuZoomIn.Size = new Size(222, 22);
        mnuZoomIn.Text = "Zoom &In";
        // 
        // mnuZoomOut
        // 
        mnuZoomOut.Name = "mnuZoomOut";
        mnuZoomOut.ShortcutKeys = Keys.Control | Keys.OemMinus;
        mnuZoomOut.Size = new Size(222, 22);
        mnuZoomOut.Text = "Zoom O&ut";
        // 
        // mnuZoomActual
        // 
        mnuZoomActual.Name = "mnuZoomActual";
        mnuZoomActual.ShortcutKeys = Keys.Control | Keys.D0;
        mnuZoomActual.Size = new Size(222, 22);
        mnuZoomActual.Text = "&Reset zoom (100%)";
        // 
        // mnuClearPixel
        // 
        mnuClearPixel.Name = "mnuClearPixel";
        mnuClearPixel.Size = new Size(222, 22);
        mnuClearPixel.Text = "&Clear pixel selection";
        // 
        // statusStrip
        // 
        statusStrip.ImageScalingSize = new Size(24, 24);
        statusStrip.Items.AddRange(new ToolStripItem[] { lblZoomStatus, lblPixelStatus, lblScreenshotHint, lblBackendStatus, lblCameraStatus, lblFrameStatus });
        statusStrip.Location = new Point(0, 502);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(864, 32);
        statusStrip.SizingGrip = false;
        statusStrip.TabIndex = 11;
        // 
        // lblZoomStatus
        // 
        lblZoomStatus.AutoSize = false;
        lblZoomStatus.Name = "lblZoomStatus";
        lblZoomStatus.Size = new Size(210, 27);
        lblZoomStatus.Text = "Zoom: 100%";
        lblZoomStatus.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // lblPixelStatus
        // 
        lblPixelStatus.AutoSize = false;
        lblPixelStatus.Name = "lblPixelStatus";
        lblPixelStatus.Size = new Size(1, 27);
        lblPixelStatus.Spring = true;
        lblPixelStatus.Text = "Selected pixel: none";
        lblPixelStatus.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // lblScreenshotHint
        // 
        lblScreenshotHint.Name = "lblScreenshotHint";
        lblScreenshotHint.Size = new Size(89, 27);
        lblScreenshotHint.Text = "F12: Screenshot";
        lblScreenshotHint.TextAlign = ContentAlignment.MiddleRight;
        lblScreenshotHint.ToolTipText = "Save the current view as a PNG (File → Save screenshot…)";
        // 
        // lblBackendStatus
        // 
        lblBackendStatus.AutoSize = false;
        lblBackendStatus.Name = "lblBackendStatus";
        lblBackendStatus.Size = new Size(230, 27);
        lblBackendStatus.Text = "CPU";
        lblBackendStatus.TextAlign = ContentAlignment.MiddleRight;
        lblBackendStatus.ToolTipText = "What the viewport is being rasterized by (View → Rendered by)";
        // 
        // lblCameraStatus
        // 
        lblCameraStatus.AutoSize = false;
        lblCameraStatus.Name = "lblCameraStatus";
        lblCameraStatus.Size = new Size(240, 27);
        lblCameraStatus.Text = "Camera:";
        lblCameraStatus.TextAlign = ContentAlignment.MiddleRight;
        // 
        // lblFrameStatus
        // 
        lblFrameStatus.AutoSize = false;
        lblFrameStatus.Name = "lblFrameStatus";
        lblFrameStatus.Size = new Size(190, 27);
        lblFrameStatus.Text = "Frame:";
        lblFrameStatus.TextAlign = ContentAlignment.MiddleRight;
        // 
        // splitMain
        // 
        splitMain.Dock = DockStyle.Fill;
        splitMain.FixedPanel = FixedPanel.Panel1;
        splitMain.Location = new Point(0, 24);
        splitMain.Name = "splitMain";
        // 
        // splitMain.Panel1
        // 
        splitMain.Panel1.Controls.Add(splitLeft);
        splitMain.Panel1MinSize = 180;
        // 
        // splitMain.Panel2
        // 
        splitMain.Panel2.Controls.Add(splitRight);
        splitMain.Panel2MinSize = 320;
        splitMain.Size = new Size(1546, 812);
        splitMain.SplitterDistance = 290;
        splitMain.SplitterWidth = 6;
        splitMain.TabIndex = 0;
        // 
        // splitLeft
        // 
        splitLeft.Dock = DockStyle.Fill;
        splitLeft.Location = new Point(0, 0);
        splitLeft.Name = "splitLeft";
        splitLeft.Orientation = Orientation.Horizontal;
        // 
        // splitLeft.Panel1
        // 
        splitLeft.Panel1.Controls.Add(pnlSidebar);
        splitLeft.Panel1MinSize = 160;
        // 
        // splitLeft.Panel2
        // 
        splitLeft.Panel2.Controls.Add(pixelHistoryPanel);
        splitLeft.Panel2MinSize = 120;
        splitLeft.Size = new Size(290, 812);
        splitLeft.SplitterDistance = 469;
        splitLeft.SplitterWidth = 6;
        splitLeft.TabIndex = 0;
        // 
        // pixelHistoryPanel
        // 
        pixelHistoryPanel.BackColor = Color.FromArgb(37, 37, 43);
        pixelHistoryPanel.Dock = DockStyle.Fill;
        pixelHistoryPanel.Location = new Point(0, 0);
        pixelHistoryPanel.Name = "pixelHistoryPanel";
        pixelHistoryPanel.Size = new Size(290, 337);
        pixelHistoryPanel.TabIndex = 0;
        // 
        // splitRight
        // 
        splitRight.Dock = DockStyle.Fill;
        splitRight.FixedPanel = FixedPanel.Panel2;
        splitRight.Location = new Point(0, 0);
        splitRight.Name = "splitRight";
        // 
        // splitRight.Panel1
        // 
        splitRight.Panel1.Controls.Add(splitCenter);
        splitRight.Panel1MinSize = 240;
        // 
        // splitRight.Panel2
        // 
        splitRight.Panel2.Controls.Add(eventListPanel);
        splitRight.Panel2MinSize = 120;
        splitRight.Size = new Size(1250, 812);
        splitRight.SplitterDistance = 864;
        splitRight.SplitterWidth = 6;
        splitRight.TabIndex = 1;
        // 
        // splitCenter
        // 
        splitCenter.Dock = DockStyle.Fill;
        splitCenter.FixedPanel = FixedPanel.Panel2;
        splitCenter.Location = new Point(0, 0);
        splitCenter.Name = "splitCenter";
        splitCenter.Orientation = Orientation.Horizontal;
        // 
        // splitCenter.Panel1
        // 
        splitCenter.Panel1.Controls.Add(pnlViewport);
        splitCenter.Panel1.Controls.Add(statusStrip);
        splitCenter.Panel1MinSize = 160;
        // 
        // splitCenter.Panel2
        // 
        splitCenter.Panel2.Controls.Add(objectTablePanel);
        splitCenter.Panel2MinSize = 80;
        splitCenter.Size = new Size(864, 812);
        splitCenter.SplitterDistance = 534;
        splitCenter.SplitterWidth = 6;
        splitCenter.TabIndex = 0;
        // 
        // objectTablePanel
        // 
        objectTablePanel.BackColor = Color.FromArgb(37, 37, 43);
        objectTablePanel.Dock = DockStyle.Fill;
        objectTablePanel.Location = new Point(0, 0);
        objectTablePanel.Name = "objectTablePanel";
        objectTablePanel.Size = new Size(864, 272);
        objectTablePanel.TabIndex = 0;
        // 
        // eventListPanel
        // 
        eventListPanel.BackColor = Color.FromArgb(37, 37, 43);
        eventListPanel.Dock = DockStyle.Fill;
        eventListPanel.Location = new Point(0, 0);
        eventListPanel.Name = "eventListPanel";
        eventListPanel.Size = new Size(380, 812);
        eventListPanel.TabIndex = 0;
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
        pnlSidebar.ResumeLayout(false);
        pnlSidebar.PerformLayout();
        tlpSidebar.ResumeLayout(false);
        tlpSidebar.PerformLayout();
        flpDisplay.ResumeLayout(false);
        flpDisplay.PerformLayout();
        flpShading.ResumeLayout(false);
        flpShading.PerformLayout();
        flpPost.ResumeLayout(false);
        flpPost.PerformLayout();
        pnlViewport.ResumeLayout(false);
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        splitLeft.Panel1.ResumeLayout(false);
        splitLeft.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitLeft).EndInit();
        splitLeft.ResumeLayout(false);
        splitRight.Panel1.ResumeLayout(false);
        splitRight.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitRight).EndInit();
        splitRight.ResumeLayout(false);
        splitCenter.Panel1.ResumeLayout(false);
        splitCenter.Panel1.PerformLayout();
        splitCenter.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitCenter).EndInit();
        splitCenter.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Panel pnlSidebar;
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
    private CheckBox chkShowSkeleton;
    private CheckBox chkAnimate;
    private CheckBox chkFog;
    private CheckBox chkShadows;
    private CheckBox chkGammaCorrect;
    private CheckBox chkHighDynamicRange;
    private CheckBox chkSky;
    private CheckBox chkHdrSky;
    private CheckBox chkPanorama;
    private Button btnPanorama;
    private CheckBox chkBakedLight;
    private Button btnBake;
    private CheckBox chkTextureFiltering;
    private CheckBox chkTrilinear;
    private CheckBox chkSuperSampling;
    private CheckBox chkTemporalAntiAliasing;
    private CheckBox chkMotionBlur;
    private Label lblShadingHeader;
    private FlowLayoutPanel flpShading;
    private RadioButton rdbNoneShading;
    private RadioButton rdbClassicShading;
    private RadioButton rdbFlatShading;
    private RadioButton rdbGouraudShading;
    private RadioButton rdbPhongShading;
    private RadioButton rdbTexturedShading;
    private RadioButton rdbMaterialShading;
    private RadioButton rdbPbrShading;
    private Label lblBufferHeader;
    private ComboBox cboBufferView;
    private Label lblCascadeHeader;
    private ComboBox cboCascades;
    private Label lblGizmoHeader;
    private ComboBox cboGizmo;
    private CheckBox chkSnap;
    private Label lblPostHeader;
    private FlowLayoutPanel flpPost;
    private CheckBox chkBloom;
    private CheckBox chkToneMap;
    private CheckBox chkFxaa;
    private CheckBox chkVignette;
    private CheckBox chkSsao;
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
    private ToolStripMenuItem mnuRenderedBy;
    private ToolStripMenuItem mnuRenderCpu;
    private ToolStripMenuItem mnuRenderGpu;
    private ToolStripMenuItem mnuRenderTrace;
    private ToolStripMenuItem mnuFrameHistory;
    private ToolStripMenuItem mnuKeepFrames;
    private ToolStripMenuItem mnuPreviousFrame;
    private ToolStripMenuItem mnuNextFrame;
    private ToolStripMenuItem mnuLatestFrame;
    private ToolStripMenuItem mnuZoomIn;
    private ToolStripMenuItem mnuZoomOut;
    private ToolStripMenuItem mnuZoomActual;
    private ToolStripMenuItem mnuClearPixel;
    private ToolStripMenuItem mnuAxisViews;
    private ToolStripMenuItem mnuViewFront;
    private ToolStripMenuItem mnuViewBack;
    private ToolStripMenuItem mnuViewRight;
    private ToolStripMenuItem mnuViewLeft;
    private ToolStripMenuItem mnuViewTop;
    private ToolStripMenuItem mnuViewBottom;
    private ToolStripMenuItem mnuViewOpposite;
    private ToolStripMenuItem mnuTurnX;
    private ToolStripMenuItem mnuTurnY;
    private ToolStripMenuItem mnuTurnZ;
    private ToolStripMenuItem mnuOpenScene;
    private ToolStripMenuItem mnuSaveScene;
    private ToolStripMenuItem mnuEdit;
    private ToolStripMenuItem mnuUndo;
    private ToolStripMenuItem mnuRedo;
    private ToolStripMenuItem mnuSnap;

    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblZoomStatus;
    private ToolStripStatusLabel lblScreenshotHint;
    private ToolStripStatusLabel lblPixelStatus;
    private ToolStripStatusLabel lblBackendStatus;
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
