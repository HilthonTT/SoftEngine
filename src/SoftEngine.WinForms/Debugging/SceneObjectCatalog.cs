using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Textures;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.WinForms.Debugging;

internal sealed class SceneObjectCatalog
{
    private static readonly SceneObjectRow[] _empty = [];

    private readonly Dictionary<int, SceneObjectRow> _byId;

    private SceneObjectCatalog(IReadOnlyList<SceneObjectRow> rows, string signature)
    {
        Rows = rows;
        Signature = signature;
        _byId = rows.ToDictionary(row => row.Id);
    }

    public IReadOnlyList<SceneObjectRow> Rows { get; }

    public string Signature { get; }

    public static SceneObjectCatalog Empty { get; } = new(_empty, "empty");

    public SceneObjectRow? Find(int id) => _byId.GetValueOrDefault(id);

    public string Describe(int id)
    {
        if (id < 0)
        {
            return string.Empty;
        }

        SceneObjectRow? row = Find(id);
        return row is null ? $"obj:{id}" : $"obj:{id} ({row.Type})";
    }

    public static SceneObjectCatalog Build(Scene? scene, IPainter? painter, PostProcessStack? postProcess = null)
    {
        if (scene?.World is null)
        {
            return Empty;
        }

        var surface = scene.Surface;
        var lights = scene.World.Lights;
        var meshes = scene.World.Meshes;

        var width = surface?.Width ?? 0;
        var height = surface?.Height ?? 0;
        long targetBytes = (long)width * height * sizeof(int);

        var rows = new List<SceneObjectRow>(meshes.Count + lights.Count + 8)
        {
            new(SceneObjectIds.RenderTarget, "RenderTarget", "32 bpp ARGB", targetBytes, 0, 0, width, height, null),
            new(SceneObjectIds.DepthBuffer, "DepthBuffer", "32 bit", targetBytes, 0, 0, width, height, null),
            new(SceneObjectIds.Camera, scene.Camera?.GetType().Name ?? "Camera", Format(scene.Camera?.Position), 0, 0, 0, 0, 0, null),
            new(SceneObjectIds.Projection, scene.Projection?.GetType().Name ?? "Projection",
                scene.Projection is null ? string.Empty : $"near {scene.Projection.ZNear:0.###}, far {scene.Projection.ZFar:0.###}",
                0, 0, 0, 0, 0, null),
            new(SceneObjectIds.Painter, painter?.GetType().Name ?? "None", painter is null ? "no shading" : string.Empty, 0, 0, 0, 0, 0, null),
        };

        if (scene.ShadowMap is { } shadowMap)
        {
            rows.Add(new SceneObjectRow(SceneObjectIds.ShadowMap, "ShadowMap", "32 bit depth",
                (long)shadowMap.Resolution * shadowMap.Resolution * sizeof(float),
                0, 0, shadowMap.Resolution, shadowMap.Resolution, null));
        }

        if (postProcess is { HasEffects: true })
        {
            rows.Add(new SceneObjectRow(SceneObjectIds.PostProcess, "PostProcess", DescribeEffects(postProcess),
                (long)width * height * 3 * sizeof(float) * 2, 0, 0, width, height, null));
        }

        for (var i = 0; i < lights.Count; i++)
        {
            rows.Add(new SceneObjectRow(SceneObjectIds.Light(i), lights[i].GetType().Name, string.Empty, 0, 0, 0, 0, 0, null));
        }

        var textureIds = new Dictionary<Texture, int>();
        var nextTextureId = SceneObjectIds.AfterMeshes(lights.Count, meshes.Count);

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            var textureId = -1;

            if (mesh.Texture is { } texture)
            {
                if (!textureIds.TryGetValue(texture, out textureId))
                {
                    textureId = nextTextureId++;
                    textureIds.Add(texture, textureId);
                }
            }

            rows.Add(new SceneObjectRow(
                SceneObjectIds.Mesh(lights.Count, i),
                mesh.GetType().Name,
                textureId < 0 ? string.Empty : $"texture obj:{textureId}",
                MeshBytes(mesh),
                mesh.Vertices.Length,
                mesh.Triangles.Length,
                0,
                0,
                mesh as Mesh));
        }

        foreach (var (texture, id) in textureIds.OrderBy(pair => pair.Value))
        {
            rows.Add(new SceneObjectRow(id, "Texture", "32 bpp ARGB",
                (long)texture.Width * texture.Height * sizeof(int), 0, 0, texture.Width, texture.Height, null));
        }

        return new SceneObjectCatalog(rows, SignatureOf(scene, painter, postProcess));
    }

    private static string DescribeEffects(PostProcessStack stack) =>
        string.Join(" → ", stack.Effects.Where(effect => effect.Enabled).Select(effect => effect.Name));

    public static string SignatureOf(Scene? scene, IPainter? painter, PostProcessStack? postProcess = null)
    {
        if (scene?.World is null)
        {
            return "empty";
        }

        var meshes = scene.World.Meshes;

        return $"{RuntimeHelpers.GetHashCode(scene.World)}|" +
               $"{(meshes.Count > 0 ? RuntimeHelpers.GetHashCode(meshes[0]) : 0)}|" +
               $"{(meshes.Count > 0 ? RuntimeHelpers.GetHashCode(meshes[^1]) : 0)}|" +
               $"{scene.Surface?.Width ?? 0}x{scene.Surface?.Height ?? 0}|{painter?.GetType().Name}|" +
               $"{scene.Camera?.GetType().Name}|{scene.Projection?.GetType().Name}|" +
               $"{scene.ShadowMap?.Resolution ?? 0}|{(postProcess is null ? string.Empty : DescribeEffects(postProcess))}|" +
               $"{scene.World.Lights.Count}|{meshes.Count}|" +
               $"{(meshes.Count > 0 ? meshes[0].Triangles.Length : 0)}|" +
               $"{(meshes.Count > 0 ? meshes[^1].Vertices.Length : 0)}";
    }

    private static string Format(Vector3? position) =>
        position is { } p ? $"({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})" : string.Empty;

    private static long MeshBytes(IMesh mesh)
    {
        const int vector3 = 3 * sizeof(float);
        const int triangle = 3 * sizeof(int);

        long bytes = (long)mesh.Vertices.Length * vector3;
        bytes += (long)mesh.NormVertices.Length * vector3;
        bytes += (long)mesh.Triangles.Length * triangle;
        bytes += (long)mesh.TriangleColors.Length * sizeof(int);
        bytes += (long)(mesh.TexCoords?.Length ?? 0) * 2 * sizeof(float);

        return bytes;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024f:0.#} KB",
        _ => $"{bytes / (1024f * 1024f):0.#} MB",
    };
}
