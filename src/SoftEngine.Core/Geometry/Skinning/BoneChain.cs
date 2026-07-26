using SoftEngine.Core.Animation;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

/// <summary>
/// A tube rigged to a chain of joints, generated rather than loaded.
///
/// Every part of skinning can be wrong in a way that still produces a picture — a transposed
/// bind matrix, a joint order that does not match the weights, a blend that normalizes when it
/// should not. Debugging that against a 30,000-vertex figure from a file means guessing which
/// of the two is at fault. This builds the smallest thing that exercises the whole path, with
/// geometry whose correct answer is obvious: a straight tube, bent by a chain of joints.
/// </summary>
public static class BoneChain
{
    /// <param name="Root">The chain's root node; the first joint is this node itself.</param>
    public sealed record Rig(SceneNode Root, Skeleton Skeleton, SkinnedMesh Mesh);

    /// <param name="boneCount">Joints in the chain, each driving one segment of the tube.</param>
    /// <param name="boneLength">Length of one segment, along +Y.</param>
    /// <param name="radius">Radius of the tube.</param>
    /// <param name="sides">Vertices around the tube — its smoothness in cross-section.</param>
    /// <param name="ringsPerBone">
    /// Rings of vertices per segment. One ring per bone would make each segment rigid and the
    /// chain fold at hard corners; the intermediate rings are where the blend is visible.
    /// </param>
    public static Rig Create(
        int boneCount = 6,
        float boneLength = 2f,
        float radius = 0.7f,
        int sides = 16,
        int ringsPerBone = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boneCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(ringsPerBone, 1);

        var root = BuildJoints(boneCount, boneLength, out var joints);
        var skeleton = Skeleton.FromBindPose(root, joints);

        var height = boneCount * boneLength;
        var ringCount = boneCount * ringsPerBone + 1;

        var vertices = new List<Vector3>(ringCount * sides + 2);
        var normals = new List<Vector3>(ringCount * sides + 2);
        var weights = new SkinWeights.Builder(ringCount * sides + 2);

        for (var ring = 0; ring < ringCount; ring++)
        {
            var y = height * ring / (ringCount - 1f);

            for (var side = 0; side < sides; side++)
            {
                var angle = MathF.Tau * side / sides;
                var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));

                vertices.Add(direction * radius + new Vector3(0f, y, 0f));
                normals.Add(direction);

                Weight(weights, vertices.Count - 1, y, boneLength, boneCount);
            }
        }

        // Caps, so the tube is a solid with back-face culling on rather than a pipe you can
        // see the inside of.
        var bottomCenter = vertices.Count;
        vertices.Add(Vector3.Zero);
        normals.Add(-Vector3.UnitY);
        Weight(weights, bottomCenter, 0f, boneLength, boneCount);

        var topCenter = vertices.Count;
        vertices.Add(new Vector3(0f, height, 0f));
        normals.Add(Vector3.UnitY);
        Weight(weights, topCenter, height, boneLength, boneCount);

        var triangles = BuildTriangles(ringCount, sides, bottomCenter, topCenter);

        var mesh = new SkinnedMesh(
            [.. vertices],
            triangles,
            skeleton,
            weights.Build(),
            [.. normals]);

        mesh.UpdatePose();

        return new Rig(root, skeleton, mesh);
    }

    /// <summary>
    /// A clip that sends a travelling wave down the chain — each joint swinging about Z, and
    /// each one lagging the one before it, which is what reads as a whip rather than a wiper.
    /// </summary>
    /// <param name="phasePerBone">Radians of lag added per joint down the chain.</param>
    /// <param name="keysPerCycle">
    /// Keyframes sampled across one period. The clip is linearly interpolated, so this is how
    /// finely a sine wave is approximated by straight lines.
    /// </param>
    public static AnimationClip Wave(
        int boneCount,
        float amplitudeDegrees = 24f,
        float period = 2.4f,
        float phasePerBone = 0.7f,
        int keysPerCycle = 24,
        string namePrefix = "bone")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boneCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(keysPerCycle, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);

        var amplitude = amplitudeDegrees * MathF.PI / 180f;
        var channels = new List<NodeChannel>(boneCount);

        for (var bone = 0; bone < boneCount; bone++)
        {
            // One extra key, holding the value of the first, so the loop point is seamless
            // rather than a step from the last sample back to the start.
            var times = new float[keysPerCycle + 1];
            var rotations = new Quaternion[keysPerCycle + 1];

            for (var key = 0; key <= keysPerCycle; key++)
            {
                var time = period * key / keysPerCycle;
                var phase = MathF.Tau * key / keysPerCycle - bone * phasePerBone;

                times[key] = time;
                rotations[key] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, amplitude * MathF.Sin(phase));
            }

            channels.Add(new NodeChannel($"{namePrefix}{bone}")
            {
                Rotation = new QuaternionTrack(times, rotations),
            });
        }

        return new AnimationClip("Wave", channels);
    }

    private static SceneNode BuildJoints(int boneCount, float boneLength, out SceneNode[] joints)
    {
        joints = new SceneNode[boneCount];

        var root = new SceneNode("bone0");
        joints[0] = root;

        for (var bone = 1; bone < boneCount; bone++)
        {
            // Each joint sits one segment above its parent, so rotating a joint swings
            // everything above it — the property that makes a chain a chain.
            joints[bone] = joints[bone - 1].Add(new SceneNode($"bone{bone}")
            {
                Position = new Vector3(0f, boneLength, 0f),
            });
        }

        root.UpdateWorldMatrices();

        return root;
    }

    /// <summary>
    /// Splits a vertex between the joint below it and the one above, by how far along the
    /// segment it sits. A blend spanning the whole segment is wider than a rigger would paint,
    /// and deliberately so: it makes the bend smooth and the weighting visible.
    /// </summary>
    private static void Weight(SkinWeights.Builder weights, int vertex, float y, float boneLength, int boneCount)
    {
        var position = y / boneLength;
        var lower = System.Math.Clamp((int)position, 0, boneCount - 1);
        var fraction = System.Math.Clamp(position - lower, 0f, 1f);

        if (lower + 1 >= boneCount)
        {
            // Past the last joint there is nothing to blend toward, so the tip is rigid.
            weights.Add(vertex, lower, 1f);
            return;
        }

        weights.Add(vertex, lower, 1f - fraction);
        weights.Add(vertex, lower + 1, fraction);
    }

    private static Triangle[] BuildTriangles(int ringCount, int sides, int bottomCenter, int topCenter)
    {
        var triangles = new List<Triangle>((ringCount - 1) * sides * 2 + sides * 2);

        for (var ring = 0; ring + 1 < ringCount; ring++)
        {
            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;

                var lowerLeft = ring * sides + side;
                var lowerRight = ring * sides + next;
                var upperLeft = (ring + 1) * sides + side;
                var upperRight = (ring + 1) * sides + next;

                // Wound so that the cross product of the first two edges points away from the
                // axis, which is what the back-face test reads as facing the camera.
                triangles.Add(new Triangle(lowerLeft, upperLeft, lowerRight));
                triangles.Add(new Triangle(lowerRight, upperLeft, upperRight));
            }
        }

        var top = (ringCount - 1) * sides;

        for (var side = 0; side < sides; side++)
        {
            var next = (side + 1) % sides;

            triangles.Add(new Triangle(bottomCenter, side, next));
            triangles.Add(new Triangle(topCenter, top + next, top + side));
        }

        return [.. triangles];
    }
}
