using SoftEngine.Core.Geometry;
using SoftEngine.Core.Gizmos;
using SoftEngine.WinForms.Cameras;
using System.ComponentModel;
using System.Numerics;

namespace SoftEngine.WinForms;

public partial class Panel3D
{
    #region Input

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        Keys.X or Keys.Y or Keys.Z or Keys.G or Keys.Delete => true,
        Keys.NumPad0 or Keys.NumPad1 or Keys.NumPad2 or Keys.NumPad3 or Keys.NumPad4 => true,
        Keys.NumPad5 or Keys.NumPad6 or Keys.NumPad7 or Keys.NumPad8 or Keys.NumPad9 => true,
        Keys.Home => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Transform is { IsActive: true })
        {
            HandleTransformKey(e.KeyCode);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ClearSelectedPixel();
            return;
        }

        if (HandleEditKey(e.KeyCode, e.Shift, e.Control, e.Alt))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Home)
        {
            ZoomActualSize();
            e.Handled = true;
            return;
        }

        if (HandleViewKey(e.KeyCode, e.Shift, e.Control))
        {
            e.Handled = true;
            return;
        }

        if (CameraMotion.IsMovementKey(e.KeyCode) && _heldKeys.Add(e.KeyCode))
        {
            _moveTimer.Enabled = true;
            e.Handled = true;
        }
    }

    private bool HandleEditKey(Keys key, bool shift, bool control, bool alt)
    {
        if (control || alt)
        {
            return false;
        }

        if (key == Keys.A && shift)
        {
            RequestAdd();
            return true;
        }

        if (shift || Picked?.Mesh is not { } mesh || Scene is null)
        {
            return false;
        }

        switch (key)
        {
            case Keys.G:
                return BeginTransform(mesh, GizmoMode.Translate);

            case Keys.S:
                return BeginTransform(mesh, GizmoMode.Scale);

            case Keys.X:
            case Keys.Delete:
                DeleteRequested?.Invoke(this, mesh);
                return true;

            default:
                return false;
        }
    }

    public event EventHandler<Point>? AddRequested;

    public event EventHandler<IMesh>? DeleteRequested;

    private void RequestAdd()
    {
        _heldKeys.Clear();
        _moveTimer.Enabled = false;

        var location = PointToClient(Cursor.Position);

        if (!ClientRectangle.Contains(location))
        {
            location = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        }

        AddRequested?.Invoke(this, PointToScreen(location));
    }

    #region Modal transform

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModalTransform? Transform { get; set; }

    private bool BeginTransform(IMesh mesh, GizmoMode mode)
    {
        if (Transform is not { } transform || Scene is not { } scene)
        {
            return false;
        }

        var pixel = CursorSamplePixel();

        if (!transform.Begin(scene, mesh, mode, pixel.X, pixel.Y))
        {
            return false;
        }

        SuspendCameraGestures(true);

        _heldKeys.Clear();
        _moveTimer.Enabled = false;

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();

        return true;
    }

    private void HandleTransformKey(Keys key)
    {
        if (Transform is not { IsActive: true } transform)
        {
            return;
        }

        switch (key)
        {
            case Keys.Escape:
                transform.Cancel();
                EndTransform();
                return;

            case Keys.Return:
            case Keys.Space:
                History?.Push(transform.Confirm());
                EndTransform();
                return;

            case Keys.X:
            case Keys.Y:
            case Keys.Z:
                transform.Constrain(key switch
                {
                    Keys.X => GizmoAxis.X,
                    Keys.Y => GizmoAxis.Y,
                    _ => GizmoAxis.Z,
                });

                UpdateTransform();
                return;

            default:
                return;
        }
    }

    private void UpdateTransform()
    {
        if (Transform is not { IsActive: true } transform || Scene is not { } scene)
        {
            return;
        }

        var pixel = CursorSamplePixel();
        transform.Update(scene, pixel.X, pixel.Y);

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void CancelTransform()
    {
        if (Transform is not { IsActive: true } transform)
        {
            return;
        }

        transform.Cancel();
        EndTransform();
    }

    private void EndTransform()
    {
        SuspendCameraGestures(false);

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private Point CursorSamplePixel()
    {
        var location = PointToClient(Cursor.Position);

        if (!ClientRectangle.Contains(location))
        {
            location = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        }

        return ToSamplePixel(location);
    }

    #endregion

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_heldKeys.Remove(e.KeyCode) && _heldKeys.Count == 0)
        {
            _moveTimer.Enabled = false;
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        if (Transform is { IsActive: true } transform)
        {
            transform.Cancel();
            EndTransform();
        }

        _heldKeys.Clear();
        _moveTimer.Enabled = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        Focus();
        _mouseDownAt = e.Location;
        _mouseDragged = false;

        if (Transform is { IsActive: true } running)
        {
            if (e.Button == MouseButtons.Left)
            {
                History?.Push(running.Confirm());
            }
            else
            {
                running.Cancel();
            }

            _endedTransform = true;

            EndTransform();
            return;
        }

        if (e.Button == MouseButtons.Left && Gizmo is { IsActive: true } gizmo && Scene is { } scene)
        {
            var pixel = ToSamplePixel(e.Location);

            if (gizmo.Begin(scene, pixel.X, pixel.Y))
            {
                SuspendCameraGestures(true);
                Invalidate();
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (Math.Abs(e.X - _mouseDownAt.X) > 3 || Math.Abs(e.Y - _mouseDownAt.Y) > 3)
        {
            _mouseDragged = true;
        }

        if (Transform is { IsActive: true })
        {
            UpdateTransform();
            return;
        }

        if (Gizmo is not { IsActive: true } gizmo || Scene is not { } scene)
        {
            return;
        }

        var pixel = ToSamplePixel(e.Location);

        if (gizmo.IsDragging)
        {
            gizmo.Drag(scene, pixel.X, pixel.Y);
            GizmoChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        var before = gizmo.HoveredAxis;
        gizmo.Hover(scene, pixel.X, pixel.Y);

        if (gizmo.HoveredAxis != before)
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_endedTransform)
        {
            _endedTransform = false;
            return;
        }

        if (e.Button == MouseButtons.Left && Gizmo is { IsDragging: true } gizmo)
        {
            History?.Push(gizmo.End());

            SuspendCameraGestures(false);

            GizmoChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left && !_mouseDragged)
        {
            SelectPixel(ToBufferPixel(e.Location));
        }
    }

    private Point ToSamplePixel(Point client) => SampleOf(ToBufferPixel(client));

    private void SuspendCameraGestures(bool suspended)
    {
        if (ArcBall is { } camera)
        {
            camera.GesturesSuspended = suspended;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        _wheelDelta += e.Delta;

        var notch = SpeedModifier();

        while (_wheelDelta >= WheelNotch)
        {
            _wheelDelta -= WheelNotch;
            Dolly(notch);
        }

        while (_wheelDelta <= -WheelNotch)
        {
            _wheelDelta += WheelNotch;
            Dolly(-notch);
        }
    }

    private static float SpeedModifier() =>
        CameraMotion.SpeedModifier(ModifierKeys.HasFlag(Keys.Shift), ModifierKeys.HasFlag(Keys.Control));

    private void MoveCamera(object? sender, EventArgs e)
    {
        if (Scene?.Camera is not { } camera || _heldKeys.Count == 0)
        {
            return;
        }

        var direction = CameraMotion.FlyDirection(_heldKeys);

        if (direction == Vector3.Zero)
        {
            return;
        }

        var speed = CameraMotion.FlySpeed(CameraDistance, SpeedModifier());

        var position = camera.Position;

        camera.Position = CameraMotion.ClampToPivot(position + (Vector3.Normalize(direction) * speed), position);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #endregion

    #region View orientation

    public const float RotationStep = 15f * MathF.PI / 180f;

    private ArcBallCamera? ArcBall => Scene?.Camera as ArcBallCamera;

    public void LookAlong(AxisView view)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.LookAlong(view);
        Invalidate();
    }

    public void FlipView()
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.FlipView();
        Invalidate();
    }

    public void RotateAroundWorldAxis(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateAroundWorldAxis(axis, radians);
        Invalidate();
    }

    public void RotateInView(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateInView(axis, radians);
        Invalidate();
    }

    private bool HandleViewKey(Keys key, bool shift, bool control)
    {
        if (ArcBall is null)
        {
            return false;
        }

        switch (key)
        {
            case Keys.NumPad1: LookAlong(control ? AxisView.Back : AxisView.Front); return true;
            case Keys.NumPad3: LookAlong(control ? AxisView.Left : AxisView.Right); return true;
            case Keys.NumPad7: LookAlong(control ? AxisView.Bottom : AxisView.Top); return true;
            case Keys.NumPad9: FlipView(); return true;

            case Keys.NumPad4: RotateAroundWorldAxis(Vector3.UnitY, RotationStep); return true;
            case Keys.NumPad6: RotateAroundWorldAxis(Vector3.UnitY, -RotationStep); return true;
            case Keys.NumPad8: RotateInView(Vector3.UnitX, RotationStep); return true;
            case Keys.NumPad2: RotateInView(Vector3.UnitX, -RotationStep); return true;
        }

        if (control)
        {
            return false;
        }

        var step = shift ? -RotationStep : RotationStep;

        switch (key)
        {
            case Keys.X: RotateAroundWorldAxis(Vector3.UnitX, step); return true;
            case Keys.Y: RotateAroundWorldAxis(Vector3.UnitY, step); return true;
            case Keys.Z: RotateAroundWorldAxis(Vector3.UnitZ, step); return true;
            default: return false;
        }
    }

    #endregion

}
