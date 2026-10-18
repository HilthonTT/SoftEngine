using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Animation;

public sealed class NodeChannel(string targetName)
{
    public string TargetName { get; } = targetName;

    public Vector3Track? Translation { get; set; }

    public QuaternionTrack? Rotation { get; set; }

    public Vector3Track? Scale { get; set; }

    public bool IsEmpty =>
        Translation is not { Count: > 0 } &&
        Rotation is not { Count: > 0 } &&
        Scale is not { Count: > 0 };

    public float Duration => MathF.Max(
        Translation?.Duration ?? 0f,
        MathF.Max(Rotation?.Duration ?? 0f, Scale?.Duration ?? 0f));

    public void Apply(SceneNode node, float time)
    {
        if (Translation is { Count: > 0 } translation)
        {
            node.Position = translation.Sample(time);
        }

        if (Rotation is { Count: > 0 } rotation)
        {
            node.Rotation = rotation.Sample(time);
        }

        if (Scale is { Count: > 0 } scale)
        {
            node.Scale = scale.Sample(time);
        }
    }

    public bool SampleTranslation(float time, out Vector3 value)
    {
        if (Translation is { Count: > 0 } track)
        {
            value = track.Sample(time);
            return true;
        }

        value = Vector3.Zero;
        return false;
    }

    public bool SampleRotation(float time, out Quaternion value)
    {
        if (Rotation is { Count: > 0 } track)
        {
            value = track.Sample(time);
            return true;
        }

        value = Quaternion.Identity;
        return false;
    }

    public bool SampleScale(float time, out Vector3 value)
    {
        if (Scale is { Count: > 0 } track)
        {
            value = track.Sample(time);
            return true;
        }

        value = Vector3.One;
        return false;
    }

    public static NodeChannel FromMatrices(string targetName, float[] times, Matrix4x4[] matrices)
    {
        ArgumentNullException.ThrowIfNull(times, nameof(times));
        ArgumentNullException.ThrowIfNull(matrices, nameof(matrices));

        var count = System.Math.Min(times.Length, matrices.Length);

        var keyTimes = new float[count];
        var translations = new Vector3[count];
        var rotations = new Quaternion[count];
        var scales = new Vector3[count];

        for (var i = 0; i < count; i++)
        {
            keyTimes[i] = times[i];

            if (Matrix4x4.Decompose(matrices[i], out var scale, out var rotation, out var translation))
            {
                translations[i] = translation;
                rotations[i] = rotation;
                scales[i] = scale;
            }
            else
            {
                translations[i] = matrices[i].Translation;
                rotations[i] = i > 0 ? rotations[i - 1] : Quaternion.Identity;
                scales[i] = i > 0 ? scales[i - 1] : Vector3.One;
            }
        }

        return new NodeChannel(targetName)
        {
            Translation = new Vector3Track(keyTimes, translations),
            Rotation = new QuaternionTrack((float[])keyTimes.Clone(), rotations),
            Scale = new Vector3Track((float[])keyTimes.Clone(), scales),
        };
    }
}
