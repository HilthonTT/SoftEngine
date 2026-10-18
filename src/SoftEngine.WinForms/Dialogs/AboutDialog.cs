using System.Reflection;

namespace SoftEngine.WinForms.Dialogs;

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
        ClientSize = new Size(460, 350);
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

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8),
        };

        if (Theme.AppIcon is { } appIcon)
        {
            Icon = appIcon;

            using var large = new Icon(appIcon, 48, 48);
            header.Controls.Add(new PictureBox
            {
                Image = large.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48),
                Margin = new Padding(0, 0, 12, 0),
            });
        }

        header.Controls.Add(new Label
        {
            Text = "SoftEngine",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 0, 0),
        });

        stack.Controls.Add(header);

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
