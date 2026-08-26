using SoftEngine.Core.Editing;
using SoftEngine.Core.Gizmos;

namespace SoftEngine.WinForms;

/// <summary>
/// Changing the scene rather than looking at it: the transform gizmo, the snapping that makes a
/// drag land on a number worth keeping, and the history that makes trying one cheap.
///
/// <para>
/// The three are one concern and not three. A gizmo without undo is a control you can only
/// commit with, and snapping is what gives an undoable drag somewhere to land — see
/// <see cref="InitializeEditing"/>. <c>MainScreenPrimitives.cs</c> is the other half of it: what
/// there is to drag once you have added something.
/// </para>
///
/// <para>
/// Named <c>MainScreenEditing.cs</c> rather than <c>MainScreen.Editing.cs</c> for the reason
/// spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/> invites
/// Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    /// <summary>The gizmo the viewport draws and drags. One object, so what is drawn is what is grabbed.</summary>
    private readonly TransformGizmo _gizmo = new();

    /// <summary>Completed drags, so they can be taken back. Cleared whenever a world is replaced.</summary>
    private readonly EditHistory _history = new();

    /// <summary>
    /// Fills the transform-gizmo selector and attaches the gizmo to whatever is picked.
    ///
    /// The gizmo needs a target and picking already produces one, so the two are wired
    /// together rather than given separate selections — clicking a mesh is how you say which
    /// mesh the handles belong to, and it is the gesture that already means that.
    /// </summary>
    private void InitializeGizmo()
    {
        cboGizmo.Items.AddRange(
        [
            new GizmoChoice("Off", GizmoMode.Off),
            new GizmoChoice("Move", GizmoMode.Translate),
            new GizmoChoice("Rotate", GizmoMode.Rotate),
            new GizmoChoice("Scale", GizmoMode.Scale),
        ]);

        cboGizmo.SelectedIndex = 0;

        panel3D1.Gizmo = _gizmo;

        cboGizmo.SelectedIndexChanged += (s, e) =>
        {
            if (cboGizmo.SelectedItem is GizmoChoice choice)
            {
                _gizmo.Mode = choice.Mode;
                panel3D1.Invalidate();
            }
        };

        panel3D1.PickedChanged += (s, e) =>
        {
            _gizmo.Target = panel3D1.Picked?.Mesh;
            panel3D1.Invalidate();
        };

        panel3D1.GizmoChanged += (s, e) => UpdateStatus();

        InitializeEditing();
    }

    /// <summary>
    /// Wires the edit history and the snapping toggle.
    ///
    /// <para>
    /// The two belong together: snapping is what makes a drag land on a number worth keeping,
    /// and undo is what makes trying one cheap. A gizmo without either is a control you can only
    /// commit with.
    /// </para>
    /// </summary>
    private void InitializeEditing()
    {
        panel3D1.History = _history;

        _history.Changed += (s, e) => UpdateEditMenu();

        // The gesture goes first. Ctrl+Z is a menu shortcut and so is dispatched before the
        // viewport's own key handling, which means it arrives even mid-drag — and undoing onto a
        // mesh that a half-finished gesture is still writing to would leave the history's version
        // of the transform and the mesh's disagreeing.
        mnuUndo.Click += (s, e) =>
        {
            panel3D1.CancelTransform();
            StepHistory(_history.Undo());
        };

        mnuRedo.Click += (s, e) =>
        {
            panel3D1.CancelTransform();
            StepHistory(_history.Redo());
        };

        // Two controls for one setting, because they answer different questions: the sidebar
        // checkbox is next to the gizmo selector and so is where you look for it, and the menu
        // item is where the keyboard shortcut can live.
        chkSnap.CheckedChanged += (s, e) => ApplySnapping(chkSnap.Checked);
        mnuSnap.CheckedChanged += (s, e) => ApplySnapping(mnuSnap.Checked);

        InitializePrimitives();

        UpdateEditMenu();
    }

    /// <summary>
    /// Turns snapping on or off, keeping the two controls that say so in agreement. Each one
    /// writes through the other, so the guard is what stops the pair ringing.
    /// </summary>
    private void ApplySnapping(bool enabled)
    {
        if (_gizmo.Snap.Enabled == enabled && chkSnap.Checked == enabled && mnuSnap.Checked == enabled)
        {
            return;
        }

        _gizmo.Snap.Enabled = enabled;
        chkSnap.Checked = enabled;
        mnuSnap.Checked = enabled;

        UpdateStatus();
    }

    /// <summary>
    /// Follows an undo or a redo: the mesh it changed becomes the selection, so the handles are on
    /// the thing that just moved rather than wherever they were left. Nothing happens when the
    /// stack was empty, which is the case the menu items are greyed out for anyway.
    /// </summary>
    private void StepHistory(IEditCommand? command)
    {
        switch (command)
        {
            case null:
                return;

            case TransformEdit edit:
                _gizmo.Target = edit.Mesh;
                break;

            // A mesh that has just left the world cannot stay selected: the pick addresses it by
            // its position in the world's mesh list, and that position now holds something else
            // or nothing at all. Reselecting one that has come back is the same rule the other
            // way round — either way the selection ends up on what the step changed.
            case MeshListEdit list:
                panel3D1.SelectMesh(list.Mesh);
                panel3D1.ResetTemporalHistory();
                break;
        }

        UpdateStatus();
        panel3D1.Invalidate();
    }

    /// <summary>
    /// Re-labels the two menu items from the stacks. Naming the edit — "Undo Move Cube" — is
    /// what tells you whether the next Ctrl+Z is the one you meant before you press it.
    /// </summary>
    private void UpdateEditMenu()
    {
        mnuUndo.Enabled = _history.CanUndo;
        mnuRedo.Enabled = _history.CanRedo;

        mnuUndo.Text = _history.NextUndo is { } undo ? $"&Undo {undo}" : "&Undo";
        mnuRedo.Text = _history.NextRedo is { } redo ? $"&Redo {redo}" : "&Redo";
    }

    /// <summary>
    /// Scales the snap increments to the world just loaded. A grid step is a world distance and
    /// the demos span three orders of magnitude of them: one unit is a sensible grid on a 2-unit
    /// skull and a meaningless one on a 1500-unit elephant, where a drag would snap to the same
    /// place it started from every time. The rotation step is an angle and needs no such help.
    /// </summary>
    private void ApplySnapScale()
    {
        var reference = panel3D1.ReferenceDistance;

        if (reference <= 0f)
        {
            return;
        }

        // A round number near a fiftieth of the framing distance, so the step is always
        // something a person would have typed: 0.1, 1, 10, 100.
        var rough = reference * 0.02f;
        var magnitude = MathF.Pow(10f, MathF.Round(MathF.Log10(rough)));

        _gizmo.Snap.TranslateStep = MathF.Max(magnitude, 0.001f);
    }

    private sealed record GizmoChoice(string Label, GizmoMode Mode)
    {
        public override string ToString() => Label;
    }
}
