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

        // A running modal transform owns the keyboard outright, exactly as it does in Blender.
        // Anything not part of the gesture would otherwise fly the camera or turn the view while
        // a mesh is following the cursor — and there is always Escape to get out.
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

        // The editing chords, which act on the selection and only on it. Two of them are also
        // camera keys — S flies backwards, X turns the view — so they are claimed here when
        // something is selected and left alone when nothing is, and Escape drops the selection
        // to hand them back.
        if (HandleEditKey(e.KeyCode, e.Shift, e.Control, e.Alt))
        {
            e.Handled = true;
            return;
        }

        // Somewhere to come back to when a fly-through has left the model off screen.
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

    /// <summary>
    /// The Blender chords that act on the picked mesh: G moves it, S scales it, X deletes it, and
    /// Shift+A offers something new. Returns whether the key was one of them.
    ///
    /// <para>
    /// All but Shift+A are gated on there being a selection, and that is not squeamishness about
    /// stray keystrokes — it is how the two collisions with this viewer's own bindings are
    /// resolved. S already flies the camera backwards and X already turns the view 15° about world
    /// X, and both are wanted far more often than not. Tying the editing meaning to the selection
    /// gives each key one meaning at a time, decided by something the user can see (the outlined
    /// mesh) and change in one keystroke (Escape).
    /// </para>
    /// </summary>
    private bool HandleEditKey(Keys key, bool shift, bool control, bool alt)
    {
        if (control || alt)
        {
            return false;
        }

        // Ahead of the fly keys rather than after them, because A is one of them: left unclaimed,
        // Shift+A strafes four times as fast instead of offering anything.
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

            // Delete as well as X, because X is Blender's and Delete is what every other program
            // on this machine uses — and a destructive key is the last one to be coy about.
            case Keys.X:
            case Keys.Delete:
                DeleteRequested?.Invoke(this, mesh);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Raised when the viewport is asked for something to be added to the world, carrying the
    /// screen point a menu should open at.
    ///
    /// <para>
    /// The viewport reports the request rather than answering it, for the same reason it reports
    /// edits rather than recording them: what can be added is a property of the application — its
    /// menu, its undo stack, its idea of how big a new object should be in this world — and none
    /// of that belongs to a control that draws triangles.
    /// </para>
    /// </summary>
    public event EventHandler<Point>? AddRequested;

    /// <summary>Raised when the picked mesh is asked to be removed. Reported, not performed — see <see cref="AddRequested"/>.</summary>
    public event EventHandler<IMesh>? DeleteRequested;

    private void RequestAdd()
    {
        // Before the menu opens, not after. A menu takes the keyboard, so the key-up that would
        // stop a held W or A never reaches this control — and the camera would fly on behind it
        // for as long as the menu is up.
        _heldKeys.Clear();
        _moveTimer.Enabled = false;

        // At the cursor, as Blender does, unless the cursor is somewhere else entirely: the
        // chord is a keyboard gesture and the pointer need not be over the control at all.
        var location = PointToClient(Cursor.Position);

        if (!ClientRectangle.Contains(location))
        {
            location = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        }

        AddRequested?.Invoke(this, PointToScreen(location));
    }

    #region Modal transform

    /// <summary>
    /// The keyboard-driven move and scale, or null to leave G and S alone. Held here rather than
    /// created here for the same reason as <see cref="Gizmo"/>: one object has to answer what is
    /// running, what the cursor means and what a click confirms, and the application owns it
    /// because the application owns the undo stack it feeds.
    /// </summary>
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

        // The camera and the transform read the same cursor, and only one of them can have it.
        SuspendCameraGestures(true);

        // Anything held when the gesture started would go on flying the camera underneath it: the
        // keys are swallowed from here until it ends, so no key-up ever arrives to stop them.
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

            // Blender's axis constraints, and pressing the same one again lets it go. The gesture
            // is re-applied straight away because the cursor has not moved but now means something
            // different, and waiting for the next mouse message would leave the mesh mid-gesture
            // in the shape the previous constraint gave it.
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

    /// <summary>Feeds the cursor's current position to a running gesture.</summary>
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

    /// <summary>
    /// Throws away a running gesture, for a caller about to change the world out from under it.
    ///
    /// <para>
    /// Undo is the case this exists for. It is a menu shortcut, and those are dispatched ahead of
    /// the control's own key handling — so Ctrl+Z reaches the history even while the viewport
    /// believes it has the keyboard, and would otherwise leave the gesture holding a mesh against
    /// a transform the history had just put back.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The cursor in render-target samples, falling back to the middle of the viewport when the
    /// pointer is somewhere else entirely — a keyboard gesture does not require one to be here.
    /// </summary>
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

        // A gesture driven by keys the control can no longer receive has no way to be confirmed or
        // cancelled, so it is thrown away rather than left holding a mesh to the cursor.
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

        // A running gesture is confirmed by a click and thrown away by the other button — the
        // press, not the release, because that is the half of the click Blender acts on and the
        // half that must not also be read as the start of an orbit or a pick.
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

            // The release is still to come, and a click that ends a gesture must not go on to be
            // read as a pick — landing on empty background, it would deselect the mesh that was
            // just moved.
            _endedTransform = true;

            EndTransform();
            return;
        }

        // The gizmo gets first refusal on a left press. Grabbing a handle and orbiting are the
        // same gesture on the same button, so the only way to have both is for one of them to
        // be able to claim the drag — and it has to be the gizmo, since orbiting is what
        // happens everywhere else in the viewport.
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

        // A modal gesture follows the bare cursor, with no button held — so it is answered before
        // the gizmo's hover, which would otherwise light up handles the click can no longer take.
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

        // Highlighting the handle under the cursor is what makes it obvious a gizmo can be
        // grabbed at all, and which of three overlapping handles a click would take.
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
            // Null when the drag moved nothing, and Push ignores it — a handle grabbed and
            // released in place must not put an entry on the stack that undoes nothing.
            History?.Push(gizmo.End());

            SuspendCameraGestures(false);

            GizmoChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        // A left click that didn't orbit the camera picks the pixel under the cursor.
        if (e.Button == MouseButtons.Left && !_mouseDragged)
        {
            SelectPixel(ToBufferPixel(e.Location));
        }
    }

    /// <summary>
    /// A client point in render-target samples. Under supersampling one screen pixel is a
    /// block of them, and the gizmo's ray has to go through the same sample the picker's does
    /// or the handle you grab is not the handle you clicked.
    /// </summary>
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

        // Keep the notch out of the parent chain, so the viewport is the only thing it drives.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        // Accumulate, so a high-resolution wheel that reports fractions of a notch dollies
        // once per notch rather than once per message.
        _wheelDelta += e.Delta;

        // The same modifiers the keyboard fly uses, for a coarse sweep or a fine approach.
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

    /// <summary>
    /// Flies the camera with WASD (+ Q/E for down/up). The view matrix translates by the
    /// camera position after rotating, so the position is already expressed along the
    /// camera's own axes — moving the camera one way shifts the world the other.
    /// </summary>
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

    /// <summary>How far one keyed turn takes the view.</summary>
    public const float RotationStep = 15f * MathF.PI / 180f;

    /// <summary>The scene's camera when it is one that can be aimed; null for any other.</summary>
    private ArcBallCamera? ArcBall => Scene?.Camera as ArcBallCamera;

    /// <summary>Snaps the view straight down a world axis, keeping what it is centred on.</summary>
    public void LookAlong(AxisView view)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.LookAlong(view);
        Invalidate();
    }

    /// <summary>Swings the view round to the other side of what it is centred on.</summary>
    public void FlipView()
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.FlipView();
        Invalidate();
    }

    /// <summary>Turns the model one step about a world axis, leaving the other two alone.</summary>
    public void RotateAroundWorldAxis(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateAroundWorldAxis(axis, radians);
        Invalidate();
    }

    /// <summary>Turns the view one step about an axis of the screen.</summary>
    public void RotateInView(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateInView(axis, radians);
        Invalidate();
    }

    /// <summary>
    /// The keys that aim the view rather than fly it, following the numpad Blender uses as
    /// closely as a Y-up world allows. Returns whether the key was one of them.
    /// </summary>
    private bool HandleViewKey(Keys key, bool shift, bool control)
    {
        if (ArcBall is null)
        {
            return false;
        }

        switch (key)
        {
            // Down an axis, or with Control down the axis facing it.
            case Keys.NumPad1: LookAlong(control ? AxisView.Back : AxisView.Front); return true;
            case Keys.NumPad3: LookAlong(control ? AxisView.Left : AxisView.Right); return true;
            case Keys.NumPad7: LookAlong(control ? AxisView.Bottom : AxisView.Top); return true;
            case Keys.NumPad9: FlipView(); return true;

            // Orbit a step at a time: 4 and 6 spin the model on its turntable, 8 and 2 tip it
            // towards the viewer and away.
            case Keys.NumPad4: RotateAroundWorldAxis(Vector3.UnitY, RotationStep); return true;
            case Keys.NumPad6: RotateAroundWorldAxis(Vector3.UnitY, -RotationStep); return true;
            case Keys.NumPad8: RotateInView(Vector3.UnitX, RotationStep); return true;
            case Keys.NumPad2: RotateInView(Vector3.UnitX, -RotationStep); return true;
        }

        // A step about the world axis the key names — the whole point of turning by keyboard —
        // and the other way round with Shift.
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
