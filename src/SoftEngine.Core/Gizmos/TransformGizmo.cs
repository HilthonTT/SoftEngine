using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Math;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

/// <summary>
/// The handles that let a mesh be moved, turned and stretched by dragging in the viewport.
///
/// <para>
/// It is built out of two things the engine already had. <see cref="ScenePicker"/> turns a
/// pixel into a world-space ray, and a handle is just another piece of geometry to test that
/// ray against — an axis is a line segment, a ring is a circle in a plane. And once a handle is
/// grabbed, the same ray answers the question the drag is actually asking: how far along this
/// axis, or how far around it, is the cursor now? So the gizmo never reads the frame, works
/// whether or not the mesh was rasterized, and can be driven — and tested — with no rendering
/// at all.
/// </para>
///
/// <para>
/// The handles are sized in <em>screen</em> terms rather than world ones: a fixed fraction of
/// the viewport's height, converted back to world units at the gizmo's own distance. A gizmo
/// measured in world units is unusable at both ends of the range this renderer covers — it
/// would be a speck on a 1500-unit elephant and would swallow a 2-unit skull.
/// </para>
///
/// <para>
/// <b>Rotation is in the mesh's own Euler angles</b>, because that is what <see cref="IMesh"/>
/// stores: the Y ring drives yaw, the X ring pitch, the Z ring roll. With two of the three at
/// zero that is exactly a rotation about the world axis drawn; with all three set it is not,
/// because composed Euler angles cannot express one. Turning <see cref="Mesh.Rotation"/> into
/// the quaternion <see cref="Scenes.Graph.SceneNode"/> already uses is what would fix it, and
/// this is one more reason to.
/// </para>
/// </summary>
public sealed class TransformGizmo
{
    /// <summary>
    /// Handle length as a fraction of the viewport's half-height. Large enough to grab
    /// comfortably, small enough not to hide the mesh it is attached to.
    /// </summary>
    public const float ScreenSize = 0.22f;

    /// <summary>How close to an axis a ray must pass to count as grabbing it, as a fraction of the handle length.</summary>
    private const float AxisTolerance = 0.12f;

    /// <summary>How far a ring's radius a ray may land from it and still count, as a fraction of the handle length.</summary>
    private const float RingTolerance = 0.14f;

    // Where the drag started, in whatever coordinate the active mode measures: a distance
    // along the axis for translate and scale, an angle around it for rotate.
    private float _grabParameter;

    // The handle frame as it was when the drag began, and deliberately not as it is now. The
    // gizmo is drawn at the mesh's own origin, so translating the mesh moves it — and
    // measuring each step against the moved frame would feed the mesh's motion back into the
    // number that caused it, running it away from the cursor. The line being dragged along
    // stands still; only the drawing follows.
    private Vector3 _grabOrigin;
    private float _grabScale = 1f;

    // The target's whole transform as the drag found it. A value copy rather than the mesh's own
    // Rotation3D, which is a mutable class and would be edited out from under the snapshot.
    private TransformState _startState;

    public GizmoMode Mode { get; set; } = GizmoMode.Off;

    /// <summary>
    /// The increments drags are quantized to. Off by default, so the gizmo behaves exactly as it
    /// did before there was such a thing.
    /// </summary>
    public GizmoSnap Snap { get; } = new();

    /// <summary>The mesh the handles are attached to, or null when nothing is selected.</summary>
    public IMesh? Target { get; set; }

    /// <summary>The handle under the cursor, for drawing it highlighted.</summary>
    public GizmoAxis HoveredAxis { get; private set; } = GizmoAxis.None;

    /// <summary>The handle currently being dragged, or <see cref="GizmoAxis.None"/>.</summary>
    public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;

    public bool IsDragging => ActiveAxis != GizmoAxis.None;

    /// <summary>Whether there is anything to draw or grab this frame.</summary>
    public bool IsActive => Mode != GizmoMode.Off && Target is not null;

    /// <summary>The world-space point the handles radiate from — the target's own origin.</summary>
    public Vector3 Origin => Target is { } target ? Vector3.Transform(Vector3.Zero, target.WorldMatrix) : Vector3.Zero;

    /// <summary>
    /// Handle length in world units for a gizmo at <paramref name="origin"/>: the fraction of
    /// the viewport <see cref="ScreenSize"/> asks for, converted back through the projection.
    /// </summary>
    public static float HandleScale(Scene scene, Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        var matrix = scene.Projection.ProjectionMatrix(scene.Surface.Width, scene.Surface.Height);

        // M22 is cot(halfFov) under a perspective projection and 2/height under a parallel
        // one, so its reciprocal is the viewport's half-height in world units — per unit of
        // depth in the first case, outright in the second.
        var halfHeight = matrix.M22 == 0f ? 1f : 1f / MathF.Abs(matrix.M22);

        if (!scene.Projection.IsOrthographic)
        {
            // View-space distance along the axis the projection divides by, not the straight-line
            // distance: a gizmo at the edge of a wide frame is farther from the eye than one in
            // the middle, and sizing by that would make it grow as it slid outward.
            var view = Vector3.Transform(origin, scene.Camera.ViewMatrix);
            halfHeight *= MathF.Max(-view.Z, 1e-3f);
        }

        return MathF.Max(halfHeight * ScreenSize, 1e-4f);
    }

