using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Tests.Golden;

namespace SoftEngine.Core.Tests.Golden;

/// <summary>
/// Renders every <see cref="GoldenScene"/> and compares it against the picture committed for
/// it. One test per scene, so a failure names the path that drifted rather than reporting that
/// something, somewhere, changed.
/// </summary>
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

    /// <summary>
    /// The renderer is deterministic, which is what makes every baseline above worth keeping.
    /// The fill phase runs in parallel, and if its result depended on the order the workers
    /// happened to finish in, a golden image would be a recording of one scheduling accident.
    /// It does not: a worker owns a screen tile, tiles do not overlap, and so no pixel is
    /// written by more than one of them.
    /// </summary>
    [Fact]
    public void Render_IsDeterministicAcrossRuns()
    {
        var scene = GoldenScene.All.Single(s => s.Name == "shadows-three-cascades");

        var (first, width, height) = scene.Render();
        var (second, _, _) = scene.Render();

        GoldenImage.VerifyIdentical("repeated render", first, second, width, height);
    }

    /// <summary>
    /// The occlusion pass decides what not to draw, so the only statement that makes it correct
    /// is that what <em>is</em> drawn does not change. Checked over every baseline scene rather
    /// than the one built for it: the pass runs on any world with enough meshes in it, and a
    /// conservative rule that holds on the scene it was designed against and nowhere else is
    /// not conservative.
    /// </summary>
    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Scene_RendersIdenticallyWithoutOcclusionCulling(string name)
    {
        var scene = GoldenScene.All.Single(s => s.Name == name);

        var (culled, width, height) = scene.Render(occlusionCulling: true);
        var (whole, _, _) = scene.Render(occlusionCulling: false);

        GoldenImage.VerifyIdentical($"{name} occlusion culling", whole, culled, width, height);
    }

    /// <summary>
    /// Filling a span a vector of pixels at a time is an optimization, so the statement that
    /// makes it correct is the same one the occlusion pass has to satisfy: the image does not
    /// change. Not "does not change much" — the two frames come out of one process on one
    /// machine, so every value that could legitimately drift between hosts is fixed here, and
    /// a single differing pixel is a real difference.
    ///
    /// <para>
    /// Run over every baseline scene rather than one built for it, because the block path is
    /// under every painter at once and the ways it could go wrong are per-painter: it carries
    /// the interpolation parameter and the recovered w across a lane boundary, and each
    /// varying spends them differently. It also puts the scalar tail — the pixels left when a
    /// span is not a whole number of vectors long — on every triangle edge in every scene,
    /// which is the seam most likely to be off by a pixel.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Every baseline in the folder belongs to a scene. A renamed or deleted case otherwise
    /// leaves its picture behind, where it is never compared against anything again and reads
    /// as coverage the suite does not have.
    /// </summary>
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
