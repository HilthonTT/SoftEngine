using SoftEngine.Core.Editing;
using SoftEngine.Core.Gizmos;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private readonly TransformGizmo _gizmo = new();

    private readonly EditHistory _history = new();

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

    private void InitializeEditing()
    {
        panel3D1.History = _history;

        _history.Changed += (s, e) => UpdateEditMenu();

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

        chkSnap.CheckedChanged += (s, e) => ApplySnapping(chkSnap.Checked);
        mnuSnap.CheckedChanged += (s, e) => ApplySnapping(mnuSnap.Checked);

        InitializePrimitives();

        UpdateEditMenu();
    }

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

    private void StepHistory(IEditCommand? command)
    {
        switch (command)
        {
            case null:
                return;

            case TransformEdit edit:
                _gizmo.Target = edit.Mesh;
                break;

            case MeshListEdit list:
                panel3D1.SelectMesh(list.Mesh);
                panel3D1.ResetTemporalHistory();
                break;
        }

        UpdateStatus();
        panel3D1.Invalidate();
    }

    private void UpdateEditMenu()
    {
        mnuUndo.Enabled = _history.CanUndo;
        mnuRedo.Enabled = _history.CanRedo;

        mnuUndo.Text = _history.NextUndo is { } undo ? $"&Undo {undo}" : "&Undo";
        mnuRedo.Text = _history.NextRedo is { } redo ? $"&Redo {redo}" : "&Redo";
    }

    private void ApplySnapScale()
    {
        var reference = panel3D1.ReferenceDistance;

        if (reference <= 0f)
        {
            return;
        }

        var rough = reference * 0.02f;
        var magnitude = MathF.Pow(10f, MathF.Round(MathF.Log10(rough)));

        _gizmo.Snap.TranslateStep = MathF.Max(magnitude, 0.001f);
    }

    private sealed record GizmoChoice(string Label, GizmoMode Mode)
    {
        public override string ToString() => Label;
    }
}
