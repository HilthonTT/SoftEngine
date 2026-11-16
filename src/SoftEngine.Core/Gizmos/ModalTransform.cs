using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

public sealed class ModalTransform
{
    private TransformState _start;

    private Vector3 _origin;
    private Vector3 _normal;
    private Ray _grabRay;

    private Vector3 _grabPoint;
    private float _grabRadius;
    private float _grabScale = 1f;

    private float _turned;

    public IMesh? Target { get; private set; }

    public GizmoMode Mode { get; private set; } = GizmoMode.Off;

    public GizmoAxis Axis { get; private set; } = GizmoAxis.None;

    public bool IsActive => Mode != GizmoMode.Off && Target is not null;

    public GizmoSnap Snap { get; set; } = new();

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

    public void Constrain(GizmoAxis axis)
    {
        if (!IsActive)
        {
            return;
        }

        Axis = Axis == axis ? GizmoAxis.None : axis;
    }

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

            case GizmoMode.Rotate:
                Rotate(target, ray);
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

        if (Snap.Enabled && Axis != GizmoAxis.None)
        {
            // Only the constrained coordinate snaps; rounding the other two would move the
            // object off the axis it is locked to.
            var direction = TransformGizmo.Direction(Axis);
            var start = Vector3.Dot(_origin, direction);

            delta = direction * (Snap.Round(start + Vector3.Dot(delta, direction), Snap.TranslateStep) - start);
        }
        else if (Snap.Enabled)
        {
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
        if (!GizmoMath.PlanePoint(ray, _origin, _normal, out var point, out _))
        {
            return;
        }

        var travelled = (point - _origin).Length() - _grabRadius;

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

    private void Rotate(IMesh target, in Ray ray)
    {
        // About the view axis until X, Y or Z names a world axis instead. Either way the angle is
        // the one the cursor has swept around the object since the grab, so constraining half way
        // through re-reads the same gesture rather than jumping.
        var axis = Axis == GizmoAxis.None ? _normal : TransformGizmo.Direction(Axis);

        if (!AngleAround(_grabRay, axis, out var from) || !AngleAround(ray, axis, out var to))
        {
            return;
        }

        var delta = GizmoMath.Wrap(to - from);

        if (Snap.Enabled)
        {
            delta = Snap.Round(delta, Snap.RotateStep);
        }

        _turned = delta;

        // Composed as quaternions and read back as Euler angles: an Euler triple can only turn
        // about its own three axes, and the view axis is almost never one of them.
        var local = GizmoMath.ToLocal(target, axis);

        if (local.LengthSquared() < 1e-12f)
        {
            return;
        }

        var start = new Math.Rotation3D(_start.Pitch, _start.Yaw, _start.Roll).ToQuaternion();
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(local), delta);

        target.Rotation = Math.Rotation3D.FromQuaternion(Quaternion.Concatenate(start, turn));
    }

    private bool AngleAround(in Ray ray, Vector3 axis, out float angle)
    {
        angle = 0f;

        if (!GizmoMath.PlanePoint(ray, _origin, axis, out var point, out _))
        {
            return false;
        }

        var offset = point - _origin;

        if (offset.LengthSquared() < 1e-12f)
        {
            return false;
        }

        angle = GizmoMath.Angle(offset, axis);
        return true;
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

    public IEditCommand? Confirm()
    {
        if (Target is not { } target)
        {
            Reset();
            return null;
        }

        var verb = Mode switch
        {
            GizmoMode.Scale => "Scale",
            GizmoMode.Rotate => "Rotate",
            _ => "Move",
        };

        var edit = TransformEdit.Between(target, _start, verb);

        Reset();

        return edit;
    }

    public void Cancel()
    {
        if (Target is { } target)
        {
            _start.ApplyTo(target);
        }

        Reset();
    }

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
            GizmoMode.Rotate =>
                $"Rotate about {along}: {_turned * 180f / MathF.PI:0.#}°",
            _ =>
                $"Move along {along}: ({target.Position.X:0.###}, {target.Position.Y:0.###}, {target.Position.Z:0.###})",
        };
    }

    private void Reset()
    {
        Target = null;
        Mode = GizmoMode.Off;
        Axis = GizmoAxis.None;
        _turned = 0f;
    }
}
