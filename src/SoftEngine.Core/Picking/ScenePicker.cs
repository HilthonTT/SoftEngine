using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Picking;

public static class ScenePicker
{
    public static Ray RayThrough(Scene scene, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        var surface = scene.Surface;
        var projection = scene.Projection;

        var width = surface.Width;
        var height = surface.Height;

        var matrix = projection.ProjectionMatrix(width, height);

        var ndcX = x * (2f / MathF.Max(width - 1, 1)) - 1f;
        var ndcY = 1f - y * (2f / MathF.Max(height - 1, 1));

        var scaleX = matrix.M11 == 0f ? 1f : matrix.M11;
        var scaleY = matrix.M22 == 0f ? 1f : matrix.M22;

        var (origin, direction) = projection.IsOrthographic
            ? (new Vector3(ndcX / scaleX, ndcY / scaleY, 0f), -Vector3.UnitZ)
            : (Vector3.Zero, new Vector3(ndcX / scaleX, ndcY / scaleY, -1f));

        if (!Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView))
        {
            return new Ray(origin, Vector3.Normalize(direction));
        }

        return new Ray(
            Vector3.Transform(origin, inverseView),
            Vector3.Normalize(Vector3.TransformNormal(direction, inverseView)));
    }

    public static PickHit? Pick(Scene scene, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        return Pick(scene.World, RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f));
    }

    public static PickHit? Pick(IWorld world, in Ray ray)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        var meshes = world.Meshes;

        PickHit? nearest = null;
        var nearestDistance = float.PositiveInfinity;

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];

            if (!mesh.Visible || mesh.Opacity <= 0f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var worldMatrix = mesh.WorldMatrix;

            var center = Vector3.Transform(Vector3.Zero, worldMatrix);
            var radius = mesh.WorldBoundingRadius(worldMatrix);

            if (!ray.IntersectsSphere(center, radius, out var approach) || approach > nearestDistance)
            {
                continue;
            }

            if (!Matrix4x4.Invert(worldMatrix, out var inverse))
            {
                continue;
            }

            var local = ray.Transform(inverse);

            if (!PickMesh(mesh, local, nearestDistance, out var triangleIndex, out var distance))
            {
                continue;
            }

            var triangle = mesh.Triangles[triangleIndex];

            var normal = Vector3.Cross(
                mesh.Vertices[triangle.I1] - mesh.Vertices[triangle.I0],
                mesh.Vertices[triangle.I2] - mesh.Vertices[triangle.I0]);

            normal = Vector3.TransformNormal(normal, Matrix4x4.Transpose(inverse));

            nearestDistance = distance;
            nearest = new PickHit(
                mesh,
                i,
                triangleIndex,
                distance,
                ray.At(distance),
                normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY);
        }

        return nearest;
    }

    public static PickHit? Pick(Acceleration.Bvh accelerator, in Ray ray)
    {
        ArgumentNullException.ThrowIfNull(accelerator, nameof(accelerator));

        if (!accelerator.Intersect(ray, out var hit))
        {
            return null;
        }

        var geometry = accelerator.Geometry;

        var (a, b, c) = geometry.Corners(hit.Triangle);

        var normal = Vector3.Cross(b - a, c - a);

        return new PickHit(
            geometry.Mesh(hit.Triangle),
            geometry.MeshIndex(hit.Triangle),
            geometry.SourceTriangle(hit.Triangle),
            hit.Distance,
            ray.At(hit.Distance),
            normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY);
    }

    private static bool PickMesh(IMesh mesh, in Ray ray, float limit, out int triangleIndex, out float distance)
    {
        triangleIndex = -1;
        distance = limit;

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        for (var i = 0; i < triangles.Length; i++)
        {
            var t = triangles[i];

            if (!IntersectsTriangle(ray, vertices[t.I0], vertices[t.I1], vertices[t.I2], out var hit))
            {
                continue;
            }

            if (hit < distance)
            {
                distance = hit;
                triangleIndex = i;
            }
        }

        return triangleIndex >= 0;
    }

    public static bool IntersectsTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        const float epsilon = 1e-8f;

        distance = 0f;

        var edge1 = b - a;
        var edge2 = c - a;

        var pivot = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, pivot);

        if (MathF.Abs(determinant) < epsilon)
        {
            return false;
        }

        var inverse = 1f / determinant;
        var toVertex = ray.Origin - a;

        var u = Vector3.Dot(toVertex, pivot) * inverse;
        if (u < 0f || u > 1f)
        {
            return false;
        }

        var across = Vector3.Cross(toVertex, edge1);

        var v = Vector3.Dot(ray.Direction, across) * inverse;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        distance = Vector3.Dot(edge2, across) * inverse;

        return distance > epsilon;
    }
}
