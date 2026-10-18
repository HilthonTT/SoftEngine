using SoftEngine.Core.Animation;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Dialogs;
using SoftEngine.WinForms.Interop;
using System.Numerics;

namespace SoftEngine.WinForms.Worlds;

internal static class DemoWorlds
{
    /// <summary>
    /// The vertical field of view every world is rendered with. The camera solves its pan
    /// against this too, so the two have to stay the same number.
    /// </summary>
    public const float FieldOfView = 40f * MathF.PI / 180f;

    /// <summary>The bundled worlds offered by the model picker.</summary>
    public static readonly DemoEntry[] Demos =
    [
        new("Skull", "skull"),
        new("Parrot", "parrot"),
        new("Parrot rig (animated)", "parrotanim"),
        new("Bone chain (skinned)", "bonechain"),
        new("Juliet (skinned)", "julietskin"),
        new("Elefant", "elefant"),
        new("Teapot", "teapot"),
        new("Juliet", "Juliet"),
        new("Cubes", "cubes"),
        new("Spheres", "spheres"),
        new("Little town", "littletown"),
        new("Town", "town"),
        new("Big town", "bigtown"),
        new("Cube", "cube"),
        new("Big cube", "bigcube"),
        new("Textured cube", "texturedcube"),
        new("Primitives", "primitives"),
        new("Transparency", "transparency"),
        new("Shadows", "shadows"),
        new("Cascaded shadows", "cascades"),
        new("Normal mapping", "normalmapping"),
        new("PBR spheres", "pbrspheres"),
        new("Empty", "empty"),
    ];

    /// <summary>
    /// A projection whose far plane contains the whole world from anywhere on the camera's
    /// orbit: the camera distance plus the world's farthest geometry, with headroom so
    /// dollying out a little doesn't immediately clip.
    /// </summary>
    public static PerspectiveProjection ProjectionFor(WorldSetup setup)
    {
        var worldRadius = 0f;
        foreach (var mesh in setup.World.Meshes)
        {
            var reach = mesh.WorldMatrix.Translation.Length() + mesh.WorldBoundingRadius();

            if (float.IsFinite(reach))
            {
                worldRadius = Math.Max(worldRadius, reach);
            }
        }

        var far = Math.Max(500f, (setup.CameraPosition.Length() + worldRadius) * 2f);

        return new PerspectiveProjection(FieldOfView, .01f, far);
    }

    /// <summary>
    /// Bundled models are copied next to the executable, so resolve them from the install
    /// directory — the process working directory is whatever the app was launched from.
    /// </summary>
    private static string ModelPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Models", fileName);

    /// <summary>
    /// A sway for Juliet, built against the joint names her rig actually uses. Her file has a
    /// skin but no animation — which is the common case for a downloaded character — so this
    /// is what a clip authored for an imported rig looks like.
    ///
    /// Every key is the joint's <em>rest</em> orientation with the sway composed on top. A
    /// clip read from a file holds absolute orientations because the file authored all of
    /// them; one written by hand against someone else's rig must not, or it would discard the
    /// pose she was modelled in and fold her into a heap.
    /// </summary>
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

    /// <summary>
    /// The scale a marker mesh needs to come out <paramref name="size"/> units across when it
    /// is parented to <paramref name="node"/>.
    ///
    /// A child inherits everything its parent's transform does, scale included — and exported
    /// rigs routinely carry a unit conversion on their top node, a factor of 100 in the
    /// parrot's case. A marker that ignores that is a hundred times too big on exactly the
    /// nodes it is meant to label. Dividing the node's own scale back out is what makes a
    /// marker mean "here", rather than "here, at whatever size this branch happens to use".
    /// </summary>
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

    public static WorldSetup BuildWorld(string id, IProgress<float>? progress)
    {
        var world = new SimpleWorld();
        var cameraPosition = new Vector3(0, 0, -60);
        PerspectiveProjection? projection = null;

        switch (id)
        {
            case "skull":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("skull.dae"), progress));
                cameraPosition = new Vector3(0, 0, -5);
                break;

