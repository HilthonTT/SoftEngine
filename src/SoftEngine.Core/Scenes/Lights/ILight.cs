using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public interface ILight
{
    float Intensity { get; }

    ColorRGB Color => ColorRGB.White;

    Vector3 DirectionFrom(Vector3 worldPosition);

    float AttenuationAt(Vector3 worldPosition) => 1f;
}
