using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Debugging;

namespace SoftEngine.WinForms;

/// <summary>
/// The window's arrangement, built in code: a top bar of menus and workspace tabs; the 3D
/// viewport with its header and tool shelf; the outliner and the properties editor down the
/// right; the timeline along the bottom; and a status bar of hints and statistics. The debugger's
/// three panels fold in around the viewport when the Debugging workspace asks for them.
/// </summary>
public sealed partial class MainScreen
{
    private System.ComponentModel.IContainer components = null!;

    // Window frame.
    private Panel topBar = null!;
    private MenuStrip menuStrip = null!;
    private WorkspaceTabs workspaceTabs = null!;
    private Label lblCurrentModel = null!;
    private StatusStrip statusStrip = null!;
    private ToolTip toolTip1 = null!;

    // Areas, outermost first.
    private SplitContainer splitMain = null!;       // work area | outliner and properties
    private SplitContainer splitSidebar = null!;    // outliner | properties
    private SplitContainer splitTimeline = null!;   // everything above | timeline
    private SplitContainer splitLeft = null!;       // pixel history | the rest
    private SplitContainer splitRight = null!;      // viewport and object table | event list
    private SplitContainer splitCenter = null!;     // viewport | object table

    private EditorArea viewportArea = null!;
    private EditorArea outlinerArea = null!;
    private EditorArea propertiesArea = null!;
    private EditorArea timelineArea = null!;

    private Panel3D panel3D1 = null!;
    private Panel toolShelf = null!;
    private OutlinerView outliner = null!;
    private PropertiesEditor properties = null!;
    private TimelineRuler timeline = null!;

    private PixelHistoryPanel pixelHistoryPanel = null!;
    private GraphicsObjectTablePanel objectTablePanel = null!;
    private GraphicsEventListPanel eventListPanel = null!;
    private System.Windows.Forms.Timer tmrDebugRefresh = null!;

    // Viewport header.
    private MenuStrip viewportMenu = null!;
    private IconButton btnShadeWireframe = null!;
    private IconButton btnShadeSolid = null!;
    private IconButton btnShadeMaterial = null!;
    private IconButton btnShadeRendered = null!;
    private IconButton btnOverlays = null!;
    private IconButton btnSnap = null!;
    private ContextMenuStrip overlaysMenu = null!;

    // Tool shelf.
    private IconButton btnToolSelect = null!;
    private IconButton btnToolMove = null!;
    private IconButton btnToolRotate = null!;
    private IconButton btnToolScale = null!;

    // Timeline header.
    private IconButton btnJumpStart = null!;
    private IconButton btnPlay = null!;
    private IconButton btnJumpEnd = null!;
    private Label lblTimelineFrame = null!;

    // Properties: Render.
    private ComboBox cboEngine = null!;
    private CheckBox chkSuperSampling = null!;
    private CheckBox chkTemporalAntiAliasing = null!;
    private CheckBox chkMotionBlur = null!;
    private CheckBox chkHalfSpaceFill = null!;
    private CheckBox chkShowBackFacesCulling = null!;
    private CheckBox chkOrderIndependentTransparency = null!;
    private CheckBox chkShadows = null!;
    private ComboBox cboCascades = null!;
    private CheckBox chkReflections = null!;
    private CheckBox chkSsao = null!;
    private CheckBox chkBloom = null!;
    private CheckBox chkToneMap = null!;
    private CheckBox chkFxaa = null!;
    private CheckBox chkVignette = null!;
    private CheckBox chkGammaCorrect = null!;
    private CheckBox chkHighDynamicRange = null!;

    // Properties: Scene.
    private Label lblSceneModel = null!;
    private Button btnLoadModel = null!;
    private CheckBox chkShowXZGrid = null!;
    private CheckBox chkShowAxes = null!;
    private CheckBox chkShowTriangles = null!;
    private CheckBox chkShowSkeleton = null!;
    private CheckBox chkAnimate = null!;

    // Properties: World.
    private CheckBox chkSky = null!;
    private CheckBox chkHdrSky = null!;
    private CheckBox chkPanorama = null!;
    private Button btnPanorama = null!;
    private CheckBox chkBakedLight = null!;
    private Button btnBake = null!;
    private CheckBox chkFog = null!;

    // Properties: Object.
    private Label lblObjectName = null!;
    private Label lblObjectHint = null!;
    private VectorField vecLocation = null!;
    private VectorField vecRotation = null!;
    private VectorField vecScale = null!;
    private CheckBox chkObjectVisible = null!;
    private ComboBox cboGizmo = null!;
    private CheckBox chkSnap = null!;
    private PropertyPanel pnlObject = null!;
    private PropertyPanel pnlTransform = null!;
    private PropertyPanel pnlVisibility = null!;

    // Properties: Material.
    private RadioButton rdbNoneShading = null!;
    private RadioButton rdbClassicShading = null!;
    private RadioButton rdbFlatShading = null!;
    private RadioButton rdbGouraudShading = null!;
    private RadioButton rdbPhongShading = null!;
    private RadioButton rdbTexturedShading = null!;
    private RadioButton rdbMaterialShading = null!;
    private RadioButton rdbPbrShading = null!;
    private CheckBox chkTextureFiltering = null!;
    private CheckBox chkTrilinear = null!;
    private CheckBox chkAnisotropic = null!;

    // Properties: Debug.
    private ComboBox cboBufferView = null!;

