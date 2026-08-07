using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

/// <summary>
/// Blender's <em>modal</em> transform: a keystroke starts it, the bare cursor drives it, and a
/// click confirms or Escape throws it away. G moves, S scales; X, Y or Z presses the gesture flat
/// against one world axis, and pressing the same key again lets it go again.
///
/// <para>
/// The difference from <see cref="TransformGizmo"/> is not the arithmetic — they share
/// <see cref="GizmoMath"/> — but what is being aimed at. The gizmo needs a handle drawn on screen
/// and a ray that hits it, which costs a round trip to find something small and a button held down
/// the whole way. This needs neither: the mesh is already chosen, so the gesture can start under
/// the cursor wherever it happens to be. That is why a modelling program has both, and why the
/// keyboard one is the one people end up using.
/// </para>
///
/// <para>
/// Every step is measured from where the gesture <em>began</em> rather than from the step before
/// it, which is what makes constraining to an axis half way through work at all: the constraint
/// re-reads the same original grab through a different projection, so the mesh lands where it
/// would have if the axis had been named at the start, instead of jumping by however far it had
/// travelled off-axis.
/// </para>
/// </summary>
public sealed class ModalTransform
{
    private TransformState _start;

    // The mesh's world origin, the plane the free gesture happens in, and the ray that started
    // it. The ray is kept rather than the pixel because re-deriving it needs a scene, and a
    // constraint can arrive from a keystroke that has none to hand.
    private Vector3 _origin;
    private Vector3 _normal;
    private Ray _grabRay;

    // Where the grab ray crossed that plane, how far that was from the origin, and the world
    // length one gizmo handle would have had here — the yardstick a scale is measured against.
    private Vector3 _grabPoint;
    private float _grabRadius;
    private float _grabScale = 1f;

    /// <summary>The mesh being transformed, or null when no gesture is running.</summary>
    public IMesh? Target { get; private set; }

    /// <summary><see cref="GizmoMode.Off"/> unless a gesture is running.</summary>
    public GizmoMode Mode { get; private set; } = GizmoMode.Off;

    /// <summary>The world axis the gesture is pressed against, or <see cref="GizmoAxis.None"/> for a free one.</summary>
    public GizmoAxis Axis { get; private set; } = GizmoAxis.None;

    public bool IsActive => Mode != GizmoMode.Off && Target is not null;

    /// <summary>
    /// The increments the gesture is quantized to. Settable so a front-end can hand over the same
    /// <see cref="GizmoSnap"/> its gizmo uses — snapping is a property of the scene being built,
    /// not of which of the two tools happens to be building it.
    /// </summary>
    public GizmoSnap Snap { get; set; } = new();

