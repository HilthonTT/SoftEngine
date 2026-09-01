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

        var edit = TransformEdit.Between(target, _start, Mode == GizmoMode.Scale ? "Scale" : "Move");

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
