using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Diagnostics;

/// <summary>
/// The overdraw buffer view, checked against counts worked out by hand.
///
/// The view claims to count writes the rasterizer <em>attempted</em> — not surfaces stacked
/// over a pixel — and the difference is large enough to look like a bug when you first meet
/// it. These pin the claim down in both directions.
/// </summary>
public class OverdrawViewTests
{
    private const int Size = 64;
    private const int Centre = Size / 2;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    /// <summary>
    /// N thin slabs stacked along Z, every one covering the whole frame. Draw order is list
    /// order, so <paramref name="nearestFirst"/> decides whether each new slab passes the
    /// depth test (farthest first) or fails it (nearest first).
    /// </summary>
    private static (Renderer Renderer, Scene Scene) Slabs(
        int count,
        bool nearestFirst,
        bool hierarchicalZ = false,
        bool backFaceCulling = true,
        bool sky = false)
    {
        var renderer = new Renderer();
        renderer.Settings.DebugView = DebugView.Overdraw;
        renderer.Settings.HierarchicalZ = hierarchicalZ;
        renderer.Settings.BackFaceCulling = backFaceCulling;

        // These tests are about what draw order costs, so the draw order has to be the one the
        // list gives — the same reason hierarchical-Z is off unless a test asks for it. Sorting
        // the meshes nearest-first would make every case below the nearest-first case, which is
        // exactly what NearestMeshesFirst is for and exactly what these are here to measure
        // against. Overdraw_NearestMeshesFirst_CostsWhatFrontToBackCosts covers it switched on.
        renderer.Settings.NearestMeshesFirst = false;

        var world = new SimpleWorld
        {
            Lights = [new DirectionalLight { Direction = new Vector3(0, 0, -1f) }],
        };

        for (var i = 0; i < count; i++)
        {
            // The camera sits at -20 looking at the origin, so a larger Z is farther away.
            var slot = nearestFirst ? i : count - 1 - i;

            world.Meshes.Add(new Cube
            {
                Position = new Vector3(0, 0, slot * 1.5f),

                // Wide enough to cover the centre, narrow enough to leave the frame's corners
                // uncovered so the sky has somewhere to land.
                Scale = new Vector3(8f, 8f, 0.2f),
            });
        }

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -20f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 200f),
            Surface = new FrameBuffer(Size, Size) { Stats = renderer.Stats },
            ShowSky = sky,
            Environment = sky ? SkyBox.Gradient(Vector3.Normalize(new Vector3(-0.3f, -1f, -0.4f)), resolution: 16) : null,
        };

        return (renderer, scene);
    }

    private static int CentreCount(Scene scene) => scene.Surface.Overdraw[Centre + Centre * Size];

    private static int CornerCount(Scene scene) => scene.Surface.Overdraw[0];

    /// <summary>
    /// Farthest first, so every slab passes the depth test and really is written. The count has
    /// to be exactly one per slab — one front face each, the back faces culled.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void Overdraw_SlabsDrawnFarthestFirst_CountsExactlyOneWritePerSlab(int slabs)
    {
        var (renderer, scene) = Slabs(slabs, nearestFirst: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(slabs, CentreCount(scene));
    }

    /// <summary>Both faces of each slab reach the pixel once winding stops rejecting one of them.</summary>
    [Fact]
    public void Overdraw_BackFaceCullingOff_CountsBothFacesOfEachSlab()
    {
        var (renderer, scene) = Slabs(4, nearestFirst: false, backFaceCulling: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(8, CentreCount(scene));
    }

    /// <summary>
    /// The documented reading, and the one that matters: the view counts writes the rasterizer
    /// <em>attempted</em>. Drawn nearest first, the stack behind the first slab is rejected in
    /// bulk by the scanline filler's vectorized depth test — which skips a run of pixels
    /// entirely behind the z-buffer without interpolating or shading any of it — so those
    /// writes are never attempted and correctly never counted.
    ///
    /// The view answers "what did this frame pay for", not "how many surfaces are stacked
    /// here". Nine slabs front-to-back cost one write, and the frame really did pay for one.
    /// </summary>
    [Fact]
    public void Overdraw_SlabsDrawnNearestFirst_CountsOnlyTheWritesTheFillAttempted()
    {
        var (renderer, scene) = Slabs(9, nearestFirst: true);
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, CentreCount(scene));

        // The same nine slabs the other way round cost nine, which is the comparison that says
        // the counter measures cost rather than geometry.
        var (other, otherScene) = Slabs(9, nearestFirst: false);
        other.Render(otherScene, new GouraudPainter());

        Assert.Equal(9, CentreCount(otherScene));
    }

    /// <summary>
    /// The optimization the two cases above bracket: nine slabs handed over farthest-first
    /// cost nine writes drawn in list order, and one write once the renderer is allowed to
    /// reorder them — the same number the nearest-first list costs, because it is the same
    /// order. That is <see cref="RendererSettings.NearestMeshesFirst"/> doing the only thing
    /// it claims to do, measured in the units it saves.
    /// </summary>
    [Fact]
    public void Overdraw_NearestMeshesFirst_CostsWhatFrontToBackCosts()
    {
        var (renderer, scene) = Slabs(9, nearestFirst: false);
        renderer.Settings.NearestMeshesFirst = true;

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, CentreCount(scene));
    }

    /// <summary>
    /// Reordering the meshes cannot reorder the picture. The same nine slabs drawn both ways
    /// have to come out pixel for pixel identical: the depth test decides what is seen, and
    /// the visit order only decides how much of it was drawn and thrown away.
    /// </summary>
    [Fact]
    public void NearestMeshesFirst_DrawsTheSameFrame()
    {
        var (ordered, orderedScene) = Slabs(9, nearestFirst: false);
        ordered.Settings.DebugView = DebugView.Off;
        ordered.Settings.NearestMeshesFirst = true;
        ordered.Render(orderedScene, new GouraudPainter());

        var (listOrder, listOrderScene) = Slabs(9, nearestFirst: false);
        listOrder.Settings.DebugView = DebugView.Off;
        listOrder.Render(listOrderScene, new GouraudPainter());

        Assert.Equal(
            listOrderScene.Surface.Screen.ToArray(),
            orderedScene.Surface.Screen.ToArray());
    }

    /// <summary>
    /// Hierarchical-Z drops triangles a tile at a time rather than a run at a time, so it can
    /// only ever remove writes the fill would otherwise have attempted — never add any.
    /// </summary>
    [Fact]
    public void Overdraw_HierarchicalZ_NeverRaisesTheCount()
    {
        var (without, withoutScene) = Slabs(40, nearestFirst: true, hierarchicalZ: false);
        without.Render(withoutScene, new GouraudPainter());

        var (with, withScene) = Slabs(40, nearestFirst: true, hierarchicalZ: true);
        with.Render(withScene, new GouraudPainter());

        Assert.True(
            CentreCount(withScene) <= CentreCount(withoutScene),
            $"hi-z raised the count: {CentreCount(withScene)} vs {CentreCount(withoutScene)}");
    }

    [Fact]
    public void Overdraw_EmptyFrame_IsZeroEverywhere()
    {
        var (renderer, scene) = Slabs(0, nearestFirst: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.All(scene.Surface.Overdraw.ToArray(), count => Assert.Equal(0, count));
    }

    /// <summary>
    /// The sky writes once into every pixel the opaque pass left untouched, so switching it on
    /// takes the whole background from zero writes to one — black to the first colour on the
    /// ramp. That is the counter being right rather than wrong: the sky pass really did write
    /// those pixels. It does not touch a pixel geometry already covered.
    /// </summary>
    [Fact]
    public void Overdraw_WithSky_AddsExactlyOneWriteToTheUncoveredPixelsOnly()
    {
        var (bare, bareScene) = Slabs(3, nearestFirst: false, sky: false);
        bare.Render(bareScene, new GouraudPainter());

        var (skied, skyScene) = Slabs(3, nearestFirst: false, sky: true);
        skied.Render(skyScene, new GouraudPainter());

        // A corner the slabs do not reach: nothing, then exactly the sky's one write.
        Assert.Equal(0, CornerCount(bareScene));
        Assert.Equal(1, CornerCount(skyScene));

        // The centre is covered by geometry, so the sky never gets there.
        Assert.Equal(3, CentreCount(bareScene));
        Assert.Equal(3, CentreCount(skyScene));
    }

    /// <summary>
    /// The gizmo is the one thing this change added to the write path, so: does it show up, and
    /// does it disturb anything it does not draw over?
    /// </summary>
    [Fact]
    public void Overdraw_WithAGizmo_AddsOnlyTheHandlePixels()
    {
        var (bare, bareScene) = Slabs(3, nearestFirst: false);
        bare.Render(bareScene, new GouraudPainter());
        var before = bareScene.Surface.Overdraw.ToArray();

        var (withGizmo, gizmoScene) = Slabs(3, nearestFirst: false);
        withGizmo.Settings.Gizmo = new TransformGizmo
        {
            Mode = GizmoMode.Translate,
            Target = gizmoScene.World.Meshes[0],
        };
        withGizmo.Render(gizmoScene, new GouraudPainter());
        var after = gizmoScene.Surface.Overdraw.ToArray();

        var raised = 0;
        var lowered = 0;

        for (var i = 0; i < before.Length; i++)
        {
            if (after[i] > before[i]) { raised++; }
            if (after[i] < before[i]) { lowered++; }
        }

        Assert.Equal(0, lowered);
        Assert.True(raised > 0, "the gizmo's own writes should be counted");
    }
}
