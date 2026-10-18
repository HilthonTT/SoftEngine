namespace SoftEngine.WinForms.Debugging;

internal sealed class GraphicsObjectTablePanel : UserControl
{
    private readonly ListView _list;

    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;
    private string _builtSignature = string.Empty;

    public GraphicsObjectTablePanel()
    {
        BackColor = Theme.Surface;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            VirtualMode = true,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            Font = new Font("Consolas", 8.5f),
        };

        _list.Columns.Add("Identifier", 80);
        _list.Columns.Add("Type", 150);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Vertices", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Triangles", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Width", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Height", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Detail", 200);

        _list.RetrieveVirtualItem += RetrieveVirtualItem;
        _list.ItemCheck += ItemCheck;
        _list.ItemChecked += ItemChecked;

        Controls.Add(_list);
        Controls.Add(new DockPanelHeader("Graphics Object Table"));
    }

    public event EventHandler? ActiveChanged;

    public SceneObjectCatalog Catalog => _catalog;

    public void SetCatalog(SceneObjectCatalog catalog)
    {
        _catalog = catalog;

        if (_builtSignature == catalog.Signature)
        {
            return;
        }

        _builtSignature = catalog.Signature;

        _list.SelectedIndices.Clear();
        if (_list.VirtualListSize != catalog.Rows.Count)
        {
            _list.VirtualListSize = catalog.Rows.Count;
        }
        _list.Invalidate();
    }

    public void SelectObject(int objectId)
    {
        var rows = _catalog.Rows;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == objectId)
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(i);
                _list.EnsureVisible(i);
                return;
            }
        }
    }

    private void RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_catalog.Rows.Count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        var row = _catalog.Rows[e.ItemIndex];

        var item = new ListViewItem(row.Identifier)
        {
            Checked = row.Active,
            Tag = row,
            ForeColor = row.Mesh is null ? Theme.TextSecondary : Theme.TextPrimary,
        };

        item.SubItems.Add(row.Type);
        item.SubItems.Add(SceneObjectCatalog.FormatSize(row.SizeBytes));
        item.SubItems.Add(row.VertexCount == 0 ? "—" : row.VertexCount.ToString("N0"));
        item.SubItems.Add(row.TriangleCount == 0 ? "—" : row.TriangleCount.ToString("N0"));
        item.SubItems.Add(row.Width == 0 ? "—" : row.Width.ToString());
        item.SubItems.Add(row.Height == 0 ? "—" : row.Height.ToString());
        item.SubItems.Add(row.Detail);

        e.Item = item;
    }

    private void ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if ((uint)e.Index >= (uint)_catalog.Rows.Count)
        {
            return;
        }

        if (!_catalog.Rows[e.Index].CanToggle)
        {
            e.NewValue = e.CurrentValue;
        }
    }

    private void ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (e.Item.Tag is not SceneObjectRow { Mesh: { } mesh } || mesh.Visible == e.Item.Checked)
        {
            return;
        }

        mesh.Visible = e.Item.Checked;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
