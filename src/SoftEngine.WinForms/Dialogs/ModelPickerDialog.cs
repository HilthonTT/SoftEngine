using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Dialogs;

/// <summary>
/// Picks what to render: one of the bundled demo worlds, or any OBJ/Collada file the
/// user browses to. Replaces the sidebar list, which had no room left once the
/// debugger panels moved in.
/// </summary>
internal sealed class ModelPickerDialog : Form
{
    private readonly ListBox _list;

    public ModelPickerDialog(IReadOnlyList<DemoEntry> demos, string? currentId)
    {
        Text = "Load model";
        StartPosition = FormStartPosition.CenterParent;

        // Sizable rather than fixed: the list is the point of the dialog, and a longer one
        // is worth more room. The minimum keeps the button row from ever being squeezed.
        FormBorderStyle = FormBorderStyle.Sizable;
        SizeGripStyle = SizeGripStyle.Show;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(480, 560);
        MinimumSize = new Size(440, 380);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9.75f);

        var header = new Label
        {
            Text = "Bundled worlds",
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(4, 8, 0, 0),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Theme.TextSecondary,
        };

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 32,
            IntegralHeight = false,
        };

        _list.DrawItem += DrawItem;
        _list.DoubleClick += (s, e) => Accept();

        foreach (var demo in demos)
        {
            _list.Items.Add(demo);
        }

        var selected = demos.Select((demo, index) => (demo, index)).FirstOrDefault(pair => pair.demo.Id == currentId).index;
        _list.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, demos.Count - 1));

        var load = MakeButton("Load", accent: true);
        load.Click += (s, e) => Accept();

        var cancel = MakeButton("Cancel");
        cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        var browse = MakeButton("Open file from my PC…");
        browse.Click += (s, e) => Browse();

        // Browse sits on the left and the two verbs on the right, each in its own flow
        // panel. Sharing one row is what used to wrap the widest button onto a second line
        // and then clip it — this way the row's width never decides what stays visible.
        var confirm = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
        };

        confirm.Controls.Add(load);
        confirm.Controls.Add(cancel);

        var alternatives = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
        };

        alternatives.Controls.Add(browse);

        var buttons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            Padding = new Padding(14, 12, 14, 14),
            BackColor = Theme.Background,
        };

        buttons.Controls.Add(confirm);
        buttons.Controls.Add(alternatives);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6) };
        content.Controls.Add(_list);
        content.Controls.Add(header);

        Controls.Add(content);
        Controls.Add(buttons);

        AcceptButton = load;
        CancelButton = cancel;
    }

    /// <summary>What the user picked, or null when the dialog was cancelled.</summary>
    public ModelChoice? Choice { get; private set; }

    // Sized from its own text, so a longer label widens the button instead of being cut off.
    private Button MakeButton(string text, bool accent = false) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(100, 32),
        Padding = new Padding(14, 4, 14, 4),
        FlatStyle = FlatStyle.Flat,
        BackColor = accent ? Theme.Accent : Theme.Surface,
        ForeColor = accent ? Color.White : Theme.TextPrimary,
        Margin = new Padding(6, 0, 0, 0),
        UseVisualStyleBackColor = false,
    };

    private void Accept()
    {
        if (_list.SelectedItem is not DemoEntry demo)
        {
            return;
        }

        Choice = new ModelChoice(demo.Id, null);
        DialogResult = DialogResult.OK;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open 3D model",
            Filter = "3D models (*.obj;*.dae;*.gltf;*.glb)|*.obj;*.dae;*.gltf;*.glb"
                   + "|Wavefront OBJ (*.obj)|*.obj"
                   + "|Collada (*.dae)|*.dae"
                   + "|glTF 2.0 (*.gltf;*.glb)|*.gltf;*.glb"
                   + "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        Choice = new ModelChoice(null, dialog.FileName);
        DialogResult = DialogResult.OK;
    }

    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count)
        {
            return;
        }

        var demo = (DemoEntry)_list.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;

        using var back = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(back, e.Bounds);

        var bounds = Rectangle.Inflate(e.Bounds, -2, -2);
        if (selected)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Theme.Selection);
            using var path = Theme.RoundedRect(bounds, 6);
            e.Graphics.FillPath(fill, path);

            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, bounds.Left + 2, bounds.Top + 6, 3, bounds.Height - 12);
        }

        TextRenderer.DrawText(
            e.Graphics,
            demo.Display,
            _list.Font,
            new Rectangle(bounds.Left + 14, bounds.Top, bounds.Width - 14, bounds.Height),
            selected ? Theme.TextPrimary : Theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
