using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Geometry.Import;

public class ImportedScene(
    SceneNode root,
    IReadOnlyList<IMesh> meshes,
    IReadOnlyList<AnimationClip> clips,
    IReadOnlyList<SkinnedMesh> skinnedMeshes)
{
    public SceneNode Root { get; } = root;

    public IReadOnlyList<IMesh> Meshes { get; } = meshes;

    public IReadOnlyList<AnimationClip> Clips { get; } = clips;

    public IReadOnlyList<SkinnedMesh> SkinnedMeshes { get; } = skinnedMeshes;

    public bool HasAnimation => Clips.Count > 0;

    public bool HasSkin => SkinnedMeshes.Count > 0;
}
