using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// Everything a model file contains that the engine can use: the geometry, the node hierarchy
/// it hangs off, the skins bound to that hierarchy, and the animation clips that pose it.
///
/// One type for both scene importers, because a scene is a scene — the Collada and glTF
/// readers disagree about matrix conventions, chunk layout and where a material's roughness
/// lives, and about nothing downstream of that. <see cref="MeshFactory.HackyImportCollada"/>
/// and <see cref="ObjImporter.ImportObj"/> return bare meshes instead, which is all a static
/// model needs.
/// </summary>
public class ImportedScene(
    SceneNode root,
    IReadOnlyList<IMesh> meshes,
    IReadOnlyList<AnimationClip> clips,
    IReadOnlyList<SkinnedMesh> skinnedMeshes)
{
    /// <summary>The file's node tree, under one synthetic root.</summary>
    public SceneNode Root { get; } = root;

    public IReadOnlyList<IMesh> Meshes { get; } = meshes;

    /// <summary>Clips found in the file. Empty when it holds nothing but a static pose.</summary>
    public IReadOnlyList<AnimationClip> Clips { get; } = clips;

    /// <summary>The subset of <see cref="Meshes"/> bound to a skeleton.</summary>
    public IReadOnlyList<SkinnedMesh> SkinnedMeshes { get; } = skinnedMeshes;

    public bool HasAnimation => Clips.Count > 0;

    public bool HasSkin => SkinnedMeshes.Count > 0;
}
