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

            if (!ReferenceEquals(skinned.Skeleton.Root, Root))
            {
                skinned.Skeleton.Root.UpdateWorldMatrices();
            }

            skinned.Skeleton.UpdateSkinningMatrices();
            skinned.ApplyPose();
        }
    }
}
