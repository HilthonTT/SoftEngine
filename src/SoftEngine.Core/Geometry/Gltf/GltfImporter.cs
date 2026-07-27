using SoftEngine.Core.Animation;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Shading;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace SoftEngine.Core.Geometry.Gltf;

/// <summary>
/// A glTF 2.0 reader, for both the JSON form (<c>.gltf</c>, with buffers and images beside it
/// or inline as data URIs) and the binary container (<c>.glb</c>).
///
/// glTF is the format this engine already had the shading model for. Collada carries geometry,
/// skins and clips but describes surfaces with the specular-shininess vocabulary of a decade
/// earlier, so the physically-based painter could only ever be pointed at procedural demos or
/// hand-set scalars. A glTF material is metallic-roughness, and its packed texture puts
/// roughness in green and metalness in blue — which is exactly what
/// <see cref="Material.MetallicMap"/> and <see cref="Material.RoughnessMap"/> already read,
/// because those channels were chosen for this format before there was a reader for it.
///
/// What is read: the default scene's node hierarchy, every mesh primitive as its own engine
/// mesh, metallic-roughness materials with their base-colour, normal, metallic-roughness and
/// emissive maps, skins with their inverse bind matrices, and animation samplers in all three
/// interpolation modes. What is not: morph targets, cameras, lights (<c>KHR_lights_punctual</c>)
/// and Draco-compressed geometry — the last of which is refused loudly rather than read as an
/// empty mesh, since a file that requires an extension is a file this reader cannot honestly
/// claim to have loaded.
/// </summary>
public static class GltfImporter
{
    /// <summary>
    /// Decodes an encoded image (PNG or JPEG) into a texture, or returns null when it cannot.
    ///
    /// glTF images arrive as a file beside the model, as a <c>data:</c> URI, or as a stretch of
    /// the binary chunk, and the importer resolves all three to bytes before asking — so a
    /// front-end supplies one decoder rather than three loaders. The Core has no image codec of
    /// its own, which is what keeps it platform-neutral.
    /// </summary>
    public delegate Texture? TextureLoader(ReadOnlyMemory<byte> encodedImage);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Whether a path names something this importer reads.</summary>
    public static bool Handles(string fileName) =>
        Path.GetExtension(fileName) is { } extension &&
        (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".glb", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a glTF or GLB file as a scene: meshes, the node tree, skins and clips.</summary>
    /// <param name="fileName">Path of the <c>.gltf</c> or <c>.glb</c> file.</param>
    /// <param name="progress">Optional overall progress in the range 0..1.</param>
    /// <param name="textureLoader">Decodes the file's images; without one the meshes keep their UVs and factors but get no maps.</param>
    public static ImportedScene Import(
        string fileName,
        IProgress<float>? progress = null,
        TextureLoader? textureLoader = null)
    {
        ArgumentNullException.ThrowIfNull(fileName, nameof(fileName));

        progress?.Report(0f);

        var bytes = File.ReadAllBytes(fileName);
        var directory = Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? ".";

        return Import(bytes, directory, progress, textureLoader);
    }

    /// <summary>
    /// Reads a glTF or GLB already in memory. <paramref name="baseDirectory"/> is where
    /// external buffers and images are resolved from; a self-contained GLB never uses it.
    /// Exists so tests can supply a document inline, as the Collada reader's overload does.
    /// </summary>
    public static ImportedScene Import(
        ReadOnlyMemory<byte> bytes,
        string baseDirectory,
        IProgress<float>? progress = null,
        TextureLoader? textureLoader = null)
    {
        // The container is told apart by its first four bytes rather than by the extension:
        // a .glb served as .gltf is a real thing, and the magic number is unambiguous.
        var span = bytes.Span;
        var isBinary = span.Length >= 4 && span[0] == (byte)'g' && span[1] == (byte)'l' && span[2] == (byte)'T' && span[3] == (byte)'F';

        string json;
        byte[]? binaryChunk = null;

        if (isBinary)
        {
            (json, binaryChunk) = GltfBuffers.ReadGlb(span);
        }
        else
        {
            // A BOM is legal in a .gltf and JsonSerializer will not read past one.
            json = Encoding.UTF8.GetString(span).TrimStart('﻿');
        }

        var root = JsonSerializer.Deserialize<GltfRoot>(json, _json)
            ?? throw new InvalidDataException("The glTF document is empty.");

        progress?.Report(0.15f);

        return new Reader(root, binaryChunk, baseDirectory, textureLoader).Build(progress);
    }

    /// <summary>
    /// One import in progress. It exists as an object because every stage needs the buffers,
    /// the node array and the caches, and threading six of those through a dozen static
    /// methods is how an importer becomes unreadable.
    /// </summary>
    private sealed class Reader(GltfRoot root, byte[]? binaryChunk, string baseDirectory, TextureLoader? textureLoader)
    {
        private readonly GltfBuffers _buffers = new(root, binaryChunk, baseDirectory);

        private readonly SceneNode _root = new("<scene>");
        private readonly SceneNode?[] _nodes = new SceneNode?[root.Nodes.Count];

        // Images decode once however many materials sample them, and a material becomes one
        // engine Material however many primitives share it.
        private readonly Dictionary<int, Texture?> _textures = [];
        private readonly Dictionary<int, (Material Material, float Opacity)> _materials = [];
        private readonly Dictionary<int, Skeleton> _skeletons = [];

        private readonly List<IMesh> _meshes = [];
        private readonly List<SkinnedMesh> _skinned = [];

        public ImportedScene Build(IProgress<float>? progress)
        {
            Reject();

            BuildNodes();
            progress?.Report(0.3f);

            BuildMeshes();
            progress?.Report(0.75f);

            // Skins index joints by node, so the hierarchy has to be posed before an inverse
            // bind matrix means anything relative to it.
            _root.UpdateWorldMatrices();

            var clips = BuildAnimations();
            progress?.Report(0.9f);

            foreach (var mesh in _skinned)
            {
                mesh.UpdatePose();
            }

            progress?.Report(1f);

            return new ImportedScene(_root, _meshes, clips, _skinned);
        }

        /// <summary>
        /// Refuses a file whose geometry this reader would silently get wrong.
        ///
        /// <c>extensionsRequired</c> is the author saying the file does not mean what it
        /// appears to mean without a particular extension. Draco is the one that matters here:
        /// a Draco primitive's accessors describe a compressed stream, and reading them as
        /// though they were vertices produces a mesh made of noise. Failing with the
        /// extension's name beats rendering that.
        /// </summary>
        private void Reject()
        {
            var blocking = root.ExtensionsRequired
                .Where(static name =>
                    name.Contains("draco", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("meshopt", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (blocking.Count > 0)
            {
                throw new NotSupportedException(
                    $"This glTF requires compressed geometry ({string.Join(", ", blocking)}), which this reader cannot decode. " +
                    "Re-export it without mesh compression.");
            }
        }

        private void BuildNodes()
        {
            for (var i = 0; i < root.Nodes.Count; i++)
            {
                _nodes[i] = MakeNode(root.Nodes[i]);
            }

            // Parent before rooting, so a node claimed as a child never also hangs off the root.
            var parented = new bool[_nodes.Length];

            for (var i = 0; i < root.Nodes.Count; i++)
            {
                foreach (var child in root.Nodes[i].Children)
                {
                    if (Node(child) is { } node && !parented[child] && child != i)
                    {
                        _nodes[i]!.Add(node);
                        parented[child] = true;
                    }
                }
            }

            foreach (var index in RootNodeIndices())
            {
                if (Node(index) is { } node && !parented[index])
                {
                    _root.Add(node);
                    parented[index] = true;
                }
            }

            // A node the default scene never mentions still has to hang somewhere: a skin can
            // name it as a joint, and a joint outside the tree never gets a world matrix.
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (!parented[i] && _nodes[i] is { } orphan)
                {
                    _root.Add(orphan);
                }
            }
        }

        /// <summary>
        /// The nodes the file wants shown: the default scene's, or the first scene's, or —
        /// for a file that is a library rather than a scene — every node at all.
        /// </summary>
        private IEnumerable<int> RootNodeIndices()
        {
            if (root.Scene is { } index && index >= 0 && index < root.Scenes.Count)
            {
                return root.Scenes[index].Nodes;
            }

            return root.Scenes.Count > 0 ? root.Scenes[0].Nodes : Enumerable.Range(0, root.Nodes.Count);
        }

        private static SceneNode MakeNode(GltfNode source)
        {
            var node = new SceneNode(source.Name ?? string.Empty)
            {
                Kind = source.Camera is not null ? SceneNodeKind.Camera : SceneNodeKind.Transform,
            };

            // A node carries either a baked matrix or the three components, never both.
            if (source.Matrix is { Length: >= 16 } matrix)
            {
                node.SetLocalMatrix(new Matrix4x4(
                    matrix[0], matrix[1], matrix[2], matrix[3],
                    matrix[4], matrix[5], matrix[6], matrix[7],
                    matrix[8], matrix[9], matrix[10], matrix[11],
                    matrix[12], matrix[13], matrix[14], matrix[15]));

                return node;
            }

            if (source.Translation is { Length: >= 3 } t)
            {
                node.Position = new Vector3(t[0], t[1], t[2]);
            }

            if (source.Rotation is { Length: >= 4 } r)
            {
                node.Rotation = Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]));
            }

            if (source.Scale is { Length: >= 3 } s)
            {
                node.Scale = new Vector3(s[0], s[1], s[2]);
            }

            return node;
        }

        private SceneNode? Node(int? index) =>
            index is { } i && i >= 0 && i < _nodes.Length ? _nodes[i] : null;

        /// <summary>
        /// Turns every node that instances a mesh into engine meshes — one per primitive,
        /// because the engine keeps one material per mesh and a glTF primitive is exactly the
        /// span of a mesh that shares one.
        /// </summary>
        private void BuildMeshes()
        {
            // Geometry is built once per glTF mesh and shared by every node that instances it.
            // Two instances of a static mesh differ only in their transform, and the renderer
            // never writes to a vertex array — so the second instance costs one small object
            // rather than another copy of the model.
            var built = new Dictionary<int, List<Primitive>>();

            for (var i = 0; i < root.Nodes.Count; i++)
            {
                var source = root.Nodes[i];

                if (source.Mesh is not { } meshIndex || meshIndex < 0 || meshIndex >= root.Meshes.Count)
                {
                    continue;
                }

                if (Node(i) is not { } node)
                {
                    continue;
                }

                if (!built.TryGetValue(meshIndex, out var primitives))
                {
                    primitives = ReadPrimitives(root.Meshes[meshIndex]);
                    built[meshIndex] = primitives;
                }

                var skin = ReadSkin(source.Skin);

                foreach (var primitive in primitives)
                {
                    _meshes.Add(Instantiate(primitive, node, skin));
                }
            }
        }

        /// <summary>One primitive's decoded attributes, before it is bound to any particular node.</summary>
        private sealed record Primitive(
            Vector3[] Positions,
            Triangle[] Triangles,
            Vector3[]? Normals,
            Vector2[]? TexCoords,
            Vector4[]? Tangents,
            ColorRGB[] Colors,
            Material Material,
            float Opacity,
            int[]? JointIndices,
            float[]? JointWeights);

        private List<Primitive> ReadPrimitives(GltfMesh mesh)
        {
            var result = new List<Primitive>(mesh.Primitives.Count);

            foreach (var primitive in mesh.Primitives)
            {
                if (primitive.Extensions.ContainsKey("KHR_draco_mesh_compression"))
                {
                    throw new NotSupportedException(
                        "This glTF has Draco-compressed geometry, which this reader cannot decode. Re-export it without mesh compression.");
                }

                if (!primitive.Attributes.TryGetValue("POSITION", out var positionAccessor))
                {
                    continue;
                }

                var positions = _buffers.ReadVector3(positionAccessor);
                if (positions.Length == 0)
                {
                    continue;
                }

                var triangles = ReadTopology(primitive, positions.Length);
                if (triangles.Length == 0)
                {
                    continue;
                }

                var normals = Attribute(primitive, "NORMAL") is { } normalAccessor
                    ? _buffers.ReadVector3(normalAccessor)
                    : null;

                var texCoords = Attribute(primitive, "TEXCOORD_0") is { } uvAccessor
                    ? _buffers.ReadVector2(uvAccessor)
                    : null;

                // glTF's TANGENT is already the xyz + handedness vector the engine's tangent
                // frame uses, so a file that ships one saves deriving it from the UVs.
                var tangents = Attribute(primitive, "TANGENT") is { } tangentAccessor
                    ? _buffers.ReadVector4(tangentAccessor)
                    : null;

                var material = ReadMaterial(primitive.Material, out var opacity);

                var vertexColors = ReadVertexColors(primitive, positions.Length);

                result.Add(new Primitive(
                    positions,
                    triangles,
                    normals is { Length: > 0 } && normals.Length >= positions.Length ? normals : null,
                    texCoords is { Length: > 0 } && texCoords.Length >= positions.Length ? texCoords : null,
                    tangents is { Length: > 0 } && tangents.Length >= positions.Length ? tangents : null,
                    TriangleColors(triangles, material, vertexColors),
                    material,
                    opacity,
                    Attribute(primitive, "JOINTS_0") is { } joints ? _buffers.ReadIndices(joints) : null,
                    Attribute(primitive, "WEIGHTS_0") is { } weights ? _buffers.ReadFloats(weights) : null));
            }

            return result;
        }

        private static int? Attribute(GltfPrimitive primitive, string name) =>
            primitive.Attributes.TryGetValue(name, out var index) ? index : null;

        /// <summary>
        /// The primitive's triangle list. Strips and fans are expanded here rather than
        /// rejected — they cost four lines and a file that uses them is otherwise unreadable.
        /// A primitive with no index accessor is drawn in vertex order, which the spec allows.
        /// </summary>
        private Triangle[] ReadTopology(GltfPrimitive primitive, int vertexCount)
        {
            var indices = primitive.Indices is { } accessor
                ? _buffers.ReadIndices(accessor)
                : [.. Enumerable.Range(0, vertexCount)];

            var valid = new List<Triangle>(indices.Length / 3 + 1);

            void Add(int a, int b, int c)
            {
                // A degenerate or out-of-range face is dropped rather than allowed through to
                // index past the vertex array during the first frame.
                if ((uint)a < (uint)vertexCount && (uint)b < (uint)vertexCount && (uint)c < (uint)vertexCount &&
                    a != b && b != c && a != c)
                {
                    valid.Add(new Triangle(a, b, c));
                }
            }

            switch (primitive.Mode)
            {
                case 4: // TRIANGLES
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Add(indices[i], indices[i + 1], indices[i + 2]);
                    }
                    break;

                case 5: // TRIANGLE_STRIP — every other triangle is wound the other way.
                    for (var i = 0; i + 2 < indices.Length; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            Add(indices[i], indices[i + 1], indices[i + 2]);
                        }
                        else
                        {
                            Add(indices[i + 1], indices[i], indices[i + 2]);
                        }
                    }
                    break;

                case 6: // TRIANGLE_FAN
                    for (var i = 1; i + 1 < indices.Length; i++)
                    {
                        Add(indices[0], indices[i], indices[i + 1]);
                    }
                    break;

                default:
                    // Points and lines have no area to rasterize; this engine draws surfaces.
                    break;
            }

