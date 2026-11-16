using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>The small line icons the editor chrome is drawn with.</summary>
internal enum Glyph
{
    None,
    Select,
    Move,
    Rotate,
    Scale,
    Wireframe,
    Solid,
    MaterialPreview,
    Rendered,
    Eye,
    EyeClosed,
    Camera,
    Light,
    Mesh,
    Collection,
    Scene,
    Render,
    World,
    Object,
    Material,
    Debug,
    View,
    Play,
    Pause,
    JumpStart,
    JumpEnd,
    Magnet,
    Overlays,
    Grid,
    Viewport3D,
    Outliner,
    Properties,
    Timeline,
    Mouse,
}

/// <summary>
/// Draws every <see cref="Glyph"/> as a few strokes in a square, so they scale with the display
/// and pick up whatever colour the control they sit on is drawn in.
/// </summary>
internal static class GlyphPainter
{
    public static void Draw(Graphics g, Glyph glyph, RectangleF r, Color color)
    {
        if (glyph == Glyph.None)
        {
            return;
        }

        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var s = MathF.Min(r.Width, r.Height);
        var x = r.X + (r.Width - s) / 2f;
        var y = r.Y + (r.Height - s) / 2f;

        using var pen = new Pen(color, MathF.Max(1.2f, s / 11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        PointF P(float u, float v) => new(x + u * s, y + v * s);
        RectangleF R(float u, float v, float w, float h) => new(x + u * s, y + v * s, w * s, h * s);

        switch (glyph)
        {
            case Glyph.Select:
                g.FillPolygon(brush, [P(0.25f, 0.12f), P(0.25f, 0.8f), P(0.42f, 0.64f), P(0.55f, 0.9f), P(0.66f, 0.85f), P(0.53f, 0.6f), P(0.75f, 0.58f)]);
                break;

            case Glyph.Move:
                g.DrawLine(pen, P(0.5f, 0.1f), P(0.5f, 0.9f));
                g.DrawLine(pen, P(0.1f, 0.5f), P(0.9f, 0.5f));
                Arrow(P(0.5f, 0.1f), 0, -1);
                Arrow(P(0.5f, 0.9f), 0, 1);
                Arrow(P(0.1f, 0.5f), -1, 0);
                Arrow(P(0.9f, 0.5f), 1, 0);
                break;

            case Glyph.Rotate:
                g.DrawArc(pen, R(0.15f, 0.15f, 0.7f, 0.7f), 300, 280);
                Arrow(P(0.82f, 0.32f), 0.35f, -1f);
                break;

            case Glyph.Scale:
                g.DrawRectangle(pen, x + 0.12f * s, y + 0.45f * s, 0.43f * s, 0.43f * s);
                g.DrawLine(pen, P(0.45f, 0.55f), P(0.88f, 0.12f));
                g.DrawLine(pen, P(0.62f, 0.12f), P(0.88f, 0.12f));
                g.DrawLine(pen, P(0.88f, 0.12f), P(0.88f, 0.38f));
                break;

            case Glyph.Wireframe:
                g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                g.DrawEllipse(pen, R(0.32f, 0.12f, 0.36f, 0.76f));
                g.DrawLine(pen, P(0.12f, 0.5f), P(0.88f, 0.5f));
                break;

            case Glyph.Solid:
                g.FillEllipse(brush, R(0.12f, 0.12f, 0.76f, 0.76f));
                break;

            case Glyph.MaterialPreview:
            {
                using var gradient = new LinearGradientBrush(R(0.12f, 0.12f, 0.76f, 0.76f), Color.FromArgb(255, color), Color.FromArgb(70, color), 45f);
                g.FillEllipse(gradient, R(0.12f, 0.12f, 0.76f, 0.76f));
                g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                break;
            }

            case Glyph.Rendered:
                g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                g.FillPie(brush, x + 0.12f * s, y + 0.12f * s, 0.76f * s, 0.76f * s, 90, 180);
                break;

            case Glyph.Eye:
            case Glyph.EyeClosed:
                if (glyph == Glyph.Eye)
                {
                    g.DrawBezier(pen, P(0.08f, 0.5f), P(0.3f, 0.15f), P(0.7f, 0.15f), P(0.92f, 0.5f));
                    g.DrawBezier(pen, P(0.08f, 0.5f), P(0.3f, 0.85f), P(0.7f, 0.85f), P(0.92f, 0.5f));
                    g.FillEllipse(brush, R(0.38f, 0.38f, 0.24f, 0.24f));
                }
                else
                {
                    g.DrawBezier(pen, P(0.08f, 0.45f), P(0.3f, 0.8f), P(0.7f, 0.8f), P(0.92f, 0.45f));
                    g.DrawLine(pen, P(0.3f, 0.7f), P(0.24f, 0.86f));
                    g.DrawLine(pen, P(0.5f, 0.74f), P(0.5f, 0.9f));
                    g.DrawLine(pen, P(0.7f, 0.7f), P(0.76f, 0.86f));
                }

                break;

            case Glyph.Camera:
                g.DrawRectangle(pen, x + 0.08f * s, y + 0.3f * s, 0.56f * s, 0.42f * s);
                g.DrawPolygon(pen, [P(0.64f, 0.45f), P(0.92f, 0.3f), P(0.92f, 0.72f), P(0.64f, 0.58f)]);
                break;

            case Glyph.Light:
                g.DrawEllipse(pen, R(0.28f, 0.1f, 0.44f, 0.44f));
                g.DrawLine(pen, P(0.4f, 0.62f), P(0.6f, 0.62f));
                g.DrawLine(pen, P(0.42f, 0.76f), P(0.58f, 0.76f));
                g.DrawLine(pen, P(0.46f, 0.9f), P(0.54f, 0.9f));
                break;

            case Glyph.Mesh:
                g.DrawPolygon(pen, [P(0.5f, 0.1f), P(0.88f, 0.3f), P(0.88f, 0.72f), P(0.5f, 0.92f), P(0.12f, 0.72f), P(0.12f, 0.3f)]);
                g.DrawLine(pen, P(0.12f, 0.3f), P(0.5f, 0.5f));
                g.DrawLine(pen, P(0.88f, 0.3f), P(0.5f, 0.5f));
                g.DrawLine(pen, P(0.5f, 0.5f), P(0.5f, 0.92f));
                break;

            case Glyph.Collection:
                g.DrawRectangle(pen, x + 0.12f * s, y + 0.3f * s, 0.76f * s, 0.55f * s);
                g.DrawLine(pen, P(0.12f, 0.3f), P(0.22f, 0.15f));
                g.DrawLine(pen, P(0.22f, 0.15f), P(0.45f, 0.15f));
                g.DrawLine(pen, P(0.45f, 0.15f), P(0.52f, 0.3f));
                break;

            case Glyph.Scene:
                g.DrawPolygon(pen, [P(0.1f, 0.85f), P(0.38f, 0.35f), P(0.58f, 0.68f), P(0.7f, 0.5f), P(0.9f, 0.85f)]);
                g.FillEllipse(brush, R(0.66f, 0.12f, 0.18f, 0.18f));
                break;

            case Glyph.Render:
                g.DrawRectangle(pen, x + 0.1f * s, y + 0.18f * s, 0.8f * s, 0.6f * s);
                g.DrawLine(pen, P(0.3f, 0.9f), P(0.7f, 0.9f));
                g.DrawLine(pen, P(0.5f, 0.78f), P(0.5f, 0.9f));
                break;

            case Glyph.World:
                g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                g.DrawBezier(pen, P(0.5f, 0.12f), P(0.25f, 0.35f), P(0.25f, 0.65f), P(0.5f, 0.88f));
                g.DrawBezier(pen, P(0.5f, 0.12f), P(0.75f, 0.35f), P(0.75f, 0.65f), P(0.5f, 0.88f));
                g.DrawLine(pen, P(0.14f, 0.5f), P(0.86f, 0.5f));
                break;

            case Glyph.Object:
                g.FillRectangle(brush, R(0.22f, 0.22f, 0.56f, 0.56f));
                break;

            case Glyph.Material:
                g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                g.FillEllipse(brush, R(0.3f, 0.26f, 0.2f, 0.2f));
                break;

            case Glyph.Debug:
                g.DrawEllipse(pen, R(0.3f, 0.3f, 0.4f, 0.55f));
                g.DrawLine(pen, P(0.3f, 0.5f), P(0.1f, 0.42f));
                g.DrawLine(pen, P(0.7f, 0.5f), P(0.9f, 0.42f));
                g.DrawLine(pen, P(0.3f, 0.7f), P(0.12f, 0.8f));
                g.DrawLine(pen, P(0.7f, 0.7f), P(0.88f, 0.8f));
                g.DrawLine(pen, P(0.42f, 0.3f), P(0.34f, 0.12f));
                g.DrawLine(pen, P(0.58f, 0.3f), P(0.66f, 0.12f));
                break;

            case Glyph.View:
                g.DrawEllipse(pen, R(0.12f, 0.2f, 0.76f, 0.6f));
                g.FillEllipse(brush, R(0.38f, 0.38f, 0.24f, 0.24f));
                break;

            case Glyph.Play:
                g.FillPolygon(brush, [P(0.28f, 0.15f), P(0.82f, 0.5f), P(0.28f, 0.85f)]);
                break;

            case Glyph.Pause:
                g.FillRectangle(brush, R(0.25f, 0.18f, 0.17f, 0.64f));
                g.FillRectangle(brush, R(0.58f, 0.18f, 0.17f, 0.64f));
                break;

            case Glyph.JumpStart:
                g.FillRectangle(brush, R(0.18f, 0.18f, 0.12f, 0.64f));
                g.FillPolygon(brush, [P(0.82f, 0.18f), P(0.34f, 0.5f), P(0.82f, 0.82f)]);
                break;

            case Glyph.JumpEnd:
                g.FillRectangle(brush, R(0.7f, 0.18f, 0.12f, 0.64f));
                g.FillPolygon(brush, [P(0.18f, 0.18f), P(0.66f, 0.5f), P(0.18f, 0.82f)]);
                break;

            case Glyph.Magnet:
                g.DrawArc(pen, R(0.18f, 0.3f, 0.64f, 0.6f), 0, 180);
                g.DrawLine(pen, P(0.18f, 0.6f), P(0.18f, 0.12f));
                g.DrawLine(pen, P(0.82f, 0.6f), P(0.82f, 0.12f));
                g.DrawLine(pen, P(0.08f, 0.28f), P(0.28f, 0.28f));
                g.DrawLine(pen, P(0.72f, 0.28f), P(0.92f, 0.28f));
                break;

            case Glyph.Overlays:
                g.DrawEllipse(pen, R(0.1f, 0.25f, 0.5f, 0.5f));
                g.DrawEllipse(pen, R(0.4f, 0.25f, 0.5f, 0.5f));
                break;

            case Glyph.Grid:
                for (var i = 0; i < 4; i++)
                {
                    var t = 0.15f + i * 0.233f;
                    g.DrawLine(pen, P(t, 0.15f), P(t, 0.85f));
                    g.DrawLine(pen, P(0.15f, t), P(0.85f, t));
                }

                break;

            case Glyph.Viewport3D:
                g.DrawLine(pen, P(0.2f, 0.8f), P(0.2f, 0.15f));
                g.DrawLine(pen, P(0.2f, 0.8f), P(0.88f, 0.8f));
                g.DrawLine(pen, P(0.2f, 0.8f), P(0.62f, 0.4f));
                break;

            case Glyph.Outliner:
                g.DrawLine(pen, P(0.15f, 0.2f), P(0.85f, 0.2f));
                g.DrawLine(pen, P(0.35f, 0.5f), P(0.85f, 0.5f));
                g.DrawLine(pen, P(0.35f, 0.8f), P(0.85f, 0.8f));
                g.DrawLine(pen, P(0.2f, 0.28f), P(0.2f, 0.8f));
                break;

            case Glyph.Properties:
                g.DrawEllipse(pen, R(0.25f, 0.25f, 0.5f, 0.5f));
                for (var i = 0; i < 8; i++)
                {
                    var a = i * MathF.PI / 4f;
                    g.DrawLine(pen, P(0.5f + MathF.Cos(a) * 0.27f, 0.5f + MathF.Sin(a) * 0.27f), P(0.5f + MathF.Cos(a) * 0.4f, 0.5f + MathF.Sin(a) * 0.4f));
                }

                break;

            case Glyph.Timeline:
                g.DrawLine(pen, P(0.1f, 0.7f), P(0.9f, 0.7f));
                g.DrawLine(pen, P(0.55f, 0.15f), P(0.55f, 0.85f));
                g.FillPolygon(brush, [P(0.45f, 0.15f), P(0.65f, 0.15f), P(0.55f, 0.3f)]);
                break;

            case Glyph.Mouse:
                g.DrawEllipse(pen, R(0.26f, 0.1f, 0.48f, 0.8f));
                g.DrawLine(pen, P(0.5f, 0.12f), P(0.5f, 0.4f));
                break;
        }

        g.Restore(state);

        void Arrow(PointF tip, float dx, float dy)
        {
            var length = MathF.Sqrt(dx * dx + dy * dy);
            dx /= length;
            dy /= length;

            var size = s * 0.14f;
            var back = new PointF(tip.X - dx * size, tip.Y - dy * size);

            g.FillPolygon(brush,
            [
                tip,
                new PointF(back.X - dy * size * 0.8f, back.Y + dx * size * 0.8f),
                new PointF(back.X + dy * size * 0.8f, back.Y - dx * size * 0.8f),
            ]);
        }
    }
}
