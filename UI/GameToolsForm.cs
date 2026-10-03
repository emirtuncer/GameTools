using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GameTools.Core;
using GameTools.Data;
using GameTools.Layout;
using Microsoft.Win32;

namespace GameTools.UI;

public class GameToolsForm : Form
{
    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string SettingsPath = Path.Combine(AppDir, "gametools_settings.json");
    static readonly string ProfilesPath = Path.Combine(AppDir, "gametools_profiles.json");
    static readonly string ButtonsPath = Path.Combine(AppDir, "gametools_buttons.json");

    // Web server for iOS remote control
    WebServer? webServer;
    Dictionary<string, List<ButtonSlot>> buttonProfiles = new(StringComparer.OrdinalIgnoreCase);

    // State
    IntPtr targetHwnd;
    string? targetProcess;
    uint targetPid;
    bool cleanedUp;
    volatile bool running = true, clipRunning;
    Thread? clipThread, autoDetectThread;
    Form? blackBg;
    NotifyIcon? trayIcon;
    ContextMenuStrip? trayMenu;
    bool exiting;
    bool trayMode;


    // Original window state
    Win32.RECT originalRect;
    int originalStyle, originalExStyle;
    bool hasOriginalState;
    readonly object _appliedPidsLock = new();
    HashSet<uint> appliedPids = [];
    Dictionary<string, GameProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
    // Settings used for a newly captured game that has no profile yet.
    GameProfile defaults = new();

    // Hotkey
    uint hkMod = Win32.MOD_CONTROL | Win32.MOD_ALT, hkVk = 0x47;
    string hkLabel = "Ctrl+Alt+G";
    const int HOTKEY_ID = 9001;

    // Controls
    Label lblTarget = null!, lblStatus = null!, lblHotkeyHint = null!, lblListEmpty = null!;
    ModernButton btnCapture = null!;
    InputBox search = null!;
    ProfileList profileList = null!;
    ProfileDetailView detail = null!;
    string? selectedExe;
    bool loadingDetail;
    Anim statusFlash = null!;

    // Running-process indicator for the list
    HashSet<string> runningExes = new(StringComparer.OrdinalIgnoreCase);
    System.Windows.Forms.Timer? runningTimer;
    bool refreshingRunning;

    // Gamepad controls
    CheckBox chkGamepad = null!, chkWasd = null!, chkMouse = null!, chkInvertRX = null!, chkInvertRY = null!;
    TrackBar trkSensitivity = null!, trkRampUp = null!, trkRampDown = null!;
    Label lblSensVal = null!, lblRampUpVal = null!, lblRampDownVal = null!, lblGamepadStatus = null!;
    Button btnDebugHid = null!;
    ProgressBar barW = null!, barA = null!, barS = null!, barD = null!;
    Label lblBarW = null!, lblBarA = null!, lblBarS = null!, lblBarD = null!;
    System.Windows.Forms.Timer? gamepadUiTimer;
    bool vigemAvailable;

    static readonly uint WM_SHOWME = Win32.RegisterWindowMessage("GameTools_ShowMe");

    Dictionary<string, object>? _cachedSettings;

