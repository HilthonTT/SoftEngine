using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;
using System.Xml.Linq;

namespace SoftEngine.Core.Tests;

public class ColladaSceneTests
{
    /// <summary>
    /// A whole Collada document small enough to reason about: one quad, two joints, a skin
    /// binding the quad to them, and a two-key animation on the second joint.
    ///
    /// Written out longhand rather than loaded from a file because the interesting part is the
    /// <em>convention</em> — Collada's matrices are column-vector, so the translation of
    /// <c>JointA</c> sits in the fourth column, and everything about the import is wrong in a
    /// hard-to-see way if that is not transposed. A hand-written matrix makes the expected
    /// answer unambiguous.
    /// </summary>
    private const string Document = """
        <?xml version="1.0" encoding="utf-8"?>
        <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
          <library_geometries>
            <geometry id="Geo" name="Geo">
              <mesh>
                <source id="GeoPos">
                  <float_array id="GeoPosArray" count="12">-1 0 0  1 0 0  1 1 0  -1 1 0</float_array>
                  <technique_common>
                    <accessor source="#GeoPosArray" count="4" stride="3">
                      <param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/>
                    </accessor>
                  </technique_common>
                </source>
                <vertices id="GeoVertices">
                  <input semantic="POSITION" source="#GeoPos"/>
                </vertices>
                <triangles count="2">
                  <input semantic="VERTEX" source="#GeoVertices" offset="0"/>
                  <p>0 1 2  0 2 3</p>
                </triangles>
              </mesh>
            </geometry>
          </library_geometries>
          <library_controllers>
            <controller id="Ctrl">
              <skin source="#Geo">
                <bind_shape_matrix>1 0 0 0  0 1 0 0  0 0 1 0  0 0 0 1</bind_shape_matrix>
                <source id="Ctrl-joints">
                  <Name_array id="Ctrl-joints-array" count="2">JointA JointB</Name_array>
                  <technique_common>
                    <accessor source="#Ctrl-joints-array" count="2" stride="1">
                      <param name="JOINT" type="Name"/>
                    </accessor>
                  </technique_common>
                </source>
                <source id="Ctrl-binds">
                  <float_array id="Ctrl-binds-array" count="32">
                    1 0 0 -5  0 1 0 0  0 0 1 0  0 0 0 1
                    1 0 0 -5  0 1 0 -3  0 0 1 0  0 0 0 1
                  </float_array>
                  <technique_common>
                    <accessor source="#Ctrl-binds-array" count="2" stride="16">
                      <param name="TRANSFORM" type="float4x4"/>
                    </accessor>
                  </technique_common>
                </source>
                <source id="Ctrl-weights">
                  <float_array id="Ctrl-weights-array" count="2">1 0.5</float_array>
                  <technique_common>
                    <accessor source="#Ctrl-weights-array" count="2" stride="1">
                      <param name="WEIGHT" type="float"/>
                    </accessor>
                  </technique_common>
                </source>
                <joints>
                  <input semantic="JOINT" source="#Ctrl-joints"/>
                  <input semantic="INV_BIND_MATRIX" source="#Ctrl-binds"/>
                </joints>
                <vertex_weights count="4">
                  <input semantic="JOINT" source="#Ctrl-joints" offset="0"/>
                  <input semantic="WEIGHT" source="#Ctrl-weights" offset="1"/>
                  <vcount>1 1 2 2</vcount>
                  <v>0 0   0 0   0 1 1 1   0 1 1 1</v>
                </vertex_weights>
              </skin>
            </controller>
          </library_controllers>
          <library_visual_scenes>
            <visual_scene id="Scene">
              <node id="JointA" sid="JointA" name="JointA" type="JOINT">
                <matrix>1 0 0 5  0 1 0 0  0 0 1 0  0 0 0 1</matrix>
                <node id="JointB" sid="JointB" name="JointB" type="JOINT">
                  <matrix>1 0 0 0  0 1 0 3  0 0 1 0  0 0 0 1</matrix>
                </node>
              </node>
              <node id="Lamp" name="Lamp">
                <matrix>1 0 0 100  0 1 0 0  0 0 1 0  0 0 0 1</matrix>
                <instance_light url="#SomeLight"/>
              </node>
            </visual_scene>
          </library_visual_scenes>
          <library_animations>
            <animation id="Anim" name="Anim">
              <source id="Anim-input">
                <float_array id="Anim-input-array" count="2">0 2</float_array>
                <technique_common>
                  <accessor source="#Anim-input-array" count="2" stride="1">
                    <param name="TIME" type="float"/>
                  </accessor>
                </technique_common>
              </source>
              <source id="Anim-output">
                <float_array id="Anim-output-array" count="32">
                  1 0 0 0  0 1 0 3  0 0 1 0  0 0 0 1
                  1 0 0 0  0 1 0 4  0 0 1 0  0 0 0 1
                </float_array>
                <technique_common>
                  <accessor source="#Anim-output-array" count="2" stride="16">
                    <param type="float4x4"/>
                  </accessor>
                </technique_common>
              </source>
              <sampler id="Anim-sampler">
                <input semantic="INPUT" source="#Anim-input"/>
                <input semantic="OUTPUT" source="#Anim-output"/>
              </sampler>
              <channel source="#Anim-sampler" target="JointB/matrix"/>
            </animation>
          </library_animations>
        </COLLADA>
        """;