            case "parrot":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("parrot.dae"), progress));
                cameraPosition = new Vector3(0, 0, -500);

                // A warm key and a cool fill from the other side — the classic two-light
                // setup, and the clearest demonstration that lights sum and carry colour.
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
                break;

            case "teapot":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("teapot.dae"), progress));
                break;

            case "elefant":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("elefant.dae"), progress));
                cameraPosition = new Vector3(0, 0, -1500);
                projection = new PerspectiveProjection(FieldOfView, .01f, 65535f);
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
                break;

            case "Juliet":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("Juliet.dae"), progress));
                cameraPosition = new Vector3(0, 0, -500);
                world.Lights.Add(new PointLight { Position = new Vector3(150, 200, 400) });
                break;

            case "bonechain":
            {
                // Nothing is loaded: the geometry, the rig and the clip are all generated, so
                // this demo shows the skinning path with no importer between it and the eye.
                const int bones = 7;

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

                cameraPosition = new Vector3(0, 8, -34);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 0.9f };
            }

            case "julietskin":
            {
                // A real 55,000-vertex skin off a real file — 205 joints, weights painted by
                // whoever rigged her. The file carries no animation, so the clip that bends
                // her is generated against the joint names the rig actually uses.
                var scene = ColladaImporter.ImportScene(ModelPath("Juliet.dae"), progress);

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

                cameraPosition = new Vector3(0, 0, -320);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 3f };
            }

            case "parrotanim":
            {
                // The parrot's file has the opposite half of the problem from Juliet's: a
                // twelve-second clip over a sixty-node rig, and no skin binding the mesh to
                // any of it — so there is nothing for the pose to deform, and the bird itself
                // would stand still while its skeleton danced inside it.
                //
                // A cube on each joint makes the hierarchy the model. Every cube is placed by
                // its node and nothing else, which is the scene graph doing its whole job:
                // move a wing joint and the four cubes below it go with it.
                var scene = ColladaImporter.ImportScene(ModelPath("parrot.dae"), progress);

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

                cameraPosition = new Vector3(0, 0, -230);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 5f };
            }

            case "empty":
                break;

            case "town":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 50;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "littletown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 10;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "bigtown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 200;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cube":
                world.Meshes.Add(new Cube());
                break;

            case "bigcube":
                world.Meshes.Add(new Cube() { Scale = new Vector3(100, 100, 100) });
                break;

            case "texturedcube":
                world.Meshes.Add(new TexturedCube
                {
                    Scale = new Vector3(20, 20, 20),
                    Rotation = new Rotation3D(25, 35, 0).ToRad(),
                });
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });
                break;

            case "primitives":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.8f, 0.4f) });

                // One texture across all of them, because the point of the scene is the UVs:
                // a checker shows a stretched pole, a mirrored seam or a twisted cap at a
                // glance, and a flat colour hides all three.
                var checker = Texture.Checkerboard(256, 8, new ColorRGB(225, 225, 230), new ColorRGB(98, 88, 158));

                world.Meshes.Add(new PlaneMesh(48f, 48f, 8, 8, uvScale: 12f)
                {
                    Position = new Vector3(0, -3f, 0),
                    Texture = checker,
                });

                world.Meshes.Add(new UvSphere(1.6f) { Position = new Vector3(-7.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Cylinder(1.4f, 3.2f) { Position = new Vector3(-2.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Cone(1.5f, 3.2f) { Position = new Vector3(2.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Torus(1.5f, 0.5f)
                {
                    Position = new Vector3(7.5f, -1f, 0),
                    Rotation = new Rotation3D(70, 0, 0).ToRad(),
                    Texture = checker,
                });

                cameraPosition = new Vector3(0, 2f, -16f);
                break;
            }

            case "transparency":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.7f, -1f) });

                var floor = new Cube { Position = new Vector3(0, -3.5f, 0), Scale = new Vector3(14, 0.5f, 14) };
                Array.Fill(floor.TriangleColors, ColorRGB.Gray);
                world.Meshes.Add(floor);

                var solid = new IcoSphere(2) { Position = new Vector3(0, 0, 2.5f), Scale = new Vector3(1.5f, 1.5f, 1.5f) };
                Array.Fill(solid.TriangleColors, new ColorRGB(220, 60, 50));
                world.Meshes.Add(solid);

                var glass = new IcoSphere(2) { Position = new Vector3(-1.8f, 0, 0), Scale = new Vector3(2, 2, 2), Opacity = 0.55f };
                Array.Fill(glass.TriangleColors, new ColorRGB(70, 200, 120));
                world.Meshes.Add(glass);

                var mist = new IcoSphere(2) { Position = new Vector3(1.8f, 0, -1f), Scale = new Vector3(2, 2, 2), Opacity = 0.35f };
                Array.Fill(mist.TriangleColors, new ColorRGB(80, 140, 255));
                world.Meshes.Add(mist);

                cameraPosition = new Vector3(0, 0, -12);
                break;
            }

            case "shadows":
            {
                // Nearly overhead and tilted toward the camera, so a caster's shadow lands
                // in front of it rather than behind it where it cannot be seen.
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.3f, -1f, 0.35f) });

                var ground = new Cube { Position = new Vector3(0, -4f, 0), Scale = new Vector3(26, 0.5f, 26) };
                Array.Fill(ground.TriangleColors, new ColorRGB(190, 188, 182));
                world.Meshes.Add(ground);

                var pillar = new Cube { Position = new Vector3(-5.5f, -1.2f, -1f), Scale = new Vector3(1.4f, 5f, 1.4f) };
                Array.Fill(pillar.TriangleColors, new ColorRGB(150, 120, 90));
                world.Meshes.Add(pillar);

                // Everything else floats well clear of the ground: a caster resting on the
                // floor hides its own shadow under itself.
                var beam = new Cube
                {
                    Position = new Vector3(1f, 3f, -1f),
                    Scale = new Vector3(9f, 0.5f, 0.6f),
                    Rotation = new Rotation3D(0, 0, 10).ToRad(),
                };
                Array.Fill(beam.TriangleColors, new ColorRGB(150, 120, 90));
                world.Meshes.Add(beam);

                var ball = new IcoSphere(3) { Position = new Vector3(2f, 0.2f, 3f), Scale = new Vector3(1.8f, 1.8f, 1.8f) };
                Array.Fill(ball.TriangleColors, new ColorRGB(200, 70, 60));
                world.Meshes.Add(ball);

                var small = new IcoSphere(3) { Position = new Vector3(-2f, -0.8f, 1.5f) };
                Array.Fill(small.TriangleColors, new ColorRGB(70, 150, 210));
                world.Meshes.Add(small);

                cameraPosition = new Vector3(0, 0, -24);
                break;
            }

            case "cascades":
            {
                // A colonnade running away from the eye for three hundred units — the case one
                // shadow map cannot serve. Fitted to the whole scene, its texels are metres
                // across and the near pillars' shadows come out as staircases; split into
                // cascades, the first buffer covers only the few units in front of the camera
                // and the same resolution lands where the pixels are.
                //
                // Switch the cascade count in the sidebar and watch the nearest shadow edge;
                // the Shadow map buffer view shows each cascade's own square beside the others.
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -1f, -0.15f) });

                world.Meshes.Add(ColoredBox(
                    new Vector3(0, -6f, -150f),
                    new Vector3(60f, 1f, 340f),
                    new ColorRGB(190, 188, 182)));

                for (var i = 0; i < 24; i++)
                {
                    var z = -8f - i * 13f;

                    // The far pillars are drawn in the same colours as the near ones, so any
                    // difference down the row is the shadowing rather than the shading.
                    world.Meshes.Add(ColoredBox(
                        new Vector3(-9f, -1.5f, z),
                        new Vector3(2.4f, 8f, 2.4f),
                        new ColorRGB(150, 120, 90)));

                    world.Meshes.Add(ColoredBox(
                        new Vector3(9f, -1.5f, z),
                        new Vector3(2.4f, 8f, 2.4f),
                        new ColorRGB(150, 120, 90)));

                    world.Meshes.Add(ColoredBox(
                        new Vector3(0f, 3f, z),
                        new Vector3(22f, 1f, 1.6f),
                        new ColorRGB(170, 140, 110)));
                }

                cameraPosition = new Vector3(0, -1f, 16f);
                break;
            }

            case "normalmapping":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.35f, -1f) });

                // The same albedo on both cubes, so the only difference on screen is the
                // normal map — the point being that it costs no extra geometry.
                var albedo = Texture.Checkerboard(256, 8, new ColorRGB(210, 205, 195), new ColorRGB(150, 145, 138));
                var normals = NormalMapBuilder.FromHeight(Texture.Bumps(256, 8), 3f);

                var bumpy = new TexturedCube
                {
                    Position = new Vector3(-1.2f, 0, 0),
                    Scale = new Vector3(18, 18, 18),
                    Rotation = new Rotation3D(20, 30, 0).ToRad(),
                };
                bumpy.Material.DiffuseMap = albedo;
                bumpy.Material.NormalMap = normals;
                bumpy.Material.SpecularStrength = 0.5f;
                world.Meshes.Add(bumpy);

                var flat = new TexturedCube
                {
                    Position = new Vector3(24f, 0, 0),
                    Scale = new Vector3(18, 18, 18),
                    Rotation = new Rotation3D(20, 30, 0).ToRad(),
                };
                flat.Material.DiffuseMap = albedo;
                flat.Material.SpecularStrength = 0.5f;
                world.Meshes.Add(flat);

                cameraPosition = new Vector3(-11f, 0, -70);
                break;
            }

            case "pbrspheres":
            {
                // The chart every physically-based renderer is checked against: one albedo,
                // one lighting setup, and the two parameters that describe the surface varied
                // across the grid. Roughness runs left to right, metalness bottom to top.
                //
                // What it is for is that the two rows are supposed to look like different
                // *materials* rather than like the same material at two brightnesses — the
                // metals lose their diffuse entirely and tint what they reflect, and every
                // sphere on the top row is showing you the sky rather than the lights.
                const int columns = 6;
                const int rows = 3;
                const float spacing = 2.6f;

                var albedo = new ColorRGB(222, 180, 140);

                for (var row = 0; row < rows; row++)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        var sphere = new IcoSphere(3)
                        {
                            Position = new Vector3(
                                (column - (columns - 1) / 2f) * spacing,
                                (row - (rows - 1) / 2f) * spacing,
                                0f),
                        };

                        sphere.Material.Diffuse = albedo;
                        sphere.Material.Metallic = rows == 1 ? 0f : row / (float)(rows - 1);

                        // Away from 0 at the smooth end: a perfect mirror lit by point lights
                        // shows no highlight at all, which reads as a bug rather than as the
                        // consequence of a light with no area that it is.
                        sphere.Material.Roughness = 0.06f + 0.94f * column / (columns - 1);

                        world.Meshes.Add(sphere);
                    }
                }

                // A key and a fill, so the highlights have somewhere to be. Most of what the
                // metals show, though, comes from the environment rather than from these.
                world.Lights.Add(new DirectionalLight
                {
                    Direction = new Vector3(-0.4f, -0.5f, 1f),
                    Color = new ColorRGB(255, 244, 224),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-14f, 6f, -14f),
                    Color = new ColorRGB(150, 185, 255),
                    Intensity = 0.5f,
                });

                cameraPosition = new Vector3(0, 0, -24f);
                break;
            }

            case "spheres":
            {
                int d = 5;
                int s = 2;
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new IcoSphere(2)
                            {
                                Position = new Vector3(x, y, z)
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cubes":
            {
                var d = 20;
                var s = 2;
                var r = new Random();
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new Cube()
                            {
                                Position = new Vector3(x, y, z),
                                Rotation = new Rotation3D(
                                    r.Next(-90, 90),
                                    r.Next(-90, 90),
                                    r.Next(-90, 90)).ToRad()
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }
        }

        return new WorldSetup(world, cameraPosition, projection);
    }

    /// <summary>
    /// A cube in one colour, as a mesh of its own.
    ///
    /// <see cref="Cube"/> instances share a single static colour array between them, so
    /// <c>Array.Fill</c> on one cube's colours recolours every cube in the world. A scene that
    /// wants each box a different colour therefore has to bring its own array — the geometry
    /// is still shared, since nothing in the pipeline writes to a vertex.
    /// </summary>
    private static Mesh ColoredBox(Vector3 position, Vector3 scale, ColorRGB color)
    {
        var source = new Cube();

        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, color);

        return new Mesh(source.Vertices, source.Triangles, source.NormVertices, colors)
        {
            Position = position,
            Scale = scale,
        };
    }

    /// <summary>
    /// Loads a model file (OBJ, Collada or glTF) into a fresh world, framing the camera and
    /// depth range from the model's own size so any scale of mesh shows up on load.
    /// </summary>
    public static WorldSetup BuildWorldFromFile(string path, IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        // glTF is the one format here that carries a whole scene rather than a pile of
        // meshes, so it is read as one: the node tree becomes the world's root, the skins
        // deform against it, and any clip in the file starts playing.
        if (GltfImporter.Handles(path))
        {
            var scene = GltfImporter.Import(path, progress, ImageTexture.Load);

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
                ".obj" => ObjImporter.Import(path, progress, ImageTexture.Load),
                ".dae" => ColladaImporter.HackyImportCollada(path, progress),
                _ => throw new NotSupportedException($"Unsupported model format '{extension}'."),
            });
        }

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });

        // Frame the model: pull the camera back proportional to its extent and push the far
        // plane out far enough to contain it, whatever units the file uses. The extent is
        // measured in world space, since a glTF's node tree routinely scales its meshes.
        var radius = 0f;
        foreach (var mesh in world.Meshes)
        {
            var scaled = mesh.WorldBoundingRadius();

            if (float.IsFinite(scaled))
            {
                radius = Math.Max(radius, mesh.WorldMatrix.Translation.Length() + scaled);
            }
        }

        if (radius <= 0f)
        {
            radius = 1f;
        }

        var cameraPosition = new Vector3(0, 0, -radius * 3f);
        var projection = new PerspectiveProjection(FieldOfView, .01f, Math.Max(500f, radius * 20f));

        return new WorldSetup(world, cameraPosition, projection) { SkeletonTickSize = radius * 0.05f };
    }
}
