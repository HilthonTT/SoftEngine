namespace SoftEngine.WinForms.Cameras;

/// <summary>
/// One of the six views straight down a world axis. Named for the side of the model the
/// camera ends up on: <see cref="Front"/> is the +Z side, which is where every world here is
/// framed from, so it is also the view a model loads with.
/// </summary>
public enum AxisView
{
    /// <summary>From +Z, looking along -Z.</summary>
    Front,

    /// <summary>From -Z, looking along +Z.</summary>
    Back,

    /// <summary>From +X, looking along -X.</summary>
    Right,

    /// <summary>From -X, looking along +X.</summary>
    Left,

    /// <summary>From +Y, looking straight down.</summary>
    Top,

    /// <summary>From -Y, looking straight up.</summary>
    Bottom,
}
