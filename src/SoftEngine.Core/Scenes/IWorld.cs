using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Scenes;

public interface IWorld
{
    List<IMesh> Meshes { get; set; }

    List<ILight> Lights { get; set; }

    /// <summary>
    /// The transform hierarchy the world's meshes and skeletons hang off, or null for a world
    /// whose meshes are all placed absolutely.
    /// </summary>
    SceneNode? Root => null;

    /// <summary>The clips currently playing against <see cref="Root"/>.</summary>
    IReadOnlyList<AnimationPlayer> Animations => [];

    /// <summary>
    /// Advances the world by <paramref name="deltaSeconds"/>: plays the animations, refreshes
    /// the node hierarchy, and re-deforms anything skinned.
    ///
    /// The renderer never calls this. Rendering a frame twice — which the graphics debugger
    /// does every time a probed pixel is re-recorded — must not advance time, or the second
    /// frame would not be the one being inspected.
    /// </summary>
    void Update(float deltaSeconds)
    {
    }

    /// <summary>Whether anything in this world moves on its own.</summary>
    bool IsAnimated => Animations.Count > 0;
}
