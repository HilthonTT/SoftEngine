using SoftEngine.Core.Animation;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal static class ModelDemos
{
    public static WorldSetup Skull(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.AddRange(ColladaImporter.HackyImportCollada(DemoDefaults.ModelPath("skull.dae"), progress));

        return new WorldSetup(world, new Vector3(0, 0, -5), null);
    }

    public static WorldSetup Parrot(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.AddRange(ColladaImporter.HackyImportCollada(DemoDefaults.ModelPath("parrot.dae"), progress));

        world.Lights.Add(new PointLight
        {
            Position = new Vector3(150, 200, 400),
            Color = new ColorRGB(255, 236, 205),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-300, 100, -200),
            Color = new ColorRGB(120, 170, 255),
            Intensity = 0.55f,
        });

        return new WorldSetup(world, new Vector3(0, 0, -500), null);
    }

    public static WorldSetup Teapot(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.AddRange(ColladaImporter.HackyImportCollada(DemoDefaults.ModelPath("teapot.dae"), progress));

        return new WorldSetup(world, DemoDefaults.CameraPosition, null);
    }

    public static WorldSetup Elefant(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.AddRange(ColladaImporter.HackyImportCollada(DemoDefaults.ModelPath("elefant.dae"), progress));

        world.Lights.Add(new PointLight
        {
            Position = new Vector3(500, 800, 1200),
            Color = new ColorRGB(255, 240, 214),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-900, 300, -600),
            Color = new ColorRGB(130, 175, 255),
            Intensity = 0.5f,
        });

        var projection = new PerspectiveProjection(DemoDefaults.FieldOfView, .01f, 65535f);

        return new WorldSetup(world, new Vector3(0, 0, -1500), projection);
    }

    public static WorldSetup Juliet(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        world.Meshes.AddRange(ColladaImporter.HackyImportCollada(DemoDefaults.ModelPath("Juliet.dae"), progress));

        world.Lights.Add(new PointLight { Position = new Vector3(150, 200, 400) });

        return new WorldSetup(world, new Vector3(0, 0, -500), null);
    }

    public static WorldSetup BoneChainRig(IProgress<float>? progress)
    {
        const int bones = 7;

        var world = new SimpleWorld();

        var rig = BoneChain.Create(bones, boneLength: 2.2f, radius: 0.75f, sides: 20);

        world.Root = rig.Root;
        world.Meshes.Add(rig.Mesh);
        world.Players.Add(new AnimationPlayer(rig.Root, BoneChain.Wave(bones)));

        world.Lights.Add(new PointLight
        {
            Position = new Vector3(12, 20, -18),
            Color = new ColorRGB(255, 238, 210),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-16, 6, 14),
            Color = new ColorRGB(130, 175, 255),
            Intensity = 0.5f,
        });

        return new WorldSetup(world, new Vector3(0, 8, -34), null) { SkeletonTickSize = 0.9f };
    }

    public static WorldSetup JulietSkinned(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        var scene = ColladaImporter.ImportScene(DemoDefaults.ModelPath("Juliet.dae"), progress);

        world.Root = scene.Root;
        world.Meshes.AddRange(scene.Meshes);
        world.Players.Add(new AnimationPlayer(scene.Root, JulietPose(scene.Root)));

        world.Lights.Add(new PointLight
        {
            Position = new Vector3(150, 200, 400),
            Color = new ColorRGB(255, 240, 220),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-250, 120, -200),
            Color = new ColorRGB(140, 180, 255),
            Intensity = 0.5f,
        });

        return new WorldSetup(world, new Vector3(0, 0, -320), null) { SkeletonTickSize = 3f };
    }

    public static WorldSetup ParrotRig(IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        var scene = ColladaImporter.ImportScene(DemoDefaults.ModelPath("parrot.dae"), progress);

        world.Root = scene.Root;

        foreach (var node in scene.Root.SelfAndDescendants())
        {
            if (node.Kind is SceneNodeKind.Light or SceneNodeKind.Camera || ReferenceEquals(node, scene.Root))
            {
                continue;
            }

            world.Meshes.Add(new Cube { Parent = node, Scale = MarkerScale(node, 2.2f) });
        }

        foreach (var clip in scene.Clips)
        {
            world.Players.Add(new AnimationPlayer(scene.Root, clip));
        }

        world.Lights.Add(new PointLight
        {
            Position = new Vector3(150, 200, 400),
            Color = new ColorRGB(255, 236, 205),
        });
        world.Lights.Add(new PointLight
        {
            Position = new Vector3(-300, 100, -200),
            Color = new ColorRGB(120, 170, 255),
            Intensity = 0.55f,
        });

        return new WorldSetup(world, new Vector3(0, 0, -230), null) { SkeletonTickSize = 5f };
    }

    private static AnimationClip JulietPose(SceneNode root)
    {
        const float period = 4.5f;
        const int keyCount = 32;

        (string Joint, Vector3 Axis, float Degrees, float Phase)[] motions =
        [
            ("spineAJT", Vector3.UnitZ, 4f, 0f),
            ("spineBJT", Vector3.UnitZ, 5f, 0.35f),
            ("spineCJT", Vector3.UnitZ, 5f, 0.7f),
            ("neckJT", Vector3.UnitZ, 4f, 1.05f),
            ("armJTL", Vector3.UnitZ, 12f, 0.5f),
            ("elbowJTL", Vector3.UnitZ, 14f, 1.1f),
            ("armJTR", Vector3.UnitZ, -12f, 0.5f),
            ("elbowJTR", Vector3.UnitZ, -14f, 1.1f),
        ];

        var channels = new List<NodeChannel>(motions.Length);

        foreach (var (jointName, axis, degrees, phase) in motions)
        {
            if (root.Find(jointName) is not { } joint)
            {
                continue;
            }

            var rest = joint.Rotation;
            var amplitude = degrees * MathF.PI / 180f;

            var times = new float[keyCount + 1];
            var rotations = new Quaternion[keyCount + 1];

            for (var key = 0; key <= keyCount; key++)
            {
                times[key] = period * key / keyCount;

                var angle = amplitude * MathF.Sin(MathF.Tau * key / keyCount + phase);

                rotations[key] = Quaternion.Concatenate(rest, Quaternion.CreateFromAxisAngle(axis, angle));
            }

            channels.Add(new NodeChannel(joint.Name)
            {
                Rotation = new QuaternionTrack(times, rotations),
            });
        }

        return new AnimationClip("Sway", channels);
    }

    private static Vector3 MarkerScale(SceneNode node, float size)
    {
        if (!Matrix4x4.Decompose(node.WorldMatrix, out var scale, out _, out _))
        {
            return new Vector3(size);
        }

        return new Vector3(
            size / MathF.Max(MathF.Abs(scale.X), 1e-4f),
            size / MathF.Max(MathF.Abs(scale.Y), 1e-4f),
            size / MathF.Max(MathF.Abs(scale.Z), 1e-4f));
    }
}
