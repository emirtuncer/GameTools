using GameTools.Core;
using GameTools.Data;

namespace GameTools.UI;

// Toggles + resolution for one GameProfile. Used by the detail view and by Settings ("defaults").
public class OptionsEditor : Panel
{
    public event Action? Changed;
    readonly ToggleSwitch customRes, center, border, clip, blackBg, mute;
    readonly InputBox resW, resH;
    readonly Panel resRow;
    bool loading;

    public OptionsEditor()
    {
        customRes = new ToggleSwitch("Custom resolution");
        center = new ToggleSwitch("Center on monitor");
        border = new ToggleSwitch("Remove window border");
        clip = new ToggleSwitch("Lock cursor to window");
        blackBg = new ToggleSwitch("Black background behind game");
        mute = new ToggleSwitch("Mute when in background");

        resW = new InputBox { Width = 84, Location = new Point(0, 4) };
        var lblX = new Label { Text = "×", ForeColor = Theme.Dim, Font = Theme.Body, AutoSize = true, Location = new Point(92, 12) };
        resH = new InputBox { Width = 84, Location = new Point(112, 4) };
        resW.Box.TextAlign = resH.Box.TextAlign = HorizontalAlignment.Center;
        resRow = new Panel { Height = 46 };
        resRow.Controls.AddRange([resW, lblX, resH]);

        var rows = new Control[]
        {
            Stacking.Section("Window"), customRes, resRow, center, border,
            Stacking.Section("While playing"), clip, blackBg, mute
        };
        Stacking.Stack(this, rows);
        Height = rows.Sum(r => r.Height);

        foreach (var t in new[] { customRes, center, border, clip, blackBg, mute })
            t.CheckedChanged += (_, _) => OnChanged();
        resW.Box.TextChanged += (_, _) => OnChanged();
        resH.Box.TextChanged += (_, _) => OnChanged();
    }

    void OnChanged()
    {
        resW.Enabled = resH.Enabled = customRes.Checked;
        if (!loading) Changed?.Invoke();
    }

    public void Load(GameProfile p)
    {
        loading = true;
        customRes.Checked = p.CustomRes; center.Checked = p.Center; border.Checked = p.RemoveBorder;
        clip.Checked = p.Clip; blackBg.Checked = p.BlackBg; mute.Checked = p.MuteBg;
        resW.Box.Text = p.ResW.ToString(); resH.Box.Text = p.ResH.ToString();
        loading = false;
        resW.Enabled = resH.Enabled = customRes.Checked;
    }

    // Copies the editor state into p. An invalid resolution field keeps the profile's previous value.
    public void ReadInto(GameProfile p)
    {
        p.CustomRes = customRes.Checked; p.Center = center.Checked; p.RemoveBorder = border.Checked;
        p.Clip = clip.Checked; p.BlackBg = blackBg.Checked; p.MuteBg = mute.Checked;
        if (int.TryParse(resW.Box.Text, out int w) && w > 0 && w <= 15360) p.ResW = w;
        if (int.TryParse(resH.Box.Text, out int h) && h > 0 && h <= 8640) p.ResH = h;
    }
}

// Right-hand card: header with name/state/actions, the options editor, and Apply / Release.
public class ProfileDetailView : RoundedPanel
{
    public event Action? Changed, ApplyClicked, ReleaseClicked, FavoriteClicked, DeleteClicked;
    public readonly OptionsEditor Options = new();

    readonly Label lblName, lblExe, lblState, lblEmpty;
    readonly ModernButton btnFav;
    readonly Panel content, scroll;
    readonly Anim slide;
    string? exe;
    long lastUsed;

