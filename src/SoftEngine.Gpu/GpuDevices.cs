using Microsoft.Win32;

namespace SoftEngine.Gpu;

/// <summary>One graphics adapter this machine has a driver for, as the driver names itself.</summary>
/// <param name="Name">The driver's description of the device — "NVIDIA GeForce RTX 5060 Laptop GPU".</param>
/// <param name="Kind">Discrete, integrated, or unknown when the name settles nothing.</param>
public readonly record struct GpuDevice(string Name, GpuAdapterKind Kind)
{
    public override string ToString() => Kind switch
    {
        GpuAdapterKind.Discrete => $"{Name} (discrete)",
        GpuAdapterKind.Integrated => $"{Name} (integrated)",
        _ => Name,
    };
}

/// <summary>
/// The adapters installed on this machine, so a preference can be offered by name rather than
/// as a word about power.
///
/// <para>
/// Nothing here creates a context or touches a driver: <see cref="GpuAdapter"/> is the device a
/// render is actually running on and costs an OpenGL context to find out, which is precisely
/// what a menu cannot do for every adapter it wants to list. This is the cheap half — what
/// Windows has drivers for — and the two are deliberately separate types, because a list of
/// installed devices is a guess about what a preference will select and the adapter behind a
/// live context is a fact.
/// </para>
///
/// <para>
/// Read from the display-adapter class key, which is where the driver's own
/// <c>DriverDesc</c> lives. WMI would answer the same question and takes hundreds of
/// milliseconds to start its infrastructure the first time; this is a registry enumeration of
/// a handful of subkeys, which is the difference between a menu that opens and one that hangs.
/// </para>
/// </summary>
public static class GpuDevices
{
    /// <summary>The device class Windows files display adapters under.</summary>
    private const string DisplayClassKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static readonly Lock Gate = new();

    private static IReadOnlyList<GpuDevice>? _installed;

    /// <summary>
    /// Every adapter with a driver on this machine, in the order Windows lists them. Empty on
    /// any platform where the question cannot be asked — a caller should read that as "unknown",
    /// not as "none", and go on offering the preference.
    /// </summary>
    public static IReadOnlyList<GpuDevice> Installed
    {
        get
        {
            lock (Gate)
            {
                return _installed ??= Enumerate();
            }
        }
    }

    /// <summary>
    /// The adapter a preference will most likely select, or null when that cannot be told:
    /// <see cref="GpuPreference.Automatic"/>, which is the driver's business, and any machine
    /// whose adapters do not sort into exactly one obvious candidate.
    ///
    /// <para>
    /// Deliberately null rather than a guess when there are two discrete cards or none. A menu
    /// naming the wrong device is worse than one naming no device: the first is believed.
    /// </para>
    /// </summary>
    public static GpuDevice? For(GpuPreference preference)
    {
        var wanted = preference switch
        {
            GpuPreference.HighPerformance => GpuAdapterKind.Discrete,
            GpuPreference.PowerSaving => GpuAdapterKind.Integrated,
            _ => GpuAdapterKind.Unknown,
        };

        if (wanted == GpuAdapterKind.Unknown)
        {
            return null;
        }

        GpuDevice? only = null;

        foreach (var device in Installed)
        {
            if (device.Kind != wanted)
            {
                continue;
            }

            if (only is not null)
            {
                return null;
            }

            only = device;
        }

        return only;
    }

    /// <summary>
    /// Whether this machine has both a discrete and an integrated adapter — the only case where
    /// the preference is a choice rather than a word with one possible outcome.
    /// </summary>
    public static bool HasChoice
    {
        get
        {
            var discrete = false;
            var integrated = false;

            foreach (var device in Installed)
            {
                discrete |= device.Kind == GpuAdapterKind.Discrete;
                integrated |= device.Kind == GpuAdapterKind.Integrated;
            }

            return discrete && integrated;
        }
    }

    private static IReadOnlyList<GpuDevice> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKeyPath);

            if (displayClass is null)
            {
                return [];
            }

            var devices = new List<GpuDevice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in displayClass.GetSubKeyNames())
            {
                // The class key holds the numbered driver instances alongside bookkeeping
                // subkeys such as "Properties" and "Configuration". Only the four-digit ones
                // are devices.
                if (name.Length != 4 || !name.All(char.IsAsciiDigit))
                {
                    continue;
                }

                using var instance = displayClass.OpenSubKey(name);

                if (instance?.GetValue("DriverDesc") is not string description || description.Length == 0)
                {
                    continue;
                }

                // A machine that has had a card replaced keeps the old instance's key, and a
                // driver update can leave two instances describing the same part.
                if (!seen.Add(description))
                {
                    continue;
                }

                var vendor = instance.GetValue("ProviderName") as string ?? string.Empty;

                devices.Add(new GpuDevice(description, GpuAdapter.KindOf(vendor, description)));
            }

            return devices;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // An unreadable hive means the list is unknown, which is what an empty one says.
            return [];
        }
    }
}
