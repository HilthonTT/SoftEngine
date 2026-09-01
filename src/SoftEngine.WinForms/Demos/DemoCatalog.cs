using SoftEngine.Core.Scenes;

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

    private static WorldSetup Empty(IProgress<float>? progress) =>
        new(new SimpleWorld(), DemoDefaults.CameraPosition, null);
}