    public GameToolsForm(bool startMinimized = false)
    {
        LoadSettings();
        LoadProfiles();
        LoadButtonProfiles();
        BuildUI();
        // Create the JSON files on first launch (BuildUI has applied loaded/default state by now),
        // so they exist from start rather than only appearing after a save or on close.
        try { if (!File.Exists(SettingsPath)) SaveSettings(); } catch (Exception ex) { Debug.WriteLine("Init settings: " + ex.Message); }
        try { if (!File.Exists(ProfilesPath)) SaveProfiles(); } catch (Exception ex) { Debug.WriteLine("Init profiles: " + ex.Message); }
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception ex) { Debug.WriteLine("Load icon: " + ex.Message); }
        // Only start hidden when launched at Windows startup (--minimized); a manual launch opens in front.
        WindowState = startMinimized ? FormWindowState.Minimized : FormWindowState.Normal;
        Win32.RegisterHotKey(Handle, HOTKEY_ID, hkMod | Win32.MOD_NOREPEAT, hkVk);
        RegisterRawInput();
        autoDetectThread = new Thread(AutoDetectLoop) { IsBackground = true };
        autoDetectThread.Start();
        StartWebServer();
    }

    void RegisterRawInput()
    {
        var rid = new Win32.RAWINPUTDEVICE[]
        {
            new()
            {
                UsagePage = 0x01, // HID_USAGE_PAGE_GENERIC
                Usage = 0x02,     // HID_USAGE_GENERIC_MOUSE
                Flags = (uint)Win32.RIDEV_INPUTSINK,
                Target = Handle
            }
        };
        Win32.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>());
    }

    void BuildUI()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        Text = $"GameTools v{ver.Major}.{ver.Minor}.{ver.Build}";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Mantle;
        ForeColor = Theme.FG;
        Font = Theme.Body;
        ClientSize = new Size(840, 640);
        MinimumSize = new Size(720, 520);
        KeyPreview = true;

        BuildTray();
        BuildGamepadControls();

        // Header: logo, title, active-game indicator, settings
        var header = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(18, 0, 12, 0) };
        var logo = new PictureBox { Size = new Size(28, 28), Location = new Point(18, 16), SizeMode = PictureBoxSizeMode.Zoom };
        try { logo.Image = new Icon(Icon.ExtractAssociatedIcon(Application.ExecutablePath)!, 32, 32).ToBitmap(); } catch (Exception ex) { Debug.WriteLine("Logo: " + ex.Message); }
        var lblTitle = new Label { Text = "GameTools", Font = Theme.Heading, ForeColor = Theme.FG, AutoSize = true, Location = new Point(54, 18) };
        var lblVer = new Label { Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}", Font = Theme.Caption, ForeColor = Theme.Dim, AutoSize = true, Location = new Point(150, 22) };
        lblTitle.SizeChanged += (_, _) => lblVer.Left = lblTitle.Right + 4;

        lblTarget = new Label { Font = Theme.Caption, AutoSize = true, Margin = new Padding(0, 22, 12, 0) };
        var btnSettings = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Theme.GlyphSettings, Size = new Size(38, 38), Margin = new Padding(0, 11, 0, 0) };
        btnSettings.Click += (_, _) => OpenSettings();
        var headerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        headerRight.Controls.AddRange([lblTarget, btnSettings]);
        header.Controls.AddRange([logo, lblTitle, lblVer, headerRight]);

        // Footer: status + hotkey hint
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Crust, Padding = new Padding(18, 0, 18, 0) };
        lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Subtext, Font = Theme.Caption, AutoEllipsis = true, Text = "Ready" };
        lblHotkeyHint = new Label { Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Dim, Font = Theme.Caption };
        statusFlash = new Anim(0, () => lblStatus.ForeColor = Theme.Blend(Theme.Subtext, Theme.Accent, statusFlash.Value), 900);
        footer.Controls.Add(lblStatus);
        footer.Controls.Add(lblHotkeyHint);

        // Left column: search, game list, capture
        search = new InputBox(Theme.GlyphSearch, "Search games  (Ctrl+F)");
        search.Box.TextChanged += (_, _) => RefreshProfileList();
        search.Box.KeyDown += OnSearchKeyDown;

        profileList = new ProfileList { Dock = DockStyle.Fill };
        profileList.SelectedIndexChanged += (_, _) => OnListSelectionChanged();
        profileList.ItemActivated += (_, _) => ApplySelected();
        // No Enter-to-apply: the window grabs focus on launch, and a stray Enter must not move a window.
        profileList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { DeleteSelectedProfile(); e.SuppressKeyPress = true; }
        };
        lblListEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = Theme.Dim, Padding = new Padding(8, 24, 8, 0), Visible = false };
        var listHost = new Panel { Dock = DockStyle.Fill };
        listHost.Controls.Add(profileList);
        listHost.Controls.Add(lblListEmpty);

        btnCapture = new ModernButton { Kind = ButtonKind.Accent, Text = "Capture window", Glyph = Theme.GlyphAdd, Height = 42, Dock = DockStyle.Bottom };
        btnCapture.Click += (_, _) => StartCapture();
        new ToolTip().SetToolTip(btnCapture, "Switch to your game within 5 seconds; its window gets captured and set up");

        var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(14, 2, 8, 14) };
        left.Controls.Add(listHost);
        left.Controls.Add(Stacking.Spacer(10));
        left.Controls.Add(search);
        search.Dock = DockStyle.Top;
        left.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Bottom });
        left.Controls.Add(btnCapture);

        // Right: detail card for the selected profile
        detail = new ProfileDetailView { Dock = DockStyle.Fill };
        detail.Changed += OnDetailChanged;
        detail.ApplyClicked += ApplySelected;
        detail.ReleaseClicked += Release;
        detail.FavoriteClicked += ToggleFavorite;
        detail.DeleteClicked += DeleteSelectedProfile;
        if (Constants.GamepadPluginEnabled) detail.AddSection(GamepadPluginGroup(340));
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 14, 14) };
        right.Controls.Add(detail);

        // WinForms docks the last-added control first, so the edges go in after the fill.
        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(header);
        Controls.Add(footer);

        ApplyUiSettings();
        UpdateHotkeyHint();
        UpdateTargetDisplay();
        RefreshProfileList();
        detail.ShowEmpty();
        // Start focus in search, where a stray Enter/Space is harmless (a focused button would capture/apply).
        ActiveControl = search.Box;
        if (profiles.Count > 0)
            lblStatus.Text = $"Loaded {profiles.Count} profile(s)";

        runningTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        runningTimer.Tick += (_, _) => RefreshRunning();
        runningTimer.Start();
        RefreshRunning();
    }

    void BuildTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => { exiting = true; if (trayIcon != null) trayIcon.Visible = false; Close(); });
        trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = Text,
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    void BuildGamepadControls()
    {
        vigemAvailable = GamepadEmulator.IsDriverAvailable();

        chkGamepad = Theme.MakeCheck("Enable Virtual Gamepad");
        chkGamepad.Enabled = vigemAvailable;
        chkGamepad.CheckedChanged += (_, _) => ToggleGamepad();

        lblGamepadStatus = Theme.MakeLabel(
            vigemAvailable ? "ViGEmBus: Ready" : "ViGEmBus: Not installed (required)",
            vigemAvailable ? Theme.Green : Theme.Orange, 8);

        chkWasd = Theme.MakeCheck("WASD \u2192 Left Stick"); chkWasd.Checked = true;
        chkMouse = Theme.MakeCheck("Mouse \u2192 Right Stick"); chkMouse.Checked = true;
        chkInvertRX = Theme.MakeCheck("Invert X"); chkInvertRX.Font = Theme.Small; chkInvertRX.ForeColor = Theme.Dim;
        chkInvertRY = Theme.MakeCheck("Invert Y"); chkInvertRY.Font = Theme.Small; chkInvertRY.ForeColor = Theme.Dim;

        trkRampUp = Theme.MakeTrackBar(20, 500, Constants.DefaultRampUpMs, 160);
        lblRampUpVal = Theme.MakeLabel(trkRampUp.Value + "ms", Theme.Dim, 8);
        trkRampUp.ValueChanged += (_, _) => lblRampUpVal.Text = trkRampUp.Value + "ms";

        trkRampDown = Theme.MakeTrackBar(20, 300, Constants.DefaultRampDownMs, 160);
        lblRampDownVal = Theme.MakeLabel(trkRampDown.Value + "ms", Theme.Dim, 8);
        trkRampDown.ValueChanged += (_, _) => lblRampDownVal.Text = trkRampDown.Value + "ms";

        trkSensitivity = Theme.MakeTrackBar(1, 100, Constants.DefaultMouseSensitivity, 160);
        lblSensVal = Theme.MakeLabel(trkSensitivity.Value.ToString(), Theme.Dim, 8);
        trkSensitivity.ValueChanged += (_, _) => lblSensVal.Text = trkSensitivity.Value.ToString();

        btnDebugHid = Theme.MakeButton("Debug HID", 90);
        btnDebugHid.Click += (_, _) => { using var f = new GamepadDebugForm(); f.ShowDialog(this); };

        // WASD actuation bars
        barW = new ProgressBar { Size = new Size(70, 14), Maximum = 100 };
        barA = new ProgressBar { Size = new Size(70, 14), Maximum = 100 };
        barS = new ProgressBar { Size = new Size(70, 14), Maximum = 100 };
        barD = new ProgressBar { Size = new Size(70, 14), Maximum = 100 };
        lblBarW = Theme.MakeLabel("W: 0%", Theme.Dim, 8);
        lblBarA = Theme.MakeLabel("A: 0%", Theme.Dim, 8);
        lblBarS = Theme.MakeLabel("S: 0%", Theme.Dim, 8);
        lblBarD = Theme.MakeLabel("D: 0%", Theme.Dim, 8);

        // Gamepad tuning is stored per profile, so edits save like the other detail options.
        if (Constants.GamepadPluginEnabled)
        {
            foreach (var c in new[] { chkWasd, chkMouse, chkInvertRX, chkInvertRY }) c.CheckedChanged += (_, _) => OnDetailChanged();
            foreach (var t in new[] { trkRampUp, trkRampDown, trkSensitivity }) t.ValueChanged += (_, _) => OnDetailChanged();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { search.Box.Focus(); search.Box.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Status(string text)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { Invoke(() => { if (!IsDisposed) SetStatus(text); }); }
            catch (ObjectDisposedException) { }
        }
        else SetStatus(text);
    }

    // New messages flash in the accent color, then settle back to the normal status color.
    void SetStatus(string text)
    {
        if (lblStatus.Text == text) return;
        lblStatus.Text = text;
        statusFlash.Snap(1);
        statusFlash.To(0);
    }

    const string StartupRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string StartupValueName = "GameTools";

    static string? GetStartupValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
        return key?.GetValue(StartupValueName) as string;
    }

    // Pull the exe path out of a Run command like: "C:\path\GameTools.exe" --minimized
    static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith("\""))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return command.Substring(1, end - 1);
        }
        int sp = command.IndexOf(' ');
        return sp > 0 ? command.Substring(0, sp) : command;
    }

    static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    // True only when the Run entry exists AND points to *this* exe. A different copy reads as
    // disabled (so its toggle is accurate), and a dead entry (target deleted) is cleaned up.
    static bool IsStartupEnabled()
    {
        try
        {
            var val = GetStartupValue();
            if (string.IsNullOrWhiteSpace(val)) return false;
            var exe = ExtractExePath(val);
            if (!File.Exists(exe))
            {
                try { using var k = Registry.CurrentUser.OpenSubKey(StartupRegKey, true); k?.DeleteValue(StartupValueName, false); }
                catch (Exception ex) { Debug.WriteLine("Startup stale cleanup: " + ex.Message); }
                return false;
            }
            return PathsEqual(exe, Application.ExecutablePath);
        }
        catch { return false; }
    }

    static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
            if (key == null) return;
            if (enable)
                key.SetValue(StartupValueName, $"\"{Application.ExecutablePath}\" --minimized");
            else
                key.DeleteValue(StartupValueName, false);
        }
        catch (Exception ex) { Debug.WriteLine("Startup registry: " + ex.Message); }
    }

    void LoadSettings()
    {
        _cachedSettings = SimpleJson.LoadSettings(SettingsPath);
        if (_cachedSettings.TryGetValue("hk_mod", out var mod)) hkMod = (uint)Convert.ToInt32(mod);
        if (_cachedSettings.TryGetValue("hk_vk", out var vk)) hkVk = (uint)Convert.ToInt32(vk);
        if (_cachedSettings.TryGetValue("hk_label", out var lbl)) hkLabel = lbl.ToString()!;
    }

    void ApplyUiSettings()
    {
        var s = _cachedSettings ?? SimpleJson.LoadSettings(SettingsPath);
        _cachedSettings = null;
        ApplyDefaultsFrom(s);
        if (s.TryGetValue("tray_mode", out var v)) trayMode = Convert.ToBoolean(v);
        ApplyGamepadSettingsToUi(GamepadSettings.FromDict(s));
    }

    // The settings file keeps the new-game defaults under the same keys the old global options used.
    void ApplyDefaultsFrom(Dictionary<string, object> s)
    {
        if (s.TryGetValue("center", out var v)) defaults.Center = Convert.ToBoolean(v);
        if (s.TryGetValue("clip", out v)) defaults.Clip = Convert.ToBoolean(v);
        if (s.TryGetValue("remove_border", out v)) defaults.RemoveBorder = Convert.ToBoolean(v);
        if (s.TryGetValue("black_bg", out v)) defaults.BlackBg = Convert.ToBoolean(v);
        if (s.TryGetValue("mute_bg", out v)) defaults.MuteBg = Convert.ToBoolean(v);
        if (s.TryGetValue("custom_res", out v)) defaults.CustomRes = Convert.ToBoolean(v);
        if (s.TryGetValue("res_w", out v) && int.TryParse(v.ToString(), out int w) && w > 0) defaults.ResW = w;
        if (s.TryGetValue("res_h", out v) && int.TryParse(v.ToString(), out int h) && h > 0) defaults.ResH = h;
    }

    Dictionary<string, object> DefaultsDict() => new()
    {
        ["center"] = defaults.Center, ["clip"] = defaults.Clip,
        ["remove_border"] = defaults.RemoveBorder, ["black_bg"] = defaults.BlackBg,
        ["mute_bg"] = defaults.MuteBg, ["custom_res"] = defaults.CustomRes,
        ["res_w"] = defaults.ResW, ["res_h"] = defaults.ResH
    };

    void SaveSettings()
    {
        var d = DefaultsDict();
        d["hk_mod"] = (int)hkMod; d["hk_vk"] = (int)hkVk; d["hk_label"] = hkLabel;
        d["tray_mode"] = trayMode;
        d["gp_wasd"] = chkWasd.Checked; d["gp_ramp_up"] = trkRampUp.Value;
        d["gp_ramp_down"] = trkRampDown.Value; d["gp_mouse"] = chkMouse.Checked;
        d["gp_sensitivity"] = trkSensitivity.Value;
        d["gp_invert_rx"] = chkInvertRX.Checked; d["gp_invert_ry"] = chkInvertRY.Checked;
        SimpleJson.SaveSettings(SettingsPath, d);
    }

    void LoadProfiles()
    {
        profiles.Clear();
        var raw = SimpleJson.LoadProfiles(ProfilesPath);
        foreach (var kv in raw)
            profiles[kv.Key] = GameProfile.FromDict(kv.Value);
    }

    void SaveProfiles()
    {
        SimpleJson.SaveProfiles(ProfilesPath, profiles);
    }

    // ---- Game list ------------------------------------------------------------------------

    void RefreshProfileList()
    {
        if (profileList == null) return;
        string filter = search.Box.Text.Trim();
        var rows = profiles
            .Where(kv => filter.Length == 0
                || kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || DisplayText.DisplayName(kv.Key).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value.LastUsed)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProfileRow(kv.Key, kv.Value) { Running = runningExes.Contains(kv.Key) })
            .ToList();

        if (selectedExe != null && !profiles.ContainsKey(selectedExe))
        {
            selectedExe = null;
            detail.ShowEmpty();
        }

        int sel = rows.FindIndex(r => string.Equals(r.Exe, selectedExe, StringComparison.OrdinalIgnoreCase));
        profileList.SetItems(rows, sel);

        lblListEmpty.Text = profiles.Count == 0
            ? "No games yet.\nStart a game, then click Capture window."
            : $"No games match \u201C{filter}\u201D";
        lblListEmpty.Visible = rows.Count == 0;
        profileList.Visible = rows.Count > 0;
    }

    void OnListSelectionChanged()
    {
        if (profileList.SelectedItem is not ProfileRow row) return;
        selectedExe = row.Exe;
        ShowSelectedDetail();
    }

    void ShowSelectedDetail()
    {
        if (selectedExe == null || !profiles.TryGetValue(selectedExe, out var p)) { detail.ShowEmpty(); return; }
        loadingDetail = true;
        detail.ShowProfile(selectedExe, p, runningExes.Contains(selectedExe));
        if (Constants.GamepadPluginEnabled) ApplyGamepadSettingsToUi(p.Gamepad);
        loadingDetail = false;
    }

    // Selects exe in the list (clearing a search that hides it) and shows it in the detail card.
    void SelectProfile(string exe)
    {
        var key = profiles.Keys.FirstOrDefault(k => string.Equals(k, exe, StringComparison.OrdinalIgnoreCase));
        if (key == null) return;
        selectedExe = key;
        if (search.Box.Text.Length > 0) search.Box.Text = ""; // triggers RefreshProfileList
        else RefreshProfileList();
        ShowSelectedDetail();
    }

    void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                search.Box.Text = "";
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
            case Keys.Down:
                if (profileList.Items.Count > 0)
                {
                    if (profileList.SelectedIndex < 0) profileList.SelectedIndex = 0;
                    profileList.Focus();
                }
                e.SuppressKeyPress = true;
                break;
        }
    }

    void OnDetailChanged()
    {
        if (loadingDetail || selectedExe == null || !profiles.TryGetValue(selectedExe, out var p)) return;
        detail.Options.ReadInto(p);
        if (Constants.GamepadPluginEnabled) p.Gamepad = GetGamepadSettings();
        SaveProfiles();
    }

    async void RefreshRunning()
    {
        if (refreshingRunning || !Visible || WindowState == FormWindowState.Minimized) return;
        refreshingRunning = true;
        try
        {
            var names = await Task.Run(() =>
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try { set.Add(p.ProcessName + ".exe"); }
                    catch (Exception) { }
                    finally { p.Dispose(); }
                }
                return set;
            });
            if (IsDisposed || names.SetEquals(runningExes)) return;
            runningExes = names;
            foreach (var row in profileList.Items) row.Running = runningExes.Contains(row.Exe);
            profileList.Invalidate();
            if (selectedExe != null) detail.SetRunning(runningExes.Contains(selectedExe));
        }
        catch (Exception ex) { Debug.WriteLine("RefreshRunning: " + ex.Message); }
        finally { refreshingRunning = false; }
    }

    // ---- Settings -------------------------------------------------------------------------

    void OpenSettings()
    {
        var edited = defaults.Clone();
        bool startup = IsStartupEnabled();
        using var f = new SettingsForm(hkLabel, startup, trayMode, edited, ChangeHotkey);
        f.ShowDialog(this);
        defaults = edited;
        if (f.StartWithWindows != startup) SetStartupEnabled(f.StartWithWindows);
        trayMode = f.TrayMode;
        SaveSettings();
        Status("Settings saved");
    }

    void UpdateHotkeyHint() => lblHotkeyHint.Text = $"Capture: {hkLabel}";

    // ---- Gamepad (optional plugin) --------------------------------------------------------

    Control GamepadPluginGroup(int contentW)
    {
        if (!Constants.GamepadPluginEnabled)
            return new Panel { Size = Size.Empty };

        return Flex.Group("Virtual Gamepad", Theme.Bold, Theme.Accent, 4, contentW,
            chkGamepad, lblGamepadStatus,
            chkWasd,
            Flex.Row(8, Theme.MakeLabel("Ramp Up:"), trkRampUp, lblRampUpVal),
            Flex.Row(8, Theme.MakeLabel("Ramp Down:"), trkRampDown, lblRampDownVal),
            chkMouse,
            Flex.Row(8, Theme.MakeLabel("Sensitivity:"), trkSensitivity, lblSensVal),
            Flex.Row(8, chkInvertRX, chkInvertRY),
            Flex.Row(6, lblBarW, barW, lblBarA, barA),
            Flex.Row(6, lblBarS, barS, lblBarD, barD),
            btnDebugHid);
    }

    GamepadSettings GetGamepadSettings() => new()
    {
        Enabled = chkGamepad.Checked,
        WasdEnabled = chkWasd.Checked,
        RampUpMs = trkRampUp.Value,
        RampDownMs = trkRampDown.Value,
        MouseEnabled = chkMouse.Checked,
        MouseSensitivity = trkSensitivity.Value,
        MouseDecayMs = Constants.DefaultMouseDecayMs,
        InvertRightX = chkInvertRX.Checked,
        InvertRightY = chkInvertRY.Checked,
    };

    void ApplyGamepadSettingsToUi(GamepadSettings s)
    {
        chkGamepad.Checked = s.Enabled && vigemAvailable;
        chkWasd.Checked = s.WasdEnabled;
        trkRampUp.Value = Math.Clamp(s.RampUpMs, trkRampUp.Minimum, trkRampUp.Maximum);
        trkRampDown.Value = Math.Clamp(s.RampDownMs, trkRampDown.Minimum, trkRampDown.Maximum);
        chkMouse.Checked = s.MouseEnabled;
        trkSensitivity.Value = Math.Clamp(s.MouseSensitivity, trkSensitivity.Minimum, trkSensitivity.Maximum);
        chkInvertRX.Checked = s.InvertRightX;
        chkInvertRY.Checked = s.InvertRightY;
    }

    void ToggleGamepad()
    {
        if (chkGamepad.Checked)
        {
            var settings = GetGamepadSettings();
            if (GamepadEmulator.Start(settings))
            {
                lblGamepadStatus.Text = "Virtual Xbox 360 controller active";
                lblGamepadStatus.ForeColor = Theme.Green;
                Status("Virtual gamepad connected");
                StartGamepadUiTimer();
            }
            else
            {
                chkGamepad.Checked = false;
                lblGamepadStatus.Text = "Failed to start (is ViGEmBus installed?)";
                lblGamepadStatus.ForeColor = Theme.Orange;
                Status("Gamepad emulation failed");
            }
        }
        else
        {
            StopGamepadUiTimer();
            GamepadEmulator.Stop();
            lblGamepadStatus.Text = vigemAvailable ? "ViGEmBus: Ready" : "ViGEmBus: Not installed";
            lblGamepadStatus.ForeColor = vigemAvailable ? Theme.Green : Theme.Orange;
            Status("Virtual gamepad disconnected");
            barW.Value = barA.Value = barS.Value = barD.Value = 0;
            lblBarW.Text = "W: 0%"; lblBarA.Text = "A: 0%"; lblBarS.Text = "S: 0%"; lblBarD.Text = "D: 0%";
        }
        SaveSettings();
    }

    void StartGamepadUiTimer()
    {
        gamepadUiTimer ??= new System.Windows.Forms.Timer { Interval = 50 };
        gamepadUiTimer.Tick -= OnGamepadUiTick;
        gamepadUiTimer.Tick += OnGamepadUiTick;
        gamepadUiTimer.Start();
    }

    void StopGamepadUiTimer()
    {
        gamepadUiTimer?.Stop();
    }

    void OnGamepadUiTick(object? sender, EventArgs e)
    {
        if (!GamepadEmulator.IsRunning) { StopGamepadUiTimer(); return; }

        int w = (int)(GamepadEmulator.CurrentW * 100);
        int a = (int)(GamepadEmulator.CurrentA * 100);
        int s = (int)(GamepadEmulator.CurrentS * 100);
        int d = (int)(GamepadEmulator.CurrentD * 100);

        barW.Value = Math.Clamp(w, 0, 100);
        barA.Value = Math.Clamp(a, 0, 100);
        barS.Value = Math.Clamp(s, 0, 100);
        barD.Value = Math.Clamp(d, 0, 100);

        lblBarW.Text = $"W:{w}%"; lblBarA.Text = $"A:{a}%";
        lblBarS.Text = $"S:{s}%"; lblBarD.Text = $"D:{d}%";
    }

    // ---- Profile actions ------------------------------------------------------------------

    void ToggleFavorite()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        bool nowFav = profiles[exe].Favorite = !profiles[exe].Favorite;
        SaveProfiles();
        detail.SetFavorite(nowFav);
        profileList.Invalidate();
        Status($"{exe} {(nowFav ? "favorited \u2014 settings auto-apply when it starts" : "unfavorited")}");

        // Auto-apply immediately if the favorited window is already open
        if (!nowFav) return;
        try
        {
            var win = FindOpenWindow(exe);
            if (win == null) return;
            lock (_appliedPidsLock) appliedPids.Add(win.Pid);
            ApplyProfileActions(win.Hwnd, profiles[exe]);
        }
        catch (Exception ex) { Debug.WriteLine("Favorite auto-apply: " + ex.Message); }
    }

    // Applies the selected profile to its game's window, if the game is running.
    void ApplySelected()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        WindowInfo? win;
        try { win = FindOpenWindow(exe); }
        catch (Exception ex) { Status("Window lookup failed: " + ex.Message); return; }
        if (win == null) { Status($"{exe} is not running \u2014 start the game, then click Apply"); return; }
        lock (_appliedPidsLock) appliedPids.Add(win.Pid);
        ApplyProfileActions(win.Hwnd, profiles[exe]);
    }

    void DeleteSelectedProfile()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        if (MessageBox.Show(this, $"Delete the profile for {exe}?", "Delete profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        profiles.Remove(exe);
        selectedExe = null;
        SaveProfiles(); RefreshProfileList();
        detail.ShowEmpty();
        Status($"Deleted: {exe}");
    }

    string? SelectedExe()
    {
        if (selectedExe == null || !profiles.ContainsKey(selectedExe)) { Status("Select a game first"); return null; }
        return selectedExe;
    }

    static WindowInfo? FindOpenWindow(string exe) =>
        WindowHelper.GetWindows()
            .Where(w => w.Width > Constants.MinWindowSize && w.Height > Constants.MinWindowSize)
            .FirstOrDefault(w => string.Equals(w.Process, exe, StringComparison.OrdinalIgnoreCase));

    GameProfile? TargetProfile() =>
        targetProcess != null && profiles.TryGetValue(targetProcess, out var p) ? p : null;

    // ---- Capture / apply / release --------------------------------------------------------

    void StartCapture()
    {
        btnCapture.Enabled = false;
        SaveSettings();
        WindowState = FormWindowState.Minimized;

        var overlay = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(280, 140), BackColor = Theme.BG, Opacity = 0.92, TopMost = true, ShowInTaskbar = false
        };
        var lblCount = new Label
        {
            Text = "5", Font = new Font("Segoe UI", 52, FontStyle.Bold),
            ForeColor = Theme.Accent, BackColor = Theme.BG, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        overlay.Controls.Add(lblCount);
        overlay.Show();

        int count = Constants.CaptureCountdownSec;
        var timer = new System.Windows.Forms.Timer { Interval = Constants.CaptureIntervalMs };
        timer.Tick += (_, _) =>
        {
            count--;
            if (count <= 0)
            {
                timer.Stop(); overlay.Close(); overlay.Dispose(); timer.Dispose();
                BeginInvoke(DoCapture);
            }
            else lblCount.Text = count.ToString();
        };
        timer.Start();
    }

    void SaveOriginalState(IntPtr hwnd)
    {
        if (hasOriginalState && hwnd == targetHwnd) return;
        Win32.GetWindowRect(hwnd, out originalRect);
        originalStyle = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
        originalExStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        hasOriginalState = true;
    }

    void DoCapture()
    {
        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd))
        {
            var info = WindowHelper.GetInfo(hwnd);
            if (!profiles.TryGetValue(info.Process, out var profile))
            {
                profile = defaults.Clone();
                profile.Favorite = false;
                profiles[info.Process] = profile;
            }
            ApplyProfileActions(hwnd, profile);
        }
        else Status("No valid window detected");

        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        if (trayIcon != null) trayIcon.Visible = false;
        btnCapture.Enabled = true;
    }

    void UpdateTargetDisplay()
    {
        if (targetHwnd == IntPtr.Zero || !Win32.IsWindow(targetHwnd))
        {
            lblTarget.Text = "No active game";
            lblTarget.ForeColor = Theme.Dim;
            return;
        }
        var info = WindowHelper.GetInfo(targetHwnd);
        lblTarget.Text = $"\u25CF  {DisplayText.DisplayName(info.Process)}  \u00B7  {info.Width}\u00D7{info.Height}";
        lblTarget.ForeColor = Theme.Green;
    }

    // Single path for every apply: capture, Apply button, favorite toggle and auto-detect.
    void ApplyProfileActions(IntPtr hwnd, GameProfile p)
    {
        if (InvokeRequired) { Invoke(() => ApplyProfileActions(hwnd, p)); return; }

        SaveOriginalState(hwnd);
        targetHwnd = hwnd;
        var info = WindowHelper.GetInfo(hwnd);
        targetProcess = info.Process;
        targetPid = info.Pid;

        p.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveProfiles();
        SelectProfile(info.Process);

        var actions = new List<string>();
        if (p.RemoveBorder) { WindowHelper.RemoveBorder(hwnd); Thread.Sleep(Constants.ActionSettleMs); actions.Add("border removed"); }

        if (p.CustomRes || p.Center)
        {
            int? tw = null, th = null;
            if (p.CustomRes) { tw = p.ResW; th = p.ResH; }
            WindowHelper.Center(hwnd, tw, th);
            actions.Add(p.Center ? "centered" : "resized");
            Thread.Sleep(Constants.ActionSettleMs);
        }

        if (p.Clip) { StartMonitoring(); actions.Add("clip"); }
        if (p.BlackBg) { ShowBlackBg(); actions.Add("black bg"); }
        if (p.Gamepad.Enabled && vigemAvailable) { chkGamepad.Checked = true; actions.Add("gamepad"); }
        UpdateTargetDisplay();
        Status(actions.Count > 0
            ? $"Applied to {info.Process}: {string.Join(", ", actions)}"
            : $"Captured {info.Process} (no actions enabled)");
    }

    void Release()
    {
        StopMonitoring();
        HideBlackBg();
        if (GamepadEmulator.IsRunning) { GamepadEmulator.Stop(); chkGamepad.Checked = false; }
        Win32.ClipCursor(IntPtr.Zero);
        if (targetPid != 0) AudioMuter.SetMute(targetPid, false);

        if (hasOriginalState && targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
        {
            Win32.SetWindowLong(targetHwnd, Win32.GWL_STYLE, originalStyle);
            Win32.SetWindowLong(targetHwnd, Win32.GWL_EXSTYLE, originalExStyle);
            int x = originalRect.Left, y = originalRect.Top;
            int w = originalRect.Right - originalRect.Left, h = originalRect.Bottom - originalRect.Top;
            Win32.SetWindowPos(targetHwnd, Win32.HWND_NOTOPMOST, x, y, w, h,
                Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
            hasOriginalState = false;
        }
        // Clear target state so AutoDetectLoop's auto-release guard (targetPid != 0)
        // stops firing on the dead PID and resumes evaluating favorites. Without this,
        // favorite auto-load silently dies after the first favorited game is closed.
        targetHwnd = IntPtr.Zero;
        targetPid = 0;
        targetProcess = null;
        UpdateTargetDisplay();
        Status("Released");
    }

    void ShowBlackBg()
    {
        if (targetHwnd == IntPtr.Zero) return;
        var mi = WindowHelper.GetMonitor(targetHwnd);
        var mon = mi.rcMonitor;
        int mw = mon.Right - mon.Left, mh = mon.Bottom - mon.Top;

        blackBg ??= new Form { FormBorderStyle = FormBorderStyle.None, BackColor = Color.Black, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual };
        blackBg.Location = new Point(mon.Left, mon.Top);
        blackBg.Size = new Size(mw, mh);
        blackBg.Show();

        Win32.SetWindowPos(blackBg.Handle, Win32.HWND_TOPMOST, mon.Left, mon.Top, mw, mh, Win32.SWP_NOACTIVATE);
        Win32.SetWindowPos(targetHwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    void HideBlackBg()
    {
        if (blackBg != null) { try { blackBg.Hide(); } catch (Exception ex) { Debug.WriteLine("HideBlackBg: " + ex.Message); } }
        if (targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
            Win32.SetWindowPos(targetHwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    void StartMonitoring()
    {
        if (clipRunning) StopMonitoring();
        clipRunning = true;
        var p = TargetProfile() ?? defaults;
        bool useBlack = p.BlackBg, useMute = p.MuteBg;
        uint pid = targetPid;
        clipThread = new Thread(() => ClipLoop(useBlack, useMute, pid)) { IsBackground = true };
        clipThread.Start();
    }

    void StopMonitoring()
    {
        clipRunning = false;
        if (clipThread != null) { clipThread.Join(Constants.ThreadJoinMs); clipThread = null; }
        Win32.ClipCursor(IntPtr.Zero);
        HideBlackBg();
    }

    void ClipLoop(bool useBlack, bool useMute, uint pid)
    {
        bool clipped = false;
        while (clipRunning && running)
        {
            if (targetHwnd == IntPtr.Zero || !Win32.IsWindow(targetHwnd))
            {
                if (clipped) { Win32.ClipCursor(IntPtr.Zero); if (useMute) AudioMuter.SetMute(pid, false); }
                if (useBlack) try { Invoke(HideBlackBg); } catch (ObjectDisposedException) { }
                clipRunning = false;
                try { BeginInvoke(Release); } catch (ObjectDisposedException) { }
                break;
            }

            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == targetHwnd)
            {
                WindowHelper.ClipToWindow(targetHwnd);
                if (!clipped)
                {
                    if (useBlack) Invoke(ShowBlackBg);
                    if (useMute) AudioMuter.SetMute(pid, false);
                    Status("Cursor clipped (focused)");
                }
                clipped = true;
            }
            else
            {
                if (clipped)
                {
                    Win32.ClipCursor(IntPtr.Zero);
                    if (useBlack) Invoke(HideBlackBg);
                    if (useMute) AudioMuter.SetMute(pid, true);
                    Status("Cursor free (alt-tabbed)");
                }
                clipped = false;
            }
            Thread.Sleep(Constants.ClipLoopMs);
        }
    }

    void AutoDetectLoop()
    {
        while (running)
        {
            Thread.Sleep(Constants.AutoDetectMs);

            // Auto-release: detect target process exit when no clip loop is watching
            if (targetPid != 0 && !clipRunning)
            {
                try { System.Diagnostics.Process.GetProcessById((int)targetPid); }
                catch (ArgumentException)
                {
                    try { BeginInvoke(Release); } catch (ObjectDisposedException) { }
                    continue;
                }
                catch (InvalidOperationException) { }
            }

            if (clipRunning) continue;

            Dictionary<string, GameProfile> favs;
            try { favs = (Invoke(() => profiles.Where(p => p.Value.Favorite).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)) as Dictionary<string, GameProfile>)!; }
            catch (Exception) { continue; }
            if (favs.Count == 0) continue;

            List<WindowInfo> windows;
            try { windows = WindowHelper.GetWindows().Where(w => w.Width > Constants.MinWindowSize && w.Height > Constants.MinWindowSize).ToList(); }
            catch (Exception ex) { Debug.WriteLine("AutoDetect: " + ex.Message); continue; }

            foreach (var win in windows)
            {
                if (!favs.TryGetValue(win.Process, out var profile)) continue;
                lock (_appliedPidsLock)
                {
                    if (appliedPids.Contains(win.Pid)) continue;
                    appliedPids.Add(win.Pid);
                }
                try { Invoke(() => ApplyProfileActions(win.Hwnd, profile)); }
                catch (Exception ex) { Debug.WriteLine("AutoDetect apply: " + ex.Message); }
                break;
            }

            lock (_appliedPidsLock)
            {
                var activePids = new HashSet<uint>(windows.Select(w => w.Pid));
                appliedPids.RemoveWhere(p => !activePids.Contains(p));
            }
        }
    }

    // ---- Window / tray --------------------------------------------------------------------

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Ensure a normal (non-startup) launch grabs the foreground.
        if (WindowState != FormWindowState.Minimized)
        {
            Show();
            Activate();
            BringToFront();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && trayMode)
        {
            Hide();
            if (trayIcon != null) trayIcon.Visible = true;
        }
    }

    void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        if (trayIcon != null) trayIcon.Visible = false;
        Activate();
        RefreshRunning();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            StartCapture();
        if (m.Msg == Win32.WM_INPUT && GamepadEmulator.IsRunning)
        {
            uint size = (uint)Marshal.SizeOf<Win32.RAWINPUT>();
            if (Win32.GetRawInputData(m.LParam, Win32.RID_INPUT, out Win32.RAWINPUT raw,
                ref size, (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>()) != unchecked((uint)-1))
            {
                if (raw.Header.Type == 0) // RIM_TYPEMOUSE
                    GamepadEmulator.OnRawMouseInput(raw.Mouse.LastX, raw.Mouse.LastY);
            }
        }
        if (m.Msg == (int)WM_SHOWME)
        {
            if (trayIcon is { Visible: true }) RestoreFromTray();
            else
            {
                WindowState = FormWindowState.Normal;
                Show();
                Win32.ShowWindow(Handle, Win32.SW_RESTORE);
                Activate();
            }
        }
        base.WndProc(ref m);
    }

    // Shows the key-capture dialog over `owner`; returns the (possibly unchanged) hotkey label.
    string ChangeHotkey(IWin32Window owner)
    {
        Win32.UnregisterHotKey(Handle, HOTKEY_ID);

        using var dlg = new Form
        {
            Text = "Set Hotkey", FormBorderStyle = FormBorderStyle.FixedDialog,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(340, 110), ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.BG, MaximizeBox = false, MinimizeBox = false
        };
        dlg.HandleCreated += (_, _) => Theme.ApplyWindowChrome(dlg);
        var lbl = new Label { Text = "Press a key combination\u2026", ForeColor = Theme.FG, Font = Theme.Heading, Location = new Point(24, 24), AutoSize = true };
        var hint = new Label { Text = "Ctrl / Alt / Shift + a key  \u00B7  Esc to cancel", ForeColor = Theme.Dim, Font = Theme.Caption, Location = new Point(24, 62), AutoSize = true };
        dlg.Controls.Add(lbl); dlg.Controls.Add(hint);

        uint newMod = 0, newVk = 0; string? newLabel = null;
        dlg.KeyPreview = true;
        dlg.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { dlg.DialogResult = DialogResult.Cancel; return; }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            uint mod = 0; var parts = new List<string>();
            if (e.Control) { mod |= Win32.MOD_CONTROL; parts.Add("Ctrl"); }
            if (e.Alt) { mod |= Win32.MOD_ALT; parts.Add("Alt"); }
            if (e.Shift) { mod |= Win32.MOD_SHIFT; parts.Add("Shift"); }
            if (mod == 0) { hint.Text = "Need at least one modifier!"; hint.ForeColor = Theme.Red; return; }
            parts.Add(e.KeyCode.ToString());
            newMod = mod; newVk = (uint)e.KeyCode; newLabel = string.Join("+", parts);
            dlg.DialogResult = DialogResult.OK;
        };

        if (dlg.ShowDialog(owner) == DialogResult.OK && newLabel != null)
        {
            hkMod = newMod; hkVk = newVk; hkLabel = newLabel;
            UpdateHotkeyHint();
            Status($"Hotkey changed to {hkLabel}");
            SaveSettings();
        }
        else Status("Hotkey change cancelled");

        Win32.RegisterHotKey(Handle, HOTKEY_ID, hkMod | Win32.MOD_NOREPEAT, hkVk);
        return hkLabel;
    }

    // ---- Remote control -------------------------------------------------------------------

    void LoadButtonProfiles()
    {
        buttonProfiles = ButtonSlot.LoadAll(ButtonsPath);
        if (buttonProfiles.Count == 0)
        {
            // Create default buttons
            buttonProfiles["default"] = new List<ButtonSlot>
            {
                new() { Id = "slot1", Label = "Mute", Icon = "speaker.slash.fill", Type = "command", Target = "mute" },
                new() { Id = "slot2", Label = "Capture", Icon = "camera.fill", Type = "command", Target = "capture" },
                new() { Id = "slot3", Label = "Release", Icon = "lock.open.fill", Type = "command", Target = "release" },
                new() { Id = "slot4", Label = "Clip", Icon = "cursorarrow.motionlines", Type = "command", Target = "clip" }
            };
            ButtonSlot.SaveAll(ButtonsPath, buttonProfiles);
        }
    }

    void StartWebServer()
    {
        try
        {
            webServer = new WebServer
            {
                GetSettings = () =>
                {
                    var d = DefaultsDict();
                    d["hk_label"] = hkLabel;
                    return d;
                },
                UpdateSettings = dict =>
                {
                    if (InvokeRequired) { Invoke(() => UpdateSettingsFromRemote(dict)); return; }
                    UpdateSettingsFromRemote(dict);
                },
                GetProfiles = () => profiles,
                UpdateProfile = (exe, profile) =>
                {
                    if (InvokeRequired) { Invoke(() => UpdateProfileFromRemote(exe, profile)); return; }
                    UpdateProfileFromRemote(exe, profile);
                },
                DeleteProfile = exe =>
                {
                    if (InvokeRequired) { Invoke(() => DeleteProfileFromRemote(exe)); return; }
                    DeleteProfileFromRemote(exe);
                },
                GetCurrentGame = () => targetProcess,
                IsTargetAlive = () => targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd),
                ExecuteAction = id =>
                {
                    if (InvokeRequired) { Invoke(() => ExecuteRemoteAction(id)); return; }
                    ExecuteRemoteAction(id);
                },
                GetButtonProfiles = () => buttonProfiles,
                UpdateButtonProfile = (exe, slots) =>
                {
                    buttonProfiles[exe] = slots;
                    ButtonSlot.SaveAll(ButtonsPath, buttonProfiles);
                }
            };
            webServer.Start();
            Debug.WriteLine("WebServer started for iOS remote control");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("WebServer failed to start: " + ex.Message);
        }
    }

    void UpdateSettingsFromRemote(Dictionary<string, object> dict)
    {
        ApplyDefaultsFrom(dict);
        SaveSettings();
        Status("Default settings updated from remote");
    }

    void UpdateProfileFromRemote(string exe, GameProfile profile)
    {
        if (profile.LastUsed == 0 && profiles.TryGetValue(exe, out var existing)) profile.LastUsed = existing.LastUsed;
        profiles[exe] = profile;
        SaveProfiles();
        RefreshProfileList();
        if (string.Equals(exe, selectedExe, StringComparison.OrdinalIgnoreCase)) ShowSelectedDetail();
        Status($"Profile updated from remote: {exe}");
    }

    void DeleteProfileFromRemote(string exe)
    {
        profiles.Remove(exe);
        SaveProfiles();
        RefreshProfileList();
        Status($"Profile deleted from remote: {exe}");
    }

    // Persists a remote toggle of the active game's profile and refreshes the card if it's shown.
    void SaveTargetProfileChange()
    {
        SaveProfiles();
        if (targetProcess != null && string.Equals(targetProcess, selectedExe, StringComparison.OrdinalIgnoreCase)) ShowSelectedDetail();
    }

    void ExecuteRemoteAction(string id)
    {
        switch (id)
        {
            case "mute":
                if (targetPid != 0) AudioMuter.SetMute(targetPid, true);
                Status("Muted from remote");
                break;
            case "unmute":
                if (targetPid != 0) AudioMuter.SetMute(targetPid, false);
                Status("Unmuted from remote");
                break;
            case "capture":
                StartCapture();
                break;
            case "release":
                Release();
                break;
            case "clip":
                if (targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
                {
                    if (clipRunning) StopMonitoring();
                    else StartMonitoring();
                }
                Status(clipRunning ? "Clip enabled from remote" : "Clip disabled from remote");
                break;
            case "blackbg":
            {
                var p = TargetProfile();
                if (p == null) { Status("No active game (remote)"); break; }
                p.BlackBg = !p.BlackBg;
                if (p.BlackBg) ShowBlackBg(); else HideBlackBg();
                SaveTargetProfileChange();
                Status(p.BlackBg ? "Black BG on (remote)" : "Black BG off (remote)");
                break;
            }
            case "remove_border":
            {
                var p = TargetProfile();
                if (p == null || !Win32.IsWindow(targetHwnd)) { Status("No active game (remote)"); break; }
                p.RemoveBorder = !p.RemoveBorder;
                if (p.RemoveBorder) WindowHelper.RemoveBorder(targetHwnd);
                SaveTargetProfileChange();
                Status(p.RemoveBorder ? "Border removed (remote)" : "Border toggle (remote)");
                break;
            }
            case "center":
                if (targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
                    WindowHelper.Center(targetHwnd);
                Status("Centered from remote");
                break;
            default:
                // Handle hotkey actions: "hotkey:alt+1"
                if (id.StartsWith("hotkey:"))
                {
                    string keys = id[7..];
                    SendHotkey(keys);
                    Status($"Hotkey sent from remote: {keys}");
                }
                else
                {
                    // Check button profiles for mapped actions
                    ExecuteButtonSlotAction(id);
                }
                break;
        }
    }

    void ExecuteButtonSlotAction(string slotId)
    {
        string key = targetProcess ?? "default";
        if (!buttonProfiles.TryGetValue(key, out var slots))
            buttonProfiles.TryGetValue("default", out slots);

        var slot = slots?.FirstOrDefault(s => s.Id == slotId);
        if (slot == null) { Status($"Unknown action: {slotId}"); return; }

        if (slot.Type == "command")
            ExecuteRemoteAction(slot.Target);
        else if (slot.Type == "hotkey")
        {
            SendHotkey(slot.Target);
            Status($"Hotkey sent: {slot.Target}");
        }
    }

    void SendHotkey(string keys)
    {
        // Parse "alt+1", "ctrl+shift+f5", "n" etc. and send via SendInput
        var parts = keys.ToLower().Split('+');
        var modifiers = new List<ushort>();
        ushort mainKey = 0;

        foreach (var part in parts)
        {
            switch (part.Trim())
            {
                case "ctrl" or "control": modifiers.Add(Win32.VK_CONTROL); break;
                case "alt": modifiers.Add(Win32.VK_MENU); break;
                case "shift": modifiers.Add(Win32.VK_SHIFT); break;
                default:
                    mainKey = part.Trim().ToUpper() switch
                    {
                        "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
                        "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
                        "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                        "ESC" or "ESCAPE" => 0x1B,
                        "TAB" => 0x09,
                        "SPACE" => 0x20,
                        "ENTER" or "RETURN" => 0x0D,
                        "BACKSPACE" => 0x08,
                        "DELETE" or "DEL" => 0x2E,
                        var k when k.Length == 1 && char.IsLetterOrDigit(k[0]) => (ushort)k[0],
                        _ => 0
                    };
                    break;
            }
        }

        if (mainKey == 0) return;

        // Press modifiers, press key, release key, release modifiers
        foreach (var mod in modifiers) Win32.keybd_event((byte)mod, 0, 0, 0);
        Win32.keybd_event((byte)mainKey, 0, 0, 0);
        Win32.keybd_event((byte)mainKey, 0, Win32.KEYEVENTF_KEYUP, 0);
        foreach (var mod in modifiers) Win32.keybd_event((byte)mod, 0, Win32.KEYEVENTF_KEYUP, 0);
    }

    void Cleanup()
    {
        if (cleanedUp) return;
        cleanedUp = true;
        running = false;

        // Signal the clip loop to stop but don't block on Join — it's a background thread that
        // dies on process exit, and the cursor clip is released below (and by the OS on exit).
        clipRunning = false;
        try { runningTimer?.Stop(); runningTimer?.Dispose(); } catch { }
        try { Win32.ClipCursor(IntPtr.Zero); } catch (Exception ex) { Debug.WriteLine("Cleanup cursor: " + ex.Message); }
        try { if (targetPid != 0) AudioMuter.SetMute(targetPid, false); } catch (Exception ex) { Debug.WriteLine("Cleanup unmute: " + ex.Message); }
        try
        {
            if (hasOriginalState && targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
            {
                Win32.SetWindowLong(targetHwnd, Win32.GWL_STYLE, originalStyle);
                Win32.SetWindowLong(targetHwnd, Win32.GWL_EXSTYLE, originalExStyle);
                int x = originalRect.Left, y = originalRect.Top;
                int w = originalRect.Right - originalRect.Left, h = originalRect.Bottom - originalRect.Top;
                Win32.SetWindowPos(targetHwnd, Win32.HWND_NOTOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
            }
            else if (targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
                Win32.SetWindowPos(targetHwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
        catch (Exception ex) { Debug.WriteLine("Cleanup restore: " + ex.Message); }
        try { blackBg?.Close(); blackBg?.Dispose(); blackBg = null; } catch (Exception ex) { Debug.WriteLine("Cleanup blackbg: " + ex.Message); }
        try { gamepadUiTimer?.Stop(); gamepadUiTimer?.Dispose(); } catch { }
        try { GamepadEmulator.Stop(); } catch (Exception ex) { Debug.WriteLine("Cleanup gamepad: " + ex.Message); }
        try { if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; } } catch (Exception ex) { Debug.WriteLine("Cleanup tray: " + ex.Message); }
        try { trayMenu?.Dispose(); trayMenu = null; } catch (Exception ex) { Debug.WriteLine("Cleanup trayMenu: " + ex.Message); }
        try { Win32.UnregisterHotKey(Handle, HOTKEY_ID); } catch (Exception ex) { Debug.WriteLine("Cleanup hotkey: " + ex.Message); }
        try { webServer?.Dispose(); webServer = null; } catch (Exception ex) { Debug.WriteLine("Cleanup webserver: " + ex.Message); }
        // Only settings are saved here; profiles are already persisted on every change
        // (apply / favorite / edit / delete), so re-writing the profiles file on close is
        // redundant and was causing churn.
        try { SaveSettings(); } catch (Exception ex) { Debug.WriteLine("Cleanup save: " + ex.Message); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Close (X) / Alt+F4 hides to tray instead of exiting when tray mode is on.
        // The tray "Exit" menu sets `exiting` first so it still quits for real.
        if (!exiting && e.CloseReason == CloseReason.UserClosing && trayMode)
        {
            e.Cancel = true;
            Hide();
            if (trayIcon != null) trayIcon.Visible = true;
            return;
        }
        Cleanup();
        base.OnFormClosing(e);
    }
}
