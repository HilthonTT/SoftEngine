namespace SoftEngine.WinForms.Controls;

/// <summary>
/// A sidebar heading that rolls its section up.
///
/// <para>
/// The sidebar is about 1230 pixels tall and its pane is about 470, so two thirds of it has always
/// been below the fold — and the three flow panels are nearly 800 of those pixels. Rolling one up
/// is the difference between scrolling to reach the shading radios and having them on screen.
/// </para>
///
/// <para>
/// Hiding the content control is all it takes: the sidebar is a <see cref="TableLayoutPanel"/> with
/// auto-sized rows, and a row whose only control is invisible has no height. Nothing has to be
/// measured or moved.
/// </para>
///
/// <para>
/// A <see cref="Label"/> cannot take focus, so the heading alone would put this out of reach of
/// anybody working from the keyboard. That is what the paired menu item is for — the two drive the
/// same state, and <c>_syncing</c> is what stops each one's change notification writing back
/// through the other.
/// </para>
/// </summary>
internal sealed class SidebarSection
{
    // Escaped rather than written literally: this file has no byte-order mark, and a chevron is
    // not worth depending on every tool in the chain guessing UTF-8 correctly.
    private const string ExpandedChevron = "▾";  // BLACK DOWN-POINTING SMALL TRIANGLE
    private const string CollapsedChevron = "▸"; // BLACK RIGHT-POINTING SMALL TRIANGLE

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

        // The heading is the only affordance there is, so it has to answer the pointer — a
        // caption that does nothing on hover reads as a caption.
        _header.MouseEnter += (s, e) => _header.ForeColor = Theme.Accent;
        _header.MouseLeave += (s, e) => _header.ForeColor = Theme.TextSecondary;

        _menuItem.CheckedChanged += (s, e) => Expanded = _menuItem.Checked;

        Apply();
    }

    /// <summary>Whether the section's controls are on screen.</summary>
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

    /// <summary>
    /// Brings the heading and the menu item into agreement with the content's own visibility,
    /// which is where this state actually lives.
    /// </summary>
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