    private static ImportedScene Import() => ColladaImporter.ImportScene(XDocument.Parse(Document));

    [Fact]
    public void Import_NodeMatrix_IsTransposedIntoRowVectorForm()
    {
        var scene = Import();

        var jointA = scene.Root.Find("JointA");
        Assert.NotNull(jointA);

        // Collada put the translation in the fourth column; read without transposing it would
        // land in the matrix's projective row and the joint would sit at the origin.
        Approx.Equal(new Vector3(5, 0, 0), jointA!.Position);
    }

    [Fact]
    public void Import_ChildNode_ComposesDownTheHierarchy()
    {
        var scene = Import();

        var jointB = scene.Root.Find("JointB");

        Approx.Equal(new Vector3(0, 3, 0), jointB!.Position);
        Approx.Equal(new Vector3(5, 3, 0), jointB.WorldMatrix.Translation);
    }

    [Fact]
    public void Import_LabelsJointsAndLights()
    {
        var scene = Import();

        Assert.Equal(SceneNodeKind.Joint, scene.Root.Find("JointA")!.Kind);
        Assert.Equal(SceneNodeKind.Light, scene.Root.Find("Lamp")!.Kind);
    }

    [Fact]
    public void Import_Skin_BecomesASkinnedMesh()
    {
        var scene = Import();

        Assert.True(scene.HasSkin);
        var mesh = Assert.Single(scene.SkinnedMeshes);

        Assert.Equal(2, mesh.Skeleton.JointCount);
        Assert.Equal(4, mesh.Vertices.Length);
        Assert.Same(mesh, scene.Meshes[0]);
    }

    [Fact]
    public void Import_AtRestPose_ReproducesTheOriginalGeometry()
    {
        var scene = Import();
        var mesh = scene.SkinnedMeshes[0];

        // The inverse bind matrices are the true inverses of the joints' bind world matrices,
        // so every skinning matrix is the identity and the mesh must be untouched. Any error
        // in the transpose, the joint order or the multiplication order shows up here.
        Approx.Equal(new Vector3(-1, 0, 0), mesh.Vertices[0]);
        Approx.Equal(new Vector3(1, 0, 0), mesh.Vertices[1]);
        Approx.Equal(new Vector3(1, 1, 0), mesh.Vertices[2]);
        Approx.Equal(new Vector3(-1, 1, 0), mesh.Vertices[3]);
    }

