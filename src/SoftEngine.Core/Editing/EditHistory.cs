namespace SoftEngine.Core.Editing;

public sealed class EditHistory
{
    private readonly List<IEditCommand> _done = [];
    private readonly List<IEditCommand> _undone = [];

    public int Capacity { get; set; } = 128;

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    public string? NextUndo => CanUndo ? _done[^1].Description : null;

    public string? NextRedo => CanRedo ? _undone[^1].Description : null;

    public event EventHandler? Changed;

    public void Push(IEditCommand? command)
    {
        if (command is null)
        {
            return;
        }

        _done.Add(command);

        _undone.Clear();

        while (_done.Count > System.Math.Max(1, Capacity))
        {
            _done.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IEditCommand? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        var command = _done[^1];
        _done.RemoveAt(_done.Count - 1);

        command.Revert();
        _undone.Add(command);

        Changed?.Invoke(this, EventArgs.Empty);

        return command;
    }

    public IEditCommand? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var command = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);

        command.Apply();
        _done.Add(command);

        Changed?.Invoke(this, EventArgs.Empty);

        return command;
    }

    public void Clear()
    {
        if (_done.Count == 0 && _undone.Count == 0)
        {
            return;
        }

        _done.Clear();
        _undone.Clear();

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
