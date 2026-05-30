namespace GameTools.UI;

public static class Theme
{
    public static readonly Color BG = Color.FromArgb(30, 30, 46);
    public static readonly Color BG2 = Color.FromArgb(49, 50, 68);
    public static readonly Color FG = Color.FromArgb(205, 214, 244);
    public static readonly Color Accent = Color.FromArgb(137, 180, 250);
    public static readonly Color Dim = Color.FromArgb(108, 112, 134);
    public static readonly Color Green = Color.FromArgb(166, 227, 161);
    public static readonly Color Yellow = Color.FromArgb(249, 226, 175);
    public static readonly Color Orange = Color.FromArgb(250, 179, 135);

    public static readonly Font Normal = new("Segoe UI", 9);
    public static readonly Font Small = new("Segoe UI", 8);
    public static readonly Font Bold = new("Segoe UI", 10, FontStyle.Bold);

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

    public static Button MakeAccentButton(string text, int width, int height = 38) => new()
    {
        Text = text,
        Size = new Size(width, height),
        FlatStyle = FlatStyle.Flat,
        BackColor = Accent,
        ForeColor = BG,
        Font = Bold,
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

    public static ListView MakeListView(int width, int height) => new()
    {
        Size = new Size(width, height),
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        BackColor = BG2,
        ForeColor = FG,
        BorderStyle = BorderStyle.None,
        Font = Normal
    };
}
