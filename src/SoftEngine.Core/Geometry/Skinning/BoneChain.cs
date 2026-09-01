using SoftEngine.Core.Animation;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

public static class BoneChain
{
    public sealed record Rig(SceneNode Root, Skeleton Skeleton, SkinnedMesh Mesh);

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
            joints[bone] = joints[bone - 1].Add(new SceneNode($"bone{bone}")
            {
                Position = new Vector3(0f, boneLength, 0f),
            });
        }

        root.UpdateWorldMatrices();

        return root;
    }

    private static void Weight(SkinWeights.Builder weights, int vertex, float y, float boneLength, int boneCount)
    {
        var position = y / boneLength;
        var lower = System.Math.Clamp((int)position, 0, boneCount - 1);
        var fraction = System.Math.Clamp(position - lower, 0f, 1f);

        if (lower + 1 >= boneCount)
        {
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
