using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using GameTools.Core;
using GameTools.Data;

namespace GameTools.UI;

// A float that eases toward a target. All live animations share one UI-thread timer,
// which stops whenever nothing is moving, so idle cost is zero.
public sealed class Anim
{
    static readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    static readonly List<Anim> active = [];
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static double lastTick;

    static Anim()
    {
        timer.Tick += (_, _) =>
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double dt = Math.Min(now - lastTick, 50);
            lastTick = now;
            for (int i = active.Count - 1; i >= 0; i--)
                if (!active[i].Step(dt)) active.RemoveAt(i);
            if (active.Count == 0) timer.Stop();
        };
    }

    readonly Action onChange;
    readonly double tau; // time constant of the exponential ease-out
    public float Value { get; private set; }
    public float Target { get; private set; }

    public Anim(float initial, Action onChange, double durationMs = 160)
    {
        Value = Target = initial;
        this.onChange = onChange;
        tau = durationMs / 4.5;
    }

    public void To(float target)
    {
        Target = target;
        if (Math.Abs(Value - target) < 0.0005f) return;
        if (!active.Contains(this)) active.Add(this);
        if (!timer.Enabled) { lastTick = clock.Elapsed.TotalMilliseconds; timer.Start(); }
    }

    public void Snap(float value)
    {
        Value = Target = value;
        active.Remove(this);
        onChange();
    }

    bool Step(double dt)
    {
        float diff = Target - Value;
        bool done = Math.Abs(diff) < 0.0015f * Math.Max(1f, Math.Abs(Target));
        Value = done ? Target : Value + diff * (float)(1 - Math.Exp(-dt / tau));
        onChange();
        return !done;
    }
}

// Panel with rounded corners. BackColor is the fill; the corners show the parent's color,
// so child controls inherit the fill color as their ambient background.
public class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(e.Graphics, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(Radius));
    }
}

public enum ButtonKind { Normal, Accent, Ghost }

