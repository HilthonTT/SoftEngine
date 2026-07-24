using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.WinForms.Extensions;
using SoftEngine.WinForms.Helpers;
using System.Diagnostics;
using System.Numerics;

namespace SoftEngine.WinForms.Cameras;

public sealed class ArcBallCamera : ICamera
{
    private const float Radius = 0.3f;
    private const float YCoeff = 10f;

    private Point _oldMousePosition;
    private Vector3 _oldCameraPosition;
    private Quaternion _oldCameraRotation;

    private bool _right;
    private bool _left;

    public ArcBallCamera(Control control)
    {
        Rotation = Quaternion.Identity;
        Control = control;
        _control = control;
    }

    public Quaternion Rotation { get; set; }

    public Vector3 Position { get; set; }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    private Control _control;

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
                }

                if (_control is not null)
                {
                    _control.MouseDown += Control_MouseDown;
                    _control.MouseMove += Control_MouseMove;
                    _control.MouseUp += Control_MouseUp;
                }
            }
        }
    }

    private void Control_MouseUp(object? sender, MouseEventArgs e)
    {
        _left = false;
        _right = false;
        _control.Cursor = Cursors.Default;
    }

    private void Control_MouseDown(object? sender, MouseEventArgs e)
    {
        _left = Control.MouseButtons.HasFlag(MouseButtons.Left);
        _right = Control.MouseButtons.HasFlag(MouseButtons.Right);

        _oldMousePosition = e.Location;

        if (_left && _right)
        {
            _oldCameraPosition = Position;
            _control.Cursor = Cursors.SizeNS;
        }
        else if (_left)
        {
            _oldCameraRotation = Rotation;
            _control.Cursor = Cursors.NoMove2D;
        }
        else if (_right)
        {
            _oldCameraPosition = Position;
            _control.Cursor = Cursors.SizeAll;
        }
    }

    private void Control_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_left && _right)
        {
            var deltaY = _oldMousePosition.Y - e.Location.Y;
            Debug.WriteLine(deltaY);
            Position = _oldCameraPosition + new Vector3(0, 0, deltaY / YCoeff);
        }
        else if (_left)
        {
            var oldNpc = _control.NormalizePointClient(_oldMousePosition);
            var oldVector = MapToSphere(oldNpc);

            var curNpc = _control.NormalizePointClient(e.Location);
            var curVector = MapToSphere(curNpc);

            var deltaRotation = CalculateQuaternion(oldVector, curVector);
            Rotation = deltaRotation * _oldCameraRotation;
        }
        else if (_right)
        {
            var deltaPosition = new Vector3(new Vector2(e.Location.X, e.Location.Y) - new Vector2(_oldMousePosition.X, _oldMousePosition.Y), 0);
            Position = _oldCameraPosition + (deltaPosition * new Vector3(1, -1, 1)) / 100;
        }

        if (_left || _right)
        {
            _control.Invalidate();
        }
    }

    public static Vector3 MapToSphere(Vector2 v)
    {
        var P = new Vector3(v.X, -v.Y, 0);

        var XY_squared = P.LengthSquared();
        var radius_squared = Radius * Radius;

        if (XY_squared <= .5f * radius_squared)
        {
            P.Z = (float)Math.Sqrt(radius_squared - XY_squared);  // Pythagore
        }
        else
        {
            P.Z = 0.5f * radius_squared / P.Length();  // Hyperboloid
        }

        return Vector3.Normalize(P);
    }

    public static Quaternion CalculateQuaternion(Vector3 startV, Vector3 currentV)
    {
        var cross = Vector3.Cross(startV, currentV);

        if (cross.Length() > MathConstants.Epsilon)
        {
            return new Quaternion(cross, Vector3.Dot(startV, currentV));
        }

        return Quaternion.Identity;
    }
}
