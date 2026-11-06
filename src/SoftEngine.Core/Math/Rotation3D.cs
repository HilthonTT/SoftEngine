using System.Numerics;

namespace SoftEngine.Core.Math;

public sealed class Rotation3D(float x, float y, float z)
{
    private const float PiDeg = (float)System.Math.PI * 1f / 180f;

    public float XPitch { get; set; } = x;

    public float YYaw { get; set; } = y;

    public float ZRoll { get; set; } = z;

    public Rotation3D ToRad() => this * PiDeg;

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
