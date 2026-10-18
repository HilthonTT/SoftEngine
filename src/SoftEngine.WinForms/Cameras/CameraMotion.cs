using System.Numerics;

namespace SoftEngine.WinForms.Cameras;

internal static class CameraMotion
{
    /// <summary>How much of the distance to the scene a wheel notch closes.</summary>
    public const float ZoomPerNotch = 1.15f;

    /// <summary>How close the camera may get to what it is looking at.</summary>
    public const float MinCameraDistance = 0.001f;

    public static float DollyDistance(float distance, float notches) =>
        MathF.Max(MinCameraDistance, distance * MathF.Pow(ZoomPerNotch, -notches));

    /// <summary>
    /// Holds the camera on its own side of what it is looking at. Flying or dollying past the
    /// pivot turns the view inside out — the scene swings around and is drawn back to front —
    /// and there is no way to tell from the picture how to get back.
    /// </summary>
    public static Vector3 ClampToPivot(Vector3 position, Vector3 previous)
    {
        // Every world here is framed from negative Z, so that is the side to stay on unless
        // the camera was explicitly placed on the other one.
        var side = previous.Z > 0f ? 1f : -1f;

        return position.Z * side >= MinCameraDistance
            ? position
            : new Vector3(position.X, position.Y, side * MinCameraDistance);
    }

    /// <summary>Shift for a coarse move, Control for a fine one.</summary>
    public static float SpeedModifier(bool shift, bool control)
    {
        var speed = 1f;

        if (shift)
        {
            speed *= 4f;
        }

        if (control)
        {
            speed *= 0.25f;
        }

        return speed;
    }

    public static bool IsMovementKey(Keys key) => key switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        _ => false,
    };

    public static Vector3 FlyDirection(IReadOnlySet<Keys> held)
    {
        var direction = Vector3.Zero;

        if (held.Contains(Keys.W) || held.Contains(Keys.Up)) { direction.Z += 1f; }
        if (held.Contains(Keys.S) || held.Contains(Keys.Down)) { direction.Z -= 1f; }
        if (held.Contains(Keys.A) || held.Contains(Keys.Left)) { direction.X += 1f; }
        if (held.Contains(Keys.D) || held.Contains(Keys.Right)) { direction.X -= 1f; }
        if (held.Contains(Keys.E)) { direction.Y -= 1f; }
        if (held.Contains(Keys.Q)) { direction.Y += 1f; }

        return direction;
    }

    // Step with the scale of the scene: models here range from a 2-unit skull to a
    // 1500-unit elephant, and a fixed step would be useless for one of them.
    public static float FlySpeed(float distance, float modifier) => MathF.Max(0.02f, distance * 0.015f) * modifier;
}
