using System.Numerics;

namespace SoftEngine.Core.Math;

public sealed class Rotation3D(float x, float y, float z)
{
    private const float PiDeg = (float)System.Math.PI * 1f / 180f;

    public float XPitch { get; set; } = x;

    public float YYaw { get; set; } = y;

    public float ZRoll { get; set; } = z;

    public Rotation3D ToRad() => this * PiDeg;

    /// <summary>
    /// The same turn as a quaternion — the form two rotations can be composed in. The angles are
    /// radians, read the way <see cref="Matrix4x4.CreateFromYawPitchRoll"/> reads them.
    /// </summary>
    public Quaternion ToQuaternion() => Quaternion.CreateFromYawPitchRoll(YYaw, XPitch, ZRoll);

    /// <summary>
    /// Euler angles that turn a vector the way <paramref name="rotation"/> does. They are one
    /// answer of several — pitch is kept within ±90° — so compare what two rotations do to a
    /// vector, not their angles.
    /// </summary>
    public static Rotation3D FromQuaternion(Quaternion rotation)
    {
        // CreateFromYawPitchRoll rolls about Z, then pitches about X, then yaws about Y, so in
        // System.Numerics' row-vector form the matrix is Rz · Rx · Ry. Its third row is
        // (cos p · sin y, −sin p, cos p · cos y) and its second column (sin r · cos p, cos r · cos p, …),
        // which is where each angle is read back from.
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));

        var sinPitch = System.Math.Clamp(-m.M32, -1f, 1f);
        var pitch = MathF.Asin(sinPitch);

        if (MathF.Abs(sinPitch) > 0.99999f)
        {
            // Gimbal lock: pitched straight up or down, roll and yaw turn about the same axis and
            // only their sum is defined. Putting all of it in yaw is as good an answer as any.
            return new Rotation3D(pitch, MathF.Atan2(-m.M13, m.M11), 0f);
        }

        return new Rotation3D(pitch, MathF.Atan2(m.M31, m.M33), MathF.Atan2(m.M12, m.M22));
    }

    public static bool operator ==(Rotation3D? p, Rotation3D? other) =>
        p is null
            ? other is null
            : other is not null && p.Equals(other);

    public static bool operator !=(Rotation3D? p, Rotation3D? other) => !(other == p);

    public static Rotation3D operator +(Rotation3D p, Rotation3D other) => new(other.XPitch + p.XPitch, other.YYaw + p.YYaw, other.ZRoll + p.ZRoll);

    public static Rotation3D operator *(Rotation3D p, float a) => new(p.XPitch * a, p.YYaw * a, p.ZRoll * a);

    public static bool operator ==(Rotation3D p, Vector3 other) =>
        p.XPitch.Equals(other.X) && p.YYaw.Equals(other.Y) && p.ZRoll.Equals(other.Z);

    public static bool operator !=(Rotation3D p, Vector3 other) => !(p == other);

    public override string ToString()
    {
        return $"<{XPitch}\t{YYaw}\t{ZRoll}>";
    }

    public override bool Equals(object? obj)
    {
        // float.Equals rather than ==, so a rotation holding NaN still equals itself and
        // equality agrees with GetHashCode.
        return obj is Rotation3D d &&
               XPitch.Equals(d.XPitch) &&
               YYaw.Equals(d.YYaw) &&
               ZRoll.Equals(d.ZRoll);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(XPitch, YYaw, ZRoll);
    }
}
