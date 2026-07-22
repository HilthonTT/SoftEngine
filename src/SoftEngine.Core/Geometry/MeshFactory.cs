using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// A minimal, best-effort Collada (.dae) reader. It understands just enough of the format
/// to pull positions, normals and vertex indices out of a mesh's <c>polylist</c> and/or
/// <c>triangles</c> blocks — it is not a general-purpose importer and assumes a
/// well-formed, single-source layout.
/// </summary>
public static class MeshFactory
{
    private static readonly XNamespace _collada = "http://www.collada.org/2005/11/COLLADASchema";

    /// <param name="fileName">Path of the .dae file to read.</param>
    /// <param name="progress">Optional overall progress in the range 0..1.</param>
    public static IMesh[] HackyImportCollada(string fileName, IProgress<float>? progress = null)
    {
        // Loading the XML document is roughly as expensive as walking it afterwards,
        // so it gets a fixed share of the reported progress.
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

            var polylist = mesh.Element(_collada + "polylist");
            if (polylist is not null)
            {
                ReadPolylist(mesh, polylist, buffers);
            }
            progress?.Report(done + share * 0.4f);

            var triangles = mesh.Element(_collada + "triangles");
            if (triangles is not null)
            {
                ReadTriangles(mesh, triangles, buffers);
            }
            progress?.Report(done + share * 0.8f);

            meshes.Add(new Mesh(
                buffers.Vertices.ToArray(),
                buffers.Indices.ToArray().BuildTriangleIndices(),
                buffers.Normals.Count > 0 ? buffers.Normals.ToArray() : null,
                triangleColors: null));
            progress?.Report(done + share);
        }

        progress?.Report(1f);

        return meshes.ToArray();
    }

    /// <summary>
    /// A <c>polylist</c> references its positions and normals through a shared
    /// <c>vertices</c> element, and its indices are already vertex indices.
    /// </summary>
    private static void ReadPolylist(XElement mesh, XElement polylist, GeometryBuffers buffers)
    {
        GetInput(polylist, "VERTEX", out var vertexInputId, out _);
        var vertices = FindVertices(mesh, vertexInputId);

        GetInput(vertices, "POSITION", out var positionSourceId, out _);
        GetInput(vertices, "NORMAL", out var normalSourceId, out _);

        buffers.Vertices.AddRange(ReadVectors(mesh, positionSourceId));
        buffers.Normals.AddRange(ReadVectors(mesh, normalSourceId));
        buffers.Indices.AddRange(ParseArray<int>(polylist.Element(_collada + "p")?.Value));
    }

    /// <summary>
    /// A <c>triangles</c> block interleaves several index streams (one per input) in its
    /// <c>p</c> element. Each input declares an <c>offset</c> into that interleaved stream,
    /// so the stride is <c>maxOffset + 1</c> and each attribute is read from its own lane.
    /// </summary>
    private static void ReadTriangles(XElement mesh, XElement triangles, GeometryBuffers buffers)
    {
        var interleavedIndices = ParseArray<int>(triangles.Element(_collada + "p")?.Value);
        var stride = triangles.Elements(_collada + "input")
            .Max(input => int.Parse(input.Attribute("offset")?.Value ?? "0")) + 1;

        GetInput(triangles, "VERTEX", out var vertexInputId, out var vertexOffset);
        var vertices = FindVertices(mesh, vertexInputId);
        GetInput(vertices, "POSITION", out var positionSourceId, out _);

        GetInput(triangles, "NORMAL", out var normalSourceId, out var normalOffset);

        buffers.Vertices.AddRange(ReadVectors(mesh, positionSourceId));
        buffers.Normals.AddRange(normalSourceId is null
            ? []
            : ReadVectors(mesh, normalSourceId, interleavedIndices, normalOffset, stride));
        buffers.Indices.AddRange(ExtractLane(interleavedIndices, vertexOffset, stride));
    }

    /// <summary>Picks every <paramref name="stride"/>-th index starting at <paramref name="lane"/>.</summary>
    private static List<int> ExtractLane(List<int> interleaved, int lane, int stride)
    {
        var laneIndices = new List<int>();
        for (var i = lane; i < interleaved.Count; i += stride)
        {
            laneIndices.Add(interleaved[i]);
        }
        return laneIndices;
    }

    /// <summary>
    /// Reads a <c>float_array</c> source as a list of <see cref="Vector3"/>. When
    /// <paramref name="indices"/> is supplied the values are gathered through the given lane
    /// of the interleaved index stream; otherwise the array is read straight through.
    /// </summary>
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
            for (var i = 0; i < indices.Count; i += stride)
            {
                var index = indices[i + offset];
                vectors.Add(new Vector3(floats[index * 3], floats[index * 3 + 1], floats[index * 3 + 2]));
            }
        }
        else
        {
            for (var i = 0; i < floats.Count; i += 3)
            {
                vectors.Add(new Vector3(floats[i], floats[i + 1], floats[i + 2]));
            }
        }

        return vectors;
    }

    /// <summary>Finds the <c>vertices</c> element a <c>VERTEX</c> input points at.</summary>
    private static XElement? FindVertices(XElement mesh, string? vertexInputId) =>
        mesh.Elements(_collada + "vertices")
            .FirstOrDefault(v => v.Attribute("id")?.Value == vertexInputId);

    /// <summary>Returns the raw text of the <c>float_array</c> owned by the given source.</summary>
    private static string ReadFloatArray(XElement mesh, string? sourceId) =>
        mesh.Elements(_collada + "source")
            .FirstOrDefault(source => source?.Attribute("id")?.Value == sourceId)
            !.Element(_collada + "float_array")
            !.Value;

    /// <summary>
    /// Reads the <c>input</c> with the given <paramref name="semantic"/> off an element,
    /// returning the source id it references (without the leading '#') and its offset.
    /// </summary>
    private static void GetInput(XElement? element, string semantic, out string? sourceId, out int offset)
    {
        var ns = element?.GetDefaultNamespace() ?? XNamespace.None;
        var input = element?.Elements(ns + "input")
            .FirstOrDefault(i => string.Equals(i.Attribute("semantic")?.Value, semantic));

        sourceId = input?.Attribute("source")?.Value?.TrimStart('#');
        offset = int.Parse(input?.Attribute("offset")?.Value ?? "0");
    }

    /// <summary>Splits a whitespace-separated value string into a typed list.</summary>
    private static List<T> ParseArray<T>(string? value)
    {
        if (value is null)
        {
            return [];
        }

        return value
            .Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => (T)Convert.ChangeType(token, typeof(T), CultureInfo.InvariantCulture))
            .ToList();
    }

    /// <summary>The positions, normals and vertex indices accumulated for one mesh.</summary>
    private sealed class GeometryBuffers
    {
        public List<Vector3> Vertices { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<int> Indices { get; } = [];
    }
}
