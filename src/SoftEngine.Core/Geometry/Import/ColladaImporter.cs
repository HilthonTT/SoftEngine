using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace SoftEngine.Core.Geometry.Import;

public static partial class ColladaImporter
{
    private static readonly XNamespace _collada = "http://www.collada.org/2005/11/COLLADASchema";

    public static IMesh[] HackyImportCollada(string fileName, IProgress<float>? progress = null)
    {
        const float parseShare = 0.4f;

        progress?.Report(0f);

        var document = XDocument.Load(fileName);

        progress?.Report(parseShare);

        var geometries = (document.Root
            ?.Element(_collada + "library_geometries")
            ?.Elements(_collada + "geometry")
            ?? []).ToArray();

        var meshes = new List<IMesh>();

        for (var i = 0; i < geometries.Length; i++)
        {
            var mesh = geometries[i].Element(_collada + "mesh");
            if (mesh is null)
            {
                continue;
            }

            var share = (1f - parseShare) / geometries.Length;
            var done = parseShare + share * i;

            var buffers = new GeometryBuffers();

            // An exporter writes one <polylist> or <triangles> per material, so every one of
            // them is read; reading only the first drops all the other materials' faces.
            foreach (var polylist in mesh.Elements(_collada + "polylist"))
            {
                ReadPolylist(mesh, polylist, buffers);
            }
            progress?.Report(done + share * 0.4f);

            foreach (var triangles in mesh.Elements(_collada + "triangles"))
            {
                ReadTriangles(mesh, triangles, buffers);
            }
            progress?.Report(done + share * 0.8f);

            meshes.Add(new Mesh(
                buffers.Vertices.ToArray(),
                buffers.Indices.ToArray().BuildTriangleIndices(buffers.Vertices.Count),
                buffers.Normals.Count == buffers.Vertices.Count && buffers.Normals.Count > 0
                    ? buffers.Normals.ToArray()
                    : null,
                triangleColors: null));
            progress?.Report(done + share);
        }

        progress?.Report(1f);

        return meshes.ToArray();
    }

    private static void ReadPolylist(XElement mesh, XElement polylist, GeometryBuffers buffers)
    {
        var interleavedIndices = ParseArray<int>(polylist.Element(_collada + "p")?.Value);
        var stride = Stride(polylist);

        GetInput(polylist, "VERTEX", out var vertexInputId, out var vertexOffset);
        var vertices = FindVertices(mesh, vertexInputId);

        GetInput(vertices, "POSITION", out var positionSourceId, out _);
        GetInput(vertices, "NORMAL", out var normalSourceId, out _);

        // Every primitive of a <mesh> indexes the mesh's one <vertices>, so the positions are
        // read once and later primitives add only their faces. Copying them per primitive
        // would put a skin's weights on the first copy alone.
        if (buffers.Vertices.Count == 0)
        {
            buffers.Vertices.AddRange(ReadVectors(mesh, positionSourceId));
        }
        if (normalSourceId is not null && buffers.Normals.Count == 0)
        {
            buffers.Normals.AddRange(ReadVectors(mesh, normalSourceId));
        }

        var vertexIndices = ExtractLane(interleavedIndices, vertexOffset, stride);
        var vcounts = ParseArray<int>(polylist.Element(_collada + "vcount")?.Value);

        if (vcounts.Count == 0)
        {
            buffers.Indices.AddRange(vertexIndices);
            return;
        }

        var cursor = 0;
        foreach (var vcount in vcounts)
        {
            if (vcount < 0 || cursor + vcount > vertexIndices.Count)
            {
                break;
            }

            for (var corner = 1; corner + 1 < vcount; corner++)
            {
                buffers.Indices.Add(vertexIndices[cursor]);
                buffers.Indices.Add(vertexIndices[cursor + corner]);
                buffers.Indices.Add(vertexIndices[cursor + corner + 1]);
            }

            cursor += vcount;
        }
    }

