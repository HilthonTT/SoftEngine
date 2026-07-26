using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// Everything a Collada file contains that the engine can use: the geometry, the node
/// hierarchy it hangs off, and the animation clips that pose it.
///
/// <see cref="MeshFactory.HackyImportCollada"/> returns only the meshes, which is all a static
/// model needs. This is the same import with the parts a moving one needs kept.
/// </summary>
public sealed class ColladaScene(
    SceneNode root,
    IReadOnlyList<IMesh> meshes,
    IReadOnlyList<AnimationClip> clips,
    IReadOnlyList<SkinnedMesh> skinnedMeshes)
{
    /// <summary>The visual scene's node tree, under one synthetic root.</summary>
    public SceneNode Root { get; } = root;

    public IReadOnlyList<IMesh> Meshes { get; } = meshes;

    /// <summary>Clips found in the file. Empty when it holds nothing but a static pose.</summary>
    public IReadOnlyList<AnimationClip> Clips { get; } = clips;

    /// <summary>The subset of <see cref="Meshes"/> bound to a skeleton.</summary>
    public IReadOnlyList<SkinnedMesh> SkinnedMeshes { get; } = skinnedMeshes;

    public bool HasAnimation => Clips.Count > 0;

    public bool HasSkin => SkinnedMeshes.Count > 0;
}
