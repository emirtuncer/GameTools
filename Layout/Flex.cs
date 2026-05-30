namespace GameTools.Layout;

public static class Flex
{
    public static void Apply(Control container, int gap = 0, Padding? padding = null, params Control[] children)
    {
        var pad = padding ?? Padding.Empty;
        container.SuspendLayout();

        int x = pad.Left;
        int y = pad.Top;
        int maxW = 0;

        foreach (var child in children)
        {
            child.Location = new Point(x, y);
            container.Controls.Add(child);

            if (child.Width > maxW) maxW = child.Width;
            y += child.Height + gap;
        }

        y -= gap;
        y += pad.Bottom;

        int contentW = maxW;
        foreach (var child in children)
        {
            if (child is not CheckBox and not RadioButton)
                child.Width = contentW;
        }

        container.ClientSize = new Size(pad.Left + contentW + pad.Right, y);
        container.ResumeLayout(true);
    }

    public static Panel Column(int gap = 0, params Control[] children)
        => Column(gap, stretch: true, padding: null, children);

    public static Panel Column(int gap, bool stretch, Padding? padding, params Control[] children)
    {
        var pad = padding ?? Padding.Empty;
        var panel = new Panel { AutoSize = false };

        int x = pad.Left;
        int y = pad.Top;
        int maxW = 0;

        foreach (var child in children)
        {
            child.Location = new Point(x, y);
            panel.Controls.Add(child);

            if (child.Width > maxW) maxW = child.Width;
            y += child.Height + gap;
        }

        y -= gap;
        y += pad.Bottom;

        if (stretch)
        {
            foreach (var child in children)
            {
                if (child is not CheckBox and not RadioButton)
                    child.Width = maxW;
            }
        }

        panel.Size = new Size(pad.Left + maxW + pad.Right, y);
        return panel;
    }

    public static Panel Row(int gap = 0, params Control[] children)
        => Row(gap, stretch: true, padding: null, children);

    public static Panel Row(int gap, bool stretch, Padding? padding, params Control[] children)
    {
        var pad = padding ?? Padding.Empty;
        var panel = new Panel { AutoSize = false };

        int x = pad.Left;
        int y = pad.Top;
        int maxH = 0;

        foreach (var child in children)
        {
            child.Location = new Point(x, y);
            panel.Controls.Add(child);

            if (child.Height > maxH) maxH = child.Height;
            x += child.Width + gap;
        }

        x -= gap;
        x += pad.Right;

        if (stretch)
        {
            foreach (var child in children)
            {
                if (child.Height < maxH)
                    child.Top = pad.Top + (maxH - child.Height) / 2;
            }
        }

        panel.Size = new Size(x, pad.Top + maxH + pad.Bottom);
        return panel;
    }

    public static GroupBox Group(string title, Font titleFont, Color titleColor, int gap, int innerWidth, params Control[] children)
    {
        const int topOffset = 22;
        const int sideOffset = 10;

        var group = new GroupBox
        {
            Text = title,
            Font = titleFont,
            ForeColor = titleColor,
        };

        int y = topOffset;
        int maxW = 0;

        foreach (var child in children)
        {
            child.Location = new Point(sideOffset, y);
            group.Controls.Add(child);

            if (child.Width > maxW) maxW = child.Width;
            y += child.Height + gap;
        }

        y -= gap;
        y += 10;

        int availW = innerWidth - sideOffset * 2;
        foreach (var child in children)
        {
            if (child is not CheckBox and not RadioButton)
                child.Width = availW;
        }

        group.Size = new Size(innerWidth, y);
        return group;
    }

    public static T Sized<T>(T control, int width, int height) where T : Control
    {
        control.Size = new Size(width, height);
        return control;
    }

    public static T Width<T>(T control, int width) where T : Control
    {
        control.Width = width;
        return control;
    }
}
