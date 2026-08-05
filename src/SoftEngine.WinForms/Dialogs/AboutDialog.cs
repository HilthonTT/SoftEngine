using System.Reflection;

namespace SoftEngine.WinForms.Dialogs;

/// <summary>
/// What this is, what it is running on, and where to find the rest of it.
///
/// The runtime and adapter lines are the point: "it looks wrong on my machine" is a bug report
/// that needs to say <em>which</em> machine, and this is somewhere to copy that from without
/// knowing where else to look.
/// </summary>
internal sealed class AboutDialog : Form
{
    public const string ProjectUrl = "https://github.com/HilthonTT/SoftEngine";

    public AboutDialog(string backendDescription)
    {
        Text = "About SoftEngine";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 300);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9.75f);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(20, 18, 20, 8),
            BackColor = Theme.Background,
        };

        stack.Controls.Add(new Label
        {
            Text = "SoftEngine",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(0, 0, 0, 2),
        });

        stack.Controls.Add(new Label
        {
            Text = "A software 3D rasterizer in C#. The whole pipeline — transforms,\n"
                 + "culling, clipping, scanline fill, z-buffering and shading — runs\n"
                 + "on the CPU.",
            AutoSize = true,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 0, 0, 14),
        });

        foreach (var (label, value) in Facts(backendDescription))
        {
            stack.Controls.Add(new Label
            {
                Text = $"{label}   {value}",
                AutoSize = true,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 4),
            });
        }

        var link = new LinkLabel
        {
            Text = ProjectUrl,
            AutoSize = true,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.TextPrimary,
            VisitedLinkColor = Theme.Accent,
            Margin = new Padding(0, 14, 0, 0),
        };

        link.LinkClicked += (s, e) => OpenProjectPage();

        stack.Controls.Add(link);

        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(104, 36),
            Padding = new Padding(16, 6, 16, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Margin = Padding.Empty,
            UseVisualStyleBackColor = false,
            DialogResult = DialogResult.OK,
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 8, 20, 14),
            BackColor = Theme.Background,
        };

        footer.Controls.Add(close);

        Controls.Add(stack);
        Controls.Add(footer);

        AcceptButton = close;
        CancelButton = close;
    }

    private static (string Label, string Value)[] Facts(string backendDescription) =>
    [
        ("Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—"),
        ("Runtime", Environment.Version.ToString()),
        ("Rendering", backendDescription),
        ("Processors", $"{Environment.ProcessorCount} logical"),
        ("Licence", "MIT"),
    ];

    /// <summary>
    /// Hands the project page to whatever the machine opens links with.
    ///
    /// <c>UseShellExecute</c> is required: without it <see cref="System.Diagnostics.Process"/>
    /// tries to execute the URL as a program. A machine with no browser association is a
    /// shrug rather than a crash — nothing here is worth an error dialog.
    /// </summary>
    public static void OpenProjectPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or System.IO.FileNotFoundException)
        {
        }
    }
}
