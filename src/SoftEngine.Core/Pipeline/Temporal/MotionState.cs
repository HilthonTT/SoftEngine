using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

/// <summary>
/// Where everything was last frame.
///
/// <para>
/// A renderer is otherwise memoryless by design: every frame is built from the scene as it stands,
/// which is what makes it possible to render one frame of a scene and have it be right. Temporal
/// techniques break that — they need the difference between two frames, and a difference needs
/// something kept. This is the smallest thing that can be kept: one matrix per mesh and one for the
/// camera.
/// </para>
///
/// <para>
/// Meshes are keyed by reference, not by index. A world whose list is reordered — or one where a
/// mesh is inserted at the front — would otherwise hand every mesh its neighbour's previous
/// position, and the entire frame would appear to have moved. The cost is a dictionary lookup per
/// mesh per frame, which is nothing beside transforming its vertices twice.
/// </para>
/// </summary>
public sealed class MotionState
{
    // Not readonly: Advance swaps them, so this frame's matrices become the history without
    // being copied into it.
    private Dictionary<IMesh, Matrix4x4> _previous = new(ReferenceEqualityComparer.Instance);
    private Dictionary<IMesh, Matrix4x4> _current = new(ReferenceEqualityComparer.Instance);

    /// <summary>The camera and projection of the previous frame, composed.</summary>
    public Matrix4x4 PreviousViewProjection { get; private set; } = Matrix4x4.Identity;

    /// <summary>Whether there is a previous frame at all. False until <see cref="Advance"/> has run once.</summary>
    public bool HasHistory { get; private set; }

    /// <summary>
    /// Where a mesh was last frame, or where it is now when it was not in the last one. A mesh that
    /// has just appeared has no motion — reporting the identity instead would smear it in from the
    /// corner of the screen.
    /// </summary>
    public Matrix4x4 PreviousWorldMatrix(IMesh mesh, in Matrix4x4 current) =>
        _previous.TryGetValue(mesh, out var previous) ? previous : current;

    /// <summary>
    /// Records this frame as the one the next frame will compare against.
    ///
    /// The two dictionaries are swapped rather than one being cleared and refilled, so a mesh that
    /// has left the world stops being remembered — which is what keeps this from growing without
    /// bound in a scene that loads and unloads geometry.
    ///
    /// <para>
    /// Swapped, and not also copied. Nothing reads the history between here and the next frame's
    /// velocity pass, so filling one dictionary and then replaying it into the other did the same
    /// work twice — once per mesh per frame, on a path every frame runs whether or not anything
    /// temporal is switched on.
    /// </para>
    /// </summary>
    public void Advance(IWorld world, in Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        _current.Clear();

        foreach (var mesh in world.Meshes)
        {
            _current[mesh] = mesh.WorldMatrix;
        }

        // The one this frame filled becomes the history; what the previous frame left behind
        // becomes the scratch the next one clears and refills.
        (_previous, _current) = (_current, _previous);

        PreviousViewProjection = viewProjection;
        HasHistory = true;
    }

    /// <summary>
    /// Forgets everything, so the next frame is treated as the first one. What a resize, a scene
    /// load or a backend switch has to do: whatever the history holds is no longer about the picture
    /// that is on screen.
    /// </summary>
    public void Reset()
    {
        _previous.Clear();
        _current.Clear();

        PreviousViewProjection = Matrix4x4.Identity;
        HasHistory = false;
    }
}
