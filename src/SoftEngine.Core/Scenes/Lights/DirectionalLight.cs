using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public sealed class DirectionalLight : ILight
{
    private Vector3 _direction = -Vector3.UnitY;
    private Vector3 _towardLight = Vector3.UnitY;

    public Vector3 Direction
    {
        get => _direction;
        set
        {
            _direction = value;
            _towardLight = Vector3.Normalize(-value);
        }
    }

    public float Intensity { get; set; } = 1f;

    public ColorRGB Color { get; set; } = ColorRGB.White;

    public Vector3 DirectionFrom(Vector3 worldPosition) => _towardLight;
}
