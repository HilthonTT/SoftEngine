using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// What <see cref="MeshFactory.ImportColladaScene(string, IProgress{float}?)"/> returns: an
/// <see cref="ImportedScene"/> under the name it had before there was a second scene importer
/// to share the type with. It adds nothing of its own.
/// </summary>
public sealed class ColladaScene(
    SceneNode root,
    IReadOnlyList<IMesh> meshes,
    IReadOnlyList<AnimationClip> clips,
    IReadOnlyList<SkinnedMesh> skinnedMeshes)
    : ImportedScene(root, meshes, clips, skinnedMeshes);