    /// <summary>Updates <see cref="HoveredAxis"/> from a cursor position. Ignored mid-drag.</summary>
    public void Hover(Scene scene, int pixelX, int pixelY)
    {
        if (IsDragging || !IsActive)
        {
            return;
        }

        HoveredAxis = Hit(scene, pixelX, pixelY, out _);
    }

    /// <summary>
    /// Tries to grab a handle under a pixel. Returns true when one was taken, which is the
    /// caller's signal to spend the drag on the gizmo rather than on the camera.
    /// </summary>
    public bool Begin(Scene scene, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        ActiveAxis = GizmoAxis.None;

        if (!IsActive || Target is not { } target)
        {
            return false;
        }

        var axis = Hit(scene, pixelX, pixelY, out var parameter);
        if (axis == GizmoAxis.None)
        {
            return false;
        }

        ActiveAxis = axis;
        HoveredAxis = axis;

        _grabParameter = parameter;
        _grabOrigin = Origin;
        _grabScale = HandleScale(scene, _grabOrigin);

        _startState = TransformState.Of(target);

        return true;
    }

    /// <summary>
    /// Applies the drag to the target. Every step is measured from where the drag
    /// <em>started</em> rather than from the previous step, so a cursor that wanders out of the
    /// handle and back does not leave the mesh somewhere the pointer never was.
    /// </summary>
    public void Drag(Scene scene, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        if (!IsDragging || Target is not { } target)
        {
            return;
        }

        var origin = _grabOrigin;
        var scale = _grabScale;
        var direction = Direction(ActiveAxis);
        var ray = ScenePicker.RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f);

