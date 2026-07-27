using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.WinForms.Extensions;
using SoftEngine.WinForms.Helpers;
using System.Numerics;

namespace SoftEngine.WinForms.Cameras;

/// <summary>
/// Orbits the view around whatever sits at the centre of the viewport: left-drag turns,
/// right- or middle-drag pans, and left+right-drag moves in and out. Every gesture is
/// measured against the frame the drag started from, so the model tracks the cursor
/// instead of drifting with the rate of mouse messages.
/// </summary>
public sealed class ArcBallCamera : ICamera
{
    /// <summary>What the drag operates on, decided by the buttons held.</summary>
    private enum Gesture { None, Orbit, Pan, Dolly }

    /// <summary>
    /// Radius of the virtual ball, in units of half the viewport's short side. Just inside
    /// the edge, so a drag across the viewport turns the model roughly half a revolution
    /// and the corners still fall on the hyperbolic skirt that keeps turning past that.
    /// </summary>
    private const float Radius = 0.9f;

    /// <summary>
    /// How much of the distance to the pivot a dolly drag covers per pixel. Applied as a
    /// ratio rather than a step, so the last stretch towards a surface is as controllable
    /// as the first — and the camera can never overshoot through it.
    /// </summary>
    private const float DollyPerPixel = 0.005f;

    /// <summary>How close the camera may get to its pivot before a dolly stops closing in.</summary>
    private const float MinDistance = 0.001f;

    // The frame the current drag is measured from: re-anchored whenever a button goes down
    // or up, so picking up a second button continues from where the first one left off
    // instead of jumping.
    private Point _dragOrigin;
    private Vector3 _dragPosition;
    private Quaternion _dragRotation;
    private float _dragPivotDepth;

    private MouseButtons _buttons;

    private Control _control;

    private Vector3 _position;

    // What the view turns about, as its depth in view space. Position.Z is the depth of the
    // world origin, which is the same point only until a pan moves the two apart; deriving
    // the pivot from it after that slides the model towards and away from the camera every
    // time the view turns.
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

