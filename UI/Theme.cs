using System.Drawing.Drawing2D;
using GameTools.Core;

namespace GameTools.UI;

public static class Theme
{
    // Catppuccin Mocha
    public static readonly Color Crust = Color.FromArgb(17, 17, 27);
    public static readonly Color Mantle = Color.FromArgb(24, 24, 37);
    public static readonly Color BG = Color.FromArgb(30, 30, 46);          // Base
    public static readonly Color BG2 = Color.FromArgb(49, 50, 68);         // Surface0
    public static readonly Color Surface1 = Color.FromArgb(69, 71, 90);
    public static readonly Color Surface2 = Color.FromArgb(88, 91, 112);
    public static readonly Color Hover = Color.FromArgb(37, 37, 54);
    public static readonly Color FG = Color.FromArgb(205, 214, 244);
    public static readonly Color Subtext = Color.FromArgb(166, 173, 200);
    public static readonly Color Accent = Color.FromArgb(137, 180, 250);
    public static readonly Color AccentHover = Color.FromArgb(160, 196, 252);
    public static readonly Color AccentPressed = Color.FromArgb(116, 160, 230);
    public static readonly Color Dim = Color.FromArgb(108, 112, 134);
    public static readonly Color Green = Color.FromArgb(166, 227, 161);
    public static readonly Color Yellow = Color.FromArgb(249, 226, 175);
    public static readonly Color Orange = Color.FromArgb(250, 179, 135);
    public static readonly Color Red = Color.FromArgb(243, 139, 168);

    public static readonly Font Normal = new("Segoe UI", 9);
    public static readonly Font Small = new("Segoe UI", 8);
    public static readonly Font Bold = new("Segoe UI", 10, FontStyle.Bold);

    public static readonly Font Body = new("Segoe UI", 10);
    public static readonly Font BodySemibold = new("Segoe UI Semibold", 10);
    public static readonly Font Caption = new("Segoe UI", 8.5f);
    public static readonly Font Section = new("Segoe UI Semibold", 8f);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);

    // Segoe Fluent Icons ships with Windows 11; Segoe MDL2 Assets (Windows 10) has the same code points.
    static readonly string IconFamily =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    static readonly Dictionary<float, Font> iconFonts = [];
    public static Font Icon(float size)
    {
        if (!iconFonts.TryGetValue(size, out var f)) iconFonts[size] = f = new Font(IconFamily, size);
        return f;
    }

    public const string GlyphSettings = "", GlyphDelete = "", GlyphStar = "",
        GlyphStarFill = "", GlyphSearch = "", GlyphAdd = "", GlyphPlay = "",
        GlyphUnlock = "";

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    // Dark title bar, matching caption color and rounded corners (Windows 11; silently ignored elsewhere).
    public static void ApplyWindowChrome(Form form)
    {
        try
        {
            int on = 1;
            Win32.DwmSetWindowAttribute(form.Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            int round = 2;
            Win32.DwmSetWindowAttribute(form.Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int caption = form.BackColor.R | form.BackColor.G << 8 | form.BackColor.B << 16;
            Win32.DwmSetWindowAttribute(form.Handle, Win32.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        }
        catch (Exception) { }
    }

    public static Label MakeLabel(string text, Color? color = null, float size = 9, bool bold = false)
    {
        Font f = (size == 9 && !bold) ? Normal : (size == 10 && bold) ? Bold : new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color ?? FG,
            BackColor = Color.Transparent,
            Font = f
        };
    }

    public static CheckBox MakeCheck(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = FG,
        Font = Normal
    };

    public static TextBox MakeTextBox(string text, int width = 55) => new()
    {
        Text = text,
        Size = new Size(width, 24),
        BackColor = BG2,
        ForeColor = FG,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Normal
    };

    public static Button MakeButton(string text, int width = 82, int height = 30) => new()
    {
        Text = text,
        Size = new Size(width, height),
        FlatStyle = FlatStyle.Flat,
        BackColor = BG2,
        ForeColor = FG,
        Font = Small,
        FlatAppearance = { BorderSize = 0 }
    };

    public static TrackBar MakeTrackBar(int min, int max, int value, int width = 200) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        TickFrequency = (max - min) / 10,
        Size = new Size(width, 30),
        BackColor = BG,
    };
}
