using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public sealed class PointLight : ILight
{
    public Vector3 Direction { get; set; }

    public Vector3 Position { get; set; }
}