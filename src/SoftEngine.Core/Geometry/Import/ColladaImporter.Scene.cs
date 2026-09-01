using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace SoftEngine.Core.Geometry.Import;

public static partial class ColladaImporter
{
    public static ImportedScene ImportScene(string fileName, IProgress<float>? progress = null) =>
        ImportScene(XDocument.Load(fileName), progress);

    public static ImportedScene ImportScene(XDocument document, IProgress<float>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(document, nameof(document));

        progress?.Report(0f);

        var nodes = ReadVisualScenes(document);
        progress?.Report(0.2f);

        var meshes = ReadGeometries(document, out var meshByGeometryId);
        progress?.Report(0.6f);

        var skinned = ReadSkins(document, nodes, meshes, meshByGeometryId);
        AttachToNodes(nodes, meshes, meshByGeometryId);
        progress?.Report(0.8f);

        var clips = ReadAnimations(document, nodes);

        nodes.Root.UpdateWorldMatrices();
        foreach (var mesh in skinned)
        {
            mesh.UpdatePose();
        }

        progress?.Report(1f);

        return new ImportedScene(nodes.Root, meshes, clips, skinned);
    }

    private sealed class NodeIndex
    {
        public SceneNode Root { get; } = new("<scene>");

        public Dictionary<string, SceneNode> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SceneNode> BySid { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SceneNode> ByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SceneNode> GeometryInstances { get; } = new(StringComparer.Ordinal);

        public SceneNode? Resolve(string? reference, bool preferId)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            return preferId
                ? ById.GetValueOrDefault(reference) ?? BySid.GetValueOrDefault(reference) ?? ByName.GetValueOrDefault(reference)
                : BySid.GetValueOrDefault(reference) ?? ByName.GetValueOrDefault(reference) ?? ById.GetValueOrDefault(reference);
        }
    }

    private static NodeIndex ReadVisualScenes(XDocument document)
    {
        var index = new NodeIndex();

        var scenes = document.Root?.Element(_collada + "library_visual_scenes")
            ?.Elements(_collada + "visual_scene") ?? [];

        foreach (var scene in scenes)
        {
            foreach (var node in scene.Elements(_collada + "node"))
            {
                ReadNode(node, index.Root, index);
            }
        }

        return index;
    }

    private static void ReadNode(XElement element, SceneNode parent, NodeIndex index)
    {
        var id = element.Attribute("id")?.Value;
        var sid = element.Attribute("sid")?.Value;
        var name = element.Attribute("name")?.Value;

        var node = new SceneNode(name ?? id ?? sid ?? string.Empty);
        parent.Add(node);

        if (id is not null)
        {
            index.ById.TryAdd(id, node);
        }
        if (sid is not null)
        {
            index.BySid.TryAdd(sid, node);
        }
        if (name is not null)
        {
            index.ByName.TryAdd(name, node);
        }

        node.Kind = ClassifyNode(element);

        node.SetLocalMatrix(ReadNodeTransform(element));

        foreach (var instance in element.Elements(_collada + "instance_geometry"))
        {
            if (instance.Attribute("url")?.Value?.TrimStart('#') is { } geometryId)
            {
                index.GeometryInstances.TryAdd(geometryId, node);
            }
        }

        foreach (var child in element.Elements(_collada + "node"))
        {
            ReadNode(child, node, index);
        }
    }

    private static SceneNodeKind ClassifyNode(XElement element)
    {
        if (element.Element(_collada + "instance_light") is not null)
        {
            return SceneNodeKind.Light;
        }

        if (element.Element(_collada + "instance_camera") is not null)
        {
            return SceneNodeKind.Camera;
        }

        return string.Equals(element.Attribute("type")?.Value, "JOINT", StringComparison.OrdinalIgnoreCase)
            ? SceneNodeKind.Joint
            : SceneNodeKind.Transform;
    }