    [Fact]
    public void Import_VertexWeights_AreReadFromTheirOwnLanes()
    {
        var scene = Import();
        var weights = scene.SkinnedMeshes[0].Weights;

        // Vertices 0 and 1: one influence, joint 0, full weight.
        Assert.Equal(0, weights.JointIndices[0]);
        Assert.Equal(1f, weights.Weights[0], 4);
        Assert.Equal(-1, weights.JointIndices[1]);

        // Vertices 2 and 3: half of joint 0 and half of joint 1.
        Assert.Equal(0, weights.JointIndices[2 * SkinWeights.InfluencesPerVertex]);
        Assert.Equal(1, weights.JointIndices[2 * SkinWeights.InfluencesPerVertex + 1]);
        Assert.Equal(0.5f, weights.Weights[2 * SkinWeights.InfluencesPerVertex], 4);
        Assert.Equal(0.5f, weights.Weights[2 * SkinWeights.InfluencesPerVertex + 1], 4);
    }

    [Fact]
    public void Import_Animation_BecomesAClipTargetingTheNode()
    {
        var scene = Import();

        Assert.True(scene.HasAnimation);
        var clip = Assert.Single(scene.Clips);

        var channel = Assert.Single(clip.Channels);
        Assert.Equal("JointB", channel.TargetName);
        Assert.Equal(2f, clip.Duration, 4);
    }

    [Fact]
    public void Import_MatrixKeys_DecomposeIntoTranslation()
    {
        var scene = Import();
        var player = new AnimationPlayer(scene.Root, scene.Clips[0]);

        Assert.Equal(1, player.BoundChannelCount);

        player.Time = 2f;
        player.Apply();
        scene.Root.UpdateWorldMatrices();

        var jointB = scene.Root.Find("JointB")!;

        Approx.Equal(new Vector3(0, 4, 0), jointB.Position);
        Approx.Equal(new Vector3(5, 4, 0), jointB.WorldMatrix.Translation);
    }

    [Fact]
    public void Import_PosedSkin_MovesTheWeightedVertices()
    {
        var scene = Import();
        var mesh = scene.SkinnedMeshes[0];

        var player = new AnimationPlayer(scene.Root, scene.Clips[0]) { Time = 2f };
        player.Apply();
        mesh.UpdatePose();

        // The lower vertices belong entirely to JointA, which the clip does not touch.
        Approx.Equal(new Vector3(-1, 0, 0), mesh.Vertices[0]);

        // The upper ones are half JointA (still) and half JointB (up one unit), so they
        // travel half of that unit.
        Approx.Equal(new Vector3(1, 1.5f, 0), mesh.Vertices[2]);
    }

    [Fact]
    public void Import_HalfwayThroughTheClip_InterpolatesTheKeys()
    {
        var scene = Import();

        var player = new AnimationPlayer(scene.Root, scene.Clips[0]) { Time = 1f };
        player.Apply();

        Approx.Equal(new Vector3(0, 3.5f, 0), scene.Root.Find("JointB")!.Position);
    }

