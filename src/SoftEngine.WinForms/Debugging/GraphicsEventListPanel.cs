using SoftEngine.Core.Diagnostics;

namespace SoftEngine.WinForms.Debugging;

internal sealed class GraphicsEventListPanel : UserControl
{
    private readonly ListView _list;

    private GraphicsEvent[] _events = [];
    private int _count;

    public GraphicsEventListPanel()
    {
        BackColor = Theme.Surface;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            VirtualMode = true,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            Font = new Font("Consolas", 8.5f),
        };

        _list.Columns.Add("#", 44, HorizontalAlignment.Right);
        _list.Columns.Add("Event", 168);
        _list.Columns.Add("Parameters", 320);

        _list.RetrieveVirtualItem += RetrieveVirtualItem;
        _list.SelectedIndexChanged += (s, e) =>
        {
            if (_list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _count)
            {
                EventSelected?.Invoke(this, _events[_list.SelectedIndices[0]]);
            }
        };

        Controls.Add(_list);
        Controls.Add(new DockPanelHeader("Graphics Event List"));
    }

    public event EventHandler<GraphicsEvent>? EventSelected;

    public void SetEvents(GraphicsEventLog log) => SetEvents(log.AsSpan());

    public void SetEvents(ReadOnlySpan<GraphicsEvent> source)
    {
        if (_events.Length < source.Length)
        {
            _events = new GraphicsEvent[Math.Max(source.Length, 256)];
        }

        source.CopyTo(_events);
        _count = source.Length;

        if (_list.VirtualListSize != _count)
        {
            _list.VirtualListSize = _count;
        }

        _list.Invalidate();
    }

    public void SelectEvent(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            return;
        }

        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(index);
        _list.EnsureVisible(index);
    }

    private void RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        var graphicsEvent = _events[e.ItemIndex];

        var item = new ListViewItem(e.ItemIndex.ToString())
        {
            ForeColor = ColorOf(graphicsEvent.Kind),
        };

        item.SubItems.Add(graphicsEvent.Name);
        item.SubItems.Add(graphicsEvent.Describe());

        e.Item = item;
    }

    private static Color ColorOf(GraphicsEventKind kind) => kind switch
    {
        GraphicsEventKind.PainterDrawTriangles => Theme.Accent,
        GraphicsEventKind.FrameBegin or GraphicsEventKind.FramePresent => Color.FromArgb(226, 192, 141),
        GraphicsEventKind.MeshSkipInactive or GraphicsEventKind.MeshCullBoundingSphere => Theme.TextSecondary,
        _ => Theme.TextPrimary,
    };
}
