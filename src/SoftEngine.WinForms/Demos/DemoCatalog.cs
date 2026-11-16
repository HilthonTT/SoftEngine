using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Interop;
using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal static class DemoCatalog
{
    public static readonly DemoDefinition[] All =
    [
        new("skull", "Skull", ModelDemos.Skull),
        new("parrot", "Parrot", ModelDemos.Parrot),
        new("parrotanim", "Parrot rig (animated)", ModelDemos.ParrotRig),
        new("bonechain", "Bone chain (skinned)", ModelDemos.BoneChainRig),
        new("julietskin", "Juliet (skinned)", ModelDemos.JulietSkinned),
        new("elefant", "Elefant", ModelDemos.Elefant),
        new("teapot", "Teapot", ModelDemos.Teapot),
        new("Juliet", "Juliet", ModelDemos.Juliet),
        new("fox", "Fox (animated glTF)", ModelDemos.Fox),
        new("waterbottle", "Water bottle (PBR glTF)", ModelDemos.WaterBottle),
        new("lantern", "Lantern (PBR glTF)", ModelDemos.Lantern),
        new("cubes", "Cubes", GeometryDemos.Cubes),
        new("spheres", "Spheres", GeometryDemos.Spheres),
        new("littletown", "Little town", GeometryDemos.LittleTown),
        new("town", "Town", GeometryDemos.Town),
        new("bigtown", "Big town", GeometryDemos.BigTown),
        new("cube", "Cube", GeometryDemos.SingleCube),
        new("bigcube", "Big cube", GeometryDemos.BigCube),
        new("texturedcube", "Textured cube", GeometryDemos.TexturedCubeScene),
        new("primitives", "Primitives", ShowcaseDemos.Primitives),
        new("transparency", "Transparency", ShowcaseDemos.Transparency),
        new("shadows", "Shadows", ShowcaseDemos.Shadows),
        new("cascades", "Cascaded shadows", ShowcaseDemos.CascadedShadows),
        new("normalmapping", "Normal mapping", ShowcaseDemos.NormalMapping),
        new("pbrspheres", "PBR spheres", ShowcaseDemos.PbrSpheres),
        new("empty", "Empty", Empty),
    ];

    public static DemoDefinition? Find(string id) =>
        Array.Find(All, demo => demo.Id == id);

    public static WorldSetup Build(string id, IProgress<float>? progress) =>
        Find(id) is { } demo ? demo.Build(progress) : Empty(progress);

    /// <summary>
    /// A projection whose far plane contains the whole world. Each mesh's reach is measured in
    /// world space — through its parent node's transform, not just its own position — since an
    /// imported rig routinely places and scales its meshes from far up the scene graph.
    /// </summary>
    public static PerspectiveProjection ProjectionFor(WorldSetup setup)
    {
        var worldRadius = 0f;

        foreach (var mesh in setup.World.Meshes)
        {
            var reach = mesh.WorldMatrix.Translation.Length() + mesh.WorldBoundingRadius();

            if (float.IsFinite(reach))
            {
                worldRadius = MathF.Max(worldRadius, reach);
            }
        }

        var far = MathF.Max(500f, (setup.CameraPosition.Length() + worldRadius) * 2f);

        return new PerspectiveProjection(DemoDefaults.FieldOfView, .01f, far);
    }

    /// <summary>
    /// Loads a model file (OBJ, Collada or glTF) into a fresh world, framing the camera and
    /// depth range from the model's own size so any scale of mesh shows up on load.
    /// </summary>
    public static WorldSetup BuildWorldFromFile(string path, IProgress<float>? progress)
    {
        var world = ModelFileLoader.Load(path, progress, ImageTexture.Load, ImageTexture.Load);

        // Measured in world space, since a glTF's node tree routinely scales its meshes.
        var radius = 0f;

        foreach (var mesh in world.Meshes)
        {
            var scaled = mesh.WorldBoundingRadius();

            if (float.IsFinite(scaled))
            {
                radius = MathF.Max(radius, mesh.WorldMatrix.Translation.Length() + scaled);
            }
        }

        if (radius <= 0f)
        {
            radius = 1f;
        }

        var cameraPosition = new Vector3(0, 0, -radius * 3f);
        var projection = new PerspectiveProjection(DemoDefaults.FieldOfView, .01f, MathF.Max(500f, radius * 20f));

        return new WorldSetup(world, cameraPosition, projection) { SkeletonTickSize = radius * 0.05f };
    }

    private static WorldSetup Empty(IProgress<float>? progress) =>
        new(new SimpleWorld(), DemoDefaults.CameraPosition, null);
}
