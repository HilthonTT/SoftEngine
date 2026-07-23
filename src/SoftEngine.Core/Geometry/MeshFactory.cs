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

            // Normals are only usable when there is exactly one per vertex; otherwise let
            // the mesh compute its own instead of indexing out of range while rendering.
            meshes.Add(new Mesh(
                buffers.Vertices.ToArray(),
                buffers.Indices.ToArray().BuildTriangleIndices(),
                buffers.Normals.Count == buffers.Vertices.Count && buffers.Normals.Count > 0
                    ? buffers.Normals.ToArray()
                    : null,
                triangleColors: null));
            progress?.Report(done + share);
        }

        progress?.Report(1f);

        return meshes.ToArray();
    }

    /// <summary>
    /// A <c>polylist</c> references its positions (and possibly normals) through a shared
    /// <c>vertices</c> element. Its <c>p</c> stream interleaves one lane per input and its
    /// <c>vcount</c> element gives the size of each polygon, which is fan-triangulated here.
    /// </summary>
    private static void ReadPolylist(XElement mesh, XElement polylist, GeometryBuffers buffers)
    {
        var interleavedIndices = ParseArray<int>(polylist.Element(_collada + "p")?.Value);
        var stride = Stride(polylist);

        GetInput(polylist, "VERTEX", out var vertexInputId, out var vertexOffset);
        var vertices = FindVertices(mesh, vertexInputId);

        GetInput(vertices, "POSITION", out var positionSourceId, out _);
        GetInput(vertices, "NORMAL", out var normalSourceId, out _);

        var baseVertex = buffers.Vertices.Count;
        buffers.Vertices.AddRange(ReadVectors(mesh, positionSourceId));
        if (normalSourceId is not null)
        {
            buffers.Normals.AddRange(ReadVectors(mesh, normalSourceId));
        }

        var vertexIndices = ExtractLane(interleavedIndices, vertexOffset, stride);
        var vcounts = ParseArray<int>(polylist.Element(_collada + "vcount")?.Value);

        if (vcounts.Count == 0)
        {
            // No vcount — assume the stream is already triangles.
            buffers.Indices.AddRange(vertexIndices.Select(index => index + baseVertex));
            return;
        }

        var cursor = 0;
        foreach (var vcount in vcounts)
        {
            if (cursor + vcount > vertexIndices.Count)
            {
                break;
            }

            for (var corner = 1; corner + 1 < vcount; corner++)
            {
                buffers.Indices.Add(vertexIndices[cursor] + baseVertex);
                buffers.Indices.Add(vertexIndices[cursor + corner] + baseVertex);
                buffers.Indices.Add(vertexIndices[cursor + corner + 1] + baseVertex);
            }

            cursor += vcount;
        }
    }

    /// <summary>
    /// A <c>triangles</c> block interleaves several index streams (one per input) in its
    /// <c>p</c> element. Each input declares an <c>offset</c> into that interleaved stream,
    /// so the stride is <c>maxOffset + 1</c> and each attribute is read from its own lane.
    /// </summary>
    private static void ReadTriangles(XElement mesh, XElement triangles, GeometryBuffers buffers)
    {
        var interleavedIndices = ParseArray<int>(triangles.Element(_collada + "p")?.Value);
        var stride = Stride(triangles);

        GetInput(triangles, "VERTEX", out var vertexInputId, out var vertexOffset);
        var vertices = FindVertices(mesh, vertexInputId);
        GetInput(vertices, "POSITION", out var positionSourceId, out _);

        GetInput(triangles, "NORMAL", out var normalSourceId, out var normalOffset);

        var baseVertex = buffers.Vertices.Count;
        var positions = ReadVectors(mesh, positionSourceId);
        buffers.Vertices.AddRange(positions);

        var vertexIndices = ExtractLane(interleavedIndices, vertexOffset, stride);

        if (normalSourceId is not null)
        {
            // The gathered normals are one per triangle corner; the mesh consumes them by
            // vertex index, so scatter each corner's normal to its vertex slot.
            var cornerNormals = ReadVectors(mesh, normalSourceId, interleavedIndices, normalOffset, stride);
            var vertexNormals = new Vector3[positions.Count];
            for (var corner = 0; corner < vertexIndices.Count && corner < cornerNormals.Count; corner++)
            {
                var vertexIndex = vertexIndices[corner];
                if (vertexIndex >= 0 && vertexIndex < vertexNormals.Length)
                {
                    vertexNormals[vertexIndex] = cornerNormals[corner];
                }
            }
            buffers.Normals.AddRange(vertexNormals);
        }

        buffers.Indices.AddRange(vertexIndices.Select(index => index + baseVertex));
    }

    /// <summary>The interleaved stream's stride: one lane per declared input offset.</summary>
    private static int Stride(XElement primitives) =>
        primitives.Elements(_collada + "input")
            .Select(input => int.Parse(input.Attribute("offset")?.Value ?? "0"))
            .DefaultIfEmpty(0)
            .Max() + 1;

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

    /// <summary>
    /// Returns the raw text of the <c>float_array</c> owned by the given source, or an
    /// empty string when the source (or its array) is missing.
    /// </summary>
    private static string ReadFloatArray(XElement mesh, string? sourceId) =>
        mesh.Elements(_collada + "source")
            .FirstOrDefault(source => source?.Attribute("id")?.Value == sourceId)
            ?.Element(_collada + "float_array")
            ?.Value ?? string.Empty;

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
