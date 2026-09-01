using Microsoft.Win32;

namespace SoftEngine.Gpu;

public readonly record struct GpuDevice(string Name, GpuAdapterKind Kind)
{
    public override string ToString() => Kind switch
    {
        GpuAdapterKind.Discrete => $"{Name} (discrete)",
        GpuAdapterKind.Integrated => $"{Name} (integrated)",
        _ => Name,
    };
}

public static class GpuDevices
{
    private const string DisplayClassKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static readonly Lock Gate = new();

    private static IReadOnlyList<GpuDevice>? _installed;

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
                if (name.Length != 4 || !name.All(char.IsAsciiDigit))
                {
                    continue;
                }

                using var instance = displayClass.OpenSubKey(name);

                if (instance?.GetValue("DriverDesc") is not string description || description.Length == 0)
                {
                    continue;
                }

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
            return [];
        }
    }
}
