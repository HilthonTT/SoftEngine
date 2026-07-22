using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

/// <summary>A sun-like light: parallel rays, the same direction everywhere.</summary>
public sealed class DirectionalLight : ILight
{
    private Vector3 _direction = -Vector3.UnitY;
    private Vector3 _towardLight = Vector3.UnitY;

    /// <summary>The direction the light travels in (it does not need to be normalized).</summary>
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

    public Vector3 DirectionFrom(Vector3 worldPosition) => _towardLight;
}