    private static void ReadTriangles(XElement mesh, XElement triangles, GeometryBuffers buffers)
    {
        var interleavedIndices = ParseArray<int>(triangles.Element(_collada + "p")?.Value);
        var stride = Stride(triangles);

        GetInput(triangles, "VERTEX", out var vertexInputId, out var vertexOffset);
        var vertices = FindVertices(mesh, vertexInputId);
        GetInput(vertices, "POSITION", out var positionSourceId, out _);

        GetInput(triangles, "NORMAL", out var normalSourceId, out var normalOffset);

        // The positions are shared with any earlier primitive of the mesh, as in ReadPolylist.
        if (buffers.Vertices.Count == 0)
        {
            buffers.Vertices.AddRange(ReadVectors(mesh, positionSourceId));
        }

        var vertexIndices = ExtractLane(interleavedIndices, vertexOffset, stride);

        if (normalSourceId is not null)
        {
            var cornerNormals = ReadVectors(mesh, normalSourceId, interleavedIndices, normalOffset, stride);
            while (buffers.Normals.Count < buffers.Vertices.Count)
            {
                buffers.Normals.Add(Vector3.Zero);
            }
            for (var corner = 0; corner < vertexIndices.Count && corner < cornerNormals.Count; corner++)
            {
                var vertexIndex = vertexIndices[corner];
                if (vertexIndex >= 0 && vertexIndex < buffers.Vertices.Count)
                {
                    buffers.Normals[vertexIndex] = cornerNormals[corner];
                }
            }
        }

        buffers.Indices.AddRange(vertexIndices);
    }

    private static int Stride(XElement primitives) =>
        System.Math.Max(
            primitives.Elements(_collada + "input")
                .Select(input => ParseOffset(input.Attribute("offset")?.Value))
                .DefaultIfEmpty(0)
                .Max() + 1,
            1);

    private static int ParseOffset(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : 0;

    private static List<int> ExtractLane(List<int> interleaved, int lane, int stride)
    {
        stride = System.Math.Max(stride, 1);

        var laneIndices = new List<int>();
        for (var i = System.Math.Max(lane, 0); i < interleaved.Count; i += stride)
        {
            laneIndices.Add(interleaved[i]);
        }
        return laneIndices;
    }

    private static List<Vector3> ReadVectors(
        XElement mesh,
        string? sourceId,
        List<int>? indices = null,
        int offset = -1,
        int stride = -1)
    {
        var floats = ParseArray<float>(ReadFloatArray(mesh, sourceId));
        var vectors = new List<Vector3>();

        if (indices is not null && offset != -1 && stride != -1)
        {
            for (var i = 0; i + offset < indices.Count; i += stride)
            {
                var index = indices[i + offset];
                if (index < 0 || index * 3 + 2 >= floats.Count)
                {
                    vectors.Add(Vector3.Zero);
                    continue;
                }
                vectors.Add(new Vector3(floats[index * 3], floats[index * 3 + 1], floats[index * 3 + 2]));
            }
        }
        else
        {
            for (var i = 0; i + 2 < floats.Count; i += 3)
            {
                vectors.Add(new Vector3(floats[i], floats[i + 1], floats[i + 2]));
            }
        }

        return vectors;
    }

    private static XElement? FindVertices(XElement mesh, string? vertexInputId) =>
        mesh.Elements(_collada + "vertices")
            .FirstOrDefault(v => v.Attribute("id")?.Value == vertexInputId);

    private static string ReadFloatArray(XElement mesh, string? sourceId) =>
        mesh.Elements(_collada + "source")
            .FirstOrDefault(source => source?.Attribute("id")?.Value == sourceId)
            ?.Element(_collada + "float_array")
            ?.Value ?? string.Empty;

    private static void GetInput(XElement? element, string semantic, out string? sourceId, out int offset)
    {
        var ns = element?.GetDefaultNamespace() ?? XNamespace.None;
        var input = element?.Elements(ns + "input")
            .FirstOrDefault(i => string.Equals(i.Attribute("semantic")?.Value, semantic));

        sourceId = input?.Attribute("source")?.Value?.TrimStart('#');
        offset = ParseOffset(input?.Attribute("offset")?.Value);
    }

    private static List<T> ParseArray<T>(string? value)
    {
        if (value is null)
        {
            return [];
        }

        var tokens = value.Split([' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<T>(tokens.Length);

        foreach (var token in tokens)
        {
            if (typeof(T) == typeof(int))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    result.Add((T)(object)parsed);
                }
            }
            else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Add((T)(object)parsed);
            }
        }

        return result;
    }

    private sealed class GeometryBuffers
    {
        public List<Vector3> Vertices { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<int> Indices { get; } = [];
    }
}