            return [.. valid];
        }

        /// <summary>
        /// COLOR_0 as packed colours, or an empty array. glTF writes vertex colour in linear
        /// light with an optional alpha; the engine's per-triangle colour is sRGB, and the
        /// conversion is the same one every other colour in the file goes through.
        /// </summary>
        private ColorRGB[] ReadVertexColors(GltfPrimitive primitive, int vertexCount)
        {
            if (Attribute(primitive, "COLOR_0") is not { } accessor ||
                _buffers.AccessorAt(accessor) is not { } descriptor)
            {
                return [];
            }

            var components = GltfBuffers.ComponentsOf(descriptor.Type);
            if (components is not (3 or 4))
            {
                return [];
            }

            var floats = _buffers.ReadFloats(accessor);
            var count = System.Math.Min(vertexCount, floats.Length / components);

            var colors = new ColorRGB[count];

            for (var i = 0; i < count; i++)
            {
                colors[i] = new LinearColor(
                    floats[i * components],
                    floats[i * components + 1],
                    floats[i * components + 2]).ToColorRGB();
            }

            return colors;
        }

        /// <summary>
        /// One colour per triangle: the average of its vertices' colours where the file has
        /// them, and the material's base colour otherwise.
        ///
        /// The engine's unlit and per-vertex painters take a colour per triangle, so a file's
        /// per-vertex colour has to be reduced to reach them at all. The material path
        /// multiplies by its own base colour instead, which is why this is a fallback and not
        /// the whole story.
        /// </summary>
        private static ColorRGB[] TriangleColors(Triangle[] triangles, Material material, ColorRGB[] vertexColors)
        {
            var colors = new ColorRGB[triangles.Length];

            if (vertexColors.Length == 0)
            {
                Array.Fill(colors, material.Diffuse);
                return colors;
            }

            for (var i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];

                var a = vertexColors[System.Math.Min(t.I0, vertexColors.Length - 1)];
                var b = vertexColors[System.Math.Min(t.I1, vertexColors.Length - 1)];
                var c = vertexColors[System.Math.Min(t.I2, vertexColors.Length - 1)];

                colors[i] = new ColorRGB(
                    (byte)((a.R + b.R + c.R) / 3),
                    (byte)((a.G + b.G + c.G) / 3),
                    (byte)((a.B + b.B + c.B) / 3));
            }

            return colors;
        }

        /// <summary>Binds one decoded primitive to one node, skinned or not.</summary>
        private IMesh Instantiate(Primitive primitive, SceneNode node, Skeleton? skeleton)
        {
            if (skeleton is not null && primitive.JointIndices is { Length: > 0 } && primitive.JointWeights is { Length: > 0 })
            {
                var weights = BuildWeights(primitive.Positions.Length, primitive.JointIndices, primitive.JointWeights);

                // A skinned mesh's vertices come out of the deformer in the skeleton's space
                // already; the spec says the instancing node's own transform is ignored, which
                // is also what parenting it here would wrongly apply a second time.
                var skinnedMesh = new SkinnedMesh(
                    primitive.Positions,
                    primitive.Triangles,
                    skeleton,
                    weights,
                    primitive.Normals,
                    bindShapeMatrix: null,
                    primitive.Colors)
                {
                    Material = primitive.Material,
                    Opacity = primitive.Opacity,
                    TexCoords = primitive.TexCoords,
                    Tangents = primitive.Tangents,
                };

                _skinned.Add(skinnedMesh);
                return skinnedMesh;
            }

            // Positions, normals and indices are shared with the other instances of this
            // primitive; the colour array is not. Nothing in the pipeline writes to a vertex,
            // but recolouring one mesh and finding its twin recoloured too is a trap the
            // engine's own shared-colour primitives already lay once.
            return new Mesh(primitive.Positions, primitive.Triangles, primitive.Normals, (ColorRGB[])primitive.Colors.Clone())
            {
                Parent = node,
                Material = primitive.Material,
                Opacity = primitive.Opacity,
                TexCoords = primitive.TexCoords,
                Tangents = primitive.Tangents,
            };
        }

        /// <summary>
        /// glTF stores exactly four influences per vertex, which is the budget
        /// <see cref="SkinWeights"/> keeps — so this is a straight copy through the builder,
        /// which still renormalizes because an exporter's four weights need not sum to one.
        /// </summary>
        private static SkinWeights BuildWeights(int vertexCount, int[] joints, float[] weights)
        {
            var builder = new SkinWeights.Builder(vertexCount);

            var influences = System.Math.Min(joints.Length, weights.Length);

            for (var i = 0; i < influences; i++)
            {
                builder.Add(i / 4, joints[i], weights[i]);
            }

            return builder.Build();
        }

        private Skeleton? ReadSkin(int? skinIndex)
        {
            if (skinIndex is not { } index || index < 0 || index >= root.Skins.Count)
            {
                return null;
            }

            // Two meshes bound to one skin must share one skeleton, or each would rebuild the
            // same joint matrices from the same nodes every frame.
            if (_skeletons.TryGetValue(index, out var existing))
            {
                return existing;
            }

            var skin = root.Skins[index];
            if (skin.Joints.Count == 0)
            {
                return null;
            }

            var joints = new SceneNode[skin.Joints.Count];

            for (var i = 0; i < joints.Length; i++)
            {
                // A joint the node array never declared still needs a slot, or every weight
                // after it would index the wrong joint. An empty node poses as the identity.
                joints[i] = Node(skin.Joints[i]) ?? new SceneNode($"joint{i}");

                // The skeleton view is the one thing that reads this, and it is what makes a
                // real rig legible next to the transforms holding cameras and lights.
                joints[i].Kind = SceneNodeKind.Joint;
            }

            var inverseBinds = _buffers.ReadMatrices(skin.InverseBindMatrices);

            if (inverseBinds.Length < joints.Length)
            {
                // The accessor is optional: absent, every joint's inverse bind is the identity,
                // which says the mesh was modelled in the pose the nodes are already in.
                var filled = new Matrix4x4[joints.Length];
                Array.Fill(filled, Matrix4x4.Identity);
                Array.Copy(inverseBinds, filled, inverseBinds.Length);
                inverseBinds = filled;
            }
            else if (inverseBinds.Length > joints.Length)
            {
                Array.Resize(ref inverseBinds, joints.Length);
            }

            var skeleton = new Skeleton(Node(skin.Skeleton) ?? _root, joints, inverseBinds);

            _skeletons[index] = skeleton;
            return skeleton;
        }

        /// <summary>
        /// One glTF material as an engine <see cref="Material"/>, with the opacity the mesh
        /// carries separately.
        ///
        /// Every map degrades on its own: a file with no metallic-roughness texture falls back
        /// to the material's two scalars, and one with no material at all is the engine's
        /// default satin dielectric — the same graceful path the OBJ importer takes, so a glTF
        /// can be opened under any of the shading modes rather than only the physical one.
        /// </summary>
        private Material ReadMaterial(int? materialIndex, out float opacity)
        {
            opacity = 1f;

            if (materialIndex is not { } index || index < 0 || index >= root.Materials.Count)
            {
                return new Material();
            }

            if (_materials.TryGetValue(index, out var cached))
            {
                opacity = cached.Opacity;
                return cached.Material;
            }

            var source = root.Materials[index];
            var material = new Material();

            // Only a BLEND material is see-through. OPAQUE ignores its base colour's alpha
            // entirely — a file whose factor ends in 0.5 with no alphaMode is opaque, and
            // reading that alpha anyway is how a solid model arrives half transparent. MASK is
            // a per-texel cutout the engine has no path for; drawing the whole quad is the
            // less wrong of the two answers available.
            var blended = string.Equals(source.AlphaMode, "BLEND", StringComparison.Ordinal);

            if (source.PbrMetallicRoughness is { } pbr)
            {
                if (pbr.BaseColorFactor is { Length: >= 3 } factor)
                {
                    // The factor is linear light; the engine's colours are sRGB-encoded.
                    material.Diffuse = new LinearColor(factor[0], factor[1], factor[2]).ToColorRGB();

                    if (blended && factor.Length >= 4)
                    {
                        opacity = System.Math.Clamp(factor[3], 0f, 1f);
                    }
                }

                material.Metallic = System.Math.Clamp(pbr.MetallicFactor, 0f, 1f);
                material.Roughness = System.Math.Clamp(pbr.RoughnessFactor, 0f, 1f);

                // Blinn-Phong has no notion of roughness, so the non-physical painters get the
                // nearest thing to the same surface: a rough material has a broad, weak
                // highlight and a smooth one a tight, strong one.
                material.Shininess = MathF.Max(2f, 2f / MathF.Max(material.Roughness * material.Roughness, 1e-3f));
                material.SpecularStrength = 0.04f + 0.96f * (1f - material.Roughness);

                material.DiffuseMap = ReadTexture(pbr.BaseColorTexture);

                // One packed texture assigned to both properties: the engine reads metalness
                // from blue and roughness from green, which are the channels glTF packs them
                // into — so this is one map, sampled twice, not two copies of it.
                var packed = ReadTexture(pbr.MetallicRoughnessTexture);
                material.MetallicMap = packed;
                material.RoughnessMap = packed;
            }

            if (source.NormalTexture is { } normal)
            {
                material.NormalMap = ReadTexture(normal);
                material.NormalStrength = normal.Scale;
            }

            if (source.EmissiveFactor is { Length: >= 3 } emissive)
            {
                material.Emissive = new LinearColor(emissive[0], emissive[1], emissive[2]).ToColorRGB();
            }

            material.EmissiveMap = ReadTexture(source.EmissiveTexture);
            material.EmissiveStrength = source.Extensions?.EmissiveStrength?.Strength ?? 1f;

            _materials[index] = (material, opacity);

            return material;
        }

        private Texture? ReadTexture(GltfTextureInfo? info)
        {
            if (textureLoader is null || info is null || info.Index < 0 || info.Index >= root.Textures.Count)
            {
                return null;
            }

            var source = root.Textures[info.Index].Source;

            if (source is not { } imageIndex || imageIndex < 0 || imageIndex >= root.Images.Count)
            {
                return null;
            }

            if (_textures.TryGetValue(imageIndex, out var cached))
            {
                return cached;
            }

            var image = root.Images[imageIndex];

            // Three places an image can live — a buffer view of the binary chunk, a data URI,
            // or a file beside the model — and one decoder, because they differ only in where
            // the encoded bytes come from.
            var bytes = image.BufferView is not null
                ? _buffers.ViewBytes(image.BufferView)
                : image.Uri is { } uri
                    ? GltfBuffers.ResolveUri(uri, baseDirectory).AsMemory()
                    : ReadOnlyMemory<byte>.Empty;

            var texture = bytes.Length > 0 ? textureLoader(bytes) : null;

            _textures[imageIndex] = texture;
            return texture;
        }

        /// <summary>
        /// Every animation in the file as a clip. A glTF names its animations, unlike the
        /// Collada files this engine reads, so each becomes a clip of its own rather than all
        /// of them being merged into one.
        /// </summary>
        private List<AnimationClip> BuildAnimations()
        {
            var clips = new List<AnimationClip>();

            for (var a = 0; a < root.Animations.Count; a++)
            {
                var animation = root.Animations[a];

                var byNode = new Dictionary<string, NodeChannel>(StringComparer.Ordinal);
                var order = new List<NodeChannel>();

                foreach (var channel in animation.Channels)
                {
                    if (channel.Target?.Path is not { } path ||
                        Node(channel.Target.Node) is not { } node ||
                        channel.Sampler < 0 || channel.Sampler >= animation.Samplers.Count)
                    {
                        continue;
                    }

                    var sampler = animation.Samplers[channel.Sampler];

                    var times = _buffers.ReadFloats(sampler.Input);
                    if (times.Length == 0)
                    {
                        continue;
                    }

                    // Channels address nodes by name, which is what keeps a clip independent
                    // of any one skeleton — and what an unnamed node breaks, so it gets one.
                    if (string.IsNullOrEmpty(node.Name))
                    {
                        node.Name = $"node{channel.Target.Node}";
                    }

                    if (!byNode.TryGetValue(node.Name, out var nodeChannel))
                    {
                        nodeChannel = new NodeChannel(node.Name);
                        byNode[node.Name] = nodeChannel;
                        order.Add(nodeChannel);
                    }

                    ApplySampler(nodeChannel, path, sampler, times);
                }

                var populated = order.FindAll(static channel => !channel.IsEmpty);

                if (populated.Count > 0)
                {
                    clips.Add(new AnimationClip(animation.Name ?? $"Animation {a}", populated));
                }
            }

            return clips;
        }

        private void ApplySampler(NodeChannel channel, string path, GltfAnimationSampler sampler, float[] times)
        {
            var interpolation = sampler.Interpolation switch
            {
                "STEP" => TrackInterpolation.Step,
                "CUBICSPLINE" => TrackInterpolation.CubicSpline,
                _ => TrackInterpolation.Linear,
            };

            var values = _buffers.ReadFloats(sampler.Output);

            // A cubic-spline sampler stores three values per key — the tangent arriving, the
            // value, and the tangent leaving — so the key count divides differently.
            var perKey = interpolation == TrackInterpolation.CubicSpline ? 3 : 1;

            switch (path)
            {
                case "translation":
                    channel.Translation = Vector3Curve(times, values, interpolation, perKey);
                    break;

                case "scale":
                    channel.Scale = Vector3Curve(times, values, interpolation, perKey);
                    break;

                case "rotation":
                    channel.Rotation = QuaternionCurve(times, values, interpolation, perKey);
                    break;

                default:
                    // "weights" drives morph targets, which this engine does not deform by.
                    break;
            }
        }

        private static Vector3Track? Vector3Curve(float[] times, float[] values, TrackInterpolation interpolation, int perKey)
        {
            var count = System.Math.Min(times.Length, values.Length / (3 * perKey));
            if (count == 0)
            {
                return null;
            }

            var keys = times[..count];
            var samples = new Vector3[count];

            if (perKey == 1)
            {
                for (var i = 0; i < count; i++)
                {
                    samples[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
                }

                return new Vector3Track(keys, samples, interpolation);
            }

            var inTangents = new Vector3[count];
            var outTangents = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var o = i * 9;

                inTangents[i] = new Vector3(values[o + 0], values[o + 1], values[o + 2]);
                samples[i] = new Vector3(values[o + 3], values[o + 4], values[o + 5]);
                outTangents[i] = new Vector3(values[o + 6], values[o + 7], values[o + 8]);
            }

            return new Vector3Track(keys, samples, interpolation, inTangents, outTangents);
        }

        private static QuaternionTrack? QuaternionCurve(float[] times, float[] values, TrackInterpolation interpolation, int perKey)
        {
            var count = System.Math.Min(times.Length, values.Length / (4 * perKey));
            if (count == 0)
            {
                return null;
            }

            var keys = times[..count];
            var samples = new Quaternion[count];

            if (perKey == 1)
            {
                for (var i = 0; i < count; i++)
                {
                    samples[i] = Quaternion.Normalize(
                        new Quaternion(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3]));
                }

                return new QuaternionTrack(keys, samples, interpolation);
            }

            var inTangents = new Quaternion[count];
            var outTangents = new Quaternion[count];

            for (var i = 0; i < count; i++)
            {
                var o = i * 12;

                inTangents[i] = new Quaternion(values[o + 0], values[o + 1], values[o + 2], values[o + 3]);

                // The value is normalized; the tangents are not rotations and must not be.
                samples[i] = Quaternion.Normalize(
                    new Quaternion(values[o + 4], values[o + 5], values[o + 6], values[o + 7]));

                outTangents[i] = new Quaternion(values[o + 8], values[o + 9], values[o + 10], values[o + 11]);
            }

            return new QuaternionTrack(keys, samples, interpolation, inTangents, outTangents);
        }
    }
}
