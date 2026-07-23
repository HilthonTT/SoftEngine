using SoftEngine.Core.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace SoftEngine.WinForms.Debugging;

/// <summary>
/// The pixel history: every write attempt the frame made at the selected pixel — clears,
/// triangles the depth test accepted, and the ones it rejected — with the vertex data and
/// the colour blend behind each one.
/// </summary>
internal sealed class PixelHistoryPanel : UserControl
{
    private readonly Panel _summary;
    private readonly TreeView _tree;
    private readonly ImageList _swatches;
    private readonly Dictionary<int, int> _swatchIndices = [];

    private PixelHistory? _history;
    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;
    private long _shownFrame = -1;

    public PixelHistoryPanel()
    {
        BackColor = Theme.Surface;

        _swatches = new ImageList
        {
            ImageSize = new Size(14, 14),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            LineColor = Theme.TextSecondary,
            Font = new Font("Consolas", 8.5f),
            ImageList = _swatches,
            ShowRootLines = true,
            HideSelection = false,
            ItemHeight = 18,
        };

        _tree.AfterSelect += (s, e) =>
        {
            if (FindWrite(e.Node) is { } write)
            {
                WriteSelected?.Invoke(this, write);
            }
        };

        _summary = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = Theme.Surface,
        };
        _summary.Paint += PaintSummary;

