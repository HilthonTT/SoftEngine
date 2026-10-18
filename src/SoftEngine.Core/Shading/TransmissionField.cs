using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Shading;

public readonly struct TransmissionField
{
    private readonly Backdrop? _backdrop;
    private readonly ScreenProjector _projector;

    public TransmissionField(Material? material, Backdrop? backdrop, in ScreenProjector projector)
    {
        Strength = material is null ? 0f : System.Math.Clamp(material.Transmission, 0f, 1f);
        IndexOfRefraction = MathF.Max(material?.IndexOfRefraction ?? 1.5f, 1f);
        Thickness = MathF.Max(material?.Thickness ?? 0f, 0f);

        _backdrop = backdrop;
        _projector = projector;
    }

    public float Strength { get; }

    public float IndexOfRefraction { get; }

    public float Thickness { get; }

    public bool IsActive => Strength > 0f && _backdrop is { IsFilled: true } && _projector.IsValid;

    public bool Behind(System.Numerics.Vector3 world, System.Numerics.Vector3 normal, System.Numerics.Vector3 view, float roughness, out LinearColor color)
    {
        color = LinearColor.Black;

        if (_backdrop is not { IsFilled: true } backdrop)
        {
            return false;
        }

        Refraction.Refract(-view, normal, 1f / IndexOfRefraction, out var direction);

        if (!_projector.Project(world + direction * Thickness, out var x, out var y))
        {
            return false;
        }

        color = backdrop.Sample(x, y, roughness);
        return true;
    }
}
