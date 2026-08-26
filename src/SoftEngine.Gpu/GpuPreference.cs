namespace SoftEngine.Gpu;

/// <summary>
/// Which of a machine's graphics adapters a GPU render should be given, on a machine that has
/// more than one.
///
/// <para>
/// The three values are not this engine's invention: they are exactly what Windows offers under
/// Settings ▸ Display ▸ Graphics, because that setting is the only lever an OpenGL application
/// has over the choice. A laptop with a discrete card almost always drives its display from the
/// integrated one, and every context created without saying otherwise lands on the integrated
/// one too — which is the right default for battery and the wrong one for a renderer.
/// </para>
///
/// <para>
/// Named after the preference rather than after the device because that is what is actually
/// being expressed. There is no way to name a specific adapter to an OpenGL driver; what can be
/// said is "the fast one" or "the efficient one", and which physical part answers is the
/// driver's business. <see cref="GpuDevices"/> is what turns the two back into names for a menu.
/// </para>
/// </summary>
public enum GpuPreference
{
    /// <summary>
    /// Whatever the driver picks, which on a hybrid laptop is the integrated adapter. The
    /// default, and the only value that leaves no setting behind on the machine.
    /// </summary>
    Automatic,

    /// <summary>The discrete adapter, where there is one.</summary>
    HighPerformance,

    /// <summary>The integrated adapter, where there is one.</summary>
    PowerSaving,
}
