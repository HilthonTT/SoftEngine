using SoftEngine.Core.Animation;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Math;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Golden;

/// <summary>
/// One frame the suite keeps a picture of.
///
/// <para>
/// The scenes are chosen the way the benchmark scenes are, but along a different axis. A
/// benchmark covers the shapes of *work* the renderer does; a baseline covers the paths that
/// produce a *value* — every painter, the shadow pass and its cascades, the material and
/// physically-based shading models, transparency, fog, the post-process effects, skinning, and
/// the resolve at the end. Each is a body of arithmetic no unit test pins down end to end, and
/// each is one edit away from being quietly wrong in a way only the frame shows.
/// </para>
///
/// <para>
/// Everything is generated: no scene loads a file. A baseline that depends on a model in the
/// front-end's assets folder is a baseline that breaks when the model is re-exported, which
/// teaches everyone to re-record on failure without looking — the exact habit the harness is
/// built to prevent.
/// </para>
/// </summary>
internal sealed class GoldenScene(string name, string description, Action<GoldenScene.Build> build)
{
    /// <summary>Small enough that thirteen of them are a fast test and a modest set of files, large enough to see a shading change in.</summary>
    public const int Width = 320;

    public const int Height = 180;

    /// <summary>The renderer, scene and painter a case assembles, handed to it half-built.</summary>
    internal sealed class Build
    {
        public required Renderer Renderer { get; init; }

        public required Scene Scene { get; init; }

        public IPainter Painter { get; set; } = new GouraudPainter();

        /// <summary>
        /// Supersampling factor. Above 1 the whole pipeline runs at a multiple of the display
        /// resolution and the frame is averaged back down, which is the one case where the
        /// image verified is not the render target itself.
        /// </summary>
        public int SuperSample { get; set; } = 1;

        public SimpleWorld World => (SimpleWorld)Scene.World;

        public List<IMesh> Meshes => World.Meshes;

        public List<ILight> Lights => World.Lights;
    }

    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>Renders this scene and returns the finished image, together with its dimensions.</summary>
    /// <param name="occlusionCulling">
    /// Left on, as the renderer leaves it. Switching it off is how a test asks whether the pass
    /// changed the picture — which it must not, since it only ever decides what to skip.
    /// </param>
    public (int[] Pixels, int Width, int Height) Render(bool occlusionCulling = true)
    {
        var renderer = new Renderer();
        renderer.Settings.BackFaceCulling = true;
        renderer.Settings.OcclusionCulling = occlusionCulling;

        // The event log allocates nothing but does real work per mesh, and none of it can
        // reach a pixel. A baseline should be a recording of the renderer, not of its debugger.
        renderer.Diagnostics.Events.IsEnabled = false;

        var built = new Build
        {
            Renderer = renderer,
            Scene = new Scene
            {
                Surface = new FrameBuffer(Width, Height) { Stats = renderer.Stats },
                Camera = new StillCamera(new Vector3(0f, 0f, 5f), Vector3.Zero),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 200f),
                World = new SimpleWorld { Meshes = [], Lights = [] },
            },
        };

        build(built);

        var scene = built.Scene;
        var factor = SuperSampler.ClampFactor(built.SuperSample);

        if (factor > 1)
        {
            scene.Surface = new FrameBuffer(Width * factor, Height * factor) { Stats = renderer.Stats };
        }

        renderer.Render(scene, built.Painter);

        if (factor == 1)
        {
            return (scene.Surface.Screen, Width, Height);
        }

        var resolved = new int[Width * Height];
        SuperSampler.Resolve(scene.Surface, resolved, Width, Height, factor);

