using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Geometry;

public class GltfImporterTests
{
    private static ImportedScene Import(byte[] bytes, GltfImporter.TextureLoader? loader = null) =>
        GltfImporter.Import(bytes, AppContext.BaseDirectory, null, loader);

    private static GltfBuilder Triangle() =>
        new GltfBuilder()
            .Floats("pos",
                0f, 0f, 0f,
                1f, 0f, 0f,
                0f, 1f, 0f)
            .UShorts("idx", 0, 1, 2);

    [Fact]
    public void Import_Triangle_ReadsPositionsAndIndices()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var mesh = Assert.Single(scene.Meshes);

        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(new Vector3(1f, 0f, 0f), mesh.Vertices[1]);

        var triangle = Assert.Single(mesh.Triangles);
        Assert.Equal((0, 1, 2), (triangle.I0, triangle.I1, triangle.I2));
    }

    [Fact]
    public void Import_NodeMatrix_IsReadAsColumnMajor()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0, "matrix": [0,1,0,0, -1,0,0,0, 0,0,1,0, 1,2,3,1]}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var corner = Vector3.Transform(new Vector3(1f, 0f, 0f), Assert.Single(scene.Meshes).WorldMatrix);

        Assert.Equal(1f, corner.X, 4);
        Assert.Equal(3f, corner.Y, 4);
        Assert.Equal(3f, corner.Z, 4);
    }

    [Fact]
    public void Import_NodeTrs_ComposesScaleThenRotationThenTranslation()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{
                "mesh": 0,
                "translation": [5, 0, 0],
                "rotation": [0, 0, 0.7071068, 0.7071068],
                "scale": [2, 2, 2]
              }],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var corner = Vector3.Transform(new Vector3(1f, 0f, 0f), Assert.Single(scene.Meshes).WorldMatrix);

        Assert.Equal(5f, corner.X, 4);
        Assert.Equal(2f, corner.Y, 4);
        Assert.Equal(0f, corner.Z, 4);
    }

    [Fact]
    public void Import_CyclicNodeChildren_BreaksTheLoopAndKeepsTheChain()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [
                {"children": [1], "translation": [0, 1, 0]},
                {"children": [2], "translation": [0, 1, 0]},
                {"children": [0], "translation": [0, 1, 0], "mesh": 0}
              ],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        Approx.Equal(new Vector3(0, 3, 0), Assert.Single(scene.Meshes).WorldMatrix.Translation);

        Assert.Equal(4, scene.Root.SelfAndDescendants().Count());
    }

    [Fact]
    public void Import_InterleavedAttributes_HonorTheByteStride()
    {
        var builder = new GltfBuilder()
            .Floats("data",
                0f, 0f, 0f, 0f, 0f, 1f,
                1f, 0f, 0f, 0f, 0f, 1f,
                0f, 1f, 0f, 0f, 0f, 1f)
            .UShorts("idx", 0, 1, 2);

        var scene = Import(builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1}, "indices": 2}]}],
              "accessors": [
                {"bufferView": 0, "byteOffset": 0,  "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 0, "byteOffset": 12, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @data@, "byteLength": @dataLength@, "byteStride": 24},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var mesh = Assert.Single(scene.Meshes);

        Assert.Equal(new Vector3(1f, 0f, 0f), mesh.Vertices[1]);
        Assert.Equal(new Vector3(0f, 1f, 0f), mesh.Vertices[2]);
        Assert.All(mesh.NormVertices, normal => Assert.Equal(new Vector3(0f, 0f, 1f), normal));
    }

    [Fact]
    public void Import_SparseAccessor_OverwritesTheBaseElements()
    {
        var builder = new GltfBuilder()
            .Floats("pos", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f)
            .UShorts("idx", 0, 1, 2)
            .UShorts("sparseIdx", 1)
            .Floats("sparseVal", 9f, 9f, 9f);

        var scene = Import(builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {
                  "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                  "sparse": {
                    "count": 1,
                    "indices": {"bufferView": 2, "componentType": 5123},
                    "values": {"bufferView": 3}
                  }
                },
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@},
                {"buffer": 0, "byteOffset": @sparseIdx@, "byteLength": @sparseIdxLength@},
                {"buffer": 0, "byteOffset": @sparseVal@, "byteLength": @sparseValLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var mesh = Assert.Single(scene.Meshes);

        Assert.Equal(new Vector3(0f, 0f, 0f), mesh.Vertices[0]);
        Assert.Equal(new Vector3(9f, 9f, 9f), mesh.Vertices[1]);
        Assert.Equal(new Vector3(0f, 1f, 0f), mesh.Vertices[2]);
    }

    [Fact]
    public void Import_NormalizedTexCoords_AreScaledToTheUnitRange()
    {
        var builder = new GltfBuilder()
            .Floats("pos", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f)
            .UShorts("uv", 0, 0, 65535, 0, 0, 65535)
            .UShorts("idx", 0, 1, 2);

        var scene = Import(builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "TEXCOORD_0": 1}, "indices": 2}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "normalized": true, "count": 3, "type": "VEC2"},
                {"bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @uv@, "byteLength": @uvLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        var texCoords = Assert.Single(scene.Meshes).TexCoords;

        Assert.NotNull(texCoords);
        Assert.Equal(new Vector2(0f, 0f), texCoords[0]);
        Assert.Equal(1f, texCoords[1].X, 4);
        Assert.Equal(1f, texCoords[2].Y, 4);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Import_StripsAndFans_AreExpandedToTriangles(int mode)
    {
        var builder = new GltfBuilder()
            .Floats("pos",
                0f, 0f, 0f,
                1f, 0f, 0f,
                0f, 1f, 0f,
                1f, 1f, 0f)
            .UShorts("idx", 0, 1, 2, 3)
            .With("mode", mode);

        var scene = Import(builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "mode": @mode@}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 4, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        Assert.Equal(2, Assert.Single(scene.Meshes).Triangles.Length);
    }

    [Fact]
    public void Import_MetallicRoughnessMaterial_ReachesTheEngineMaterial()
    {
        var loaded = 0;

        var scene = Import(
            Triangle().Gltf("""
                {
                  "asset": {"version": "2.0"},
                  "scene": 0,
                  "scenes": [{"nodes": [0]}],
                  "nodes": [{"mesh": 0}],
                  "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
                  "materials": [{
                    "pbrMetallicRoughness": {
                      "baseColorFactor": [1, 0, 0, 1],
                      "metallicFactor": 0.75,
                      "roughnessFactor": 0.25,
                      "baseColorTexture": {"index": 0},
                      "metallicRoughnessTexture": {"index": 1}
                    },
                    "normalTexture": {"index": 1, "scale": 0.5},
                    "emissiveFactor": [0, 1, 0]
                  }],
                  "textures": [{"source": 0}, {"source": 1}],
                  "images": [
                    {"uri": "data:image/png;base64,QQ=="},
                    {"uri": "data:image/png;base64,Qg=="}
                  ],
                  "accessors": [
                    {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                    {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
                  ],
                  "bufferViews": [
                    {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                    {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
                  ],
                  "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
                }
                """),
            _ =>
            {
                loaded++;
                return Texture.Checkerboard(2, 2, ColorRGB.White, ColorRGB.Black);
            });

        var material = Assert.Single(scene.Meshes).Material;
        Assert.NotNull(material);

        Assert.Equal(0.75f, material.Metallic, 3);
        Assert.Equal(0.25f, material.Roughness, 3);
        Assert.Equal(0.5f, material.NormalStrength, 3);

        Assert.Equal(255, material.Diffuse.R);
        Assert.Equal(0, material.Diffuse.G);

        Assert.NotNull(material.DiffuseMap);
        Assert.Same(material.MetallicMap, material.RoughnessMap);
        Assert.Same(material.MetallicMap, material.NormalMap);

        Assert.Equal(2, loaded);
    }

    [Theory]
    [InlineData("BLEND", 0.25f)]
    [InlineData("OPAQUE", 1f)]
    [InlineData("MASK", 1f)]
    public void Import_AlphaMode_DecidesWhetherBaseColorAlphaIsTransparency(string mode, float expected)
    {
        var scene = Import(Triangle().With("mode", mode).Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
              "materials": [{
                "alphaMode": "@mode@",
                "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 0.25]}
              }],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        Assert.Equal(expected, Assert.Single(scene.Meshes).Opacity, 3);
    }

    [Theory]
    [InlineData("MASK", null, 0.5f)]
    [InlineData("MASK", "0.25", 0.25f)]
    [InlineData("BLEND", "0.25", 0f)]
    [InlineData("OPAQUE", "0.25", 0f)]
    public void Import_AlphaModeMask_CarriesTheCutoffAndNothingElse(string mode, string? cutoff, float expected)
    {
        var builder = Triangle()
            .With("mode", mode)
            .With("cutoff", cutoff is null ? string.Empty : $", \"alphaCutoff\": {cutoff}");

        var scene = Import(builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
              "materials": [{
                "alphaMode": "@mode@"@cutoff@,
                "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "baseColorTexture": {"index": 0}}
              }],
              "textures": [{"source": 0}],
              "images": [{"uri": "data:image/png;base64,QQ=="}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """),
            _ => Texture.Checkerboard(2, 2, ColorRGB.White, ColorRGB.Black));

        var material = Assert.Single(scene.Meshes).Material;
        Assert.NotNull(material);

        Assert.Equal(expected, material.AlphaCutoff, 3);
        Assert.Equal(expected > 0f, material.IsCutout);
    }

    [Fact]
    public void Import_AlphaModeMask_WithZeroCutoff_StaysACutout()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
              "materials": [{
                "alphaMode": "MASK",
                "alphaCutoff": 0,
                "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}}
              }],
              "textures": [{"source": 0}],
              "images": [{"uri": "data:image/png;base64,QQ=="}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """),
            _ => Texture.Checkerboard(2, 2, ColorRGB.White, ColorRGB.Black));

        var material = Assert.Single(scene.Meshes).Material;
        Assert.NotNull(material);

        Assert.True(material.IsCutout);
        Assert.True(material.AlphaCutoff > 0f);
    }

    [Fact]
    public void Import_SkinAtRestPose_ReproducesTheBindGeometry()
    {
        var scene = Import(SkinnedTriangle(

            [
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1,
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -5, 0, 1,
            ],
            jointA: "[1, 0, 0]",
            jointB: "[0, 5, 0]"));

        var skinned = Assert.Single(scene.SkinnedMeshes);

        Assert.Equal(2, skinned.Skeleton.JointCount);

        Assert.Equal(new Vector3(0f, 0f, 0f), skinned.Vertices[0]);
        Assert.Equal(new Vector3(1f, 0f, 0f), skinned.Vertices[1]);
        Assert.Equal(new Vector3(0f, 1f, 0f), skinned.Vertices[2]);
    }

    [Fact]
    public void Import_SkinnedMesh_FollowsItsJoints()
    {
        var scene = Import(SkinnedTriangle(
            [
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
            ],
            jointA: "[0, 0, 0]",
            jointB: "[0, 0, 0]"));

        var skinned = Assert.Single(scene.SkinnedMeshes);

        skinned.Skeleton.Joints[1].Position += new Vector3(0f, 10f, 0f);
        skinned.UpdatePose();

        Assert.Equal(10f, skinned.Vertices[1].Y, 4);
        Assert.Equal(new Vector3(0f, 1f, 0f), skinned.Vertices[2]);
    }

    private static byte[] SkinnedTriangle(float[] inverseBinds, string jointA, string jointB)
    {
        var builder = new GltfBuilder()
            .Floats("pos", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f)
            .UShorts("idx", 0, 1, 2)

            .UShorts("joints", 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0)
            .Floats("weights", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)
            .Floats("binds", inverseBinds)
            .With("jointA", jointA)
            .With("jointB", jointB);

        return builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0, 1, 2]}],
              "nodes": [
                {"mesh": 0, "skin": 0},
                {"name": "JointA", "translation": @jointA@},
                {"name": "JointB", "translation": @jointB@}
              ],
              "meshes": [{"primitives": [{
                "attributes": {"POSITION": 0, "JOINTS_0": 2, "WEIGHTS_0": 3},
                "indices": 1
              }]}],
              "skins": [{"joints": [1, 2], "inverseBindMatrices": 4}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"},
                {"bufferView": 2, "componentType": 5123, "count": 3, "type": "VEC4"},
                {"bufferView": 3, "componentType": 5126, "count": 3, "type": "VEC4"},
                {"bufferView": 4, "componentType": 5126, "count": 2, "type": "MAT4"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@},
                {"buffer": 0, "byteOffset": @joints@, "byteLength": @jointsLength@},
                {"buffer": 0, "byteOffset": @weights@, "byteLength": @weightsLength@},
                {"buffer": 0, "byteOffset": @binds@, "byteLength": @bindsLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """);
    }

    [Theory]
    [InlineData("LINEAR", 5f)]
    [InlineData("STEP", 0f)]
    public void Import_SamplerInterpolation_DecidesTheValueBetweenKeys(string interpolation, float expected)
    {
        var scene = Import(Animated(interpolation, cubic: false));

        var channel = Assert.Single(Assert.Single(scene.Clips).Channels);

        Assert.NotNull(channel.Translation);

        Assert.Equal(expected, channel.Translation.Sample(0.5f).Y, 4);
    }

    [Fact]
    public void Import_CubicSplineSampler_ReadsTangentsAndValuesApart()
    {
        var scene = Import(Animated("CUBICSPLINE", cubic: true));

        var channel = Assert.Single(Assert.Single(scene.Clips).Channels);

        Assert.NotNull(channel.Translation);
        Assert.Equal(2, channel.Translation.Count);

        Assert.Equal(0f, channel.Translation.Sample(0f).Y, 4);
        Assert.Equal(5f, channel.Translation.Sample(0.5f).Y, 4);
        Assert.Equal(10f, channel.Translation.Sample(1f).Y, 4);
    }

    [Fact]
    public void Import_NamedAnimation_BecomesItsOwnClip()
    {
        var clip = Assert.Single(Import(Animated("LINEAR", cubic: false)).Clips);

        Assert.Equal("Bob", clip.Name);
        Assert.Equal(1f, clip.Duration, 4);
    }

    private static byte[] Animated(string interpolation, bool cubic)
    {
        var builder = Triangle()
            .Floats("times", 0f, 1f)
            .With("interpolation", interpolation)
            .With("keys", cubic ? 6 : 2);

        builder = cubic
            ? builder.Floats("values",
                0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 10, 0, 0, 0, 0)
            : builder.Floats("values", 0, 0, 0, 0, 10, 0);

        return builder.Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"name": "Mover", "mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "animations": [{
                "name": "Bob",
                "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}],
                "samplers": [{"input": 2, "output": 3, "interpolation": "@interpolation@"}]
              }],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"},
                {"bufferView": 2, "componentType": 5126, "count": 2, "type": "SCALAR"},
                {"bufferView": 3, "componentType": 5126, "count": @keys@, "type": "VEC3"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@},
                {"buffer": 0, "byteOffset": @times@, "byteLength": @timesLength@},
                {"buffer": 0, "byteOffset": @values@, "byteLength": @valuesLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """);
    }

    [Fact]
    public void Import_GlbContainer_ReadsTheBinaryChunk()
    {
        var scene = Import(Triangle().Glb("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"byteLength": @LENGTH@}]
            }
            """));

        var mesh = Assert.Single(scene.Meshes);

        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(new Vector3(0f, 1f, 0f), mesh.Vertices[2]);
    }

    [Fact]
    public void Import_MeshInstancedTwice_SharesGeometryButNotTransforms()
    {
        var scene = Import(Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0, 1]}],
              "nodes": [
                {"mesh": 0, "translation": [-2, 0, 0]},
                {"mesh": 0, "translation": [2, 0, 0]}
              ],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """));

        Assert.Equal(2, scene.Meshes.Count);
        Assert.Same(scene.Meshes[0].Vertices, scene.Meshes[1].Vertices);

        Assert.Equal(-2f, scene.Meshes[0].WorldMatrix.Translation.X, 4);
        Assert.Equal(2f, scene.Meshes[1].WorldMatrix.Translation.X, 4);

        Assert.NotSame(scene.Meshes[0].TriangleColors, scene.Meshes[1].TriangleColors);
    }

    [Fact]
    public void Import_RequiredCompressionExtension_IsRefusedByName()
    {
        var bytes = Triangle().Gltf("""
            {
              "asset": {"version": "2.0"},
              "extensionsRequired": ["KHR_draco_mesh_compression"],
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [{"mesh": 0}],
              "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
              "accessors": [
                {"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}
              ],
              "bufferViews": [
                {"buffer": 0, "byteOffset": @pos@, "byteLength": @posLength@},
                {"buffer": 0, "byteOffset": @idx@, "byteLength": @idxLength@}
              ],
              "buffers": [{"uri": "@BUFFER@", "byteLength": @LENGTH@}]
            }
            """);

        var thrown = Assert.Throws<NotSupportedException>(() => Import(bytes));

        Assert.Contains("draco", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handles_RecognizesBothContainerExtensions()
    {
        Assert.True(GltfImporter.Handles("model.gltf"));
        Assert.True(GltfImporter.Handles("MODEL.GLB"));
        Assert.False(GltfImporter.Handles("model.dae"));
    }
}
