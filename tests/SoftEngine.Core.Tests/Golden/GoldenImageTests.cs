using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Tests.Golden;

namespace SoftEngine.Core.Tests.Golden;

public class GoldenImageTests
{
    public static TheoryData<string> SceneNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var scene in GoldenScene.All)
            {
                data.Add(scene.Name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Scene_StillMatchesItsBaseline(string name)
    {
        var scene = GoldenScene.All.Single(s => s.Name == name);

        var (pixels, width, height) = scene.Render();

        GoldenImage.Verify(scene.Name, pixels, width, height);
    }

    [Fact]
    public void Render_IsDeterministicAcrossRuns()
    {
        var scene = GoldenScene.All.Single(s => s.Name == "shadows-three-cascades");

        var (first, width, height) = scene.Render();
        var (second, _, _) = scene.Render();

        GoldenImage.VerifyIdentical("repeated render", first, second, width, height);
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Scene_RendersIdenticallyWithoutOcclusionCulling(string name)
    {
        var scene = GoldenScene.All.Single(s => s.Name == name);

        var (culled, width, height) = scene.Render(occlusionCulling: true);
        var (whole, _, _) = scene.Render(occlusionCulling: false);

        GoldenImage.VerifyIdentical($"{name} occlusion culling", whole, culled, width, height);
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Scene_RendersIdenticallyWithScalarSpans(string name)
    {
        var scene = GoldenScene.All.Single(s => s.Name == name);

        var restore = ScanlineRasterizer.VectorizedSpans;

        try
        {
            ScanlineRasterizer.VectorizedSpans = true;
            var (vectorized, width, height) = scene.Render();

            ScanlineRasterizer.VectorizedSpans = false;
            var (scalar, _, _) = scene.Render();

            GoldenImage.VerifyIdentical($"{name} vectorized spans", scalar, vectorized, width, height);
        }
        finally
        {
            ScanlineRasterizer.VectorizedSpans = restore;
        }
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Scene_RendersIdenticallyWithASequentialCullPhase(string name)
    {
        var scene = GoldenScene.All.Single(s => s.Name == name);

        var restore = Renderer.ParallelCullPhase;

        try
        {
            Renderer.ParallelCullPhase = true;
            var (parallel, width, height) = scene.Render();

            Renderer.ParallelCullPhase = false;
            var (sequential, _, _) = scene.Render();

            GoldenImage.VerifyIdentical($"{name} sequential cull phase", sequential, parallel, width, height);
        }
        finally
        {
            Renderer.ParallelCullPhase = restore;
        }
    }

    [Fact]
    public void EveryBaseline_BelongsToAScene()
    {
        var names = GoldenScene.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        var orphans = Directory
            .EnumerateFiles(GoldenImage.ReferenceDirectory, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !names.Contains(name))
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"Baselines with no scene: {string.Join(", ", orphans)}. Delete them, or restore the scenes they belong to.");
    }
}