        return (resolved, Width, Height);
    }

    /// <summary>A camera that does not move, so a baseline is a function of the scene alone.</summary>
    private sealed class StillCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix { get; } = Matrix4x4.CreateLookAt(position, target, Vector3.UnitY);
    }

    public override string ToString() => Name;

    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.8f, -0.4f));

    public static IReadOnlyList<GoldenScene> All { get; } =
    [
        new("classic-flat-gouraud",
            "one sphere per unlit and per-triangle painter, side by side",
            b =>
            {
                // Three painters cannot draw one frame, so the case that covers the cheap end
                // of the ladder is Gouraud over geometry whose facets are large enough that
                // per-vertex interpolation is unmistakable.
                b.Meshes.Add(Sphere(2, new Vector3(-1.3f, 0f, 0f), 1f, new ColorRGB(210, 90, 70)));
                b.Meshes.Add(Sphere(4, new Vector3(1.3f, 0f, 0f), 1f, new ColorRGB(70, 150, 210)));
                b.Lights.Add(Sun(1.1f));
                b.Painter = new GouraudPainter();
            }),

        new("phong-point-and-spot",
            "a sphere and a ground plane under a warm point light and a cool spot",
            b =>
            {
                b.Meshes.Add(Sphere(4, new Vector3(0f, 0.35f, 0f), 1f, ColorRGB.Gray));
                b.Meshes.Add(Ground(6f, -0.7f, new ColorRGB(150, 148, 140)));

                // Two coloured lights rather than one white one: the whole point of summing
                // over a light list is that the lit side and the fill differ in hue, and a
                // single light would verify none of it.
                b.Lights.Add(new PointLight
                {
                    Position = new Vector3(2.4f, 2.6f, 2.2f),
                    Color = new ColorRGB(255, 214, 170),
                    Intensity = 1.3f,
                    Range = 14f,
                });

                b.Lights.Add(new SpotLight
                {
                    Position = new Vector3(-2.6f, 3.2f, 1.4f),
                    Direction = Vector3.Normalize(new Vector3(0.6f, -1f, -0.35f)),
                    Color = new ColorRGB(150, 190, 255),
                    Intensity = 1.6f,
                });

                b.Scene.Camera = Look(new Vector3(0f, 1.9f, 5.2f), new Vector3(0f, 0.1f, 0f));
                b.Painter = new PhongPainter();
            }),

        new("shadows-single-map",
            "the shadow pass from one light, over a ground plane",
            b =>
            {
                b.Meshes.Add(Sphere(4, new Vector3(-0.9f, 0.55f, 0.3f), 0.85f, new ColorRGB(206, 108, 88)));
                b.Meshes.Add(Cube(new Vector3(1.1f, 0.35f, -0.4f), 0.7f, new ColorRGB(96, 150, 116)));
                b.Meshes.Add(Ground(7f, -0.35f, new ColorRGB(158, 156, 150)));

                b.Lights.Add(Sun(1.25f));

                b.Scene.Shadows.Enabled = true;
                b.Scene.Camera = Look(new Vector3(0.6f, 2.4f, 5.4f), new Vector3(0f, 0.2f, 0f));
                b.Painter = new PhongPainter();
            }),

        new("shadows-three-cascades",
            "the same pass split into three cascades over a long view",
            b =>
            {
                // A receding row is the case cascades exist for: the near cubes want texels
                // the far ones would otherwise take an equal share of.
                for (var i = 0; i < 9; i++)
                {
                    b.Meshes.Add(Cube(new Vector3(i % 2 == 0 ? -1.1f : 1.1f, 0.5f, -i * 3.1f), 0.5f,
                        new ColorRGB((byte)(90 + i * 16), 140, (byte)(200 - i * 14))));
                }

                b.Meshes.Add(Ground(34f, 0f, new ColorRGB(150, 150, 146)));

                b.Lights.Add(Sun(1.2f));

                b.Scene.Shadows.Enabled = true;
                b.Scene.Shadows.CascadeCount = 3;
                b.Scene.Camera = Look(new Vector3(0f, 2.2f, 4.2f), new Vector3(0f, 0.6f, -8f));
                b.Painter = new PhongPainter();
            }),

        new("textured-mipmaps",
            "a checkerboard floor running to the horizon",
            b =>
            {
                // The one scene where mip selection is the subject. A checkerboard receding to
                // the horizon aliases into noise the moment the chain or the level choice is
                // wrong, and does it in a way no per-pixel assertion would describe.
                var floor = Ground(40f, -1f, ColorRGB.White);
                floor.TexCoords = [new Vector2(0f, 0f), new Vector2(16f, 0f), new Vector2(16f, 16f), new Vector2(0f, 16f)];
                floor.Texture = Texture.Checkerboard(128, 8, new ColorRGB(232, 228, 216), new ColorRGB(52, 60, 78));
                floor.Texture.EnsureMipMaps();

                b.Meshes.Add(floor);
                b.Lights.Add(Sun(1.1f));

                b.Scene.Camera = Look(new Vector3(0f, 0.6f, 6f), new Vector3(0f, -0.9f, -14f));
                b.Painter = new TexturedPainter();
            }),

        new("material-normal-mapping",
            "two cubes, one with a normal map and one without",
            b =>
            {
                var albedo = Texture.Checkerboard(64, 4, new ColorRGB(214, 206, 190), new ColorRGB(120, 112, 100));
                var normals = NormalMapBuilder.FromHeight(Texture.Bumps(64, 4), 1.6f);

                var mapped = new TexturedCube(albedo)
                {
                    Position = new Vector3(-1.25f, 0f, 0f),
                    Rotation = new Rotation3D(0.5f, 0.7f, 0f),
                };

                mapped.Material.NormalMap = normals;
                mapped.Material.SpecularStrength = 0.5f;

                var plain = new TexturedCube(albedo)
                {
                    Position = new Vector3(1.25f, 0f, 0f),
                    Rotation = new Rotation3D(0.5f, 0.7f, 0f),
                };

                plain.Material.SpecularStrength = 0.5f;

                b.Meshes.Add(mapped);
                b.Meshes.Add(plain);
                b.Lights.Add(Sun(1.3f));
                b.Painter = new MaterialPainter();
            }),

        new("pbr-roughness-metalness",
            "a grid of spheres across roughness and metalness, lit by its environment",
            b =>
            {
                for (var x = 0; x < 5; x++)
                {
                    for (var y = 0; y < 3; y++)
                    {
                        var sphere = Sphere(3, new Vector3(x - 2f, y - 1f, 0f) * 1.15f, 0.5f, ColorRGB.Gray);

                        // On the material rather than the triangle colours, which this painter
                        // only falls back to when a material has nothing to say. A warm albedo
                        // rather than a grey one because the metallic row *tints its reflection
                        // with it* — against grey, a broken tint and a working one look alike.
                        sphere.Material.Diffuse = new ColorRGB(214, 162, 92);
                        sphere.Material.Roughness = (x + 0.5f) / 5f;
                        sphere.Material.Metallic = y / 2f;

                        b.Meshes.Add(sphere);
                    }
                }

                b.Lights.Add(Sun(1.2f));

                // The environment is half of what this painter reads: the split-sum lookup and
                // the ambient cube both come off it, and with no sky the grid would be testing
                // the direct term alone.
                b.Scene.Environment = SkyBox.Gradient(SunDirection, resolution: 32);
                b.Scene.HighDynamicRange = true;
                b.Scene.GammaCorrect = true;
                b.Scene.Camera = Look(new Vector3(0f, 0f, 6.6f), Vector3.Zero);
                b.Painter = new PbrPainter();
            }),

        new("transparency-and-sky",
            "three tinted panes over an opaque cube, against the sky",
            b =>
            {
                b.Meshes.Add(Cube(new Vector3(0f, 0f, -1.2f), 0.9f, new ColorRGB(196, 92, 76)));

                // Overlapping, at stepped depths, so the frame is only right if the panes were
                // sorted farthest-first and blended in linear light.
                b.Meshes.Add(Pane(new Vector3(-0.55f, 0.25f, 0.6f), 1.5f, new ColorRGB(90, 200, 140), 0.45f));
                b.Meshes.Add(Pane(new Vector3(0.15f, -0.1f, 1.1f), 1.5f, new ColorRGB(120, 140, 235), 0.5f));
                b.Meshes.Add(Pane(new Vector3(0.7f, 0.35f, 1.6f), 1.5f, new ColorRGB(240, 200, 90), 0.4f));

                b.Lights.Add(Sun(1.1f));

                b.Scene.Environment = SkyBox.Gradient(SunDirection, resolution: 32);
                b.Scene.GammaCorrect = true;
                b.Scene.Camera = Look(new Vector3(0.4f, 0.6f, 4.6f), Vector3.Zero);
                b.Painter = new PhongPainter();
            }),

        new("post-process-stack",
            "an HDR frame through bloom, tone mapping, FXAA and the vignette",
            b =>
            {
                b.Meshes.Add(Sphere(4, new Vector3(-1.2f, 0f, 0f), 0.9f, new ColorRGB(230, 232, 236)));
                b.Meshes.Add(Cube(new Vector3(1.15f, 0f, 0.2f), 0.75f, new ColorRGB(180, 70, 60)));

                // Bright enough to sit well above white, which is the whole reason the target
                // is a float buffer: what bloom gathers and what the curve rolls off are both
                // range an 8-bit target would have flattened before either ran.
                var lamp = Sphere(3, new Vector3(0.1f, 1.35f, 1f), 0.28f, ColorRGB.White);
                lamp.Material.Emissive = new ColorRGB(255, 236, 190);
                lamp.Material.EmissiveStrength = 7f;

                b.Meshes.Add(lamp);
                b.Meshes.Add(Ground(6f, -1.05f, new ColorRGB(120, 124, 132)));

                // The emissive lamp carries the range this case is about; the sun only has to
                // light the rest, and a brighter one would clip the sphere into a flat white
                // disc that no change to the curve could ever move.
                b.Lights.Add(Sun(1.3f));

                b.Scene.HighDynamicRange = true;
                b.Scene.GammaCorrect = true;

                var stack = PostProcessStack.CreateDefault();
                stack.Find<BloomEffect>()!.Enabled = true;
                stack.Find<ToneMapEffect>()!.Enabled = true;
                stack.Find<FxaaEffect>()!.Enabled = true;
                stack.Find<VignetteEffect>()!.Enabled = true;

                b.Renderer.PostProcess = stack;
                b.Scene.Camera = Look(new Vector3(0f, 0.7f, 5f), Vector3.Zero);
                b.Painter = new PhongPainter();
            }),

        new("ambient-occlusion",
            "boxes meeting a floor, with the depth-buffer occlusion pass on",
            b =>
            {
                // Contact points and creases are the whole subject, so the geometry is chosen
                // to have them: three boxes sitting on a floor, close enough to shade each
                // other's corners.
                b.Meshes.Add(Cube(new Vector3(-1.05f, -0.35f, 0f), 0.65f, new ColorRGB(198, 196, 190)));
                b.Meshes.Add(Cube(new Vector3(0.1f, -0.45f, 0.55f), 0.55f, new ColorRGB(198, 196, 190)));
                b.Meshes.Add(Cube(new Vector3(1.15f, -0.25f, -0.15f), 0.75f, new ColorRGB(198, 196, 190)));
                b.Meshes.Add(Ground(6f, -1f, new ColorRGB(206, 204, 198)));

                b.Lights.Add(Sun(1f));

                var stack = PostProcessStack.CreateDefault();
                var ssao = stack.Find<SsaoEffect>()!;
                ssao.Enabled = true;
                ssao.Strength = 0.85f;
                ssao.Radius = 0.45f;

                b.Renderer.PostProcess = stack;
                b.Scene.Camera = Look(new Vector3(0f, 1.4f, 4.6f), new Vector3(0f, -0.35f, 0f));
                b.Painter = new PhongPainter();
            }),

        new("exponential-fog",
            "a receding row of cubes dissolving into fog",
            b =>
            {
                for (var i = 0; i < 12; i++)
                {
                    b.Meshes.Add(Cube(new Vector3(i % 2 == 0 ? -1.2f : 1.2f, 0f, -i * 2.4f), 0.6f,
                        new ColorRGB(220, 120, 90)));
                }

                b.Lights.Add(Sun(1.2f));

                b.Scene.Fog.Enabled = true;
                b.Scene.Fog.Mode = FogMode.Exponential;
                b.Scene.Fog.Density = 0.055f;
                b.Scene.Fog.Color = new ColorRGB(178, 190, 205);

                b.Scene.Camera = Look(new Vector3(0f, 1.1f, 4.5f), new Vector3(0f, 0f, -10f));
                b.Painter = new GouraudPainter();
            }),

        new("skinned-bone-chain",
            "the generated rig mid-pose, with the wireframe over it",
            b =>
            {
                var rig = BoneChain.Create(boneCount: 6, boneLength: 0.8f, radius: 0.34f);

                b.World.Root = rig.Root;
                b.World.Players.Add(new AnimationPlayer(rig.Root, BoneChain.Wave(6)));
                b.Meshes.Add(rig.Mesh);
                b.Lights.Add(Sun(1.2f));

                // One step of a fixed length rather than however long the last frame took:
                // the pose is part of what the baseline records, so the clock has to be too.
                b.World.Update(0.75f);

                // No wireframe, though seeing the rings crowd on the inside of a bend is how a
                // person reads a pose. At this size the overlay covers the tube in magenta and
                // the shading underneath stops being verified at all — and the overlay has a
                // case of its own further down.
                b.Scene.Camera = Look(new Vector3(2.6f, 2.4f, 3.6f), new Vector3(0f, 2.1f, 0f));
                b.Painter = new GouraudPainter();
            }),

        new("supersampled-2x",
            "hard silhouettes resolved from a frame rendered at twice the size",
            b =>
            {
                b.Meshes.Add(Cube(new Vector3(-0.9f, 0.1f, 0f), 0.8f, new ColorRGB(40, 46, 60)));
                b.Meshes.Add(Sphere(3, new Vector3(1f, -0.1f, 0.4f), 0.75f, new ColorRGB(230, 226, 214)));

                b.Meshes[0].Rotation = new Rotation3D(0.3f, 0.55f, 0.15f);

                b.Lights.Add(Sun(1.15f));

                b.SuperSample = 2;
                b.Scene.Camera = Look(new Vector3(0f, 0.3f, 4.4f), Vector3.Zero);
                b.Painter = new GouraudPainter();
            }),

        new("occlusion-wall",
            "a wall with meshes hidden behind it and meshes reaching past its edges",
            b =>
            {
                // Enough meshes for the occlusion pass to consider the scene worth its time, so
                // the committed baseline is a picture of a frame the pass really did act on.
                // What it pins is the invariant: the culled frame and the uncalled one are the
                // same picture, and the picture is this one.
                b.Meshes.Add(Wall(1.6f, 0f, new ColorRGB(120, 126, 140)));

                for (var i = 0; i < 40; i++)
                {
                    var angle = i * MathF.Tau / 40f;
                    var reach = 1f + (i % 5) * 0.5f;

                    b.Meshes.Add(Sphere(
                        2,
                        new Vector3(MathF.Cos(angle) * reach * 1.6f, MathF.Sin(angle) * reach, -1.5f - (i % 7) * 0.4f),
                        0.28f,
                        new ColorRGB((byte)(90 + i * 4), 170, (byte)(220 - i * 4))));
                }

                b.Lights.Add(Sun(1.2f));

                b.Scene.Camera = Look(new Vector3(0f, 0f, 5f), Vector3.Zero);
                b.Painter = new PhongPainter();
            }),

        new("overlays-grid-axes-wireframe",
            "the gizmos drawn over a shaded frame",
            b =>
            {
                b.Meshes.Add(Cube(new Vector3(0f, 0.5f, 0f), 0.7f, new ColorRGB(150, 170, 200)));

                b.Lights.Add(Sun(1.1f));

                b.Renderer.Settings.ShowXZGrid = true;
                b.Renderer.Settings.ShowAxes = true;
                b.Renderer.Settings.ShowTriangles = true;

                b.Scene.Camera = Look(new Vector3(3.4f, 2.8f, 4.6f), new Vector3(0f, 0.2f, 0f));
                b.Painter = new GouraudPainter();
            }),
    ];

    private static ICamera Look(Vector3 position, Vector3 target) => new StillCamera(position, target);

    private static DirectionalLight Sun(float intensity) =>
        new() { Direction = SunDirection, Intensity = intensity };

    /// <summary>
    /// A sphere as a plain <see cref="Mesh"/> rather than the primitive itself: primitives
    /// share one static colour array between every instance, so a scene of them would be
    /// several views of the same colours.
    /// </summary>
    private static Mesh Sphere(int recursion, Vector3 position, float radius, ColorRGB color)
    {
        var source = new IcoSphere(recursion);
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, color);

        return new Mesh(
            (Vector3[])source.Vertices.Clone(),
            source.Triangles,
            (Vector3[])source.NormVertices.Clone(),
            colors)
        {
            Position = position,
            Scale = new Vector3(radius),
        };
    }

    private static Mesh Cube(Vector3 position, float half, ColorRGB color)
    {
        var source = new Cube();
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, color);

        return new Mesh(
            (Vector3[])source.Vertices.Clone(),
            source.Triangles,
            (Vector3[])source.NormVertices.Clone(),
            colors)
        {
            Position = position,
            Scale = new Vector3(half),
        };
    }

    private static Mesh Ground(float extent, float y, ColorRGB color)
    {
        Vector3[] vertices =
        [
            new(-extent, y, -extent),
            new(extent, y, -extent),
            new(extent, y, extent),
            new(-extent, y, extent),
        ];

        Triangle[] triangles = [new(0, 2, 1), new(0, 3, 2)];

        return new Mesh(
            vertices,
            triangles,
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
            [color, color]);
    }

    /// <summary>A square facing the camera, large enough to stand in front of things.</summary>
    private static Mesh Wall(float half, float z, ColorRGB color)
    {
        Vector3[] vertices =
        [
            new(-half, -half, z),
            new(half, -half, z),
            new(half, half, z),
            new(-half, half, z),
        ];

        Triangle[] triangles = [new(0, 1, 2), new(0, 2, 3)];

        return new Mesh(
            vertices,
            triangles,
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [color, color]);
    }

    /// <summary>A square facing the camera, used where the subject is what happens when they overlap.</summary>
    private static Mesh Pane(Vector3 position, float size, ColorRGB color, float opacity)
    {
        var half = size * 0.5f;

        Vector3[] vertices =
        [
            new(-half, -half, 0f),
            new(half, -half, 0f),
            new(half, half, 0f),
            new(-half, half, 0f),
        ];

        Triangle[] triangles = [new(0, 1, 2), new(0, 2, 3)];

        // Facing the camera, which sits on +Z. A pane normal pointing away would leave every
        // one of them unlit, and the case would verify the blend over a set of black squares.
        var normal = Vector3.UnitZ;

        return new Mesh(vertices, triangles, [normal, normal, normal, normal], [color, color])
        {
            Position = position,
            Opacity = opacity,
        };
    }
}