        switch (Mode)
        {
            case GizmoMode.Translate:
            {
                if (!GizmoMath.ClosestOnAxis(ray, origin, direction, out var parameter))
                {
                    return;
                }

                var offset = parameter - _grabParameter;

                if (Snap.Enabled)
                {
                    // Snapped in *world* space, before the offset is carried into the mesh's own
                    // space. The grid the drawn XZ gizmo shows is a world grid, and a parented
                    // mesh's local axes are not it — snapping after the change of basis would put
                    // the mesh on a grid nothing else in the scene shares.
                    var axisOrigin = Vector3.Dot(origin, direction);

                    offset = Snap.Round(axisOrigin + offset, Snap.TranslateStep) - axisOrigin;
                }

                // The handles point along the world axes, but a parented mesh's Position is an
                // offset in its node's space — so the world-space delta is carried back through
                // the parent before it is applied.
                target.Position = _startState.Position + GizmoMath.ToLocal(target, direction * offset);
                break;
            }

            case GizmoMode.Scale:
            {
                if (!GizmoMath.ClosestOnAxis(ray, origin, direction, out var parameter))
                {
                    return;
                }

                // One handle length of drag doubles the axis; dragging inward shrinks toward
                // zero without ever reaching it, since a zero scale is a matrix that cannot be
                // inverted and a mesh that can never be grabbed again.
                var factor = MathF.Max(1f + (parameter - _grabParameter) / scale, 0.01f);

                var start = _startState.Scale;

                target.Scale = ActiveAxis switch
                {
                    GizmoAxis.X => new Vector3(Stretch(start.X, factor), start.Y, start.Z),
                    GizmoAxis.Y => new Vector3(start.X, Stretch(start.Y, factor), start.Z),
                    _ => new Vector3(start.X, start.Y, Stretch(start.Z, factor)),
                };
                break;
            }

            case GizmoMode.Rotate:
            {
                if (!AngleAround(ray, origin, direction, out var angle))
                {
                    return;
                }

                // Shortest way round, so a drag across the seam at ±π does not spin the mesh
                // most of a turn the other way.
                var delta = GizmoMath.Wrap(angle - _grabParameter);

                target.Rotation = ActiveAxis switch
                {
                    GizmoAxis.X => new Rotation3D(Turn(_startState.Pitch, delta), _startState.Yaw, _startState.Roll),
                    GizmoAxis.Y => new Rotation3D(_startState.Pitch, Turn(_startState.Yaw, delta), _startState.Roll),
                    _ => new Rotation3D(_startState.Pitch, _startState.Yaw, Turn(_startState.Roll, delta)),
                };
                break;
            }

            default:
                break;
        }
    }

    /// <summary>
    /// Releases the handle. The target keeps whatever the drag left it at, and the change is
    /// handed back as an undoable edit — or null when the drag moved nothing, which is what a
    /// click that grabs a handle and lets go again amounts to.
    ///
    /// <para>
    /// The gizmo produces the command rather than pushing it, because it has no opinion about
    /// whether the application keeps a history. What it does have, and nothing downstream does,
    /// is the transform from before the drag: by the time a caller sees the mouse-up, the mesh
    /// has already been moved a hundred times.
    /// </para>
    /// </summary>
    public IEditCommand? End()
    {
        if (!IsDragging || Target is not { } target)
        {
            ActiveAxis = GizmoAxis.None;
            return null;
        }

        var verb = Mode switch
        {
            GizmoMode.Rotate => "Rotate",
            GizmoMode.Scale => "Scale",
            _ => "Move",
        };

        var edit = TransformEdit.Between(target, _startState, verb);

        ActiveAxis = GizmoAxis.None;

        return edit;
    }

    /// <summary>Puts the target back where the drag found it, for a cancelled drag.</summary>
    public void Cancel()
    {
        if (IsDragging && Target is { } target)
        {
            _startState.ApplyTo(target);
        }

        ActiveAxis = GizmoAxis.None;
    }

    /// <summary>
    /// One axis of a scale, snapped. Rounding toward zero is the one result that cannot be
    /// allowed through: a zero scale is a matrix that cannot be inverted and a mesh that can
    /// never be grabbed again, which is the same thing the un-snapped path clamps against.
    /// </summary>
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
    /// One axis of a rotation, snapped. The <em>resulting</em> angle is rounded rather than the
    /// angle dragged through, so a 15° step means 15° from zero — which is what makes two meshes
    /// snapped to the same increment actually parallel.
    /// </summary>
    private float Turn(float start, float delta) => Snap.Round(start + delta, Snap.RotateStep);

    /// <summary>The world direction of one handle.</summary>
    public static Vector3 Direction(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    /// <summary>
    /// Which handle a pixel is over, and the drag parameter to measure from — a distance along
    /// the axis for the two linear modes, an angle around it for rotation.
    ///
    /// Nearest wins. Handles overlap near the origin where all three meet, and the one whose
    /// hit is closest to the eye is the one that looks grabbable there.
    /// </summary>
    public GizmoAxis Hit(Scene scene, int pixelX, int pixelY, out float parameter)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        parameter = 0f;

        if (!IsActive)
        {
            return GizmoAxis.None;
        }

        var origin = Origin;
        var scale = HandleScale(scene, origin);
        var ray = ScenePicker.RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f);

        var best = GizmoAxis.None;
        var nearest = float.PositiveInfinity;

        for (var i = 0; i < 3; i++)
        {
            var axis = (GizmoAxis)i;
            var direction = Direction(axis);

            var hit = Mode == GizmoMode.Rotate
                ? HitRing(ray, origin, direction, scale, out var value, out var distance)
                : HitAxis(ray, origin, direction, scale, out value, out distance);

            if (hit && distance < nearest)
            {
                nearest = distance;
                best = axis;
                parameter = value;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether the ray passes close enough to an axis handle, and where along the axis. The
    /// handle is a segment from the origin outward, so a ray crossing the axis's continuation
    /// behind the gizmo is not on it.
    /// </summary>
    private static bool HitAxis(in Ray ray, Vector3 origin, Vector3 direction, float scale, out float parameter, out float distance)
    {
        parameter = 0f;
        distance = float.PositiveInfinity;

        if (!GizmoMath.Closest(ray, origin, direction, out parameter, out var alongRay))
        {
            return false;
        }

        // A little past the tip, so the arrowhead is grabbable too, and a little behind the
        // origin, so a handle pointing almost straight at the eye is not a single pixel.
        if (parameter < -scale * AxisTolerance || parameter > scale * 1.15f || alongRay <= 0f)
        {
            return false;
        }

        var onAxis = origin + direction * parameter;
        var onRay = ray.Origin + ray.Direction * alongRay;

        var gap = (onAxis - onRay).Length();
        if (gap > scale * AxisTolerance)
        {
            return false;
        }

        distance = alongRay;
        return true;
    }

    /// <summary>
    /// Whether the ray lands on a rotation ring, and at what angle around it. The ring is a
    /// circle of one handle length in the plane the axis is normal to.
    /// </summary>
    private static bool HitRing(in Ray ray, Vector3 origin, Vector3 axis, float scale, out float angle, out float distance)
    {
        angle = 0f;
        distance = float.PositiveInfinity;

        if (!GizmoMath.PlanePoint(ray, origin, axis, out var point, out var alongRay) || alongRay <= 0f)
        {
            return false;
        }

        var radius = (point - origin).Length();

        if (MathF.Abs(radius - scale) > scale * RingTolerance)
        {
            return false;
        }

        angle = GizmoMath.Angle(point - origin, axis);
        distance = alongRay;

        return true;
    }

    /// <summary>
    /// The angle around an axis of the point where the ray crosses the plane the axis is
    /// normal to. Unlike the hit test this ignores the ring's radius — once a ring is grabbed,
    /// the drag follows the cursor's bearing however far outside the circle it wanders.
    /// </summary>
    private static bool AngleAround(in Ray ray, Vector3 origin, Vector3 axis, out float angle)
    {
        angle = 0f;

        if (!GizmoMath.PlanePoint(ray, origin, axis, out var point, out _))
        {
            return false;
        }

        var offset = point - origin;

        // Dead centre: no bearing to read, so the mesh keeps the angle it had.
        if (offset.LengthSquared() < 1e-12f)
        {
            return false;
        }

        angle = GizmoMath.Angle(offset, axis);
        return true;
    }
}
