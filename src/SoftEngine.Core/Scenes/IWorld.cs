using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Scenes;

public interface IWorld
{
    List<IMesh> Meshes { get; set; }

    List<ILight> Lights { get; set; }

    SceneNode? Root => null;

    IReadOnlyList<AnimationPlayer> Animations => [];

    void Update(float deltaSeconds)
    {
    }

    bool IsAnimated => Animations.Count > 0;
}
