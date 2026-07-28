namespace SoftEngine.Core.Editing;

/// <summary>
/// The undo and redo stacks for edits made in the viewport.
///
/// <para>
/// A gizmo without this is a demo rather than a tool. Dragging is an <em>estimating</em>
/// gesture — you push a mesh, look at it, and push it back — and without a way to get the
/// previous answer back, every attempt is a commitment. That is also why the entries are whole
/// transforms rather than deltas: undo has to be exact, and a chain of accumulated
/// floating-point deltas does not return to where it started.
/// </para>
/// </summary>
public sealed class EditHistory
{
    // Lists rather than Stack<T>: an over-long history is trimmed from the *bottom*, which is
    // the oldest edit, and a stack can only be popped from the top.
    private readonly List<IEditCommand> _done = [];
    private readonly List<IEditCommand> _undone = [];

    /// <summary>
    /// How many edits are kept before the oldest is dropped. Commands hold a reference to the
    /// mesh they moved, so an unbounded history is also an unbounded reason for a model to stay
    /// loaded after the world that held it was replaced.
    /// </summary>
    public int Capacity { get; set; } = 128;

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>Description of the edit <see cref="Undo"/> would reverse, for a menu label.</summary>
    public string? NextUndo => CanUndo ? _done[^1].Description : null;

    /// <summary>Description of the edit <see cref="Redo"/> would replay, for a menu label.</summary>
    public string? NextRedo => CanRedo ? _undone[^1].Description : null;

    /// <summary>Raised whenever either stack changes, so a front-end can re-label its menu.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Records an edit that has <em>already been applied</em> — the gizmo moved the mesh as the
    /// drag went along, and the command exists to reverse it, not to perform it.
    ///
    /// <para>
    /// Null is accepted and ignored, so a caller can hand over the result of
    /// <see cref="TransformEdit.Between"/> without first asking whether anything happened.
    /// </para>
    /// </summary>
    public void Push(IEditCommand? command)
    {
        if (command is null)
        {
            return;
        }

        _done.Add(command);

        // A new edit is a new branch, and the future that was undone is no longer reachable
        // from it. Keeping those entries would let redo apply a change on top of a state it was
        // never recorded against.
        _undone.Clear();

        while (_done.Count > System.Math.Max(1, Capacity))
        {
            _done.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reverses the most recent edit. Returns it, or null when there was nothing to undo.</summary>
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

    /// <summary>Replays the most recently undone edit. Returns it, or null when there was nothing to redo.</summary>
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

    /// <summary>
    /// Forgets everything. Loading a world must call this: the commands point at meshes that
    /// are no longer in the scene, and undoing one would silently move an object nothing draws.
    /// </summary>
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
