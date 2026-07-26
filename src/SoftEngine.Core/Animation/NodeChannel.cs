using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Animation;

/// <summary>
/// Everything one clip animates on one node: up to a translation, a rotation and a scale
/// curve, addressed by the node's name.
///
/// The target is a name rather than a node reference so a clip stays independent of any
/// particular skeleton instance — the same clip can pose two copies of a model, and an
/// importer can read the animation library without having built the scene tree yet.
/// </summary>
public sealed class NodeChannel(string targetName)
{
    public string TargetName { get; } = targetName;

    public Vector3Track? Translation { get; set; }

    public QuaternionTrack? Rotation { get; set; }

    public Vector3Track? Scale { get; set; }

    /// <summary>Whether this channel carries any keys at all.</summary>
    public bool IsEmpty =>
        Translation is not { Count: > 0 } &&
        Rotation is not { Count: > 0 } &&
        Scale is not { Count: > 0 };

    public float Duration => MathF.Max(
        Translation?.Duration ?? 0f,
        MathF.Max(Rotation?.Duration ?? 0f, Scale?.Duration ?? 0f));

    /// <summary>
    /// Writes this channel's value at <paramref name="time"/> into the node. Components with
    /// no curve are left as they are, so a clip that only rotates a joint does not reset the
    /// translation the rest pose gave it.
    /// </summary>
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

    /// <summary>
    /// Builds a channel from baked local matrices, the form Collada stores a node's animation
    /// in. Each is decomposed once at load, so playback interpolates translation, rotation and
    /// scale separately — blending the matrices themselves component by component shears a
    /// rotating joint, because the halfway point between two rotation matrices is not a
    /// rotation matrix.
    /// </summary>
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
                // A key that will not decompose (mirrored or sheared) still has a usable
                // position; holding the previous orientation beats emitting NaN.
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
