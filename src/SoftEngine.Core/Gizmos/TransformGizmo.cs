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

    private Vector3 _startPosition;
    private Vector3 _startScale;
    private Rotation3D _startRotation = new(0, 0, 0);

    public GizmoMode Mode { get; set; } = GizmoMode.Off;

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

        _startPosition = target.Position;
        _startScale = target.Scale;
        _startRotation = new Rotation3D(target.Rotation.XPitch, target.Rotation.YYaw, target.Rotation.ZRoll);

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
                if (!ClosestOnAxis(ray, origin, direction, out var parameter))
                {
                    return;
                }

                // The handles point along the world axes, but a parented mesh's Position is an
                // offset in its node's space — so the world-space delta is carried back through
                // the parent before it is applied.
                target.Position = _startPosition + ToLocal(target, direction * (parameter - _grabParameter));
                break;
            }

            case GizmoMode.Scale:
            {
                if (!ClosestOnAxis(ray, origin, direction, out var parameter))
                {
                    return;
                }

                // One handle length of drag doubles the axis; dragging inward shrinks toward
                // zero without ever reaching it, since a zero scale is a matrix that cannot be
                // inverted and a mesh that can never be grabbed again.
                var factor = MathF.Max(1f + (parameter - _grabParameter) / scale, 0.01f);

                target.Scale = ActiveAxis switch
                {
                    GizmoAxis.X => new Vector3(_startScale.X * factor, _startScale.Y, _startScale.Z),
                    GizmoAxis.Y => new Vector3(_startScale.X, _startScale.Y * factor, _startScale.Z),
                    _ => new Vector3(_startScale.X, _startScale.Y, _startScale.Z * factor),
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
                var delta = Wrap(angle - _grabParameter);

                target.Rotation = ActiveAxis switch
                {
                    GizmoAxis.X => new Rotation3D(_startRotation.XPitch + delta, _startRotation.YYaw, _startRotation.ZRoll),
                    GizmoAxis.Y => new Rotation3D(_startRotation.XPitch, _startRotation.YYaw + delta, _startRotation.ZRoll),
                    _ => new Rotation3D(_startRotation.XPitch, _startRotation.YYaw, _startRotation.ZRoll + delta),
                };
                break;
            }

            default:
                break;
        }
    }

    /// <summary>Releases the handle. The target keeps whatever the drag left it at.</summary>
    public void End() => ActiveAxis = GizmoAxis.None;

    /// <summary>Puts the target back where the drag found it, for a cancelled drag.</summary>
    public void Cancel()
    {
        if (IsDragging && Target is { } target)
        {
            target.Position = _startPosition;
            target.Scale = _startScale;
            target.Rotation = _startRotation;
        }

        ActiveAxis = GizmoAxis.None;
    }

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

        if (!Closest(ray, origin, direction, out parameter, out var alongRay))
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

        if (!PlanePoint(ray, origin, axis, out var point, out var alongRay) || alongRay <= 0f)
        {
            return false;
        }

        var radius = (point - origin).Length();

        if (MathF.Abs(radius - scale) > scale * RingTolerance)
        {
            return false;
        }

        angle = Angle(point - origin, axis);
        distance = alongRay;

        return true;
    }

    /// <summary>The distance along an axis of the point on it nearest to the ray.</summary>
    private static bool ClosestOnAxis(in Ray ray, Vector3 origin, Vector3 direction, out float parameter) =>
        Closest(ray, origin, direction, out parameter, out _);

    /// <summary>
    /// The angle around an axis of the point where the ray crosses the plane the axis is
    /// normal to. Unlike the hit test this ignores the ring's radius — once a ring is grabbed,
    /// the drag follows the cursor's bearing however far outside the circle it wanders.
    /// </summary>
    private static bool AngleAround(in Ray ray, Vector3 origin, Vector3 axis, out float angle)
    {
        angle = 0f;

        if (!PlanePoint(ray, origin, axis, out var point, out _))
        {
            return false;
        }

        var offset = point - origin;

        // Dead centre: no bearing to read, so the mesh keeps the angle it had.
        if (offset.LengthSquared() < 1e-12f)
        {
            return false;
        }

        angle = Angle(offset, axis);
        return true;
    }

    /// <summary>
    /// The classic line-to-line closest approach. It degenerates when the two are parallel —
    /// which here means looking straight down the handle, where there is no drag direction to
    /// read anyway, so the caller is told to leave the mesh alone rather than given a number.
    /// </summary>
    private static bool Closest(in Ray ray, Vector3 origin, Vector3 direction, out float onAxis, out float onRay)
    {
        onAxis = 0f;
        onRay = 0f;

        var between = origin - ray.Origin;

        var dd = Vector3.Dot(ray.Direction, ray.Direction);
        var da = Vector3.Dot(ray.Direction, direction);
        var aa = Vector3.Dot(direction, direction);

        var determinant = dd * aa - da * da;

        if (MathF.Abs(determinant) < 1e-7f || dd < 1e-12f)
        {
            return false;
        }

        var db = Vector3.Dot(ray.Direction, between);
        var ab = Vector3.Dot(direction, between);

        onRay = (aa * db - da * ab) / determinant;
        onAxis = (da * db - dd * ab) / determinant;

        return true;
    }

    /// <summary>Where the ray crosses the plane through a point with a given normal.</summary>
    private static bool PlanePoint(in Ray ray, Vector3 origin, Vector3 normal, out Vector3 point, out float alongRay)
    {
        point = Vector3.Zero;
        alongRay = 0f;

        var denominator = Vector3.Dot(ray.Direction, normal);

        // Edge-on: the ray runs along the plane and either misses it or lies in it, and the
        // ring's angle would be meaningless either way.
        if (MathF.Abs(denominator) < 1e-4f)
        {
            return false;
        }

        alongRay = Vector3.Dot(origin - ray.Origin, normal) / denominator;
        point = ray.Origin + ray.Direction * alongRay;

        return true;
    }

    /// <summary>An angle around an axis, measured in a basis built from the axis itself.</summary>
    private static float Angle(Vector3 offset, Vector3 axis)
    {
        var reference = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;

        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);

        return MathF.Atan2(Vector3.Dot(offset, v), Vector3.Dot(offset, u));
    }

    private static float Wrap(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    /// <summary>Carries a world-space offset into the space the mesh's own Position lives in.</summary>
    private static Vector3 ToLocal(IMesh mesh, Vector3 worldDelta)
    {
        if (mesh.Parent is not { } parent)
        {
            return worldDelta;
        }

        return Matrix4x4.Invert(parent.WorldMatrix, out var inverse)
            ? Vector3.TransformNormal(worldDelta, inverse)
            : worldDelta;
    }
}