    // Composite the whole card in one pass so the slide-in doesn't flicker child by child.
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; } // WS_EX_COMPOSITED
    }

    public ProfileDetailView()
    {
        BackColor = Theme.BG;
        Radius = 12;
        Padding = new Padding(24, 18, 24, 20);

        lblName = new Label { Font = Theme.Title, ForeColor = Theme.FG, Height = 38, AutoEllipsis = true, Dock = DockStyle.Top };
        lblExe = new Label { Font = Theme.Caption, ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 0, 10, 0) };
        lblState = new Label { Font = Theme.Caption, AutoSize = true, Margin = Padding.Empty };
        var subRow = new FlowLayoutPanel { Height = 22, Dock = DockStyle.Top, WrapContents = false };
        subRow.Controls.AddRange([lblExe, lblState]);

        btnFav = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Theme.GlyphStar, Size = new Size(38, 38) };
        var btnDelete = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Theme.GlyphDelete, GlyphColor = Theme.Red, Size = new Size(38, 38) };
        var tips = new ToolTip();
        tips.SetToolTip(btnFav, "Favorite — auto-apply when the game starts");
        tips.SetToolTip(btnDelete, "Delete profile");
        btnFav.Click += (_, _) => FavoriteClicked?.Invoke();
        btnDelete.Click += (_, _) => DeleteClicked?.Invoke();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([btnFav, btnDelete]);

        var titleBox = new Panel { Dock = DockStyle.Fill };
        Stacking.Stack(titleBox, lblName, subRow);
        var header = new Panel { Height = 64, Dock = DockStyle.Top };
        header.Controls.Add(titleBox);
        header.Controls.Add(actions);

        var btnApply = new ModernButton { Kind = ButtonKind.Accent, Text = "Apply", Glyph = Theme.GlyphPlay, Width = 150, Dock = DockStyle.Left };
        var btnRelease = new ModernButton { Text = "Release", Glyph = Theme.GlyphUnlock, Width = 130, Dock = DockStyle.Left };
        tips.SetToolTip(btnApply, "Apply these settings to the running game");
        tips.SetToolTip(btnRelease, "Restore the game window and free the cursor");
        btnApply.Click += (_, _) => ApplyClicked?.Invoke();
        btnRelease.Click += (_, _) => ReleaseClicked?.Invoke();
        var footer = new Panel { Height = 40, Dock = DockStyle.Bottom };
        footer.Controls.Add(btnRelease);
        footer.Controls.Add(new Panel { Width = 10, Dock = DockStyle.Left });
        footer.Controls.Add(btnApply);

        scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.HandleCreated += (_, _) => Win32.SetWindowTheme(scroll.Handle, "DarkMode_Explorer", null); // dark scrollbar
        Stacking.Stack(scroll, Options);
        Options.Changed += () => Changed?.Invoke();

        content = new Panel { Dock = DockStyle.Fill, Visible = false };
        content.Controls.Add(scroll);
        content.Controls.Add(new Panel { Height = 14, Dock = DockStyle.Bottom });
        content.Controls.Add(footer);
        content.Controls.Add(header);

        lblEmpty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Dim,
            Font = Theme.Body,
            Text = "Select a game from the list\nor capture a window to get started."
        };
        Controls.Add(content);
        Controls.Add(lblEmpty);
        slide = new Anim(0, () => content.Padding = new Padding(0, (int)slide.Value, 0, 0), 240);
    }

    // Extra per-profile sections (e.g. the optional gamepad plugin) go below the options.
    public void AddSection(Control c)
    {
        scroll.Controls.Add(c);
        c.Dock = DockStyle.Top;
        c.BringToFront();
    }

    public void ShowProfile(string exe, GameProfile p, bool running)
    {
        // Switching to a different game slides the card content up into place.
        if (!string.Equals(this.exe, exe, StringComparison.OrdinalIgnoreCase))
        {
            slide.Snap(LogicalToDeviceUnits(18));
            slide.To(0);
        }
        this.exe = exe;
        lastUsed = p.LastUsed;
        lblName.Text = DisplayText.DisplayName(exe);
        Options.Load(p);
        SetFavorite(p.Favorite);
        SetRunning(running);
        lblEmpty.Visible = false;
        content.Visible = true;
    }

    public void ShowEmpty()
    {
        exe = null;
        content.Visible = false;
        lblEmpty.Visible = true;
    }

    public void SetFavorite(bool fav)
    {
        btnFav.Glyph = fav ? Theme.GlyphStarFill : Theme.GlyphStar;
        btnFav.GlyphColor = fav ? Theme.Yellow : null;
        btnFav.Invalidate();
    }

    public void SetRunning(bool running)
    {
        if (exe == null) return;
        string used = lastUsed > 0 ? $"  ·  used {DisplayText.RelativeTime(lastUsed)}" : "";
        lblExe.Text = exe + used;
        lblState.Text = running ? "● Running" : "○ Not running";
        lblState.ForeColor = running ? Theme.Green : Theme.Dim;
    }
}

public class SettingsForm : Form
{
    readonly ToggleSwitch tglStartup, tglTray;
    readonly Label lblKey;
    public bool StartWithWindows => tglStartup.Checked;
    public bool TrayMode => tglTray.Checked;

    // `defaults` is edited in place. `changeHotkey` shows the key-capture dialog and returns the new label.
    public SettingsForm(string hotkeyLabel, bool startup, bool tray, GameProfile defaults, Func<IWin32Window, string> changeHotkey)
    {
        Text = "Settings";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.BG;
        ForeColor = Theme.FG;
        Font = Theme.Body;
        Padding = new Padding(24, 8, 24, 20);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        lblKey = new Label { Text = hotkeyLabel, Font = Theme.Heading, ForeColor = Theme.Yellow, AutoSize = true, Margin = new Padding(0, 6, 14, 0) };
        var btnChange = new ModernButton { Text = "Change", Width = 96 };
        btnChange.Click += (_, _) => lblKey.Text = changeHotkey(this);
        var hkRow = new FlowLayoutPanel { Height = 44, WrapContents = false };
        hkRow.Controls.AddRange([lblKey, btnChange]);
        var hkHint = new Label { Text = "Press anywhere to start a 5-second capture, then apply.", ForeColor = Theme.Dim, Font = Theme.Caption, Height = 22 };

        tglStartup = new ToggleSwitch("Start with Windows") { Checked = startup };
        tglTray = new ToggleSwitch("Minimize and close to tray") { Checked = tray };

        var defHint = new Label { Text = "Used when you capture a game that has no profile yet.", ForeColor = Theme.Dim, Font = Theme.Caption, Height = 20 };
        var editor = new OptionsEditor();
        editor.Load(defaults);
        editor.Changed += () => editor.ReadInto(defaults);

        var btnDone = new ModernButton { Kind = ButtonKind.Accent, Text = "Done", Width = 110, Dock = DockStyle.Right };
        btnDone.Click += (_, _) => Close();
        var footer = new Panel { Height = 38 };
        footer.Controls.Add(btnDone);

        var rows = new Control[]
        {
            Stacking.Section("Capture hotkey"), hkRow, hkHint,
            Stacking.Section("General"), tglStartup, tglTray,
            Stacking.Section("Defaults for new games"), defHint, editor,
            Stacking.Spacer(16), footer
        };
        Stacking.Stack(this, rows);
        ClientSize = new Size(440, Padding.Vertical + rows.Sum(r => r.Height));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
    }
}
