using SoftEngine.Core.Scenes.Cameras;
using System.Numerics;

namespace SoftEngine.Cli;

/// <summary>
/// A camera placed by yaw, pitch and distance about a point it looks at.
///
/// <para>
/// The viewer's arc-ball lives in the WinForms project and would drag a UI dependency in here. It
/// also solves a different problem: an arc-ball exists to be <em>dragged</em>, and accumulates
/// the orientation a sequence of gestures left behind. A command line has no gestures — it has
/// three numbers, and the same three numbers must produce the same frame on every machine and
/// every run, which is what makes a rendered image something a script can compare against.
/// </para>
/// </summary>
internal sealed class OrbitCamera : ICamera
{
    /// <summary>The point the camera looks at, which framing puts at the middle of the model.</summary>
    public Vector3 Target { get; set; }

    public Vector3 Position { get; set; }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up());

    /// <summary>
    /// Places the camera at a bearing and a distance from its target.
    /// </summary>
    /// <param name="yaw">Rotation about the world's Y axis, in radians. Zero looks along −Z.</param>
    /// <param name="pitch">Elevation above the horizontal, in radians.</param>
    public void Orbit(float yaw, float pitch, float distance)
    {
        var cosPitch = MathF.Cos(pitch);

        var direction = new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosPitch);

        Position = Target - direction * distance;
    }

    /// <summary>
    /// The up vector, tilted away from +Y when the camera is looking almost straight down or up.
    ///
    /// A look-at matrix is undefined when the view direction is parallel to the up vector, and
    /// straight down is exactly what <c>--pitch 90</c> asks for — a perfectly reasonable request
    /// that would otherwise produce a frame of NaNs rather than a top view.
    /// </summary>
    private Vector3 Up()
    {
        var forward = Target - Position;

        if (forward.LengthSquared() < 1e-12f)
        {
            return Vector3.UnitY;
        }

        forward = Vector3.Normalize(forward);

        return MathF.Abs(forward.Y) > 0.9995f
            ? new Vector3(0f, 0f, forward.Y > 0f ? -1f : 1f)
            : Vector3.UnitY;
    }
}
