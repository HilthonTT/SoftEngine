using SoftEngine.Core.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// A Wavefront OBJ reader (with companion MTL material support). It understands the common
/// subset used by exported models: <c>v</c>/<c>vt</c>/<c>vn</c> attributes, polygonal
/// <c>f</c> faces (any of <c>v</c>, <c>v/vt</c>, <c>v//vn</c>, <c>v/vt/vn</c>, including
/// negative relative indices), n-gon fan triangulation, <c>mtllib</c>/<c>usemtl</c>, and the
/// <c>Kd</c> diffuse colour plus <c>map_Kd</c> diffuse texture from the material file.
///
/// One <see cref="IMesh"/> is emitted per material actually used, so each mesh carries a
/// single diffuse colour and texture — matching the one-texture-per-mesh model of the engine.
/// Decoding the texture image is delegated to <paramref name="textureLoader"/> so this stays
/// platform-neutral (the Core has no image codec of its own).
/// </summary>
public static class ObjImporter
{
    /// <param name="fileName">Path of the <c>.obj</c> file to read.</param>
    /// <param name="progress">Optional overall progress in the range 0..1.</param>
    /// <param name="textureLoader">
    /// Resolves an absolute image path to a <see cref="Texture"/> (returns null if it cannot be
    /// loaded). When omitted, meshes keep their UVs and diffuse colour but get no texture.
    /// </param>
    public static IMesh[] ImportObj(
        string fileName,
        IProgress<float>? progress = null,
        Func<string, Texture?>? textureLoader = null)
    {
        progress?.Report(0f);

        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? ".";

        // Shared attribute pools — face indices reference these across the whole file.
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();

        var materials = new Dictionary<string, ObjMaterial>(StringComparer.Ordinal);
        var textureCache = new Dictionary<string, Texture?>(StringComparer.OrdinalIgnoreCase);

        // Groups keyed by the material name in effect; the empty key is the default material.
        var groups = new Dictionary<string, MeshBuilder>(StringComparer.Ordinal);
        var currentGroup = GetOrAddGroup(groups, string.Empty);

        var lines = File.ReadAllLines(fileName);
        const float readShare = 0.15f;
        progress?.Report(readShare);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var tokens = lines[lineIndex].Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0 || tokens[0].StartsWith('#'))
            {
                continue;
            }

            switch (tokens[0])
            {
                case "v":
                    positions.Add(ParseVector3(tokens));
                    break;

                case "vt":
                    // Only U and V are used; a third (W) coordinate is ignored.
                    texCoords.Add(new Vector2(ParseFloat(tokens, 1), ParseFloat(tokens, 2)));
                    break;

                case "vn":
                    normals.Add(ParseVector3(tokens));
                    break;

                case "f":
                    AddFace(tokens, positions, texCoords, normals, currentGroup);
                    break;

                case "usemtl":
                    currentGroup = GetOrAddGroup(groups, tokens.Length > 1 ? tokens[1] : string.Empty);
                    break;

                case "mtllib":
                    // The library path is the remainder of the line (filenames may contain spaces).
                    var libraryPath = lines[lineIndex]["mtllib".Length..].Trim();
                    LoadMaterialLibrary(Path.Combine(baseDirectory, libraryPath), materials);
                    break;
            }

