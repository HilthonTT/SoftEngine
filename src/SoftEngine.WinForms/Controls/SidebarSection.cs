namespace SoftEngine.WinForms.Controls;

internal sealed class SidebarSection
{
    private const string ExpandedChevron = "▾";
    private const string CollapsedChevron = "▸";

    private readonly Label _header;
    private readonly Control _content;
    private readonly ToolStripMenuItem _menuItem;
    private readonly string _title;

    private bool _syncing;

    public SidebarSection(Label header, Control content, ToolStripMenuItem menuItem)
    {
        _header = header;
        _content = content;
        _menuItem = menuItem;
        _title = header.Text;

        _header.Cursor = Cursors.Hand;
        _header.Click += (s, e) => Expanded = !Expanded;

        _header.MouseEnter += (s, e) => _header.ForeColor = Theme.Accent;
        _header.MouseLeave += (s, e) => _header.ForeColor = Theme.TextSecondary;

        _menuItem.CheckedChanged += (s, e) => Expanded = _menuItem.Checked;

        Apply();
    }

    public bool Expanded
    {
        get => _content.Visible;
        set
        {
            if (_syncing || _content.Visible == value)
            {
                return;
            }

            _content.Visible = value;
            Apply();
        }
    }

    private void Apply()
    {
        _syncing = true;

        try
        {
            _header.Text = $"{(_content.Visible ? ExpandedChevron : CollapsedChevron)}  {_title}";
            _menuItem.Checked = _content.Visible;
        }
        finally
        {
            _syncing = false;
        }
    }
}
