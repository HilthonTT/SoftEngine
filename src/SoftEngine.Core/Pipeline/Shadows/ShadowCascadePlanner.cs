using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

public readonly record struct ShadowCascadeSetup(Matrix4x4 LightViewProjection, float DepthBias, float SlopeBias);

public sealed class ShadowCascadePlanner
{
    private readonly List<int> _casters = [];
    private readonly List<int>[] _cascadeCasters =
        [.. Enumerable.Range(0, ShadowMap.MaxCascades).Select(_ => new List<int>())];

    private readonly ShadowCascadeSetup[] _setups = new ShadowCascadeSetup[ShadowMap.MaxCascades];

    private Vector3[] _casterCenter = [];
    private float[] _casterRadius = [];

    public int CascadeCount { get; private set; }

    public IReadOnlyList<int> Casters => _casters;

    public ShadowCascadeSetup SetupOf(int cascade) => _setups[System.Math.Clamp(cascade, 0, ShadowMap.MaxCascades - 1)];

    public IReadOnlyList<int> CastersOf(int cascade) =>
        _cascadeCasters[System.Math.Clamp(cascade, 0, ShadowMap.MaxCascades - 1)];

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

        var up = MathF.Abs(toLight.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        var lightView = Matrix4x4.CreateLookAt(Vector3.Zero, -toLight, up);

        var lightCenter = Vector3.Transform(center, lightView);

        var resolution = settings.Resolution;
        var halfExtent = radius * resolution / (resolution - 2f);
        var texel = halfExtent * 2f / resolution;

        var snapX = MathF.Round(lightCenter.X / texel) * texel;
        var snapY = MathF.Round(lightCenter.Y / texel) * texel;

        var lightWorld = Vector3.Transform(worldCenter, lightView);

        var nearestZ = MathF.Max(lightWorld.Z + worldRadius, lightCenter.Z + radius);
        var farthestZ = MathF.Min(lightWorld.Z - worldRadius, lightCenter.Z - radius);

        const float near = 0.01f;
        var far = near + MathF.Max(nearestZ - farthestZ, 1e-3f);

        var offset = new Vector3(-snapX, -snapY, -nearestZ - near);

        var projection = Matrix4x4.CreateOrthographic(halfExtent * 2f, halfExtent * 2f, near, far);

        var lightViewProjection = lightView * Matrix4x4.CreateTranslation(offset) * projection;

        SelectCasters(cascade, center, halfExtent, toLight);

        var texelDepth = texel / (far - near);

        return new ShadowCascadeSetup(
            lightViewProjection,
            settings.DepthBias * texelDepth,
            settings.SlopeBias * texelDepth);
    }

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
            var meshRadius = mesh.WorldBoundingRadius(worldMatrix);

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
