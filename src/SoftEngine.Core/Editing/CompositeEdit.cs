namespace SoftEngine.Core.Editing;

/// <summary>
/// Several edits that happened together, and have to be undone together.
///
/// <para>
/// What makes this necessary is multiple selection. Dragging four meshes at once is four changes to
/// four transforms, and pushing them separately would mean four presses of Ctrl+Z to put back one
/// gesture — with the scene passing through three states the user never made on the way.
/// </para>
///
/// <para>
/// Reverting walks backwards. For independent edits the order does not matter; for edits that touch
/// the same thing it is the only order that can be right, and it costs nothing to always be right.
/// </para>
/// </summary>
public sealed class CompositeEdit : IEditCommand
{
    private readonly IEditCommand[] _edits;

    public CompositeEdit(IReadOnlyList<IEditCommand> edits, string description)
    {
        ArgumentNullException.ThrowIfNull(edits, nameof(edits));

        if (edits.Count == 0)
        {
            throw new ArgumentException("A composite edit needs something to compose.", nameof(edits));
        }

        _edits = [.. edits];
        Description = description;
    }

    public string Description { get; }

    /// <summary>The edits this groups, in the order they were made.</summary>
    public IReadOnlyList<IEditCommand> Edits => _edits;

    /// <summary>
    /// One command for a set of edits: the edit itself when there is one, a composite when there are
    /// several, and null when there are none — which is what a drag that moved nothing produces, and
    /// what <see cref="EditHistory.Push"/> already knows to ignore.
    /// </summary>
    public static IEditCommand? Combine(IReadOnlyList<IEditCommand?> edits, string description)
    {
        ArgumentNullException.ThrowIfNull(edits, nameof(edits));

        var real = new List<IEditCommand>(edits.Count);

        foreach (var edit in edits)
        {
            if (edit is not null)
            {
                real.Add(edit);
            }
        }

        return real.Count switch
        {
            0 => null,
            1 => real[0],
            _ => new CompositeEdit(real, description),
        };
    }

    public void Apply()
    {
        foreach (var edit in _edits)
        {
            edit.Apply();
        }
    }

    public void Revert()
    {
        for (var i = _edits.Length - 1; i >= 0; i--)
        {
            _edits[i].Revert();
        }
    }
}
