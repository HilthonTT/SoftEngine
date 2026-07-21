using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace SoftEngine.Core.Geometry;

public static class MeshFactory
{
    private static readonly XNamespace _xNamespace = "http://www.collada.org/2005/11/COLLADASchema";

    public static IMesh[] HackyImportCollada(string fileName)
    {
        var xdoc = XDocument.Load(fileName);
        var geometries = xdoc.Root?.Element(_xNamespace + "library_geometries")?.Elements(_xNamespace + "geometry")
            ?? Enumerable.Empty<XElement>();

        var volumes = new List<IMesh>();

        foreach (var geometry in geometries)
        {
            XElement? mesh = geometry.Element(_xNamespace + "mesh");
            if (mesh is null)
            {
                continue;
            }

            List<Vector3> vertices = [];
            List<Vector3> normals = [];
            List<int> indices = [];

            var polylist = mesh.Element(_xNamespace + "polylist");

            if (polylist is not null)
            {
                GetSource(polylist, "VERTEX", out var polylist_vertex_id, out _);

                var v = mesh.Elements(_xNamespace + "vertices").FirstOrDefault(e => e.Attribute("id")?.Value == polylist_vertex_id);
                GetSource(v, "POSITION", out var vertices_position_id, out _);
                GetSource(v, "NORMAL", out var vertices_normal_id, out _);

                var polylist_vertices = GetArraySource<Vector3>(mesh, vertices_position_id);
                var polylist_normals = GetArraySource<Vector3>(mesh, vertices_normal_id);
                var polylist_indices = ParseArray<int>(polylist.Element(_xNamespace + "p")?.Value);

                vertices = vertices.Concat(polylist_vertices).ToList();
                normals = normals.Concat(polylist_normals).ToList();
                indices = indices.Concat(polylist_indices).ToList();
            }

            var triangles = mesh.Element(_xNamespace + "triangles");
            if (triangles is not null)
            {
                List<int> triangles_indices = ParseArray<int>(triangles.Element(_xNamespace + "p")?.Value);
                var maxOffset = triangles.Elements(_xNamespace + "input").Max(e => int.Parse(e.Attribute("offset")?.Value ?? "0"));

                GetSource(triangles, "VERTEX", out var triangles_vertex_id, out var triangle_vertex_offset);

                var v = mesh.Elements(_xNamespace + "vertices").FirstOrDefault(e => e.Attribute("id")?.Value == triangles_vertex_id);
                GetSource(v, "POSITION", out var vertices_position_id, out _);

                var triangles_vertices = GetArraySource<Vector3>(mesh, vertices_position_id);

                GetSource(triangles, "NORMAL", out string? triangles_normal_id, out var triangle_normal_offset);
                var triangles_normals = triangles_normal_id is null
                    ? []
                    : GetArraySource<Vector3>(mesh, triangles_normal_id, triangles_indices, triangle_normal_offset, maxOffset);

                var indices_vertexes = triangles_indices
                    .Select((index, i) => new { index, i })
                    .GroupBy(x => x.i % (maxOffset + 1), x => x.index)
                    .Where(x => x.Key == triangle_vertex_offset)
                    .SelectMany(g => g.ToArray())
                    .ToList();

                vertices = vertices.Concat(triangles_vertices).ToList();
                normals = normals.Concat(triangles_normals).ToList();
                indices = indices.Concat(indices_vertexes).ToList();
            }

            volumes.Add(new Mesh(
                vertices.ToArray(),
                indices.ToArray().BuildTriangleIndices(),
                normals.Count > 0 ? normals.ToArray() : null,
                null));
        }

        return volumes.ToArray();
    }

    private static List<T> ParseArray<T>(string? value)
    {
        return value?.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)?.Select(v => (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture))?.ToList() ?? [];
    }

    private static void GetSource(XElement? element, string semantic, out string? id, out int offset)
    {
        var ns = element?.GetDefaultNamespace() ?? XNamespace.None;
        var e = element?.Elements(ns + "input")?.FirstOrDefault(e => string.Equals(e.Attribute("semantic")?.Value, semantic));
        id = e?.Attribute("source")?.Value?.TrimStart('#');
        offset = int.Parse(e?.Attribute("offset")?.Value ?? "0");
    }

    private static List<T> GetArraySource<T>(
        XElement mesh,
        string? id,
        List<int>? indices = null,
        int offset = -1,
        int maxOffset = -1)
    {
        string data = mesh
            .Elements(_xNamespace + "source")
            .FirstOrDefault(e => e?.Attribute("id")?.Value == id)
            !.Element(_xNamespace + "float_array")
            !.Value;

        var floats = ParseArray<float>(data);

        var v = new List<Vector3>();

        if (typeof(T) == typeof(Vector3))
        {
            if (indices is not null && offset != -1 && maxOffset != -1)
            {
                for (var i = 0; i < indices.Count; i += maxOffset + 1)
                {
                    var index = indices[i + offset];
                    v.Add(new Vector3(floats[index * 3], floats[index * 3 + 1], floats[index * 3 + 2]));
                }
            }
            else
            {
                for (var i = 0; i < floats.Count; i += 3)
                {
                    v.Add(new Vector3(floats[i], floats[i + 1], floats[i + 2]));
                }
            }

            return [.. v.OfType<T>()];
        }

        throw new NotImplementedException($"Type {typeof(T)} is not supported.");
    }
}