        Controls.Add(_tree);
        Controls.Add(_summary);
        Controls.Add(new DockPanelHeader("Pixel History"));
    }

    /// <summary>Raised when the user picks a write, so the event list can jump to its event.</summary>
    public event EventHandler<PixelWrite>? WriteSelected;

    /// <summary>
    /// Shows the history of the frame just rendered. Rebuilds the tree once per frame,
    /// and does nothing while no pixel is selected.
    /// </summary>
    public void SetHistory(PixelHistory? history, SceneObjectCatalog catalog)
    {
        _catalog = catalog;

        if (ReferenceEquals(history, _history) || (history is not null && history.FrameNumber == _shownFrame))
        {
            return;
        }

        _history = history;
        _shownFrame = history?.FrameNumber ?? -1;

        _summary.Invalidate();
        BuildTree();
    }

    private void BuildTree()
    {
        _tree.BeginUpdate();

        try
        {
            _tree.Nodes.Clear();
            _swatches.Images.Clear();
            _swatchIndices.Clear();

            if (_history is null)
            {
                _tree.Nodes.Add(new TreeNode("Click a pixel in the viewport to capture its history.")
                {
                    ForeColor = Theme.TextSecondary,
                });
                return;
            }

            if (_history.Writes.Count == 0)
            {
                _tree.Nodes.Add(new TreeNode("Nothing wrote this pixel.") { ForeColor = Theme.TextSecondary });
                return;
            }

            foreach (var write in _history.Writes)
            {
                _tree.Nodes.Add(BuildWriteNode(write));
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private TreeNode BuildWriteNode(PixelWrite write)
    {
        var label = write.EventIndex >= 0 ? $"[{write.EventIndex}] " : string.Empty;
        label += write.Source switch
        {
            PixelWriteSource.Clear => "ClearRenderTarget",
            PixelWriteSource.Triangle => $"DrawTriangle {_catalog.Describe(write.ObjectId)} tri:{write.TriangleIndex}",
            PixelWriteSource.WireFrame => $"WireFrame {_catalog.Describe(write.ObjectId)} tri:{write.TriangleIndex}",
            PixelWriteSource.Grid => "GizmoDrawGrid",
            PixelWriteSource.Axes => "GizmoDrawAxes",
            _ => "Write",
        };

        if (!write.Passed)
        {
            label += "  — depth rejected";
        }

        var node = new TreeNode(label)
        {
            Tag = write,
            ForeColor = write.Passed ? Theme.TextPrimary : Theme.TextSecondary,
            ImageIndex = SwatchFor(write.Color),
            SelectedImageIndex = SwatchFor(write.Color),
        };

        if (write.Vertices is { Length: 3 } vertices)
        {
            var assembler = node.Nodes.Add("Input Assembler");
            for (var i = 0; i < 3; i++)
            {
                AddDetail(assembler.Nodes.Add($"Vertex {i}"), "MODEL", vertices[i].Model);
            }

            var transform = node.Nodes.Add("Vertex Transform");
            for (var i = 0; i < 3; i++)
            {
                var vertex = transform.Nodes.Add($"Vertex {i}");
                AddDetail(vertex, "WORLD", vertices[i].World);
                AddDetail(vertex, "VIEW", vertices[i].View);
                AddDetail(vertex, "NORMAL", vertices[i].Normal);
                AddDetail(vertex, "SV_POSITION", vertices[i].Projection);
            }
        }

        var depth = node.Nodes.Add("Depth Test");
        depth.Nodes.Add($"incoming  {Depth(write.Depth)}");
        depth.Nodes.Add($"z-buffer  {Depth(write.PreviousDepth)}");
        depth.Nodes.Add(write.Passed ? "result    passed" : "result    rejected");

        var merger = node.Nodes.Add("Output Merger");
        AddColor(merger, "Previous", write.PreviousColor);
        AddColor(merger, "Result", write.Passed ? write.Color : write.PreviousColor);

        if (!write.Passed)
        {
            AddColor(merger, "Discarded", write.Color);
        }

        node.Expand();
        merger.Expand();

        return node;
    }

    private void AddColor(TreeNode parent, string label, int argb)
    {
        var color = Color.FromArgb(argb);
        var node = parent.Nodes.Add($"{label}  R:{Channel(color.R)} G:{Channel(color.G)} B:{Channel(color.B)} A:{Channel(color.A)}");
        node.ImageIndex = SwatchFor(argb);
        node.SelectedImageIndex = node.ImageIndex;
    }

    private static void AddDetail(TreeNode parent, string label, Vector3 value) =>
        parent.Nodes.Add($"{label,-12} x={value.X,-12:0.#####} y={value.Y,-12:0.#####} z={value.Z:0.#####}");

    private static void AddDetail(TreeNode parent, string label, Vector4 value) =>
        parent.Nodes.Add($"{label,-12} x={value.X,-12:0.#####} y={value.Y,-12:0.#####} z={value.Z,-12:0.#####} w={value.W:0.#####}");

    private static string Channel(byte value) =>
        (value / 255f).ToString("0.000000", CultureInfo.InvariantCulture);

    private static string Depth(int depth) =>
        PixelWrite.Normalize(depth).ToString("0.000000", CultureInfo.InvariantCulture);

    private static PixelWrite? FindWrite(TreeNode? node)
    {
        while (node is not null)
        {
            if (node.Tag is PixelWrite write)
            {
                return write;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>Colour chips are drawn once per distinct colour and reused across the tree.</summary>
    private int SwatchFor(int argb)
    {
        if (_swatchIndices.TryGetValue(argb, out var index))
        {
            return index;
        }

        // ImageList.Images.Add copies the bitmap, so this one is disposed when done.
        using var bitmap = new Bitmap(_swatches.ImageSize.Width, _swatches.ImageSize.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            using var fill = new SolidBrush(Opaque(argb));
            g.FillRectangle(fill, 1, 1, bitmap.Width - 2, bitmap.Height - 2);

            using var border = new Pen(Theme.TextSecondary);
            g.DrawRectangle(border, 1, 1, bitmap.Width - 3, bitmap.Height - 3);
        }

        index = _swatches.Images.Count;
        _swatches.Images.Add(bitmap);
        _swatchIndices.Add(argb, index);

        return index;
    }

    private void PaintSummary(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        using var back = new SolidBrush(Theme.Surface);
        g.FillRectangle(back, _summary.ClientRectangle);

        TextRenderer.DrawText(g, "Final Pixel Color", Font, new Point(10, 6), Theme.TextSecondary);

        var swatch = new Rectangle(10, 26, 56, 56);
        var color = _history is null ? Theme.Viewport : Opaque(_history.FinalColor);

        using (var fill = new SolidBrush(color))
        {
            g.FillRectangle(fill, swatch);
        }
        using (var border = new Pen(Theme.TextSecondary))
        {
            g.DrawRectangle(border, swatch);
        }

        if (_history is null)
        {
            TextRenderer.DrawText(g, "No pixel selected", Font, new Point(78, 46), Theme.TextSecondary);
            return;
        }

        var lines = new[]
        {
            $"R: {Channel(color.R)}",
            $"G: {Channel(color.G)}",
            $"B: {Channel(color.B)}",
            $"A: {Channel(color.A)}",
        };

        var font = new Font("Consolas", 8.25f);
        for (var i = 0; i < lines.Length; i++)
        {
            TextRenderer.DrawText(g, lines[i], font, new Point(78, 26 + i * 14), Theme.TextPrimary);
        }

        TextRenderer.DrawText(g, $"Frame:  {_history.FrameNumber}", font, new Point(186, 26), Theme.TextSecondary);
        TextRenderer.DrawText(g, $"Pixel:  {_history.X}, {_history.Y}", font, new Point(186, 40), Theme.TextSecondary);
        TextRenderer.DrawText(g, $"Depth:  {Depth(_history.FinalDepth)}", font, new Point(186, 54), Theme.TextSecondary);
        TextRenderer.DrawText(g, $"Writes: {_history.Writes.Count}", font, new Point(186, 68), Theme.TextSecondary);

        font.Dispose();
    }

    private static Color Opaque(int argb) => Color.FromArgb(255, Color.FromArgb(argb));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _swatches.Dispose();
        }

        base.Dispose(disposing);
    }
}
