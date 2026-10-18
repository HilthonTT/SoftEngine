using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Cli.Loading;

internal static class WorldLoader
{
    public static LoadedWorld Load(string path)
    {
        var textures = new PngTextures();

        var world = ModelFileLoader.Load(path, progress: null, textures.FromBytes, textures.FromFile);

        var (center, radius) = Measure(world);

        return new LoadedWorld(world, center, radius, textures.Skipped);
    }

    private static (Vector3 Center, float Radius) Measure(SimpleWorld world)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var found = false;

        foreach (var mesh in world.Meshes)
        {
            var reach = mesh.WorldBoundingRadius();

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

        var radius = 0f;

        foreach (var mesh in world.Meshes)
        {
            var reach = mesh.WorldBoundingRadius();

            if (float.IsFinite(reach))
            {
                radius = MathF.Max(radius, Vector3.Distance(mesh.WorldMatrix.Translation, center) + reach);
            }
        }

        return (center, MathF.Max(radius, 1e-3f));
    }
}