            if ((lineIndex & 0x3FFF) == 0 && lines.Length > 0)
            {
                progress?.Report(readShare + (1f - readShare) * 0.85f * lineIndex / lines.Length);
            }
        }

        var meshes = new List<IMesh>();
        foreach (var (materialName, builder) in groups)
        {
            if (builder.Indices.Count == 0)
            {
                continue;
            }

            materials.TryGetValue(materialName, out var material);
            meshes.Add(builder.Build(material, baseDirectory, textureLoader, textureCache));
        }

        progress?.Report(1f);
        return meshes.ToArray();
    }

    private static MeshBuilder GetOrAddGroup(Dictionary<string, MeshBuilder> groups, string key)
    {
        if (!groups.TryGetValue(key, out var group))
        {
            group = new MeshBuilder();
            groups[key] = group;
        }
        return group;
    }

    /// <summary>Fan-triangulates a face and appends its (de-indexed) corners to the group.</summary>
    private static void AddFace(
        string[] tokens,
        List<Vector3> positions,
        List<Vector2> texCoords,
        List<Vector3> normals,
        MeshBuilder group)
    {
        // tokens[0] is "f"; the corners are tokens[1..]. Fan from the first corner.
        var cornerCount = tokens.Length - 1;
        if (cornerCount < 3)
        {
            return;
        }

        var first = group.ResolveCorner(tokens[1], positions, texCoords, normals);
        var previous = group.ResolveCorner(tokens[2], positions, texCoords, normals);

        for (var c = 3; c <= cornerCount; c++)
        {
            var current = group.ResolveCorner(tokens[c], positions, texCoords, normals);
            group.Indices.Add(first);
            group.Indices.Add(previous);
            group.Indices.Add(current);
            previous = current;
        }
    }

    private static void LoadMaterialLibrary(string path, Dictionary<string, ObjMaterial> materials)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ObjMaterial? current = null;
        foreach (var line in File.ReadLines(path))
        {
            var tokens = line.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0 || tokens[0].StartsWith('#'))
            {
                continue;
            }

            switch (tokens[0])
            {
                case "newmtl":
                    current = new ObjMaterial();
                    materials[tokens.Length > 1 ? tokens[1] : string.Empty] = current;
                    break;

                case "Kd" when current is not null:
                    current.Diffuse = new ColorRGB(
                        ToByte(ParseFloat(tokens, 1)),
                        ToByte(ParseFloat(tokens, 2)),
                        ToByte(ParseFloat(tokens, 3)));
                    break;

                case "map_Kd" when current is not null:
                    // Any texture options (-o, -s, …) precede the filename, so it is the last token.
                    current.DiffuseMap = tokens[^1];
                    break;
                default:
                    break;
            }
        }
    }

    private static Vector3 ParseVector3(string[] tokens) =>
        new(ParseFloat(tokens, 1), ParseFloat(tokens, 2), ParseFloat(tokens, 3));

    private static float ParseFloat(string[] tokens, int index) =>
        index < tokens.Length && float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0f;

    private static byte ToByte(float unit) => (byte)(System.Math.Clamp(unit, 0f, 1f) * 255f);

    /// <summary>A resolved OBJ material: diffuse colour and optional diffuse-map filename.</summary>
    private sealed class ObjMaterial
    {
        public ColorRGB Diffuse { get; set; } = ColorRGB.Gray;

        public string? DiffuseMap { get; set; }
    }

    /// <summary>
    /// Accumulates one mesh's worth of de-indexed geometry. OBJ addresses position, UV and
    /// normal with independent indices, so each unique <c>(v, vt, vn)</c> triple becomes one
    /// vertex in the unified buffers the engine expects.
    /// </summary>
    private sealed class MeshBuilder
    {
        private readonly Dictionary<(int Position, int TexCoord, int Normal), int> _lookup = [];

        public List<Vector3> Vertices { get; } = [];

        public List<Vector2> TexCoords { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<int> Indices { get; } = [];

        private bool _hasTexCoords;
        private bool _hasNormals;

        public int ResolveCorner(
            string corner,
            List<Vector3> positions,
            List<Vector2> texCoords,
            List<Vector3> normals)
        {
            var parts = corner.Split('/');
            var positionIndex = ResolveIndex(parts, 0, positions.Count);
            var texCoordIndex = ResolveIndex(parts, 1, texCoords.Count);
            var normalIndex = ResolveIndex(parts, 2, normals.Count);

            var key = (positionIndex, texCoordIndex, normalIndex);
            if (_lookup.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var index = Vertices.Count;
            Vertices.Add(positionIndex >= 0 && positionIndex < positions.Count ? positions[positionIndex] : Vector3.Zero);

            if (texCoordIndex >= 0 && texCoordIndex < texCoords.Count)
            {
                TexCoords.Add(texCoords[texCoordIndex]);
                _hasTexCoords = true;
            }
            else
            {
                TexCoords.Add(Vector2.Zero);
            }

            if (normalIndex >= 0 && normalIndex < normals.Count)
            {
                Normals.Add(normals[normalIndex]);
                _hasNormals = true;
            }
            else
            {
                Normals.Add(Vector3.Zero);
            }

            _lookup[key] = index;
            return index;
        }

        /// <summary>
        /// Parses one slash-separated component of a face corner. OBJ indices are 1-based;
        /// a negative index counts back from the current end of that attribute list.
        /// </summary>
        private static int ResolveIndex(string[] parts, int part, int count)
        {
            if (part >= parts.Length || parts[part].Length == 0)
            {
                return -1;
            }

            if (!int.TryParse(parts[part], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                return -1;
            }

            return index > 0 ? index - 1 : count + index;
        }

        public IMesh Build(
            ObjMaterial? material,
            string baseDirectory,
            Func<string, Texture?>? textureLoader,
            Dictionary<string, Texture?> textureCache)
        {
            var normals = _hasNormals ? Normals.ToArray() : ComputeNormals(Vertices, Indices);
            var diffuse = material?.Diffuse ?? ColorRGB.Gray;

            var mesh = new Mesh(
                Vertices.ToArray(),
                Indices.ToArray().BuildTriangleIndices(),
                normals,
                [.. Enumerable.Repeat(diffuse, Indices.Count / 3)]);

            if (_hasTexCoords)
            {
                mesh.TexCoords = TexCoords.ToArray();
                mesh.Texture = ResolveTexture(material, baseDirectory, textureLoader, textureCache);
            }

            return mesh;
        }

        private static Texture? ResolveTexture(
            ObjMaterial? material,
            string baseDirectory,
            Func<string, Texture?>? textureLoader,
            Dictionary<string, Texture?> textureCache)
        {
            if (material?.DiffuseMap is not { Length: > 0 } map || textureLoader is null)
            {
                return null;
            }

            var path = Path.GetFullPath(Path.Combine(baseDirectory, map));
            if (!textureCache.TryGetValue(path, out var texture))
            {
                texture = textureLoader(path);
                textureCache[path] = texture;
            }
            return texture;
        }

        /// <summary>
        /// Area-weighted per-vertex normals, computed only when the file supplies none. Cheap
        /// (single pass over the triangles) so loading a normal-less model never stalls.
        /// </summary>
        private static Vector3[] ComputeNormals(List<Vector3> vertices, List<int> indices)
        {
            var normals = new Vector3[vertices.Count];

            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
                var faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += faceNormal;
                normals[b] += faceNormal;
                normals[c] += faceNormal;
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].LengthSquared() > 1e-12f
                    ? Vector3.Normalize(normals[i])
                    : Vector3.UnitY;
            }

            return normals;
        }
    }
}
