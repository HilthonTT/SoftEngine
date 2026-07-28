using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Gltf;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Cli;

/// <summary>What loading a model produced, and how big it turned out to be.</summary>
internal sealed record LoadedWorld(SimpleWorld World, Vector3 Center, float Radius, int SkippedTextures);

/// <summary>
/// Reads a model file into a world, and measures it.
///
/// <para>
/// The measurement is the part that matters here. The viewer frames a model by pulling its camera
/// back from the origin, which works because a person can then orbit and dolly until it looks
/// right. A command line gets one shot: the frame it writes is the frame it was asked for. So the
/// model's extent is measured in <em>world</em> space — after the node tree, whose exported unit
/// conversions routinely scale a mesh by a hundred — and about the model's own centre rather than
/// about the origin, since a model exported standing on the ground has its origin at its feet.
/// </para>
/// </summary>
internal static class WorldLoader
{
    public static LoadedWorld Load(string path)
    {
        var world = new SimpleWorld();
        var textures = new PngTextures();

        // glTF is the one format here that carries a whole scene rather than a pile of meshes, so
        // it is read as one: the node tree becomes the world's root, the skins deform against it,
        // and any clip in the file is available to play.
        if (GltfImporter.Handles(path))
        {
            var scene = GltfImporter.Import(path, progress: null, textures.FromBytes);

            world.Root = scene.Root;
            world.Meshes.AddRange(scene.Meshes);

            foreach (var clip in scene.Clips)
            {
                world.Players.Add(new AnimationPlayer(scene.Root, clip));
            }
        }
        else
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            world.Meshes.AddRange(extension switch
            {
                ".obj" => ObjImporter.ImportObj(path, progress: null, textures.FromFile),
                ".dae" => MeshFactory.HackyImportCollada(path, progress: null),
                _ => throw new NotSupportedException($"unsupported model format '{extension}'"),
            });
        }

        // A key light, because a model file carries geometry and materials and almost never
        // carries lighting — and an unlit frame is indistinguishable from a failed render.
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });

        var (center, radius) = Measure(world);

        return new LoadedWorld(world, center, radius, textures.Skipped);
    }

    /// <summary>
    /// The centre and radius of a sphere containing every mesh, in world space.
    /// </summary>
    private static (Vector3 Center, float Radius) Measure(SimpleWorld world)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var found = false;

        foreach (var mesh in world.Meshes)
        {
            // The world matrix and its largest axis scale, not the mesh's own Scale: a mesh
            // hanging off a node inherits everything that node's transform does, and the three
            // other places in this engine that size a bounding sphere agree on exactly this.
            var reach = mesh.BoundingRadius * MeshExtensions.MaxScale(mesh.WorldMatrix);

            if (!float.IsFinite(reach))
            {
                continue;
            }

            var origin = mesh.WorldMatrix.Translation;

            min = Vector3.Min(min, origin - new Vector3(reach));
            max = Vector3.Max(max, origin + new Vector3(reach));
            found = true;
        }

        if (!found)
        {
            return (Vector3.Zero, 1f);
        }

        var center = (min + max) * 0.5f;

        // The sphere about that centre, measured again over the meshes — not half the box's
        // diagonal, which is what the box's own corners would give. A box fitted to spheres and
        // then re-measured by its diagonal inflates the radius by up to √3 for geometry that was
        // round to begin with, and the frame it produces stands nearly twice as far back as it
        // needs to. Framing is the one thing a one-shot render gets no second attempt at.
        var radius = 0f;

        foreach (var mesh in world.Meshes)
        {
            var reach = mesh.BoundingRadius * MeshExtensions.MaxScale(mesh.WorldMatrix);

            if (float.IsFinite(reach))
            {
                radius = MathF.Max(radius, Vector3.Distance(mesh.WorldMatrix.Translation, center) + reach);
            }
        }

        return (center, MathF.Max(radius, 1e-3f));
    }
}
