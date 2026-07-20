using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Scenes;

public interface IWorld
{
    List<IMesh> Meshes { get; set; }

    List<ILight> Lights { get; set; }
}