    [Fact]
    public void HackyImportCollada_StillReturnsPlainUnskinnedMeshes()
    {
        // The older entry point is what the static-model demos use. Adding the scene import
        // must not have changed what it hands back — in particular it must not start
        // returning skinned meshes for a file that has a controller.
        var path = Path.Combine(Path.GetTempPath(), $"softengine-{Guid.NewGuid():N}.dae");
        File.WriteAllText(path, Document);

        try
        {
            var meshes = ColladaImporter.HackyImportCollada(path);

            Assert.Single(meshes);
            Assert.Equal(2, meshes[0].Triangles.Length);
            Assert.Equal(4, meshes[0].Vertices.Length);
            Assert.IsType<Mesh>(meshes[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_DocumentWithNoSceneParts_IsStillReadable()
    {
        var bare = XDocument.Parse("""
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <library_geometries/>
            </COLLADA>
            """);

        var scene = ColladaImporter.ImportScene(bare);

        Assert.Empty(scene.Meshes);
        Assert.Empty(scene.Clips);
        Assert.False(scene.HasSkin);
    }

    [Fact]
    public void Import_EmptyAnimationArrays_ProduceNoClip()
    {
        // Exporters routinely emit channels with count="0" for lights and cameras; a clip
        // made of those would have a zero duration and nothing to play.
        var empty = XDocument.Parse("""
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <library_visual_scenes>
                <visual_scene id="Scene">
                  <node id="N" sid="N" name="N"><matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix></node>
                </visual_scene>
              </library_visual_scenes>
              <library_animations>
                <animation id="A">
                  <source id="A-in"><float_array id="A-in-array" count="0"></float_array></source>
                  <source id="A-out"><float_array id="A-out-array" count="0"></float_array></source>
                  <sampler id="A-s">
                    <input semantic="INPUT" source="#A-in"/>
                    <input semantic="OUTPUT" source="#A-out"/>
                  </sampler>
                  <channel source="#A-s" target="N/matrix"/>
                </animation>
              </library_animations>
            </COLLADA>
            """);

        var scene = ColladaImporter.ImportScene(empty);

        Assert.Empty(scene.Clips);
        Assert.NotNull(scene.Root.Find("N"));
    }

    [Fact]
    public void Import_TranslateAndRotateNodes_ComposeInDocumentOrder()
    {
        // The component form of a node transform, which Blender writes and Maya does not.
        var document = XDocument.Parse("""
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <library_visual_scenes>
                <visual_scene id="Scene">
                  <node id="N" sid="N" name="N">
                    <translate>0 10 0</translate>
                    <rotate>0 0 1 90</rotate>
                    <scale>2 2 2</scale>
                  </node>
                </visual_scene>
              </library_visual_scenes>
            </COLLADA>
            """);

        var scene = ColladaImporter.ImportScene(document);
        var node = scene.Root.Find("N")!;

        Approx.Equal(new Vector3(0, 10, 0), node.Position);
        Approx.Equal(new Vector3(2, 2, 2), node.Scale);

        // Scale, then a quarter turn about Z, then the translation: a local +X unit ends up
        // two units along +Y from the node's own position.
        Approx.Equal(new Vector3(0, 12, 0), Vector3.Transform(Vector3.UnitX, node.LocalMatrix));
    }

    [Fact]
    public void Import_GeometryInstance_ParentsTheMeshToItsNode()
    {
        var document = XDocument.Parse("""
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <library_geometries>
                <geometry id="Geo">
                  <mesh>
                    <source id="P">
                      <float_array id="PA" count="9">0 0 0  1 0 0  0 1 0</float_array>
                      <technique_common><accessor source="#PA" count="3" stride="3"/></technique_common>
                    </source>
                    <vertices id="V"><input semantic="POSITION" source="#P"/></vertices>
                    <triangles count="1">
                      <input semantic="VERTEX" source="#V" offset="0"/>
                      <p>0 1 2</p>
                    </triangles>
                  </mesh>
                </geometry>
              </library_geometries>
              <library_visual_scenes>
                <visual_scene id="Scene">
                  <node id="Holder" sid="Holder" name="Holder">
                    <matrix>1 0 0 0  0 1 0 25  0 0 1 0  0 0 0 1</matrix>
                    <instance_geometry url="#Geo"/>
                  </node>
                </visual_scene>
              </library_visual_scenes>
            </COLLADA>
            """);

        var scene = ColladaImporter.ImportScene(document);
        var mesh = Assert.Single(scene.Meshes);

        Assert.Same(scene.Root.Find("Holder"), mesh.Parent);
        Approx.Equal(new Vector3(0, 25, 0), mesh.WorldMatrix.Translation);
    }
}