    // Status bar.
    private ToolStripStatusLabel lblHintStatus = null!;
    private ToolStripStatusLabel lblPixelStatus = null!;
    private ToolStripStatusLabel lblSceneStats = null!;
    private ToolStripStatusLabel lblZoomStatus = null!;
    private ToolStripStatusLabel lblCameraStatus = null!;
    private ToolStripStatusLabel lblFrameStatus = null!;
    private ToolStripStatusLabel lblBackendStatus = null!;
    private ToolStripStatusLabel lblScreenshotHint = null!;

    // Menus.
    private ToolStripMenuItem mnuFile = null!;
    private ToolStripMenuItem mnuLoadModel = null!;
    private ToolStripMenuItem mnuOpenModel = null!;
    private ToolStripMenuItem mnuOpenScene = null!;
    private ToolStripMenuItem mnuOpenRecent = null!;
    private ToolStripMenuItem mnuClearRecent = null!;
    private ToolStripMenuItem mnuSaveScene = null!;
    private ToolStripMenuItem mnuExit = null!;

    private ToolStripMenuItem mnuEdit = null!;
    private ToolStripMenuItem mnuUndo = null!;
    private ToolStripMenuItem mnuRedo = null!;
    private ToolStripMenuItem mnuAdd = null!;
    private ToolStripMenuItem mnuDelete = null!;
    private ToolStripMenuItem mnuSnap = null!;

    private ToolStripMenuItem mnuRender = null!;
    private ToolStripMenuItem mnuScreenshot = null!;
    private ToolStripMenuItem mnuRenderedBy = null!;
    private ToolStripMenuItem mnuRenderCpu = null!;
    private ToolStripMenuItem mnuRenderGpu = null!;
    private ToolStripMenuItem mnuRenderTrace = null!;

    private ToolStripMenuItem mnuView = null!;
    private ToolStripMenuItem mnuAxisViews = null!;
    private ToolStripMenuItem mnuViewFront = null!;
    private ToolStripMenuItem mnuViewBack = null!;
    private ToolStripMenuItem mnuViewRight = null!;
    private ToolStripMenuItem mnuViewLeft = null!;
    private ToolStripMenuItem mnuViewTop = null!;
    private ToolStripMenuItem mnuViewBottom = null!;
    private ToolStripMenuItem mnuViewOpposite = null!;
    private ToolStripMenuItem mnuTurnX = null!;
    private ToolStripMenuItem mnuTurnY = null!;
    private ToolStripMenuItem mnuTurnZ = null!;
    private ToolStripMenuItem mnuZoomIn = null!;
    private ToolStripMenuItem mnuZoomOut = null!;
    private ToolStripMenuItem mnuZoomActual = null!;
    private ToolStripMenuItem mnuClearPixel = null!;
    private ToolStripMenuItem mnuFocusViewport = null!;
    private ToolStripMenuItem mnuToggleSidebar = null!;
    private ToolStripMenuItem mnuToggleToolShelf = null!;
    private ToolStripMenuItem mnuStatsOverlay = null!;

    private ToolStripMenuItem mnuWindow = null!;
    private ToolStripMenuItem mnuPixelHistory = null!;
    private ToolStripMenuItem mnuObjectTable = null!;
    private ToolStripMenuItem mnuEventList = null!;
    private ToolStripMenuItem mnuRecordEvents = null!;
    private ToolStripMenuItem mnuFrameHistory = null!;
    private ToolStripMenuItem mnuKeepFrames = null!;
    private ToolStripMenuItem mnuPreviousFrame = null!;
    private ToolStripMenuItem mnuNextFrame = null!;
    private ToolStripMenuItem mnuLatestFrame = null!;

    private ToolStripMenuItem mnuHelp = null!;
    private ToolStripMenuItem mnuShortcuts = null!;
    private ToolStripMenuItem mnuProjectPage = null!;
    private ToolStripMenuItem mnuAbout = null!;

    private static readonly string[] Workspaces = ["Layout", "Shading", "Rendering", "Debugging"];

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolTip1 = new ToolTip(components) { InitialDelay = 600, ReshowDelay = 100, AutoPopDelay = 15000 };
        tmrDebugRefresh = new System.Windows.Forms.Timer(components) { Interval = 120 };

        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "SoftEngine";
        ClientSize = new Size(1600, 940);
        MinimumSize = new Size(1000, 640);
        BackColor = Theme.Gap;
        ForeColor = Theme.TextPrimary;
        Font = Theme.UiFont;
        KeyPreview = true;

        BuildMenus();
        BuildTopBar();
        BuildStatusBar();
        BuildViewport();
        BuildOutliner();
        BuildProperties();
        BuildTimeline();
        BuildDebugPanels();
        BuildSplits();

        Controls.Add(splitMain);
        Controls.Add(statusStrip);
        Controls.Add(topBar);

        MainMenuStrip = menuStrip;