    private static Matrix4x4 ReadNodeTransform(XElement element)
    {
        var result = Matrix4x4.Identity;

        foreach (var child in element.Elements())
        {
            var values = ParseFloats(child.Value);

            Matrix4x4 step;

            if (child.Name == _collada + "matrix" && values.Length >= 16)
            {
                step = ToEngineMatrix(values, 0);
            }
            else if (child.Name == _collada + "translate" && values.Length >= 3)
            {
                step = Matrix4x4.CreateTranslation(values[0], values[1], values[2]);
            }
            else if (child.Name == _collada + "scale" && values.Length >= 3)
            {
                step = Matrix4x4.CreateScale(values[0], values[1], values[2]);
            }
            else if (child.Name == _collada + "rotate" && values.Length >= 4)
            {
                var axis = new Vector3(values[0], values[1], values[2]);

                step = axis.LengthSquared() > 0f
                    ? Matrix4x4.CreateFromAxisAngle(
                        Vector3.Normalize(axis),
                        values[3] * MathF.PI / 180f)
                    : Matrix4x4.Identity;
            }
            else
            {
                continue;
            }

            result = step * result;
        }

        return result;
    }

    private static Matrix4x4 ToEngineMatrix(ReadOnlySpan<float> values, int offset)
    {
        if (offset + 16 > values.Length)
        {
            return Matrix4x4.Identity;
        }

        var m = values.Slice(offset, 16);

        return new Matrix4x4(
            m[0], m[4], m[8], m[12],
            m[1], m[5], m[9], m[13],
            m[2], m[6], m[10], m[14],
            m[3], m[7], m[11], m[15]);
    }

    private static List<IMesh> ReadGeometries(XDocument document, out Dictionary<string, int> meshByGeometryId)
    {
        meshByGeometryId = new Dictionary<string, int>(StringComparer.Ordinal);

        var meshes = new List<IMesh>();

        var geometries = document.Root
            ?.Element(_collada + "library_geometries")
            ?.Elements(_collada + "geometry") ?? [];

        foreach (var geometry in geometries)
        {
            var mesh = geometry.Element(_collada + "mesh");
            if (mesh is null)
            {
                continue;
            }

            var buffers = new GeometryBuffers();

            var polylist = mesh.Element(_collada + "polylist");
            if (polylist is not null)
            {
                ReadPolylist(mesh, polylist, buffers);
            }

            var triangles = mesh.Element(_collada + "triangles");
            if (triangles is not null)
            {
                ReadTriangles(mesh, triangles, buffers);
            }

            if (geometry.Attribute("id")?.Value is { } id)
            {
                meshByGeometryId.TryAdd(id, meshes.Count);
            }

            meshes.Add(new Mesh(
                [.. buffers.Vertices],
                buffers.Indices.ToArray().BuildTriangleIndices(buffers.Vertices.Count),
                buffers.Normals.Count == buffers.Vertices.Count && buffers.Normals.Count > 0
                    ? [.. buffers.Normals]
                    : null,
                triangleColors: null));
        }

        return meshes;
    }

    private static List<SkinnedMesh> ReadSkins(
        XDocument document,
        NodeIndex nodes,
        List<IMesh> meshes,
        Dictionary<string, int> meshByGeometryId)
    {
        var skinned = new List<SkinnedMesh>();

        var controllers = document.Root
            ?.Element(_collada + "library_controllers")
            ?.Elements(_collada + "controller") ?? [];

        foreach (var controller in controllers)
        {
            var skin = controller.Element(_collada + "skin");

            var geometryId = skin?.Attribute("source")?.Value?.TrimStart('#');
            if (skin is null || geometryId is null || !meshByGeometryId.TryGetValue(geometryId, out var meshIndex))
            {
                continue;
            }

            if (meshes[meshIndex] is not Mesh source)
            {
                continue;
            }

            var skeleton = ReadSkeleton(skin, nodes);
            if (skeleton is null)
            {
                continue;
            }

            var weights = ReadVertexWeights(skin, source.Vertices.Length);
            if (weights is null)
            {
                continue;
            }

            var bindShape = ParseFloats(skin.Element(_collada + "bind_shape_matrix")?.Value);

            var mesh = new SkinnedMesh(
                source.Vertices,
                source.Triangles,
                skeleton,
                weights,
                source.NormVertices,
                bindShape.Length >= 16 ? ToEngineMatrix(bindShape, 0) : null,
                source.TriangleColors)
            {
                Material = source.Material,
                TexCoords = source.TexCoords,
            };

            meshes[meshIndex] = mesh;
            skinned.Add(mesh);
        }

        return skinned;
    }

