using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Import;

public static class ModelFileLoader
{
    public static readonly Vector3 DefaultLightDirection = new(-0.35f, -0.5f, -1f);

    public static SimpleWorld Load(
        string path,
        IProgress<float>? progress = null,
        GltfImporter.TextureLoader? embeddedTextures = null,
        Func<string, Texture?>? fileTextures = null)
    {
        ArgumentNullException.ThrowIfNull(path, nameof(path));

        var world = new SimpleWorld();

        if (GltfImporter.Handles(path))
        {
            var scene = GltfImporter.Import(path, progress, embeddedTextures);

            world.Root = scene.Root;
            world.Meshes.AddRange(scene.Meshes);

            // One clip, not all of them: players over one skeleton do not mix — each writes as it
            // samples, so the last would overwrite the rest, or splice in where clips key
            // different nodes. Blending several is the AnimationMixer's job.
            if (scene.Clips.Count > 0)
            {
                world.Players.Add(new AnimationPlayer(scene.Root, scene.Clips[0]));
            }
        }
        else
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            world.Meshes.AddRange(extension switch
            {
                ".obj" => ObjImporter.Import(path, progress, fileTextures),
                ".dae" => ColladaImporter.HackyImportCollada(path, progress),
                _ => throw new NotSupportedException($"Unsupported model format '{extension}'."),
            });
        }

        world.Lights.Add(new DirectionalLight { Direction = DefaultLightDirection });

        return world;
    }
}
