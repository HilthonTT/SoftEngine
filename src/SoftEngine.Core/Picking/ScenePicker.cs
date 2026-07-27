using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Picking;

/// <summary>
/// Answers "what did I just click on" by intersecting the scene with a ray, rather than by
/// reading anything the frame drew.
///
/// The alternative — rendering an identifier per pixel and looking one up — is what a GPU
/// renderer usually does, and it would answer a subtly different question: what was
/// <em>drawn</em> there, at the resolution it was drawn at, after culling and the depth test.
/// A ray answers what is <em>there</em>. It costs nothing per frame, works on geometry the
/// frame never rasterized, reports the exact triangle and the point on it rather than a
/// pixel's worth of it, and — because it is pure geometry with no framebuffer in it — can be
/// tested without rendering anything at all.
///
/// The cost is that it walks triangles. Whole meshes are rejected against their bounding
/// spheres first, which is the same rejection the renderer's frustum cull uses, so a click
/// on a scene of forty thousand cubes tests a handful of them.
/// </summary>
public static class ScenePicker
{
    /// <summary>
    /// The ray through a point of the render target, in world space.
    ///
    /// This is the rendering pipeline run backwards: undo the screen mapping to a normalized
    /// device coordinate, undo the projection to a direction in view space, undo the view to
    /// get both into the world. The mapping matches
    /// <see cref="Buffers.FrameBuffer.ToScreen3"/> exactly — NDC ±1 onto pixel 0 and pixel
    /// n - 1 — or the ray would miss what the pixel shows by half a pixel's worth of angle at
    /// the edges of the frame.
    ///
    /// The coordinates are continuous, in the space that mapping produces. The centre of
    /// pixel <c>(i, j)</c> — the point the rasterizer tests coverage at — is
    /// <c>(i + 0.5, j + 0.5)</c>, which is what <see cref="Pick(Scene, int, int)"/> passes.
    /// </summary>
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

        // The projection's scale on each axis; dividing by it undoes the projection for the
        // one point, which is all a single ray needs.
        var scaleX = matrix.M11 == 0f ? 1f : matrix.M11;
        var scaleY = matrix.M22 == 0f ? 1f : matrix.M22;

        // A parallel projection fires every ray the same way and moves the origin instead;
        // a perspective one fires them all from the eye.
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

    /// <summary>
    /// The nearest mesh under a pixel of the render target, or null when the ray hits
    /// nothing.
    ///
    /// The ray goes through the <em>centre</em> of the pixel, which is where the rasterizer
    /// decided whether a triangle covered it. Aiming at the pixel's corner instead would put
    /// the two answers half a pixel apart, and they would disagree along every silhouette in
    /// the frame — which is exactly where a person is most likely to click.
    /// </summary>
    public static PickHit? Pick(Scene scene, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        return Pick(scene.World, RayThrough(scene, pixelX + 0.5f, pixelY + 0.5f));
    }

    /// <summary>
    /// The nearest triangle the ray runs into, over every mesh the world would draw.
    ///
    /// Meshes switched off in the graphics object table, and ones faded out entirely, are
    /// skipped — clicking should select what is on screen. Transparent geometry is not:
    /// something you can see through is still something you can point at.
    /// </summary>
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

            // Reject against the bounding sphere in world space, before paying for the
            // matrix inverse the triangle test needs.
            var center = Vector3.Transform(Vector3.Zero, worldMatrix);
            var radius = mesh.BoundingRadius * MaxScale(worldMatrix);

            if (!ray.IntersectsSphere(center, radius, out var approach) || approach > nearestDistance)
            {
                continue;
            }

            if (!Matrix4x4.Invert(worldMatrix, out var inverse))
            {
                continue;
            }

            // Into the mesh's own space, where its vertices already are. Transforming one
            // ray beats transforming every vertex, and the unnormalized direction keeps the
            // distance comparable with the other meshes'.
            var local = ray.Transform(inverse);

            if (!PickMesh(mesh, local, nearestDistance, out var triangleIndex, out var distance))
            {
                continue;
            }

            var triangle = mesh.Triangles[triangleIndex];

            var normal = Vector3.Cross(
                mesh.Vertices[triangle.I1] - mesh.Vertices[triangle.I0],
                mesh.Vertices[triangle.I2] - mesh.Vertices[triangle.I0]);

            // A normal is transformed by the inverse transpose, which is what keeps it
            // perpendicular to the surface through a non-uniform scale.
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

    /// <summary>
    /// The nearest triangle of one mesh the ray runs into, closer than
    /// <paramref name="limit"/>. The ray is expected in the mesh's own space.
    /// </summary>
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

    /// <summary>
    /// Möller-Trumbore: solves the ray against the triangle's own plane and barycentric
    /// coordinates in one go, without ever building the plane equation.
    ///
    /// Both faces count. A click is a question about geometry, not about winding, and a
    /// single-sided test would make an inward-facing wall — or a mesh whose triangles were
    /// exported the other way round — unclickable for no reason the user can see.
    /// </summary>
    public static bool IntersectsTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        const float epsilon = 1e-8f;

        distance = 0f;

        var edge1 = b - a;
        var edge2 = c - a;

        var pivot = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, pivot);

        // Parallel to the triangle's plane: no crossing, or infinitely many.
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

        // Behind the ray's origin is not something it can run into.
        return distance > epsilon;
    }

    /// <summary>
    /// The largest scale factor a transform applies, read off the lengths of its rows.
    ///
    /// A mesh's own <c>Scale</c> is not enough: a mesh parented to a scene-graph node
    /// inherits everything the chain above it does, and exported rigs routinely carry a unit
    /// conversion — a factor of a hundred — on their top node. A bounding sphere sized
    /// without it rejects a mesh that the ray passes straight through.
    /// </summary>
    private static float MaxScale(in Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13).Length();
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23).Length();
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33).Length();

        return MathF.Max(x, MathF.Max(y, z));
    }
}
