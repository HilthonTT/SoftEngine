namespace SoftEngine.Core.Scenes.Graph;

/// <summary>
/// What a <see cref="SceneNode"/> is there for.
///
/// A scene file's node tree is not all rig: exported alongside the bones are the nodes holding
/// the lights and the cameras the artist set up. They transform and animate like anything else
/// — and a skeleton view that draws bones out to them is unreadable, because the lights are
/// metres away from the model and dwarf it.
/// </summary>
public enum SceneNodeKind
{
    /// <summary>A plain transform, and the default: a group, or a bone an exporter did not label.</summary>
    Transform,

    /// <summary>A bone — a node the file explicitly declared as part of a skeleton.</summary>
    Joint,

    /// <summary>Positions a light.</summary>
    Light,

    /// <summary>Positions a camera.</summary>
    Camera,
}