    private static void AttachToNodes(NodeIndex nodes, List<IMesh> meshes, Dictionary<string, int> meshByGeometryId)
    {
        foreach (var (geometryId, node) in nodes.GeometryInstances)
        {
            if (!meshByGeometryId.TryGetValue(geometryId, out var index))
            {
                continue;
            }

            if (meshes[index] is Mesh mesh and not SkinnedMesh)
            {
                mesh.Parent = node;
            }
        }
    }

    private static Skeleton? ReadSkeleton(XElement skin, NodeIndex nodes)
    {
        var joints = skin.Element(_collada + "joints");
        if (joints is null)
        {
            return null;
        }

        GetInput(joints, "JOINT", out var jointSourceId, out _);
        GetInput(joints, "INV_BIND_MATRIX", out var bindSourceId, out _);

        var jointNames = ReadNameArray(skin, jointSourceId);
        if (jointNames.Count == 0)
        {
            return null;
        }

        var bindFloats = ParseFloats(ReadFloatArray(skin, bindSourceId));

        var jointNodes = new SceneNode[jointNames.Count];
        var inverseBinds = new Matrix4x4[jointNames.Count];

        for (var i = 0; i < jointNames.Count; i++)
        {
            jointNodes[i] = nodes.Resolve(jointNames[i], preferId: false) ?? new SceneNode(jointNames[i]);

            inverseBinds[i] = ToEngineMatrix(bindFloats, i * 16);
        }

        return new Skeleton(nodes.Root, jointNodes, inverseBinds);
    }

    private static SkinWeights? ReadVertexWeights(XElement skin, int vertexCount)
    {
        var vertexWeights = skin.Element(_collada + "vertex_weights");
        if (vertexWeights is null)
        {
            return null;
        }

        GetInput(vertexWeights, "JOINT", out _, out var jointOffset);
        GetInput(vertexWeights, "WEIGHT", out var weightSourceId, out var weightOffset);

        var stride = Stride(vertexWeights);
        var counts = ParseInts(vertexWeights.Element(_collada + "vcount")?.Value);
        var pairs = ParseInts(vertexWeights.Element(_collada + "v")?.Value);
        var weightValues = ParseFloats(ReadFloatArray(skin, weightSourceId));

        if (counts.Length == 0 || pairs.Length == 0 || weightValues.Length == 0)
        {
            return null;
        }

        var builder = new SkinWeights.Builder(vertexCount);
        var cursor = 0;

        for (var vertex = 0; vertex < counts.Length; vertex++)
        {
            for (var influence = 0; influence < counts[vertex]; influence++)
            {
                var jointSlot = cursor + jointOffset;
                var weightSlot = cursor + weightOffset;
                cursor += stride;

                if (weightSlot >= pairs.Length || jointSlot >= pairs.Length)
                {
                    break;
                }

                var weightIndex = pairs[weightSlot];
                if (weightIndex < 0 || weightIndex >= weightValues.Length)
                {
                    continue;
                }

                builder.Add(vertex, pairs[jointSlot], weightValues[weightIndex]);
            }
        }

        return builder.Build();
    }

