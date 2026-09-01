namespace SoftEngine.Core.Editing;

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

    public IReadOnlyList<IEditCommand> Edits => _edits;

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
