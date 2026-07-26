using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Scenes;

public sealed class SimpleWorld : IWorld
{
    public List<IMesh> Meshes { get; set; } = [];

    public List<ILight> Lights { get; set; } = [];

    /// <summary>
    /// The root of this world's transform hierarchy. Setting it is what makes
    /// <see cref="Update"/> refresh the whole tree in one pass; a world can still parent
    /// individual meshes to nodes without one, and each skeleton then updates its own root.
    /// </summary>
    public SceneNode? Root { get; set; }

    public List<AnimationPlayer> Players { get; } = [];

    IReadOnlyList<AnimationPlayer> IWorld.Animations => Players;

    public void Update(float deltaSeconds)
    {
        foreach (var player in Players)
        {
            player.Update(deltaSeconds);
        }

        Root?.UpdateWorldMatrices();

        foreach (var mesh in Meshes)
        {
            if (mesh is not SkinnedMesh skinned)
            {
                continue;
            }

            // A skeleton rooted somewhere the world's own root pass did not reach still needs
            // its world matrices; re-walking a subtree that pass already covered is cheap
            // enough that checking properly is not worth the complexity.
            if (!ReferenceEquals(skinned.Skeleton.Root, Root))
            {
                skinned.Skeleton.Root.UpdateWorldMatrices();
            }

            skinned.Skeleton.UpdateSkinningMatrices();
            skinned.ApplyPose();
        }
    }
}
