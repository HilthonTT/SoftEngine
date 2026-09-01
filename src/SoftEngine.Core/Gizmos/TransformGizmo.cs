using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Math;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

public sealed class TransformGizmo
{
    public const float ScreenSize = 0.22f;

    private const float AxisTolerance = 0.12f;

    private const float RingTolerance = 0.14f;

    private float _grabParameter;

    private Vector3 _grabOrigin;
    private float _grabScale = 1f;

    private TransformState _startState;

    public GizmoMode Mode { get; set; } = GizmoMode.Off;

    public GizmoSnap Snap { get; } = new();

    public IMesh? Target { get; set; }

    public GizmoAxis HoveredAxis { get; private set; } = GizmoAxis.None;

    public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;

    public bool IsDragging => ActiveAxis != GizmoAxis.None;

    public bool IsActive => Mode != GizmoMode.Off && Target is not null;

    public Vector3 Origin => Target is { } target ? Vector3.Transform(Vector3.Zero, target.WorldMatrix) : Vector3.Zero;

    public static float HandleScale(Scene scene, Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        var matrix = scene.Projection.ProjectionMatrix(scene.Surface.Width, scene.Surface.Height);

        var halfHeight = matrix.M22 == 0f ? 1f : 1f / MathF.Abs(matrix.M22);

        if (!scene.Projection.IsOrthographic)
        {
            var view = Vector3.Transform(origin, scene.Camera.ViewMatrix);
            halfHeight *= MathF.Max(-view.Z, 1e-3f);
        }

        return MathF.Max(halfHeight * ScreenSize, 1e-4f);
    }

    public void Hover(Scene scene, int pixelX, int pixelY)
    {
        if (IsDragging || !IsActive)
        {
            return;
        }

        HoveredAxis = Hit(scene, pixelX, pixelY, out _);
    }

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
                    var axisOrigin = Vector3.Dot(origin, direction);

                    offset = Snap.Round(axisOrigin + offset, Snap.TranslateStep) - axisOrigin;
                }

                target.Position = _startState.Position + GizmoMath.ToLocal(target, direction * offset);
                break;
            }

            case GizmoMode.Scale:
            {
                if (!GizmoMath.ClosestOnAxis(ray, origin, direction, out var parameter))
                {
                    return;
                }

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

    public void Cancel()
    {
        if (IsDragging && Target is { } target)
        {
            _startState.ApplyTo(target);
        }

        ActiveAxis = GizmoAxis.None;
    }

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

    private float Turn(float start, float delta) => Snap.Round(start + delta, Snap.RotateStep);

    public static Vector3 Direction(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

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

    private static bool HitAxis(in Ray ray, Vector3 origin, Vector3 direction, float scale, out float parameter, out float distance)
    {
        parameter = 0f;
        distance = float.PositiveInfinity;

        if (!GizmoMath.Closest(ray, origin, direction, out parameter, out var alongRay))
        {
            return false;
        }

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

    private static bool AngleAround(in Ray ray, Vector3 origin, Vector3 axis, out float angle)
    {
        angle = 0f;

        if (!GizmoMath.PlanePoint(ray, origin, axis, out var point, out _))
        {
            return false;
        }

        var offset = point - origin;

        if (offset.LengthSquared() < 1e-12f)
        {
            return false;
        }

        angle = GizmoMath.Angle(offset, axis);
        return true;
    }
}