            // Placing the camera outright — framing a world, flying it, zooming it — puts what
            // it turns about back on the view axis at that distance. Turning and panning are
            // the moves that leave the pivot alone, and they set the field rather than this.
            _pivotDepth = value.Z;
        }
    }

    /// <summary>
    /// The vertical field of view of the scene's projection, in radians. Panning solves for
    /// the world distance a pixel covers at the pivot's depth, which only comes out at 1:1
    /// while this matches what the scene is rendered with.
    /// </summary>
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

    /// <summary>
    /// Resynchronises the drag when the control loses the mouse. An alt-tab or a menu opening
    /// mid-drag swallows the button release, which would leave the camera stuck to the cursor;
    /// asking what is physically held ends the drag in that case without ending the ones where
    /// capture merely changed hands — releasing one button of a two-button drag drops capture
    /// too, and the other button has to keep working.
    /// </summary>
    private void Control_MouseCaptureChanged(object? sender, EventArgs e)
    {
        if (_control.Capture)
        {
            return;
        }

        _buttons &= Control.MouseButtons;
        Anchor(_control.PointToClient(Cursor.Position));
    }

    /// <summary>Restarts the gesture from the current view, and shows what the buttons now do.</summary>
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

    /// <summary>
    /// Suspends the camera gestures while something else owns the drag — the transform gizmo,
    /// which lives on the same button. Dragging a handle and orbiting are the same gesture on
    /// the same control, and only one of them can be what the user meant; whoever grabbed
    /// first wins, and this is how they say so.
    /// </summary>
    public bool GesturesSuspended { get; set; }

    private void Control_MouseMove(object? sender, MouseEventArgs e)
    {
        if (GesturesSuspended)
        {
            // Keep the anchor with the cursor, so releasing the gizmo mid-drag does not hand
            // the camera a gesture measured from wherever the pointer was when it started.
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

    /// <summary>Turns the model with the cursor.</summary>
    private void Orbit(Point location)
    {
        var from = MapToSphere(_control.NormalizeAroundCenter(_dragOrigin));
        var to = MapToSphere(_control.NormalizeAroundCenter(location));

        // Measured against the frame the drag started from, not the last mouse message.
        TurnTo(RotationBetween(from, to) * _dragRotation, _dragPosition, _dragRotation);
    }

    /// <summary>
    /// Turns the view to a new orientation about the point the viewport is centred on rather
    /// than about the world origin — after a pan those are no longer the same point, and
    /// turning about the origin would swing what you are looking at out of frame.
    /// </summary>
    private void TurnTo(Quaternion rotation, Vector3 fromPosition, Quaternion fromRotation)
    {
        rotation = Quaternion.Normalize(rotation);

        // Where the pivot sits on screen, and which world point that was to begin with.
        var pivotView = new Vector3(0, 0, _pivotDepth);
        var pivotWorld = Vector3.Transform(pivotView - fromPosition, Quaternion.Inverse(fromRotation));

        Rotation = rotation;

        // Translate so the turned pivot lands back where it was: the model spins in place.
        // Straight to the field — the pivot this just turned about is still the pivot.
        _position = pivotView - Vector3.Transform(pivotWorld, rotation);
    }

    /// <summary>
    /// Slides the view sideways, one screen pixel per pixel of drag at the pivot's depth, so
    /// the model stays under the cursor on a 2-unit skull and a 1500-unit elephant alike.
    /// </summary>
    private void Pan(Point location)
    {
        var scale = WorldUnitsPerPixel(_dragPivotDepth);

        var deltaX = (location.X - _dragOrigin.X) * scale;
        var deltaY = (location.Y - _dragOrigin.Y) * scale;

        // Screen Y grows downwards and view-space Y upwards; dragging down has to take the
        // model down with it. Sideways only, so the pivot keeps the depth it had.
        _position = _dragPosition + new Vector3(deltaX, -deltaY, 0);
    }

    /// <summary>
    /// Moves in and out along the view axis, dragging up to close in. The distance is scaled
    /// rather than stepped, which keeps the pivot in front of the camera however far the drag
    /// runs.
    /// </summary>
    private void Dolly(Point location)
    {
        var pixels = _dragOrigin.Y - location.Y;
        var sign = _dragPosition.Z > 0f ? 1f : -1f;

        var distance = MathF.Max(MinDistance, MathF.Abs(_dragPosition.Z));
        var scaled = MathF.Max(MinDistance, distance * MathF.Exp(-pixels * DollyPerPixel));

        var depth = sign * scaled;

        // The camera slides along its axis and the pivot stays where it is in the world, so
        // the two close on each other by the same amount — up to the point where the camera
        // would arrive on top of it.
        _position = new Vector3(_dragPosition.X, _dragPosition.Y, depth);
        _pivotDepth = sign * MathF.Max(MinDistance, MathF.Abs(_dragPivotDepth + depth - _dragPosition.Z));
    }

    /// <summary>
    /// The world distance one screen pixel covers at the given view depth, which is what makes
    /// a pan track the cursor instead of crawling or bolting.
    /// </summary>
    private float WorldUnitsPerPixel(float depth)
    {
        var height = MathF.Max(1f, _control.ClientSize.Height);
        var distance = MathF.Max(MinDistance, MathF.Abs(depth));

        return 2f * distance * MathF.Tan(FieldOfView * 0.5f) / height;
    }

    #region Keyed turns

    /// <summary>
    /// Snaps the view straight down a world axis, keeping the pivot centred and the distance
    /// to it — the same orbit a drag performs, just landing on an exact orientation that a
    /// mouse can only approximate.
    /// </summary>
    public void LookAlong(AxisView view) => TurnTo(RotationFor(view), Position, Rotation);

    /// <summary>Swings round to the other side of what the view is centred on.</summary>
    public void FlipView() => RotateInView(Vector3.UnitY, MathF.PI);

    /// <summary>
    /// Turns the model a fixed step about a world axis, counter-clockwise looking down the
    /// axis towards the origin. This is the axis-at-a-time counterpart to a drag: the two
    /// axes the step doesn't name come out of it untouched, which is what a freehand orbit
    /// can never quite manage.
    /// </summary>
    public void RotateAroundWorldAxis(Vector3 axis, float radians) =>
        TurnTo(Rotation * Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians), Position, Rotation);

    /// <summary>
    /// Turns the view a fixed step about an axis of the screen rather than of the world:
    /// X is the horizontal of the viewport, Y its vertical, Z the axis into it. Orbiting up
    /// and down wants this — the world axis that tips the model towards the viewer depends on
    /// where the view has got to, but the screen's horizontal is always the screen's horizontal.
    /// </summary>
    public void RotateInView(Vector3 axis, float radians) =>
        TurnTo(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians) * Rotation, Position, Rotation);

    /// <summary>
    /// The axis view the camera is currently lined up with, or null when it sits between them.
    /// </summary>
    public AxisView? CurrentAxisView
    {
        get
        {
            foreach (var view in Enum.GetValues<AxisView>())
            {
                // A quaternion and its negation are the same orientation, hence the absolute value.
                if (MathF.Abs(Quaternion.Dot(Quaternion.Normalize(Rotation), RotationFor(view))) >= 0.99995f)
                {
                    return view;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The world-to-view rotation that puts the camera on the named side of the model, upright.
    /// </summary>
    public static Quaternion RotationFor(AxisView view) => view switch
    {
        // The identity: worlds are framed from +Z, so the front view is the one they load with.
        AxisView.Front => Quaternion.Identity,
        AxisView.Back => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI),
        AxisView.Right => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f),
        AxisView.Left => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        AxisView.Top => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
        AxisView.Bottom => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f),
        _ => Quaternion.Identity,
    };

    #endregion

    /// <summary>
    /// Lifts a point on the viewport onto the virtual ball: a sphere over the middle of the
    /// frame, continued as a hyperbolic skirt outside it so a drag past the ball's edge keeps
    /// turning the model smoothly instead of sticking.
    /// </summary>
    public static Vector3 MapToSphere(Vector2 v)
    {
        // Screen Y down, world Y up.
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

    /// <summary>
    /// The shortest rotation taking one point on the ball to another. Half-angle form: the
    /// spot grabbed when the drag began ends up under the cursor, where the textbook
    /// (cross, dot) quaternion would turn twice as far and overshoot it.
    /// </summary>
    public static Quaternion RotationBetween(Vector3 startV, Vector3 currentV)
    {
        var cross = Vector3.Cross(startV, currentV);

        // Zero for both a still cursor and the degenerate antipodal case, which has no
        // shortest rotation to pick.
        if (cross.Length() <= MathConstants.Epsilon)
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(new Quaternion(cross, 1f + Vector3.Dot(startV, currentV)));
    }
}
