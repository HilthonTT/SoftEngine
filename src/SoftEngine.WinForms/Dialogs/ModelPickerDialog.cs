using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Dialogs;

/// <summary>A built-in demo world, or a model file somewhere on the machine.</summary>
internal sealed record ModelChoice(string? DemoId, string? FilePath);

/// <summary>One entry of the bundled world list.</summary>
internal sealed record DemoEntry(string Display, string Id);

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 460);
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
        _list.SelectedIndex = System.Math.Clamp(selected, 0, System.Math.Max(0, demos.Count - 1));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = Theme.Background,
        };

        var load = MakeButton("Load", accent: true);
        load.Click += (s, e) => Accept();

        var cancel = MakeButton("Cancel");
        cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        var browse = MakeButton("Open file from my PC…");
        browse.Width = 180;
        browse.Click += (s, e) => Browse();

        buttons.Controls.Add(load);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(browse);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6) };
        content.Controls.Add(_list);
        content.Controls.Add(header);

        Controls.Add(content);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 0, 14, 12), Controls = { buttons } });

        AcceptButton = load;
        CancelButton = cancel;
    }

    /// <summary>What the user picked, or null when the dialog was cancelled.</summary>
    public ModelChoice? Choice { get; private set; }

    private Button MakeButton(string text, bool accent = false) => new()
    {
        Text = text,
        Width = 100,
        Height = 30,
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
            Filter = "3D models (*.obj;*.dae)|*.obj;*.dae"
                   + "|Wavefront OBJ (*.obj)|*.obj"
                   + "|Collada (*.dae)|*.dae"
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
