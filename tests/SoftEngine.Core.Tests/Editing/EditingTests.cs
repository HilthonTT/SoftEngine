using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Editing;

public class EditingTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    [Fact]
    public void Duplicate_SharesTheGeometryAndNotTheTransform()
    {
        var original = new Cube
        {
            Position = new Vector3(1f, 2f, 3f),
            Scale = new Vector3(2f, 2f, 2f),
            Opacity = 0.5f,
        };

        var copy = original.Duplicate();

        Assert.Same(original.Vertices, copy.Vertices);
        Assert.Same(original.Triangles, copy.Triangles);

        Assert.Equal(original.Position, copy.Position);
        Assert.Equal(original.Scale, copy.Scale);
        Assert.Equal(original.Opacity, copy.Opacity);

        copy.Position = new Vector3(-5f, 0f, 0f);
        Assert.Equal(new Vector3(1f, 2f, 3f), original.Position);
    }

    [Fact]
    public void Duplicate_DoesNotShareTriangleColours()
    {
        var original = new Cube();
        var copy = original.Duplicate();

        Assert.NotSame(original.TriangleColors, copy.TriangleColors);

        Array.Fill(copy.TriangleColors, ColorRGB.White);

        Assert.NotEqual(ColorRGB.White.Color, original.TriangleColors[0].Color);
    }

    [Fact]
    public void Duplicate_KeepsTheRotationSeparate()
    {
        var original = new Cube();
        original.Rotation.YYaw = 0.5f;

        var copy = original.Duplicate();

        Assert.Equal(0.5f, copy.Rotation.YYaw, 5);

        copy.Rotation.YYaw = 1.5f;

        Assert.Equal(0.5f, original.Rotation.YYaw, 5);
    }

    [Fact]
    public void MeshListEdit_AddsAndUndoes()
    {
        var world = new SimpleWorld();
        var first = new Cube();
        world.Meshes.Add(first);

        var copy = first.Duplicate();
        var edit = MeshListEdit.Add(world, copy, 1, "Duplicate");

        Assert.Equal(2, world.Meshes.Count);
        Assert.Same(copy, world.Meshes[1]);
        Assert.Contains("Duplicate", edit.Description);

        edit.Revert();
        Assert.Single(world.Meshes);

        edit.Apply();
        Assert.Equal(2, world.Meshes.Count);
    }

    [Fact]
    public void MeshListEdit_RemovesAndPutsItBackWhereItWas()
    {
        var world = new SimpleWorld();

        var a = new Cube();
        var b = new Cube();
        var c = new Cube();

        world.Meshes.AddRange([a, b, c]);

        var edit = MeshListEdit.Remove(world, b);

        Assert.NotNull(edit);
        Assert.Equal([a, c], world.Meshes);

        edit!.Revert();

        Assert.Equal([a, b, c], world.Meshes);
    }

    [Fact]
    public void MeshListEdit_RemovingWhatIsNotThereIsNotAnEdit()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube());

        Assert.Null(MeshListEdit.Remove(world, new Cube()));
    }

    [Fact]
    public void MeshListEdit_RoundTripsThroughTheHistory()
    {
        var world = new SimpleWorld();
        var cube = new Cube();
        world.Meshes.Add(cube);

        var history = new EditHistory();

        history.Push(MeshListEdit.Add(world, cube.Duplicate(), 1, "Duplicate"));
        Assert.Equal(2, world.Meshes.Count);

        history.Undo();
        Assert.Single(world.Meshes);

        history.Redo();
        Assert.Equal(2, world.Meshes.Count);

        history.Push(MeshListEdit.Remove(world, world.Meshes[0]));
        Assert.Single(world.Meshes);

        history.Undo();
        Assert.Equal(2, world.Meshes.Count);
        Assert.Same(cube, world.Meshes[0]);
    }

    [Fact]
    public void CompositeEdit_UndoesEverythingAtOnce()
    {
        var world = new SimpleWorld();

        var meshes = new[] { new Cube(), new Cube(), new Cube() };
        world.Meshes.AddRange(meshes);

        var before = meshes.Select(TransformState.Of).ToArray();

        foreach (var mesh in meshes)
        {
            mesh.Position = new Vector3(4f, 0f, 0f);
        }

        var edits = meshes.Select((mesh, i) => (IEditCommand?)TransformEdit.Between(mesh, before[i], "Move")).ToArray();
        var composite = CompositeEdit.Combine(edits, "Move 3 meshes");

        Assert.NotNull(composite);

        var history = new EditHistory();
        history.Push(composite);

        Assert.Equal("Move 3 meshes", history.NextUndo);

        history.Undo();

        foreach (var mesh in meshes)
        {
            Assert.Equal(Vector3.Zero, mesh.Position);
        }

        history.Redo();

        foreach (var mesh in meshes)
        {
            Assert.Equal(new Vector3(4f, 0f, 0f), mesh.Position);
        }
    }

    [Fact]
    public void CompositeEdit_CollapsesWhenThereIsNothingToGroup()
    {
        var mesh = new Cube();
        var unchanged = TransformState.Of(mesh);

        Assert.Null(CompositeEdit.Combine([null, null], "Move"));

        var single = TransformEdit.Between(mesh, unchanged with { Position = new Vector3(1f, 0f, 0f) }, "Move");

        Assert.NotNull(single);
        Assert.Same(single, CompositeEdit.Combine([null, single], "Move"));
    }

    [Fact]
    public void CompositeEdit_RevertsInReverseOrder()
    {
        var order = new List<string>();

        var composite = new CompositeEdit(
            [
                new Recorded("first", order),
                new Recorded("second", order),
            ],
            "Two things");

        composite.Revert();

        Assert.Equal(["second", "first"], order);
    }

    private sealed class Recorded(string name, List<string> order) : IEditCommand
    {
        public string Description => name;

        public void Apply() => order.Add(name);

        public void Revert() => order.Add(name);
    }

    [Fact]
    public void HighlightedMeshes_AndTheSingularOneAgree()
    {
        var settings = new RendererSettings();

        Assert.Equal(-1, settings.HighlightedMesh);
        Assert.Empty(settings.HighlightedMeshes);

        settings.HighlightedMesh = 3;

        Assert.Equal([3], settings.HighlightedMeshes);

        settings.HighlightedMeshes.Add(7);

        Assert.Equal(3, settings.HighlightedMesh);

        settings.HighlightedMesh = -1;

        Assert.Empty(settings.HighlightedMeshes);
    }

    [Fact]
    public void Render_OutlinesEveryHighlightedMesh()
    {
        static int Outlined(params int[] highlighted)
        {
            var world = new SimpleWorld();
            world.Meshes.Add(new Cube { Position = new Vector3(-3f, 0, 0) });
            world.Meshes.Add(new Cube { Position = new Vector3(3f, 0, 0) });

            var scene = new Scene
            {
                World = world,
                Camera = new FixedCamera(new Vector3(0, 0, 14f)),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
                Surface = new FrameBuffer(64, 64) { Stats = new RenderStats() },
            };

            var renderer = new Renderer();
            renderer.Settings.HighlightedMeshes.AddRange(highlighted);

            renderer.Render(scene, new FlatPainter());

            var amber = 0;

            for (var i = 0; i < scene.Surface.Screen.Length; i++)
            {
                var color = ColorRGB.FromPacked(scene.Surface.Screen[i]);

                if (color.R > 200 && color.G is > 120 and < 220 && color.B < 80)
                {
                    amber++;
                }
            }

            return amber;
        }

        var none = Outlined();
        var one = Outlined(0);
        var both = Outlined(0, 1);

        Assert.Equal(0, none);
        Assert.True(one > 0, "one highlighted mesh should be outlined");
        Assert.True(both > one * 1.5f, $"two should outline about twice as much: {one} → {both}");
    }

    [Fact]
    public void ShowLights_DrawsAMarkerPerLight()
    {
        static int Drawn(bool showLights, params ILight[] lights)
        {
            var world = new SimpleWorld();
            world.Lights.Clear();
            world.Lights.AddRange(lights);

            var scene = new Scene
            {
                World = world,
                Camera = new FixedCamera(new Vector3(0, 4f, 18f)),
                Projection = new PerspectiveProjection(MathF.PI / 3f, 0.1f, 200f),
                Surface = new FrameBuffer(96, 96) { Stats = new RenderStats() },
            };

            var renderer = new Renderer();
            renderer.Settings.ShowLights = showLights;
            renderer.Settings.SkeletonTickSize = 1f;

            renderer.Render(scene, new FlatPainter());

            var lit = 0;

            for (var i = 0; i < scene.Surface.Screen.Length; i++)
            {
                if ((scene.Surface.Screen[i] & 0x00FFFFFF) != 0)
                {
                    lit++;
                }
            }

            return lit;
        }

        var point = new PointLight { Position = new Vector3(0, 2f, 0) };
        var spot = new SpotLight { Position = new Vector3(4f, 6f, 0), Direction = -Vector3.UnitY, Range = 8f };
        var directional = new DirectionalLight { Direction = new Vector3(-0.4f, -1f, -0.3f) };

        Assert.Equal(0, Drawn(showLights: false, point));

        var one = Drawn(showLights: true, point);
        Assert.True(one > 20, $"a point light should draw a marker, got {one} pixels");

        var three = Drawn(showLights: true, point, spot, directional);
        Assert.True(three > one, $"three lights should draw more than one: {one} → {three}");
    }

    [Fact]
    public void LightGizmo_DrawsNothingForALightWithNoDirection()
    {
        var surface = new FrameBuffer(32, 32) { Stats = new RenderStats() };
        surface.Clear();

        LightGizmo.Draw(surface, Matrix4x4.Identity, [new DirectionalLight { Direction = Vector3.Zero }], 1f);

        Assert.All(surface.Screen, pixel => Assert.Equal(0, pixel & 0x00FFFFFF));
    }
}
