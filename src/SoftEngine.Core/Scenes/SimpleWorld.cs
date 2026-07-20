using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Scenes;

public sealed class SimpleWorld : IWorld
{
    public List<IMesh> Meshes { get; set; } = [];

    public List<ILight> Lights { get; set; } = [];
}
