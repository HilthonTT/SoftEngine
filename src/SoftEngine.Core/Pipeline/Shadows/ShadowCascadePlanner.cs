using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

/// <summary>Where one cascade looks from, and how much slack its depth comparison is given.</summary>
/// <param name="LightViewProjection">World space to the cascade's clip space; w is 1 throughout.</param>
/// <param name="DepthBias">Constant bias, already in the normalized units the comparison works in.</param>
/// <param name="SlopeBias">Bias scaled by the tangent of the surface's incidence angle.</param>
public readonly record struct ShadowCascadeSetup(Matrix4x4 LightViewProjection, float DepthBias, float SlopeBias);

/// <summary>
/// Decides where a frame's shadow cascades go, and which meshes can put something into each
/// of them. Everything up to the point where depth is actually rasterized.
///
/// <para>
/// It is separate from <see cref="ShadowMapRenderer"/> because the two backends disagree
/// about the rasterizing and must not disagree about anything else. Fitting a cascade is a
/// pile of decisions — a sphere rather than a box so the fit doesn't breathe as the camera
/// turns, a light frame anchored at the world origin so the texel grid stays put, a depth
/// range stretched to reach every caster in the world rather than only the ones inside the
/// slice, biases derived from the cascade's own texel size — and each of them shows up as a
/// visible artifact when it is got wrong. Two implementations of that would be two chances
/// to get it wrong, and the shadows would then differ between CPU and GPU renders of the
/// same scene in ways nobody could attribute.
/// </para>
///
/// Reused across frames: the caster lists and their bounding spheres are resized only when
/// a world outgrows them.
/// </summary>
public sealed class ShadowCascadePlanner
{
    private readonly List<int> _casters = [];
    private readonly List<int>[] _cascadeCasters =
        [.. Enumerable.Range(0, ShadowMap.MaxCascades).Select(_ => new List<int>())];

    private readonly ShadowCascadeSetup[] _setups = new ShadowCascadeSetup[ShadowMap.MaxCascades];

    // Per-caster world-space bounding spheres, so a cascade can reject one without
    // re-deriving it from the mesh's matrix.
    private Vector3[] _casterCenter = [];
    private float[] _casterRadius = [];

    /// <summary>Cascades the last <see cref="Plan"/> produced; 0 when the world casts nothing.</summary>
    public int CascadeCount { get; private set; }

    /// <summary>Every mesh in the world that casts, by index, whichever cascade it reaches.</summary>
    public IReadOnlyList<int> Casters => _casters;

    /// <summary>Where cascade <paramref name="cascade"/> looks from.</summary>
    public ShadowCascadeSetup SetupOf(int cascade) => _setups[System.Math.Clamp(cascade, 0, ShadowMap.MaxCascades - 1)];

    /// <summary>The meshes that can put something into cascade <paramref name="cascade"/>, by index.</summary>
    public IReadOnlyList<int> CastersOf(int cascade) =>
        _cascadeCasters[System.Math.Clamp(cascade, 0, ShadowMap.MaxCascades - 1)];

    /// <summary>
    /// Fits the cascades for one frame. Returns false when the world casts nothing — an empty
    /// world, or one whose meshes are all transparent or hidden — in which case there is no
    /// shadow map to render and nothing here has been written.
    /// </summary>
    /// <param name="view">
    /// The camera the cascades are fitted to. Without one the plan falls back to a single map
    /// over the whole world, whatever <see cref="ShadowSettings.CascadeCount"/> asks for:
    /// cascades are slices of a view frustum, and there is no honest way to slice one that was
    /// not supplied.
    /// </param>
    public bool Plan(IWorld world, ILight light, ShadowSettings settings, ShadowView? view)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(light, nameof(light));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        CascadeCount = 0;

        if (!CollectCasters(world, out var worldCenter, out var worldRadius))
        {
            return false;
        }

        var cascades = view is null ? 1 : System.Math.Clamp(settings.CascadeCount, 1, ShadowMap.MaxCascades);

        Span<float> splits = stackalloc float[ShadowMap.MaxCascades + 1];

        var camera = view ?? default;
        var sliced = view is not null && Split(camera, settings, cascades, splits);

