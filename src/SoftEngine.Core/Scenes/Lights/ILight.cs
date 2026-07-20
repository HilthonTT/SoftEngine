using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public interface ILight
{
    Vector3 Direction { get; set; }

    Vector3 Position { get; set; }
}