    /// <summary>
    /// Starts a gesture on <paramref name="target"/> from the cursor's current position. Returns
    /// false for a mode that is not a transform, which lets a caller pass a key straight through.
    /// </summary>
    public bool Begin(Scene scene, IMesh target, GizmoMode mode, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));
        ArgumentNullException.ThrowIfNull(target, nameof(target));

        Cancel();

        if (mode == GizmoMode.Off)
        {
            return false;
        }

        _origin = Vector3.Transform(Vector3.Zero, target.WorldMatrix);
        _normal = GizmoMath.ViewDirection(scene);
        _grabRay = ScenePicker.RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f);

        // A plane facing the viewer, so an unconstrained move tracks the cursor whichever way the
        // view has been turned — and edge-on is impossible, since the plane faces the ray.
        if (!GizmoMath.PlanePoint(_grabRay, _origin, _normal, out _grabPoint, out _))
        {
            return false;
        }

        _grabRadius = (_grabPoint - _origin).Length();
        _grabScale = TransformGizmo.HandleScale(scene, _origin);

        _start = TransformState.Of(target);

        Target = target;
        Mode = mode;
        Axis = GizmoAxis.None;

        return true;
    }

    /// <summary>
    /// Presses the gesture against one world axis, or releases it again when that axis is already
    /// the one in force — Blender's "X, X" to go back to a free move. The caller re-runs
    /// <see cref="Update"/> afterwards, since the same cursor now means something else.
    /// </summary>
    public void Constrain(GizmoAxis axis)
    {
        if (!IsActive)
        {
            return;
        }

        Axis = Axis == axis ? GizmoAxis.None : axis;
    }

    /// <summary>Applies the cursor's current position to the target.</summary>
    public void Update(Scene scene, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        if (Target is not { } target)
        {
            return;
        }

        var ray = ScenePicker.RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f);

        switch (Mode)
        {
            case GizmoMode.Translate:
                Translate(target, ray);
                break;

            case GizmoMode.Scale:
                Scale(target, ray);
                break;

            default:
                break;
        }
    }

    private void Translate(IMesh target, in Ray ray)
    {
        Vector3 delta;

        if (Axis == GizmoAxis.None)
        {
            if (!GizmoMath.PlanePoint(ray, _origin, _normal, out var point, out _))
            {
                return;
            }

            delta = point - _grabPoint;
        }
        else
        {
            var direction = TransformGizmo.Direction(Axis);

            if (!GizmoMath.ClosestOnAxis(_grabRay, _origin, direction, out var from) ||
                !GizmoMath.ClosestOnAxis(ray, _origin, direction, out var to))
            {
                return;
            }

            delta = direction * (to - from);
        }

        if (Snap.Enabled)
        {
            // The resulting world position is snapped, not the distance travelled — the same rule
            // the gizmo follows, and the reason two meshes moved onto "the same" gridline end up
            // on it rather than a fraction apart. Before the change of basis below, because the
            // grid belongs to the world and a parented mesh's own axes are not it.
            var landing = _origin + delta;

            delta = new Vector3(
                Snap.Round(landing.X, Snap.TranslateStep),
                Snap.Round(landing.Y, Snap.TranslateStep),
                Snap.Round(landing.Z, Snap.TranslateStep)) - _origin;
        }

        target.Position = _start.Position + GizmoMath.ToLocal(target, delta);
    }

    private void Scale(IMesh target, in Ray ray)
    {
        // Measured in the plane facing the viewer, so the cursor's distance out from the mesh's
        // centre is the same distance it traces on screen — with nothing to project.
        if (!GizmoMath.PlanePoint(ray, _origin, _normal, out var point, out _))
        {
            return;
        }

        var travelled = (point - _origin).Length() - _grabRadius;

        // How far the cursor moved, against the length of a gizmo handle — the same law the drag
        // handles use, so one handle length outward doubles the mesh either way you scale it.
        //
        // Deliberately not Blender's ratio of the two distances. That has a singularity where the
        // gesture is most likely to start: press S with the pointer on the mesh you are looking
        // at, and the initial distance is a pixel or two, so the next pixel of movement scales it
        // by tens. A difference has no such centre to avoid.
        //
        // Never zero either: a zero scale is a matrix that cannot be inverted, and a mesh that can
        // never be picked, dragged or scaled back up again.
        var factor = MathF.Max(1f + travelled / _grabScale, 0.01f);

        var start = _start.Scale;

        target.Scale = Axis switch
        {
            GizmoAxis.X => new Vector3(Stretch(start.X, factor), start.Y, start.Z),
            GizmoAxis.Y => new Vector3(start.X, Stretch(start.Y, factor), start.Z),
            GizmoAxis.Z => new Vector3(start.X, start.Y, Stretch(start.Z, factor)),
            _ => new Vector3(Stretch(start.X, factor), Stretch(start.Y, factor), Stretch(start.Z, factor)),
        };
    }

    /// <summary>One axis of a scale, snapped, and never rounded all the way to zero.</summary>
    private float Stretch(float start, float factor)
    {
        var scaled = start * factor;
        var snapped = Snap.Round(scaled, Snap.ScaleStep);

        if (snapped != 0f)
        {
            return snapped;
        }

        return scaled < 0f ? -Snap.ScaleStep : Snap.ScaleStep;
    }

    /// <summary>
    /// Ends the gesture, leaving the target where it was put, and hands back the undoable edit —
    /// or null when nothing actually moved, which is what a G pressed and confirmed without
    /// touching the mouse amounts to.
    /// </summary>
    public IEditCommand? Confirm()
    {
        if (Target is not { } target)
        {
            Reset();
            return null;
        }

        var edit = TransformEdit.Between(target, _start, Mode == GizmoMode.Scale ? "Scale" : "Move");

        Reset();

        return edit;
    }

    /// <summary>Ends the gesture and puts the target back exactly where it started.</summary>
    public void Cancel()
    {
        if (Target is { } target)
        {
            _start.ApplyTo(target);
        }

        Reset();
    }

    /// <summary>One line describing what the gesture has done so far, for a status bar.</summary>
    public string Describe()
    {
        if (Target is not { } target)
        {
            return string.Empty;
        }

        var along = Axis == GizmoAxis.None ? "view" : Axis.ToString();

        return Mode switch
        {
            GizmoMode.Scale =>
                $"Scale along {along}: ({target.Scale.X:0.###}, {target.Scale.Y:0.###}, {target.Scale.Z:0.###})",
            _ =>
                $"Move along {along}: ({target.Position.X:0.###}, {target.Position.Y:0.###}, {target.Position.Z:0.###})",
        };
    }

    private void Reset()
    {
        Target = null;
        Mode = GizmoMode.Off;
        Axis = GizmoAxis.None;
    }
}