        for (var cascade = 0; cascade < cascades; cascade++)
        {
            // A cascade that cannot be fitted — a slice a degenerate projection produces no
            // corners for — falls back to covering the world, so it shadows something rather
            // than nothing.
            var center = worldCenter;
            var radius = worldRadius;

            if (sliced && Fit(camera, splits[cascade], splits[cascade + 1], out var sliceCenter, out var sliceRadius))
            {
                center = sliceCenter;
                radius = sliceRadius;
            }

            _setups[cascade] = PlanCascade(
                light, settings, cascade, center, radius, worldCenter, worldRadius);
        }

        CascadeCount = cascades;
        return true;
    }

    /// <summary>
    /// Divides the view distance between the cascades.
    ///
    /// Neither of the two obvious schemes works alone. Splitting evenly by distance gives the
    /// near slice — where nearly all the pixels are — the same span as the far one. Splitting
    /// so each slice is a fixed multiple of the last puts the first boundary a few units in
    /// front of the eye. The standard answer is to interpolate between them, weighted toward
    /// the ratio, which is what <see cref="ShadowSettings.SplitBlend"/> selects.
    /// </summary>
    private static bool Split(in ShadowView view, ShadowSettings settings, int cascades, Span<float> splits)
    {
        var near = view.Near;
        var far = settings.MaxDistance > 0f ? MathF.Min(settings.MaxDistance, view.Far) : view.Far;

        if (far <= near)
        {
            return false;
        }

        splits[0] = near;
        splits[cascades] = far;

        for (var i = 1; i < cascades; i++)
        {
            var fraction = i / (float)cascades;

            var logarithmic = near * MathF.Pow(far / near, fraction);
            var uniform = near + (far - near) * fraction;

            splits[i] = uniform + (logarithmic - uniform) * settings.SplitBlend;
        }

        return true;
    }

    /// <summary>
    /// The sphere containing one slice of the view frustum. Its centre is on the view axis by
    /// symmetry, so the radius depends only on the slice's own dimensions — which is exactly
    /// the property that keeps the cascade from resizing as the camera turns.
    /// </summary>
    private static bool Fit(in ShadowView view, float near, float far, out Vector3 center, out float radius)
    {
        center = Vector3.Zero;
        radius = 0f;

        Span<Vector3> corners = stackalloc Vector3[8];

        if (!view.Corners(near, far, corners))
        {
            return false;
        }

        foreach (var corner in corners)
        {
            center += corner;
        }

        center *= 1f / 8f;

        foreach (var corner in corners)
        {
            radius = MathF.Max(radius, (corner - center).Length());
        }

        radius = MathF.Max(radius, 1e-3f);
        return true;
    }

    private ShadowCascadeSetup PlanCascade(
        ILight light,
        ShadowSettings settings,
        int cascade,
        Vector3 center,
        float radius,
        Vector3 worldCenter,
        float worldRadius)
    {
        var toLight = light.DirectionFrom(center);
        if (toLight.LengthSquared() < 1e-12f)
        {
            toLight = Vector3.UnitY;
        }
        toLight = Vector3.Normalize(toLight);

        // CreateLookAt degenerates when the view direction is parallel to up.
        var up = MathF.Abs(toLight.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        // The light's *orientation* only, anchored at the world origin rather than at the
        // region being covered. That anchoring is the whole point: a frame built by looking at
        // the region puts the region's centre at the origin by construction, so there is
        // nothing left to snap and the grid slides continuously with the camera. Fixed to the
        // world, the grid stays put and the region moves across it in whole texels.
        var lightView = Matrix4x4.CreateLookAt(Vector3.Zero, -toLight, up);

        var lightCenter = Vector3.Transform(center, lightView);

        // Snapping shifts the region by up to half a texel, so the box is widened by a texel
        // on each side to keep covering the sphere. Deriving the half-extent first and the
        // texel from it keeps the snap grid and the map's real texel grid the same grid.
        var resolution = settings.Resolution;
        var halfExtent = radius * resolution / (resolution - 2f);
        var texel = halfExtent * 2f / resolution;

        var snapX = MathF.Round(lightCenter.X / texel) * texel;
        var snapY = MathF.Round(lightCenter.Y / texel) * texel;

        // Depth has to span from the nearest thing that can cast — anywhere in the world,
        // including well outside this cascade and between it and the light — to the far side
        // of the region receiving. A cascade fitted to a slice of ground would otherwise clip
        // away the building standing over it.
        var lightWorld = Vector3.Transform(worldCenter, lightView);

        var nearestZ = MathF.Max(lightWorld.Z + worldRadius, lightCenter.Z + radius);
        var farthestZ = MathF.Min(lightWorld.Z - worldRadius, lightCenter.Z - radius);

        const float near = 0.01f;
        var far = near + MathF.Max(nearestZ - farthestZ, 1e-3f);

        // Looking down -Z, so the plane at light-space z = nearestZ has to land at -near.
        var offset = new Vector3(-snapX, -snapY, -nearestZ - near);

        var projection = Matrix4x4.CreateOrthographic(halfExtent * 2f, halfExtent * 2f, near, far);

        var lightViewProjection = lightView * Matrix4x4.CreateTranslation(offset) * projection;

        // Only the casters that can reach this cascade. Under a parallel projection that is a
        // question about perpendicular distance to the light axis and nothing else — which is
        // where a cascade earns its cost back, since the near one covers a few metres and
        // rejects nearly everything.
        SelectCasters(cascade, center, halfExtent, toLight);

        // The settings express bias in texels of depth; convert to the normalized units the
        // comparison works in. One texel spans `texel` world units across the map, and the
        // depth range covers (far - near) of them — so a cascade covering less ground needs
        // proportionally less bias, and gets it without being told.
        var texelDepth = texel / (far - near);

        return new ShadowCascadeSetup(
            lightViewProjection,
            settings.DepthBias * texelDepth,
            settings.SlopeBias * texelDepth);
    }

    /// <summary>
    /// Finds the meshes that cast and a world-space sphere containing them. Transparent and
    /// hidden meshes are excluded: a mesh you can see through should not block the light, and
    /// one dropped from the frame should not leave its shadow behind.
    /// </summary>
    private bool CollectCasters(IWorld world, out Vector3 center, out float radius)
    {
        _casters.Clear();
        center = Vector3.Zero;
        radius = 0f;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        var meshes = world.Meshes;

        if (_casterCenter.Length < meshes.Count)
        {
            _casterCenter = new Vector3[System.Math.Max(meshes.Count, _casterCenter.Length * 2)];
            _casterRadius = new float[_casterCenter.Length];
        }

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            if (!mesh.Visible || mesh.Opacity < 1f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;

            var meshCenter = Vector3.Transform(Vector3.Zero, worldMatrix);
            var meshRadius = mesh.BoundingRadius * MeshExtensions.MaxScale(worldMatrix);

            if (float.IsNaN(meshRadius) || float.IsInfinity(meshRadius))
            {
                continue;
            }

            min = Vector3.Min(min, meshCenter - new Vector3(meshRadius));
            max = Vector3.Max(max, meshCenter + new Vector3(meshRadius));

            _casterCenter[i] = meshCenter;
            _casterRadius[i] = meshRadius;

            _casters.Add(i);
        }

        if (_casters.Count == 0)
        {
            return false;
        }

        center = (min + max) * 0.5f;
        radius = MathF.Max((max - min).Length() * 0.5f, 1e-3f);

        return true;
    }

    /// <summary>
    /// The casters that can put something into one cascade: those whose bounding sphere comes
    /// within the cascade's radius of the light axis through its centre. Depth along that axis
    /// is not tested, because the projection was already stretched to cover every caster.
    /// </summary>
    private void SelectCasters(int cascade, Vector3 center, float radius, Vector3 toLight)
    {
        var selected = _cascadeCasters[cascade];
        selected.Clear();

        foreach (var index in _casters)
        {
            var offset = _casterCenter[index] - center;
            var perpendicular = offset - toLight * Vector3.Dot(offset, toLight);

            var reach = radius + _casterRadius[index];

            if (perpendicular.LengthSquared() <= reach * reach)
            {
                selected.Add(index);
            }
        }
    }
}