        ResumeLayout(false);
        PerformLayout();
    }

    #region Top bar and menus

    private void BuildTopBar()
    {
        topBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.TopBar };

        var logo = new PictureBox
        {
            Size = new Size(30, 30),
            Margin = new Padding(0),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = Theme.TopBar,
            Image = Theme.AppIcon is { } icon ? new Icon(icon, 16, 16).ToBitmap() : null,
        };

        workspaceTabs = new WorkspaceTabs { Height = 30, Margin = new Padding(16, 0, 0, 0) };
        workspaceTabs.SetTabs(Workspaces);

        menuStrip.Dock = DockStyle.None;
        menuStrip.Margin = new Padding(0, 1, 0, 0);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.TopBar,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        left.Controls.AddRange([logo, menuStrip, workspaceTabs]);

        lblCurrentModel = new Label
        {
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = 320,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.TopBar,
            Padding = new Padding(0, 0, 12, 0),
            AutoEllipsis = true,
            Text = "Scene",
        };

        topBar.Controls.Add(lblCurrentModel);
        topBar.Controls.Add(left);
    }

    private static ToolStripMenuItem Item(string text, Keys keys = Keys.None, string? display = null, string? tip = null)
    {
        var item = new ToolStripMenuItem(text);

        if (keys != Keys.None)
        {
            item.ShortcutKeys = keys;
        }

        if (display is not null)
        {
            item.ShortcutKeyDisplayString = display;
        }

        if (tip is not null)
        {
            item.ToolTipText = tip;
        }

        return item;
    }

    private static ToolStripMenuItem Toggle(string text, bool on, Keys keys = Keys.None, string? tip = null)
    {
        var item = Item(text, keys, tip: tip);
        item.CheckOnClick = true;
        item.Checked = on;
        return item;
    }

    private void BuildMenus()
    {
        var renderer = new EditorMenuRenderer();

        mnuLoadModel = Item("&Load Bundled Scene…", Keys.Control | Keys.M);
        mnuOpenModel = Item("&Open Model…", Keys.Control | Keys.O);
        mnuOpenScene = Item("Open Sc&ene…");
        mnuClearRecent = Item("&Clear Recent Files List");
        mnuOpenRecent = Item("Open &Recent");
        mnuOpenRecent.DropDownItems.Add(mnuClearRecent);
        mnuSaveScene = Item("&Save Scene As…", Keys.Control | Keys.S);
        mnuExit = Item("&Quit", Keys.Control | Keys.Q);

        mnuFile = new ToolStripMenuItem("&File");
        mnuFile.DropDownItems.AddRange([
            mnuLoadModel, mnuOpenModel, mnuOpenScene, mnuOpenRecent,
            new ToolStripSeparator(), mnuSaveScene,
            new ToolStripSeparator(), mnuExit,
        ]);

        mnuUndo = Item("&Undo", Keys.Control | Keys.Z);
        mnuRedo = Item("&Redo", Keys.Control | Keys.Shift | Keys.Z);
        mnuAdd = Item("&Add", display: "Shift+A");
        mnuDelete = Item("&Delete", display: "X · Del");
        mnuSnap = Toggle("S&nap", false, Keys.Control | Keys.G, "Snap moves, turns and scales to a grid: whole units, 15°, tenths of scale.");

        mnuEdit = new ToolStripMenuItem("&Edit");
        mnuEdit.DropDownItems.AddRange([
            mnuUndo, mnuRedo,
            new ToolStripSeparator(), mnuAdd, mnuDelete,
            new ToolStripSeparator(), mnuSnap,
        ]);

        mnuScreenshot = Item("Render &Image…", Keys.F12, tip: "Save the viewport's current frame as a PNG.");
        mnuRenderCpu = Item("SoftEngine &CPU — software rasterizer", tip: "Rasterize every triangle on the CPU, as this engine was written to.");
        mnuRenderCpu.Checked = true;
        mnuRenderGpu = Item("&OpenGL — graphics adapter", tip: "Fill the frame on the graphics card through OpenGL.");
        mnuRenderTrace = Item("&Path Tracer — reference", tip: "Trace light through the scene instead of filling triangles: real interreflection and occlusion, refined for as long as nothing moves.");
        mnuRenderedBy = Item("Render &Engine");
        mnuRenderedBy.DropDownItems.AddRange([mnuRenderCpu, mnuRenderGpu, mnuRenderTrace]);

        mnuRender = new ToolStripMenuItem("&Render");
        mnuRender.DropDownItems.AddRange([mnuScreenshot, new ToolStripSeparator(), mnuRenderedBy]);

        mnuViewFront = Item("&Front", display: "Numpad 1");
        mnuViewBack = Item("&Back", display: "Ctrl+Numpad 1");
        mnuViewRight = Item("&Right", display: "Numpad 3");
        mnuViewLeft = Item("&Left", display: "Ctrl+Numpad 3");
        mnuViewTop = Item("&Top", display: "Numpad 7");
        mnuViewBottom = Item("B&ottom", display: "Ctrl+Numpad 7");
        mnuViewOpposite = Item("O&pposite", display: "Numpad 9");
        mnuAxisViews = Item("&Viewpoint");
        mnuAxisViews.DropDownItems.AddRange([
            mnuViewFront, mnuViewBack, mnuViewRight, mnuViewLeft, mnuViewTop, mnuViewBottom,
            new ToolStripSeparator(), mnuViewOpposite,
        ]);

        mnuTurnX = Item("Orbit 15° about &X", display: "X · Shift+X");
        mnuTurnY = Item("Orbit 15° about &Y", display: "Y · Shift+Y");
        mnuTurnZ = Item("Orbit 15° about &Z", display: "Z · Shift+Z");
        mnuZoomIn = Item("Zoom &In", Keys.Control | Keys.Oemplus);
        mnuZoomOut = Item("Zoom O&ut", Keys.Control | Keys.OemMinus);
        mnuZoomActual = Item("&Reset Zoom", Keys.Control | Keys.D0);
        mnuClearPixel = Item("&Clear Probed Pixel");
        mnuToggleSidebar = Toggle("&Sidebar (Outliner and Properties)", true, Keys.Control | Keys.N);
        mnuToggleToolShelf = Toggle("&Toolbar", true, Keys.Control | Keys.T);
        mnuFocusViewport = Toggle("Toggle &Maximize Area", false, Keys.F11, "Give the whole window to the viewport. Press again to put everything back.");
        mnuStatsOverlay = Toggle("&Statistics Overlay", true);

        mnuView = new ToolStripMenuItem("&View");
        mnuView.DropDownItems.AddRange([
            mnuAxisViews, mnuTurnX, mnuTurnY, mnuTurnZ,
            new ToolStripSeparator(), mnuZoomIn, mnuZoomOut, mnuZoomActual,
            new ToolStripSeparator(), mnuToggleToolShelf, mnuToggleSidebar, mnuFocusViewport, mnuStatsOverlay,
            new ToolStripSeparator(), mnuClearPixel,
        ]);

        mnuPixelHistory = Toggle("&Pixel History", false);
        mnuObjectTable = Toggle("Graphics &Object Table", false);
        mnuEventList = Toggle("Graphics &Event List", false);
        mnuRecordEvents = Toggle("&Record Graphics Events", true);
        mnuKeepFrames = Toggle("&Keep Recent Frames", false);
        mnuPreviousFrame = Item("&Previous Frame", Keys.Control | Keys.Left);
        mnuNextFrame = Item("&Next Frame", Keys.Control | Keys.Right);
        mnuLatestFrame = Item("&Live (follow the newest frame)", Keys.Control | Keys.End);
        mnuFrameHistory = Item("&Frame History");
        mnuFrameHistory.DropDownItems.AddRange([mnuKeepFrames, new ToolStripSeparator(), mnuPreviousFrame, mnuNextFrame, mnuLatestFrame]);

        mnuWindow = new ToolStripMenuItem("&Window");

        for (var i = 0; i < Workspaces.Length; i++)
        {
            var name = Workspaces[i];
            var item = Item($"{name} Workspace", Keys.Control | (Keys.D1 + i));
            item.Tag = name;
            item.Click += (s, e) => workspaceTabs.Select(name);
            mnuWindow.DropDownItems.Add(item);
        }

        mnuWindow.DropDownItems.AddRange([
            new ToolStripSeparator(), mnuPixelHistory, mnuObjectTable, mnuEventList,
            new ToolStripSeparator(), mnuRecordEvents, mnuFrameHistory,
        ]);

        mnuShortcuts = Item("&Keyboard and Mouse…", Keys.F1, tip: "Every binding, including the ones with no menu item.");
        mnuProjectPage = Item("SoftEngine on &GitHub");
        mnuAbout = Item("&About SoftEngine");

        mnuHelp = new ToolStripMenuItem("&Help");
        mnuHelp.DropDownItems.AddRange([mnuShortcuts, new ToolStripSeparator(), mnuProjectPage, mnuAbout]);

        menuStrip = new MenuStrip
        {
            AutoSize = true,
            BackColor = Theme.TopBar,
            ForeColor = Theme.TextPrimary,
            Renderer = renderer,
            Padding = new Padding(2, 4, 0, 0),
            GripStyle = ToolStripGripStyle.Hidden,
        };

        menuStrip.Items.AddRange([mnuFile, mnuEdit, mnuRender, mnuView, mnuWindow, mnuHelp]);

        foreach (var item in menuStrip.Items.OfType<ToolStripMenuItem>())
        {
            item.ForeColor = Theme.TextPrimary;
            item.DropDown.BackColor = Theme.Background;
        }
    }

    #endregion

    #region Status bar

    private void BuildStatusBar()
    {
        ToolStripStatusLabel Label(string text, int width = 0, bool spring = false, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
        {
            Text = text,
            AutoSize = width == 0 && !spring,
            Width = width,
            Spring = spring,
            TextAlign = align,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(6, 3, 6, 2),
        };

        // The hints take whatever room the statistics leave and clip at its edge, rather than
        // running underneath them when the window is narrow.
        lblHintStatus = Label("", spring: true);
        lblHintStatus.ForeColor = Theme.TextPrimary;
        lblPixelStatus = Label("");
        lblSceneStats = Label("Scene");
        lblZoomStatus = Label("Zoom 100%");
        lblCameraStatus = Label("");
        lblFrameStatus = Label("");
        lblBackendStatus = Label("CPU");
        lblBackendStatus.ToolTipText = "What the viewport is rendered by (Render → Render Engine)";
        lblScreenshotHint = Label("F12 Render Image");
        lblScreenshotHint.IsLink = false;

        statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            BackColor = Theme.TopBar,
            Renderer = new EditorMenuRenderer(),
            Font = Theme.UiFont,
            ShowItemToolTips = true,
        };

        statusStrip.Items.AddRange([
            lblHintStatus, lblPixelStatus, lblSceneStats, lblZoomStatus, lblCameraStatus, lblFrameStatus, lblBackendStatus,
        ]);
    }

    #endregion

    #region Viewport

    private void BuildViewport()
    {
        viewportArea = new EditorArea("3D Viewport", Glyph.Viewport3D);

        panel3D1 = new Panel3D
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Viewport,
            BackgroundImageLayout = ImageLayout.None,
            TabStop = true,
        };

        viewportArea.Body.Controls.Add(panel3D1);

        // Header: mode, menus, and on the right the overlay, snap and shading switches.
        var mode = new Label
        {
            Text = "Object Mode",
            AutoSize = false,
            Size = new Size(96, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Widget,
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(2, 0, 6, 0),
        };

        toolTip1.SetToolTip(mode, "Objects are moved, turned and scaled whole. Edit Mode is a modelling program's next step; this one stops here.");

        viewportMenu = new MenuStrip
        {
            AutoSize = true,
            BackColor = Theme.Header,
            Renderer = new EditorMenuRenderer(),
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 0),
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.None,
        };

        viewportArea.HeaderTools.Controls.Add(mode);
        viewportArea.HeaderTools.Controls.Add(viewportMenu);

        btnOverlays = new IconButton { Glyph = Glyph.Overlays, Size = new Size(34, 22), Margin = new Padding(0, 0, 6, 0) };
        toolTip1.SetToolTip(btnOverlays, "Viewport overlays — grid, axes, wireframe, skeleton, statistics");

        btnSnap = new IconButton { Glyph = Glyph.Magnet, Toggle = true, Margin = new Padding(0, 0, 10, 0) };
        toolTip1.SetToolTip(btnSnap, "Snap (Ctrl+G) — moves, turns and scales land on a grid");

        btnShadeWireframe = new IconButton { Glyph = Glyph.Wireframe, Segment = -1, Margin = new Padding(0) };
        btnShadeSolid = new IconButton { Glyph = Glyph.Solid, Segment = 0, Margin = new Padding(0) };
        btnShadeMaterial = new IconButton { Glyph = Glyph.MaterialPreview, Segment = 0, Margin = new Padding(0) };
        btnShadeRendered = new IconButton { Glyph = Glyph.Rendered, Segment = 1, Margin = new Padding(0) };

        toolTip1.SetToolTip(btnShadeWireframe, "Wireframe — the triangles' edges and nothing else");
        toolTip1.SetToolTip(btnShadeSolid, "Solid — lit by the scene's lights, no textures");
        toolTip1.SetToolTip(btnShadeMaterial, "Material Preview — albedo, normal and specular maps");
        toolTip1.SetToolTip(btnShadeRendered, "Rendered — physically based materials lit by the lights and the environment");

        viewportArea.HeaderRight.Controls.AddRange([btnOverlays, btnSnap, btnShadeWireframe, btnShadeSolid, btnShadeMaterial, btnShadeRendered]);

        overlaysMenu = new ContextMenuStrip(components) { Renderer = new EditorMenuRenderer(), BackColor = Theme.Background };

        // Tool shelf, floating at the viewport's top-left like a toolbar region.
        btnToolSelect = new IconButton { Glyph = Glyph.Select, Size = new Size(30, 30), Location = new Point(4, 4), Segment = 2 };
        btnToolMove = new IconButton { Glyph = Glyph.Move, Size = new Size(30, 30), Location = new Point(4, 40) };
        btnToolRotate = new IconButton { Glyph = Glyph.Rotate, Size = new Size(30, 30), Location = new Point(4, 72) };
        btnToolScale = new IconButton { Glyph = Glyph.Scale, Size = new Size(30, 30), Location = new Point(4, 104) };

        toolTip1.SetToolTip(btnToolSelect, "Select — click to pick a mesh; no handles");
        toolTip1.SetToolTip(btnToolMove, "Move — drag the arrows on the selected mesh (or press G)");
        toolTip1.SetToolTip(btnToolRotate, "Rotate — drag the rings on the selected mesh (or press R)");
        toolTip1.SetToolTip(btnToolScale, "Scale — drag the handles on the selected mesh (or press S)");

        foreach (var tool in new[] { btnToolSelect, btnToolMove, btnToolRotate, btnToolScale })
        {
            tool.IdleColor = Theme.Header;
        }

        toolShelf = new Panel
        {
            Location = new Point(8, 8),
            Size = new Size(38, 138),
            BackColor = Theme.Surface,
        };

        toolShelf.Controls.AddRange([btnToolSelect, btnToolMove, btnToolRotate, btnToolScale]);
        panel3D1.Controls.Add(toolShelf);
    }

    #endregion

    #region Outliner

    private void BuildOutliner()
    {
        outlinerArea = new EditorArea("Outliner", Glyph.Outliner);
        outliner = new OutlinerView { Dock = DockStyle.Fill };
        outlinerArea.Body.Controls.Add(outliner);
    }

    #endregion

    #region Properties

    private ThemedCheckBox Check(string text, string tip)
    {
        var box = new ThemedCheckBox { Text = text };
        toolTip1.SetToolTip(box, tip);
        return box;
    }

    private ThemedRadioButton Radio(string text, Glyph glyph, string tip)
    {
        var radio = new ThemedRadioButton { Text = text, Glyph = glyph };
        toolTip1.SetToolTip(radio, tip);
        return radio;
    }

    private ThemedButton Push(string text, Glyph glyph, string tip)
    {
        var button = new ThemedButton { Text = text, Glyph = glyph };
        toolTip1.SetToolTip(button, tip);
        return button;
    }

    private static ComboBox Choice() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Widget,
        ForeColor = Theme.TextPrimary,
        Font = Theme.UiFont,
        Height = 22,
    };

    private void BuildProperties()
    {
        propertiesArea = new EditorArea("Properties", Glyph.Properties);
        properties = new PropertiesEditor { Dock = DockStyle.Fill };
        propertiesArea.Body.Controls.Add(properties);

        BuildRenderTab(properties.AddTab("Render", Glyph.Render, "Render — engine, sampling, shadows, post-processing"));
        BuildSceneTab(properties.AddTab("Scene", Glyph.Scene, "Scene — the loaded model, what the viewport overlays, animation"));
        BuildWorldTab(properties.AddTab("World", Glyph.World, "World — sky, environment light, baked light, mist"));
        BuildObjectTab(properties.AddTab("Object", Glyph.Object, "Object — the selected mesh's transform and visibility"));
        BuildMaterialTab(properties.AddTab("Material", Glyph.Material, "Material — shading model and texture filtering"));
        BuildDebugTab(properties.AddTab("Debug", Glyph.Debug, "Debug — present one of the frame's buffers"));
    }

    private void BuildRenderTab(StackPanel page)
    {
        var engine = new PropertyPanel("Render Engine");
        cboEngine = Choice();
        cboEngine.Items.AddRange(["SoftEngine CPU", "OpenGL", "Path Tracer"]);
        toolTip1.SetToolTip(cboEngine, "What fills the frame: this engine's own rasterizer, the graphics card, or a path-traced reference");
        engine.Add(new PropertyRow("Engine", cboEngine));

        var sampling = new PropertyPanel("Sampling");
        chkSuperSampling = sampling.Add(Check("Supersampling (2×)", "Render at twice the resolution and average down — anti-aliases everything, fills four times the pixels"));
        chkTemporalAntiAliasing = sampling.Add(Check("Temporal Anti-Aliasing", "Jitter the frame by a fraction of a pixel and average it with the previous ones — supersampling spread over time. Costs a velocity pass."));
        chkMotionBlur = sampling.Add(Check("Motion Blur", "Smear each pixel along the direction its surface is travelling. Costs a velocity pass."));

        var raster = new PropertyPanel("Rasterizer");
        chkHalfSpaceFill = raster.Add(Check("Half-Space Fill", "Fill triangles by classifying blocks of pixels against their three edge functions, instead of walking two edges and filling the span between them. Draws the same pixels either way"));
        chkShowBackFacesCulling = raster.Add(Check("Backface Culling", "Skip triangles that face away from the camera"));
        chkOrderIndependentTransparency = raster.Add(Check("Order-Independent Transparency", "Resolve transparency per pixel instead of by sorting the transparent triangles — correct where glass intersects glass. Costs storage for the fragments."));

        var shadows = new PropertyPanel("Shadows");
        chkShadows = shadows.Add(Check("Shadows", "Shadow-map the world from the first light (lit shading modes)"));
        cboCascades = Choice();
        toolTip1.SetToolTip(cboCascades, "Split the shadow pass across several depth buffers, each fitted to a slice of the view");
        shadows.Add(new PropertyRow("Cascades", cboCascades));

        var post = new PropertyPanel("Post Processing");
        chkReflections = post.Add(Check("Screen Space Reflections", "Reflect the scene in glossy and metallic surfaces (CPU only)"));
        chkSsao = post.Add(Check("Ambient Occlusion", "Darken creases and contact points, from the depth buffer"));
        chkBloom = post.Add(Check("Bloom", "Bleed light out of the brightest parts of the image"));
        chkToneMap = post.Add(Check("Filmic Tone Mapping", "Exposure and an ACES filmic curve instead of a hard clip"));
        chkFxaa = post.Add(Check("FXAA", "Smooth stair-stepped edges after rasterization"));
        chkVignette = post.Add(Check("Vignette", "Darken the frame toward its corners"));

        var color = new PropertyPanel("Color Management");
        chkGammaCorrect = color.Add(Check("Linear Workflow (sRGB output)", "Shade in linear light and encode to sRGB on output"));
        chkHighDynamicRange = color.Add(Check("High Dynamic Range", "Keep highlights brighter than white in a linear float buffer, for bloom and tone mapping to work with"));

        page.Controls.AddRange([engine, sampling, raster, shadows, post, color]);
    }

    private void BuildSceneTab(StackPanel page)
    {
        var scene = new PropertyPanel("Scene");
        lblSceneModel = scene.Add(new Label
        {
            Height = 22,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "—",
        });
        btnLoadModel = scene.Add(Push("Load Scene…", Glyph.Scene, "Pick a bundled scene or open an OBJ, Collada or glTF file (Ctrl+M)"));

        var display = new PropertyPanel("Viewport Display");
        chkShowXZGrid = display.Add(Check("Floor Grid", "Draw the XZ grid under the scene"));
        chkShowAxes = display.Add(Check("Axes", "Draw the world's X, Y and Z axes"));
        chkShowTriangles = display.Add(Check("Wireframe", "Outline every triangle over the shading"));
        chkShowSkeleton = display.Add(Check("Skeleton", "Draw the scene's node hierarchy as bones over the frame"));

        var animation = new PropertyPanel("Animation");
        chkAnimate = animation.Add(Check("Play Animation", "Play the loaded world's animation; unchecking holds the current pose (Space)"));

        page.Controls.AddRange([scene, display, animation]);
    }

    private void BuildWorldTab(StackPanel page)
    {
        var surface = new PropertyPanel("Surface");
        chkSky = surface.Add(Check("Procedural Sky", "Draw a procedural sky behind the scene, and take the ambient light from it"));
        chkHdrSky = surface.Add(Check("HDR Sky", "Build the procedural sky in linear light, with a sun hundreds of times brighter than white"));
        surface.Add(new SubHeading("Environment Texture"));
        chkPanorama = surface.Add(Check("No panorama loaded", "Use the loaded panorama instead of the procedural sky"));
        chkPanorama.Enabled = false;
        btnPanorama = surface.Add(Push("Open Panorama…", Glyph.World, "Open a Radiance .hdr or an image to surround and light the scene with"));

        var probes = new PropertyPanel("Light Probes");
        chkBakedLight = probes.Add(Check("No baked light", "Light the scene with the baked probes instead of the environment's ambient"));
        chkBakedLight.Enabled = false;
        btnBake = probes.Add(Push("Bake Indirect Lighting", Glyph.Light, "Trace the scene's bounce light into a grid of probes (software rasterizer only)"));

        var mist = new PropertyPanel("Mist");
        chkFog = mist.Add(Check("Mist Pass", "Fade distant geometry into the background"));

        page.Controls.AddRange([surface, probes, mist]);
    }

    private void BuildObjectTab(StackPanel page)
    {
        lblObjectHint = new Label
        {
            Height = 60,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Surface,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "No active object.\nClick a mesh in the viewport or the outliner.",
        };

        pnlObject = new PropertyPanel("Object");
        lblObjectName = pnlObject.Add(new Label
        {
            Height = 24,
            ForeColor = Theme.ActiveObject,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.UiFontBold,
            AutoEllipsis = true,
        });

        pnlTransform = new PropertyPanel("Transform");
        vecLocation = pnlTransform.Add(new VectorField("Location", 0.01f, "0.### m"));
        vecRotation = pnlTransform.Add(new VectorField("Rotation", 0.5f, "0.#", "°"));
        vecScale = pnlTransform.Add(new VectorField("Scale", 0.01f, "0.###"));

        pnlVisibility = new PropertyPanel("Visibility");
        chkObjectVisible = pnlVisibility.Add(Check("Show in Viewport", "Hide or show the selected mesh (also the eye in the outliner)"));

        var tool = new PropertyPanel("Active Tool");
        cboGizmo = Choice();
        toolTip1.SetToolTip(cboGizmo, "Drag the handles on the picked mesh to move, turn or stretch it");
        tool.Add(new PropertyRow("Gizmo", cboGizmo));
        chkSnap = tool.Add(Check("Snap", "Snap moves, turns and scales to a grid: whole units, 15°, tenths of scale (Ctrl+G)"));

        page.Controls.AddRange([lblObjectHint, pnlObject, pnlTransform, pnlVisibility, tool]);
    }

    private void BuildMaterialTab(StackPanel page)
    {
        var model = new PropertyPanel("Shading Model");
        rdbNoneShading = model.Add(Radio("None (overlays only)", Glyph.Wireframe, "No surface at all — only the wireframe and other overlays"));
        rdbClassicShading = model.Add(Radio("Classic", Glyph.Solid, "Each triangle in its own flat colour, unlit"));
        rdbFlatShading = model.Add(Radio("Flat", Glyph.Solid, "Lit once per triangle — every facet shows"));
        rdbGouraudShading = model.Add(Radio("Gouraud", Glyph.Solid, "Lit at the vertices and blended across the triangle"));
        rdbPhongShading = model.Add(Radio("Phong", Glyph.Solid, "Lit per pixel from interpolated normals, with highlights"));
        rdbTexturedShading = model.Add(Radio("Textured", Glyph.MaterialPreview, "The mesh's texture, lit per pixel"));
        rdbMaterialShading = model.Add(Radio("Material", Glyph.MaterialPreview, "Per-pixel albedo, normal and specular maps"));
        rdbPbrShading = model.Add(Radio("Principled (PBR)", Glyph.Rendered, "Metallic-roughness materials lit by the scene and by the environment"));

        var filtering = new PropertyPanel("Texture Filtering");
        chkTextureFiltering = filtering.Add(Check("Bilinear + Mipmaps", "Bilinear filtering with mip-mapping"));
        chkTrilinear = filtering.Add(Check("Trilinear", "Blend the two mip levels a surface falls between, instead of stepping from one to the next"));
        chkAnisotropic = filtering.Add(Check("Anisotropic", "Measure the two axes of a pixel's texture footprint apart and spread several taps along the longer one, so a surface seen edge-on stays sharp instead of blurring"));

        page.Controls.AddRange([model, filtering]);
    }

    private void BuildDebugTab(StackPanel page)
    {
        var buffers = new PropertyPanel("Buffer View");
        cboBufferView = Choice();
        toolTip1.SetToolTip(cboBufferView, "Present one of the frame's own buffers instead of the shaded image");
        buffers.Add(new PropertyRow("Show", cboBufferView));

        var debugger = new PropertyPanel("Graphics Debugger");
        var open = debugger.Add(Push("Open Debugging Workspace", Glyph.Debug, "Pixel history, the object table and the event list around the viewport"));
        open.Click += (s, e) => workspaceTabs.Select("Debugging");

        page.Controls.AddRange([buffers, debugger]);
    }

    #endregion

    #region Timeline

    private void BuildTimeline()
    {
        timelineArea = new EditorArea("Timeline", Glyph.Timeline);

        btnJumpStart = new IconButton { Glyph = Glyph.JumpStart, Segment = -1, Margin = new Padding(0) };
        btnPlay = new IconButton { Glyph = Glyph.Play, Segment = 0, Margin = new Padding(0) };
        btnJumpEnd = new IconButton { Glyph = Glyph.JumpEnd, Segment = 1, Margin = new Padding(0, 0, 10, 0) };

        toolTip1.SetToolTip(btnJumpStart, "Jump to the first frame (Shift+Left)");
        toolTip1.SetToolTip(btnPlay, "Play / pause the animation (Space)");
        toolTip1.SetToolTip(btnJumpEnd, "Jump to the last frame (Shift+Right)");

        lblTimelineFrame = new Label
        {
            AutoSize = false,
            Size = new Size(170, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Header,
            Text = "",
        };

        timelineArea.HeaderTools.Controls.AddRange([btnJumpStart, btnPlay, btnJumpEnd, lblTimelineFrame]);

        timeline = new TimelineRuler { Dock = DockStyle.Fill };
        timelineArea.Body.Controls.Add(timeline);
    }

    #endregion

    #region Debugger panels

    private void BuildDebugPanels()
    {
        pixelHistoryPanel = new PixelHistoryPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        objectTablePanel = new GraphicsObjectTablePanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        eventListPanel = new GraphicsEventListPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
    }

    #endregion

    #region Splits

    private static SplitContainer Split(Orientation orientation) => new()
    {
        Dock = DockStyle.Fill,
        Orientation = orientation,
        SplitterWidth = 3,
        BackColor = Theme.Gap,
        TabStop = false,
    };

    private void BuildSplits()
    {
        // Innermost first, so each split is sized by the time the next one wraps it.
        splitCenter = Split(Orientation.Horizontal);
        splitCenter.Panel1.Controls.Add(viewportArea);
        splitCenter.Panel2.Controls.Add(Framed(objectTablePanel));
        viewportArea.Dock = DockStyle.Fill;
        splitCenter.Panel2Collapsed = true;

        splitRight = Split(Orientation.Vertical);
        splitRight.FixedPanel = FixedPanel.Panel2;
        splitRight.Panel1.Controls.Add(splitCenter);
        splitRight.Panel2.Controls.Add(Framed(eventListPanel));
        splitRight.Panel2Collapsed = true;

        splitLeft = Split(Orientation.Vertical);
        splitLeft.FixedPanel = FixedPanel.Panel1;
        splitLeft.Panel1.Controls.Add(Framed(pixelHistoryPanel));
        splitLeft.Panel2.Controls.Add(splitRight);
        splitLeft.Panel1Collapsed = true;

        splitTimeline = Split(Orientation.Horizontal);
        splitTimeline.FixedPanel = FixedPanel.Panel2;
        splitTimeline.Panel1.Controls.Add(splitLeft);
        splitTimeline.Panel2.Controls.Add(timelineArea);
        timelineArea.Dock = DockStyle.Fill;

        splitSidebar = Split(Orientation.Horizontal);
        splitSidebar.Panel1.Controls.Add(outlinerArea);
        splitSidebar.Panel2.Controls.Add(propertiesArea);
        outlinerArea.Dock = DockStyle.Fill;
        propertiesArea.Dock = DockStyle.Fill;

        splitMain = Split(Orientation.Vertical);
        splitMain.FixedPanel = FixedPanel.Panel2;
        splitMain.Panel1.Controls.Add(splitTimeline);
        splitMain.Panel2.Controls.Add(splitSidebar);
        splitMain.Padding = new Padding(2, 0, 2, 0);
    }

    private static Panel Framed(Control content)
    {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Gap, Padding = new Padding(1) };
        frame.Controls.Add(content);
        return frame;
    }

    /// <summary>
    /// Splitter positions are only meaningful once the window has its size, so the defaults are
    /// applied from OnLoad rather than while the controls are being built.
    /// </summary>
    private void ApplyDefaultSplits()
    {
        splitMain.Panel2MinSize = LogicalToDeviceUnits(240);
        splitTimeline.Panel2MinSize = LogicalToDeviceUnits(60);

        SetSplitter(splitMain, splitMain.Width - LogicalToDeviceUnits(380));
        SetSplitter(splitSidebar, (int)(splitSidebar.Height * 0.3f));
        SetSplitter(splitTimeline, splitTimeline.Height - LogicalToDeviceUnits(104));
        SetSplitter(splitLeft, LogicalToDeviceUnits(300));
        SetSplitter(splitRight, splitRight.Width - LogicalToDeviceUnits(380));
        SetSplitter(splitCenter, splitCenter.Height - LogicalToDeviceUnits(230));
    }

    private static void SetSplitter(SplitContainer split, int? distance)
    {
        if (distance is not { } value)
        {
            return;
        }

        var total = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var lowest = split.Panel1MinSize;
        var highest = total - split.Panel2MinSize - split.SplitterWidth;

        if (highest < lowest)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(value, lowest, highest);
    }

    #endregion
}
