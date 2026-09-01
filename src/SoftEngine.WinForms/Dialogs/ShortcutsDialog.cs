namespace SoftEngine.WinForms.Dialogs;

internal sealed class ShortcutsDialog : Form
{
    private sealed record Section(string Title, (string Keys, string Action)[] Rows);

    private static readonly Section[] Reference =
    [
        new("Camera", [
            ("Left-drag", "Orbit around what the view is centred on"),
            ("Right-drag", "Pan"),
            ("Left + right drag", "Move in and out"),
            ("Wheel", "Move in and out"),
            ("W A S D", "Fly forward, left, back, right"),
            ("Q / E", "Fly down / up"),
            ("Arrow keys", "The same fly, for a keyboard without a comfortable WASD"),
            ("Shift (held)", "Four times faster — flying and the wheel both"),
            ("Ctrl (held)", "Four times finer"),
            ("Home", "Back to the framing the world loaded with"),
        ]),

        new("Aiming the view", [
            ("Numpad 1 / Ctrl+1", "Front (+Z) / Back (−Z)"),
            ("Numpad 3 / Ctrl+3", "Right (+X) / Left (−X)"),
            ("Numpad 7 / Ctrl+7", "Top (+Y) / Bottom (−Y)"),
            ("Numpad 9", "Swing round to the opposite side"),
            ("Numpad 4 / 6", "Turn 15° on the turntable"),
            ("Numpad 8 / 2", "Tip 15° towards the viewer / away"),
            ("X · Shift+X", "Turn 15° about world X, and back"),
            ("Y · Shift+Y", "Turn 15° about world Y, and back"),
            ("Z · Shift+Z", "Turn 15° about world Z, and back"),
            ("Ctrl+= / Ctrl+−", "Zoom in / out"),
            ("Ctrl+0", "Reset the zoom to 100%"),
        ]),

        new("Picking and editing", [
            ("Left-click", "Probe the pixel and pick the mesh under it"),
            ("Esc", "Clear the selection — and hand S and X back to the camera"),
            ("Shift+A", "Add a primitive at the centre of the view — plane, cube, sphere…"),
            ("Drag a gizmo handle", "Move, turn or stretch the picked mesh"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo an edit — the Edit menu names it"),
            ("Ctrl+G", "Snap edits to a grid: whole units, 15°, tenths of scale"),
        ]),

        new("With a mesh selected", [
            ("G", "Move it with the cursor — no handle to grab"),
            ("S", "Scale it with the cursor (S flies the camera back when nothing is selected)"),
            ("X · Del", "Delete it (X turns the view when nothing is selected)"),
            ("X / Y / Z", "During a G or S: press the gesture flat against that world axis"),
            ("Click · Enter", "Confirm the move or scale"),
            ("Esc · right-click", "Put it back where it was"),
        ]),

        new("Files", [
            ("Ctrl+M", "Load a bundled world"),
            ("Ctrl+O", "Open a model file (.obj, .dae, .gltf, .glb)"),
            ("Ctrl+S", "Save the scene as JSON"),
            ("F12", "Save the view as a PNG"),
            ("Drop a file on the viewport", "Loads it — model, scene or panorama, by extension"),
        ]),

        new("Debugger and workspace", [
            ("Ctrl+← / Ctrl+→", "Step back and forward through kept frames"),
            ("Ctrl+End", "Follow the newest frame again"),
            ("F11", "Focus the viewport — hide the sidebar and the debugger panels"),
            ("F1", "This list"),
        ]),
    ];

    public ShortcutsDialog()
    {
        Text = "Keyboard and mouse";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        SizeGripStyle = SizeGripStyle.Show;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(620, 700);
        MinimumSize = new Size(520, 360);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9.75f);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = Point.Empty,
            Margin = Padding.Empty,
            BackColor = Theme.Background,
        };

        foreach (var section in Reference)
        {
            stack.Controls.Add(BuildHeader(section.Title));
            stack.Controls.Add(BuildRows(section.Rows));
        }

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(18, 12, 18, 12),
            BackColor = Theme.Background,
        };

        host.Controls.Add(stack);

        void FitStack(object? sender, EventArgs e) =>
            stack.MaximumSize = stack.MinimumSize = new Size(
                Math.Max(240, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth),
                0);

        host.Resize += FitStack;
        FitStack(this, EventArgs.Empty);

        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(104, 36),
            Padding = new Padding(16, 6, 16, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Margin = Padding.Empty,
            UseVisualStyleBackColor = false,
            DialogResult = DialogResult.OK,
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 10, 18, 14),
            BackColor = Theme.Background,
        };

        footer.Controls.Add(close);

        Controls.Add(host);
        Controls.Add(footer);

        AcceptButton = close;
        CancelButton = close;
    }

    private static Label BuildHeader(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        AutoSize = true,
        Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        ForeColor = Theme.TextSecondary,
        Margin = new Padding(2, 14, 0, 6),
    };

    private static TableLayoutPanel BuildRows((string Keys, string Action)[] rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = rows.Length,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            BackColor = Theme.Background,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        for (var i = 0; i < rows.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            table.Controls.Add(new Label
            {
                Text = rows[i].Keys,
                AutoSize = true,
                Font = new Font("Consolas", 9.5f),
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(2, 4, 8, 4),
            }, 0, i);

            table.Controls.Add(new Label
            {
                Text = rows[i].Action,
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 4, 2, 4),
            }, 1, i);
        }

        return table;
    }
}