    private static List<AnimationClip> ReadAnimations(XDocument document, NodeIndex nodes)
    {
        var clips = new List<AnimationClip>();

        var library = document.Root?.Element(_collada + "library_animations");
        if (library is null)
        {
            return clips;
        }

        var sources = ElementsById(document, "source");
        var samplers = ElementsById(document, "sampler");

        var byNode = new Dictionary<string, NodeChannel>(StringComparer.Ordinal);
        var order = new List<NodeChannel>();

        foreach (var channel in library.Descendants(_collada + "channel"))
        {
            var samplerId = channel.Attribute("source")?.Value?.TrimStart('#');
            var target = channel.Attribute("target")?.Value;

            if (samplerId is null || target is null || !samplers.TryGetValue(samplerId, out var sampler))
            {
                continue;
            }

            var slash = target.IndexOf('/');
            var nodeReference = slash < 0 ? target : target[..slash];
            var member = slash < 0 ? string.Empty : target[(slash + 1)..];

            if (nodes.Resolve(nodeReference, preferId: true) is not { } node)
            {
                continue;
            }

            GetInput(sampler, "INPUT", out var inputId, out _);
            GetInput(sampler, "OUTPUT", out var outputId, out _);

            if (inputId is null || outputId is null ||
                !sources.TryGetValue(inputId, out var input) ||
                !sources.TryGetValue(outputId, out var output))
            {
                continue;
            }

            var times = ParseFloats(input.Element(_collada + "float_array")?.Value);
            var values = ParseFloats(output.Element(_collada + "float_array")?.Value);

            if (times.Length == 0 || values.Length == 0)
            {
                continue;
            }

            if (!byNode.TryGetValue(node.Name, out var nodeChannel))
            {
                nodeChannel = new NodeChannel(node.Name);
                byNode[node.Name] = nodeChannel;
                order.Add(nodeChannel);
            }

            ApplyCurve(nodeChannel, member, AccessorStride(output), times, values);
        }

        var populated = order.FindAll(static channel => !channel.IsEmpty);

        if (populated.Count > 0)
        {
            clips.Add(new AnimationClip(
                library.Attribute("name")?.Value ?? library.Attribute("id")?.Value ?? "Default",
                populated));
        }

        return clips;
    }

    private static void ApplyCurve(NodeChannel channel, string member, int stride, float[] times, float[] values)
    {
        if (stride >= 16 || member.Contains("matrix", StringComparison.OrdinalIgnoreCase) || member.Contains("transform", StringComparison.OrdinalIgnoreCase))
        {
            var count = System.Math.Min(times.Length, values.Length / 16);
            if (count == 0)
            {
                return;
            }

            var matrices = new Matrix4x4[count];
            for (var i = 0; i < count; i++)
            {
                matrices[i] = ToEngineMatrix(values, i * 16);
            }

            var baked = NodeChannel.FromMatrices(channel.TargetName, times[..count], matrices);

            channel.Translation = baked.Translation;
            channel.Rotation = baked.Rotation;
            channel.Scale = baked.Scale;
            return;
        }

        if (stride < 3)
        {
            return;
        }

        var keys = System.Math.Min(times.Length, values.Length / 3);
        if (keys == 0)
        {
            return;
        }

        var vectors = new Vector3[keys];
        for (var i = 0; i < keys; i++)
        {
            vectors[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        }

        var track = new Vector3Track(times[..keys], vectors);

        if (member.Contains("scale", StringComparison.OrdinalIgnoreCase))
        {
            channel.Scale = track;
        }
        else if (member.Contains("translate", StringComparison.OrdinalIgnoreCase) ||
                 member.Contains("location", StringComparison.OrdinalIgnoreCase))
        {
            channel.Translation = track;
        }
    }

    private static Dictionary<string, XElement> ElementsById(XDocument document, string name)
    {
        var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var element in document.Descendants(_collada + name))
        {
            if (element.Attribute("id")?.Value is { } id)
            {
                byId.TryAdd(id, element);
            }
        }

        return byId;
    }

    private static int AccessorStride(XElement source) =>
        int.TryParse(
            source.Element(_collada + "technique_common")?.Element(_collada + "accessor")?.Attribute("stride")?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var stride)
            ? stride
            : 1;

    private static List<string> ReadNameArray(XElement container, string? sourceId)
    {
        var array = container.Elements(_collada + "source")
            .FirstOrDefault(source => source.Attribute("id")?.Value == sourceId);

        var text = (array?.Element(_collada + "Name_array") ?? array?.Element(_collada + "IDREF_array"))?.Value;

        return text is null
            ? []
            : [.. text.Split([' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries)];
    }

    private static float[] ParseFloats(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var span = value.AsSpan();
        var result = new List<float>();

        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            var start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            if (i > start && float.TryParse(span[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return [.. result];
    }

    private static int[] ParseInts(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var span = value.AsSpan();
        var result = new List<int>();

        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            var start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            if (i > start && int.TryParse(span[start..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return [.. result];
    }
}
