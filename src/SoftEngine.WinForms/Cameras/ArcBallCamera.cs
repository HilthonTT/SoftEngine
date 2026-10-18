using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.WinForms.Utilities;
using System.Numerics;

namespace SoftEngine.WinForms.Cameras;

public sealed class ArcBallCamera : ICamera
{
    private enum Gesture { None, Orbit, Pan, Dolly }

    private const float Radius = 0.9f;

    private const float DollyPerPixel = 0.005f;

    private const float MinDistance = 0.001f;

    private Point _dragOrigin;
    private Vector3 _dragPosition;
    private Quaternion _dragRotation;
    private float _dragPivotDepth;

    private MouseButtons _buttons;

    private Control _control;

    private Vector3 _position;

    private float _pivotDepth;

    public ArcBallCamera(Control control)
    {
        Rotation = Quaternion.Identity;
        Control = control;
        _control = control;
    }

    public Quaternion Rotation { get; set; }

    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;

            _pivotDepth = value.Z;
        }
    }

    public Vector3 Pivot =>
        Vector3.Transform(new Vector3(0, 0, _pivotDepth) - _position, Quaternion.Inverse(Rotation));

    public float FieldOfView { get; set; } = 40f * MathF.PI / 180f;

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public Control Control
    {
        get => _control;
        set
        {
            Control oldControl = _control;

            if (PropertyChangedHelper.ChangeValue(ref _control, value))
            {
                if (oldControl is not null)
                {
                    oldControl.MouseDown -= Control_MouseDown;
                    oldControl.MouseMove -= Control_MouseMove;
                    oldControl.MouseUp -= Control_MouseUp;
                    oldControl.MouseCaptureChanged -= Control_MouseCaptureChanged;
                }

                if (_control is not null)
                {
                    _control.MouseDown += Control_MouseDown;
                    _control.MouseMove += Control_MouseMove;
                    _control.MouseUp += Control_MouseUp;
                    _control.MouseCaptureChanged += Control_MouseCaptureChanged;
                }
            }
        }
    }

    private void Control_MouseDown(object? sender, MouseEventArgs e)
    {
        _buttons |= e.Button;
        Anchor(e.Location);
    }

    private void Control_MouseUp(object? sender, MouseEventArgs e)
    {
        _buttons &= ~e.Button;
        Anchor(e.Location);
    }

    private void Control_MouseCaptureChanged(object? sender, EventArgs e)
    {
        if (_control.Capture)
        {
            return;
        }

        _buttons &= Control.MouseButtons;
        Anchor(_control.PointToClient(Cursor.Position));
    }

    private void Anchor(Point location)
    {
        _dragOrigin = location;
        _dragPosition = _position;
        _dragRotation = Rotation;
        _dragPivotDepth = _pivotDepth;

        _control.Cursor = CurrentGesture switch
        {
            Gesture.Orbit => Cursors.NoMove2D,
            Gesture.Pan => Cursors.SizeAll,
            Gesture.Dolly => Cursors.SizeNS,
            _ => Cursors.Default,
        };
    }

    private Gesture CurrentGesture
    {
        get
        {
            var left = _buttons.HasFlag(MouseButtons.Left);
            var right = _buttons.HasFlag(MouseButtons.Right);
            var middle = _buttons.HasFlag(MouseButtons.Middle);

            return (left, right, middle) switch
            {
                (true, true, _) => Gesture.Dolly,
                (_, _, true) => Gesture.Pan,
                (false, true, _) => Gesture.Pan,
                (true, false, _) => Gesture.Orbit,
                _ => Gesture.None,
            };
        }
    }

    public bool GesturesSuspended { get; set; }

    private void Control_MouseMove(object? sender, MouseEventArgs e)
    {
        if (GesturesSuspended)
        {
            Anchor(e.Location);
            return;
        }

        var gesture = CurrentGesture;

        switch (gesture)
        {
            case Gesture.Orbit:
                Orbit(e.Location);
                break;

            case Gesture.Pan:
                Pan(e.Location);
                break;

            case Gesture.Dolly:
                Dolly(e.Location);
                break;

            default:
                return;
        }

        _control.Invalidate();
    }

    private void Orbit(Point location)
    {
        var from = MapToSphere(_control.NormalizeAroundCenter(_dragOrigin));
        var to = MapToSphere(_control.NormalizeAroundCenter(location));

        TurnTo(RotationBetween(from, to) * _dragRotation, _dragPosition, _dragRotation);
    }

    private void TurnTo(Quaternion rotation, Vector3 fromPosition, Quaternion fromRotation)
    {
        rotation = Quaternion.Normalize(rotation);

        var pivotView = new Vector3(0, 0, _pivotDepth);
        var pivotWorld = Vector3.Transform(pivotView - fromPosition, Quaternion.Inverse(fromRotation));

        Rotation = rotation;

        _position = pivotView - Vector3.Transform(pivotWorld, rotation);
    }

    private void Pan(Point location)
    {
        var scale = WorldUnitsPerPixel(_dragPivotDepth);

        var deltaX = (location.X - _dragOrigin.X) * scale;
        var deltaY = (location.Y - _dragOrigin.Y) * scale;

        _position = _dragPosition + new Vector3(deltaX, -deltaY, 0);
    }

    private void Dolly(Point location)
    {
        var pixels = _dragOrigin.Y - location.Y;
        var sign = _dragPosition.Z > 0f ? 1f : -1f;

        var distance = MathF.Max(MinDistance, MathF.Abs(_dragPosition.Z));
        var scaled = MathF.Max(MinDistance, distance * MathF.Exp(-pixels * DollyPerPixel));

        var depth = sign * scaled;

        _position = new Vector3(_dragPosition.X, _dragPosition.Y, depth);
        _pivotDepth = sign * MathF.Max(MinDistance, MathF.Abs(_dragPivotDepth + depth - _dragPosition.Z));
    }

    private float WorldUnitsPerPixel(float depth)
    {
        var height = MathF.Max(1f, _control.ClientSize.Height);
        var distance = MathF.Max(MinDistance, MathF.Abs(depth));

        return 2f * distance * MathF.Tan(FieldOfView * 0.5f) / height;
    }

    #region Keyed turns

    public void LookAlong(AxisView view) => TurnTo(RotationFor(view), Position, Rotation);

    public void FlipView() => RotateInView(Vector3.UnitY, MathF.PI);

    public void RotateAroundWorldAxis(Vector3 axis, float radians) =>
        TurnTo(Rotation * Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians), Position, Rotation);

    public void RotateInView(Vector3 axis, float radians) =>
        TurnTo(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians) * Rotation, Position, Rotation);

    public AxisView? CurrentAxisView
    {
        get
        {
            foreach (var view in Enum.GetValues<AxisView>())
            {
                if (MathF.Abs(Quaternion.Dot(Quaternion.Normalize(Rotation), RotationFor(view))) >= 0.99995f)
                {
                    return view;
                }
            }

            return null;
        }
    }

    public static Quaternion RotationFor(AxisView view) => view switch
    {
        AxisView.Front => Quaternion.Identity,
        AxisView.Back => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI),
        AxisView.Right => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f),
        AxisView.Left => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        AxisView.Top => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
        AxisView.Bottom => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f),
        _ => Quaternion.Identity,
    };

    #endregion

    public static Vector3 MapToSphere(Vector2 v)
    {
        var P = new Vector3(v.X, -v.Y, 0);

        var XY_squared = P.LengthSquared();
        var radius_squared = Radius * Radius;

        if (XY_squared <= .5f * radius_squared)
        {
            P.Z = (float)Math.Sqrt(radius_squared - XY_squared);
        }
        else
        {
            P.Z = 0.5f * radius_squared / P.Length();
        }

        return Vector3.Normalize(P);
    }

    public static Quaternion RotationBetween(Vector3 startV, Vector3 currentV)
    {
        var cross = Vector3.Cross(startV, currentV);

        if (cross.Length() <= MathConstants.Epsilon)
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(new Quaternion(cross, 1f + Vector3.Dot(startV, currentV)));
    }
}