public class ModernButton : Button
{
    readonly Anim hover, press;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; set; } = ButtonKind.Normal;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? GlyphColor { get; set; }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.BodySemibold;
        Height = 36;
        hover = new Anim(0, Invalidate, 140);
        press = new Anim(0, Invalidate, 90);
    }

    protected override bool ShowFocusCues => false;
    protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0); press.To(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { press.To(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press.To(0); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parentBg = Parent?.BackColor ?? Theme.BG;
        g.Clear(parentBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color rest, over, down, fg;
        switch (Kind)
        {
            case ButtonKind.Accent: rest = Theme.Accent; over = Theme.AccentHover; down = Theme.AccentPressed; fg = Theme.Crust; break;
            case ButtonKind.Ghost: rest = parentBg; over = Theme.BG2; down = Theme.Surface1; fg = Theme.FG; break;
            default: rest = Theme.BG2; over = Theme.Surface1; down = Theme.Surface2; fg = Theme.FG; break;
        }
        var bg = Theme.Blend(Theme.Blend(rest, over, hover.Value), down, press.Value);
        if (!Enabled) { fg = Theme.Dim; if (Kind == ButtonKind.Accent) bg = Theme.BG2; }

        // A slight shrink while pressed gives the click some physical feedback.
        float inset = press.Value * LogicalToDeviceUnits(3) / 2f;
        var rect = new RectangleF(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
        if (bg != parentBg) Theme.FillRound(g, bg, rect, LogicalToDeviceUnits(7));

        var iconFont = Theme.Icon(Font.SizeInPoints * 1.05f);
        Size glyphSize = Glyph != null ? TextRenderer.MeasureText(g, Glyph, iconFont, Size.Empty, TextFormatFlags.NoPadding) : Size.Empty;
        Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int gap = Glyph != null && textSize.Width > 0 ? LogicalToDeviceUnits(8) : 0;
        int x = (Width - glyphSize.Width - gap - textSize.Width) / 2;

        if (Glyph != null)
        {
            var glyphColor = Enabled ? GlyphColor ?? fg : Theme.Dim;
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(x, 0, glyphSize.Width, Height), glyphColor, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            x += glyphSize.Width + gap;
        }
        if (textSize.Width > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textSize.Width + 2, Height), fg, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

// A full-width row: label on the left, pill switch on the right. Still a CheckBox underneath,
// so Checked / CheckedChanged / keyboard toggling work as usual.
public class ToggleSwitch : CheckBox
{
    readonly Anim knob, hover;

    public ToggleSwitch(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        AutoSize = false;
        Height = 38;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        ForeColor = Theme.FG;
        knob = new Anim(0, Invalidate, 200);
        hover = new Anim(0, Invalidate, 140);
    }

    protected override bool ShowFocusCues => false;
    protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnCheckedChanged(EventArgs e)
    {
        // Animate only when visible; a hidden control just snaps so it doesn't slide in later.
        if (Visible && IsHandleCreated) knob.To(Checked ? 1 : 0); else knob.Snap(Checked ? 1 : 0);
        base.OnCheckedChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = Parent?.BackColor ?? Theme.BG;
        g.Clear(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int tw = LogicalToDeviceUnits(40), th = LogicalToDeviceUnits(22);
        var track = new RectangleF(Width - tw - 1, (Height - th) / 2f, tw, th);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, (int)track.Left - LogicalToDeviceUnits(8), Height),
            Enabled ? Theme.Blend(ForeColor, Color.White, hover.Value * 0.25f) : Theme.Dim, bg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        float t = knob.Value;
        var off = Theme.Blend(Theme.Surface1, Theme.Surface2, hover.Value);
        var on = Theme.Blend(Theme.Accent, Theme.AccentHover, hover.Value);
        Theme.FillRound(g, Enabled ? Theme.Blend(off, on, t) : Theme.BG2, track, th / 2f);

        // The knob stretches a little mid-travel, which reads as motion rather than a jump.
        float pad = LogicalToDeviceUnits(4), d = th - pad * 2;
        float stretch = (float)Math.Sin(t * Math.PI) * LogicalToDeviceUnits(5);
        float kx = track.Left + pad + (track.Width - pad * 2 - d) * t - stretch / 2;
        Theme.FillRound(g, Theme.Blend(Theme.Subtext, Theme.Crust, t), new RectangleF(kx, track.Top + pad, d + stretch, d), d / 2);
    }
}

// Rounded text input with an optional leading icon glyph and an animated focus ring.
public class InputBox : RoundedPanel
{
    public readonly TextBox Box;
    readonly string? glyph;
    readonly Anim focus;

    public InputBox(string? glyph = null, string placeholder = "")
    {
        this.glyph = glyph;
        focus = new Anim(0, Invalidate, 160);
        Radius = 8;
        BackColor = Theme.BG2;
        Height = 36;
        Cursor = Cursors.IBeam;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.BG2,
            ForeColor = Theme.FG,
            Font = Theme.Body,
            PlaceholderText = placeholder
        };
        Controls.Add(Box);
        Box.GotFocus += (_, _) => focus.To(1);
        Box.LostFocus += (_, _) => focus.To(0);
        MouseDown += (_, _) => Box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Box == null) return; // layout can run from the base constructor, before Box exists
        int left = LogicalToDeviceUnits(glyph != null ? 36 : 10), right = LogicalToDeviceUnits(10);
        Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - right), Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float f = focus?.Value ?? 0;
        if (f > 0.01f)
        {
            using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), LogicalToDeviceUnits(Radius));
            using var pen = new Pen(Color.FromArgb((int)(255 * f), Theme.Accent), LogicalToDeviceUnits(1));
            g.DrawPath(pen, path);
        }
        if (glyph != null)
            TextRenderer.DrawText(g, glyph, Theme.Icon(10), new Rectangle(LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(20), Height),
                Theme.Blend(Theme.Dim, Theme.Accent, f), BackColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

public class ProfileRow(string exe, GameProfile profile)
{
    public string Exe { get; } = exe;
    public GameProfile Profile { get; } = profile;
    public bool Running { get; set; }
}

// Custom-drawn game list with pixel-smooth animated scrolling, an auto-hiding overlay scrollbar,
// a selection highlight that glides between rows, per-row hover fades and a fade-in on refresh.
public class ProfileList : Control
{
    public event EventHandler? SelectedIndexChanged, ItemActivated;

    readonly List<ProfileRow> items = [];
    readonly List<Anim> rowHover = [];
    readonly Anim scroll, selY, appear, barAlpha, barHover;
    readonly System.Windows.Forms.Timer barHideTimer = new() { Interval = 900 };
    int selected = -1, hoverRow = -1;
    float scrollTarget;
    bool overBar, draggingThumb;
    float dragStartY, dragStartScroll;

    public ProfileList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Mantle;
        Font = Theme.Body;
        TabStop = true;
        scroll = new Anim(0, Invalidate, 220);
        selY = new Anim(0, Invalidate, 180);
        appear = new Anim(1, Invalidate, 260);
        barAlpha = new Anim(0, Invalidate, 220);
        barHover = new Anim(0, Invalidate, 140);
        barHideTimer.Tick += (_, _) => { barHideTimer.Stop(); if (!overBar && !draggingThumb) barAlpha.To(0); };
    }

    public IReadOnlyList<ProfileRow> Items => items;
    public ProfileRow? SelectedItem => selected >= 0 && selected < items.Count ? items[selected] : null;

    int RowH => LogicalToDeviceUnits(42);
    float MaxScroll => Math.Max(0, items.Count * RowH - Height);

    // Replaces the rows without raising SelectedIndexChanged. Fades the rows in when the set changes.
    public void SetItems(IReadOnlyList<ProfileRow> rows, int selectedIndex)
    {
        bool sameSet = rows.Count == items.Count && rows.Select(r => r.Exe).SequenceEqual(items.Select(r => r.Exe));
        items.Clear();
        items.AddRange(rows);
        while (rowHover.Count < items.Count) rowHover.Add(new Anim(0, Invalidate, 140));
        foreach (var a in rowHover) a.Snap(0);
        hoverRow = -1;
        int prev = selected;
        selected = selectedIndex < items.Count ? selectedIndex : -1;
        if (selected >= 0)
        {
            // Same rows, new order (e.g. the applied game jumped to the top): glide there.
            if (sameSet || prev < 0) selY.Snap(selected * RowH); else selY.To(selected * RowH);
        }
        scrollTarget = Math.Clamp(scrollTarget, 0, MaxScroll);
        scroll.Snap(scrollTarget);
        if (!sameSet) { appear.Snap(0); appear.To(1); }
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            value = items.Count == 0 ? -1 : Math.Clamp(value, -1, items.Count - 1);
            if (value == selected) return;
            if (selected < 0 && value >= 0) selY.Snap(value * RowH); else selY.To(value * RowH);
            selected = value;
            if (selected >= 0) EnsureVisible(selected);
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void EnsureVisible(int index)
    {
        float top = index * RowH, bottom = top + RowH;
        if (top < scrollTarget) ScrollTo(top);
        else if (bottom > scrollTarget + Height) ScrollTo(bottom - Height);
    }

    void ScrollTo(float y)
    {
        scrollTarget = Math.Clamp(y, 0, MaxScroll);
        scroll.To(scrollTarget);
        ShowBar();
    }

    void ShowBar()
    {
        if (MaxScroll <= 0) return;
        barAlpha.To(1);
        barHideTimer.Stop();
        barHideTimer.Start();
    }

    int RowAt(Point p)
    {
        int i = (int)((p.Y + scroll.Value) / RowH);
        return p.Y >= 0 && i >= 0 && i < items.Count ? i : -1;
    }

    RectangleF ThumbRect()
    {
        float contentH = items.Count * RowH;
        float h = Math.Max(LogicalToDeviceUnits(32), Height * Height / Math.Max(contentH, 1));
        float y = MaxScroll <= 0 ? 0 : scroll.Value / MaxScroll * (Height - h);
        float w = LogicalToDeviceUnits(4) + barHover.Value * LogicalToDeviceUnits(4);
        return new RectangleF(Width - w - LogicalToDeviceUnits(3), y, w, h);
    }

    bool InBarZone(Point p) => MaxScroll > 0 && p.X >= Width - LogicalToDeviceUnits(14);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        scrollTarget = Math.Clamp(scrollTarget, 0, MaxScroll);
        scroll.Snap(scrollTarget);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(scrollTarget - e.Delta / 120f * RowH * 2.5f);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (draggingThumb)
        {
            var thumb = ThumbRect();
            float track = Height - thumb.Height;
            if (track > 0)
            {
                scrollTarget = Math.Clamp(dragStartScroll + (e.Y - dragStartY) / track * MaxScroll, 0, MaxScroll);
                scroll.Snap(scrollTarget);
            }
            return;
        }

        bool bar = InBarZone(e.Location);
        if (bar != overBar)
        {
            overBar = bar;
            barHover.To(bar ? 1 : 0);
            if (bar) { barAlpha.To(1); barHideTimer.Stop(); } else ShowBar();
        }
        int row = bar ? -1 : RowAt(e.Location);
        if (row != hoverRow)
        {
            if (hoverRow >= 0 && hoverRow < rowHover.Count) rowHover[hoverRow].To(0);
            hoverRow = row;
            if (row >= 0) rowHover[row].To(1);
        }
        Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverRow >= 0 && hoverRow < rowHover.Count) rowHover[hoverRow].To(0);
        hoverRow = -1;
        if (overBar) { overBar = false; barHover.To(0); ShowBar(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        if (InBarZone(e.Location))
        {
            var thumb = ThumbRect();
            if (e.Y >= thumb.Top && e.Y <= thumb.Bottom)
            {
                draggingThumb = true;
                dragStartY = e.Y;
                dragStartScroll = scroll.Value;
                Capture = true;
            }
            else ScrollTo(scrollTarget + (e.Y < thumb.Top ? -Height : Height) * 0.9f);
            return;
        }
        int row = RowAt(e.Location);
        if (row >= 0) SelectedIndex = row;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!draggingThumb) return;
        draggingThumb = false;
        Capture = false;
        ShowBar();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && !InBarZone(e.Location) && RowAt(e.Location) >= 0)
            ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (items.Count == 0) return;
        int page = Math.Max(1, Height / RowH - 1);
        int target = e.KeyCode switch
        {
            Keys.Up => Math.Max(0, selected - 1),
            Keys.Down => selected + 1,
            Keys.Home => 0,
            Keys.End => items.Count - 1,
            Keys.PageUp => Math.Max(0, selected - page),
            Keys.PageDown => selected + page,
            _ => int.MinValue
        };
        if (target == int.MinValue) return;
        SelectedIndex = Math.Min(target, items.Count - 1);
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int rowH = RowH;
        float sc = scroll.Value;
        float a = appear.Value;
        float rise = (1 - a) * LogicalToDeviceUnits(10);

        int first = Math.Max(0, (int)(sc / rowH));
        int last = Math.Min(items.Count - 1, (int)((sc + Height) / rowH));
        float inset = LogicalToDeviceUnits(2), radius = LogicalToDeviceUnits(8);

        for (int i = first; i <= last; i++)
        {
            float h = i < rowHover.Count ? rowHover[i].Value : 0;
            if (h > 0.01f && i != selected)
                Theme.FillRound(g, Theme.Blend(BackColor, Theme.Hover, h), new RectangleF(inset, i * rowH - sc + inset + rise, Width - inset * 2, rowH - inset * 2), radius);
        }

        // Selection highlight glides between rows.
        if (selected >= 0)
        {
            float y = selY.Value - sc + rise;
            var r = new RectangleF(inset, y + inset, Width - inset * 2, rowH - inset * 2);
            Theme.FillRound(g, Theme.Blend(BackColor, Theme.BG2, a), r, radius);
            Theme.FillRound(g, Theme.Blend(BackColor, Theme.Accent, a),
                new RectangleF(r.Left, r.Top + r.Height * 0.28f, LogicalToDeviceUnits(3), r.Height * 0.44f), LogicalToDeviceUnits(2));
        }

        for (int i = first; i <= last; i++)
            DrawRow(g, items[i], i, new Rectangle((int)inset, (int)(i * rowH - sc + inset + rise), (int)(Width - inset * 2), (int)(rowH - inset * 2)), a);

        if (MaxScroll > 0 && barAlpha.Value > 0.01f)
        {
            var thumb = ThumbRect();
            var color = Theme.Blend(Theme.Surface1, Theme.Surface2, barHover.Value);
            Theme.FillRound(g, Color.FromArgb((int)(255 * barAlpha.Value), color), thumb, thumb.Width / 2);
        }
    }

    void DrawRow(Graphics g, ProfileRow row, int index, Rectangle r, float a)
    {
        // Text is drawn transparently: while the highlight glides, the row behind it is mid-blend.
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        bool sel = index == selected;

        int x = r.Left + LogicalToDeviceUnits(12);
        int starW = LogicalToDeviceUnits(18);
        if (row.Profile.Favorite)
            TextRenderer.DrawText(g, Theme.GlyphStarFill, Theme.Icon(9), new Rectangle(x, r.Top, starW, r.Height), Theme.Blend(BackColor, Theme.Yellow, a), flags);
        x += starW + LogicalToDeviceUnits(4);

        string right = row.Running ? "● Running" : DisplayText.RelativeTime(row.Profile.LastUsed);
        var rightColor = Theme.Blend(BackColor, row.Running ? Theme.Green : Theme.Dim, a);
        var rightSize = TextRenderer.MeasureText(g, right, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding);
        int rightX = r.Right - LogicalToDeviceUnits(14) - rightSize.Width;
        TextRenderer.DrawText(g, right, Theme.Caption, new Rectangle(rightX, r.Top, rightSize.Width + 2, r.Height), rightColor, flags);

        TextRenderer.DrawText(g, DisplayText.DisplayName(row.Exe), sel ? Theme.BodySemibold : Theme.Body,
            new Rectangle(x, r.Top, Math.Max(0, rightX - x - LogicalToDeviceUnits(8)), r.Height), Theme.Blend(BackColor, Theme.FG, a),
            flags | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) barHideTimer.Dispose();
        base.Dispose(disposing);
    }
}

public static class DisplayText
{
    // "Bodycam-Win64-Shipping.exe" -> "Bodycam"
    public static string DisplayName(string exe)
    {
        string name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
        foreach (var suffix in new[] { "-Win64-Shipping", "-WinGDK-Shipping" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return name;
    }

    public static string RelativeTime(long unixSeconds)
    {
        if (unixSeconds <= 0) return "—";
        var ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalDays < 1) return $"{(int)ago.TotalHours}h ago";
        if (ago.TotalDays < 2) return "yesterday";
        if (ago.TotalDays < 7) return $"{(int)ago.TotalDays}d ago";
        if (ago.TotalDays < 30) return $"{(int)(ago.TotalDays / 7)}w ago";
        if (ago.TotalDays < 365) return $"{(int)(ago.TotalDays / 30)}mo ago";
        return $"{(int)(ago.TotalDays / 365)}y ago";
    }
}

public static class Stacking
{
    // Docks the given controls top-to-bottom in the order given (WinForms docks the last-added control first).
    public static void Stack(Control parent, params Control[] topDown)
    {
        for (int i = topDown.Length - 1; i >= 0; i--)
        {
            topDown[i].Dock = DockStyle.Top;
            parent.Controls.Add(topDown[i]);
        }
    }

    public static Panel Spacer(int height) => new() { Height = height, Dock = DockStyle.Top };

    public static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = Theme.Section,
        ForeColor = Theme.Dim,
        Height = 30,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(0, 0, 0, 4)
    };
}
