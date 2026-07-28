namespace SoftEngine.Core.Editing;

/// <summary>
/// One reversible change to the scene, as <see cref="EditHistory"/> stores it.
///
/// <para>
/// A command records the <em>values</em> on both sides of the change rather than the gesture
/// that produced it. A drag is a hundred mouse positions and a dozen intermediate transforms,
/// and none of that is worth keeping: what undo has to restore is where the mesh was before
/// the drag started, which is one transform. Replaying gestures would also make undo depend on
/// the camera the gesture was made from, which is not a thing the user changed.
/// </para>
/// </summary>
public interface IEditCommand
{
    /// <summary>What the command did, in the words a menu item would use — "Move Cube", say.</summary>
    string Description { get; }

    /// <summary>Applies the change. Called by <see cref="EditHistory.Redo"/>.</summary>
    void Apply();

    /// <summary>Puts back what <see cref="Apply"/> replaced.</summary>
    void Revert();
}
