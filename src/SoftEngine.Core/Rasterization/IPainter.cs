using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Rasterization;

public interface IPainter
{
    /// <summary>
    /// Called once per frame before any triangles are drawn, so a painter can pick up
    /// per-frame state (camera position, scene lights, …).
    /// </summary>
    void Prepare(Scene scene)
    {
    }

    /// <summary>
    /// The light this painter shades by when the world declares none of its own, or null
    /// when it has no opinion.
    ///
    /// The renderer asks before the shadow pass, because that pass runs before
    /// <see cref="Prepare"/> and has to pick the same light the shading will: a scene lit
    /// from one direction and shadowed from another is worse than one with no shadows at all,
    /// and the mismatch only appears on the worlds that declare no lights — which are exactly
    /// the ones nobody sets up carefully.
    /// </summary>
    ILight? FallbackLight => null;

    /// <summary>
    /// Whether <see cref="DrawTriangle"/> honors the tile it is given. Painters that ignore
    /// it (line drawing crosses arbitrary pixels) must return false so the renderer keeps
    /// them on the sequential path instead of racing the z-buffer.
    /// </summary>
    bool SupportsTiles => true;

    /// <summary>
    /// How this painter samples a texture. Painters that sample none say bilinear and mean
    /// nothing by it.
    ///
    /// <para>
    /// It is on the interface, rather than only on the painters that have a sampler, because
    /// a renderer that is not this one has to be able to ask. The GPU backend renders the
    /// mode the front-end selected, and "the mode" includes whether filtering is on — a scene
    /// the viewer is showing unfiltered must not turn smooth because the frame moved to the
    /// graphics card.
    /// </para>
    /// </summary>
    TextureFiltering Filtering => TextureFiltering.Bilinear;

    /// <summary>Whether this painter samples from a mip chain. See <see cref="Filtering"/>.</summary>
    bool UseMipMaps => true;

    /// <summary>
    /// The flat ambient level this painter falls back on when the scene has no environment to
    /// take one from. Unlit painters have no ambient and report zero.
    /// </summary>
    float AmbientLevel => 0f;

    /// <summary>
    /// Draws one triangle, restricted to the pixels owned by <paramref name="tile"/>.
    /// The renderer calls this concurrently with disjoint tiles; implementations must
    /// not mutate shared state here.
    /// </summary>
    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);
}
