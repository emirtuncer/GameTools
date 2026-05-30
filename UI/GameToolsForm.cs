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


    // Original window state
    Win32.RECT originalRect;
    int originalStyle, originalExStyle;
    bool hasOriginalState;
    readonly object _appliedPidsLock = new();
    HashSet<uint> appliedPids = [];
    Dictionary<string, GameProfile> profiles = new(StringComparer.OrdinalIgnoreCase);

    // Hotkey
    uint hkMod = Win32.MOD_CONTROL | Win32.MOD_ALT, hkVk = 0x47;
    string hkLabel = "Ctrl+Alt+G";
    const int HOTKEY_ID = 9001;

    // Controls
    Label lblTargetTitle = null!, lblTargetInfo = null!, lblStatus = null!, lblHotkey = null!;
    Button btnCapture = null!;
    CheckBox chkCustomRes = null!, chkCenter = null!, chkClip = null!, chkBorder = null!, chkBlackBg = null!, chkMute = null!, chkTrayMode = null!;
    TextBox txtResW = null!, txtResH = null!;
    ListView profileList = null!;

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

    public GameToolsForm()
    {
        LoadSettings();
        LoadProfiles();
        LoadButtonProfiles();
        BuildUI();
        WindowState = FormWindowState.Minimized;
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
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BG;
        ForeColor = Theme.FG;
        Font = Theme.Normal;
        AutoScaleMode = AutoScaleMode.Dpi;

        const int contentW = 376;

        lblTargetTitle = Theme.MakeLabel("(no window selected)", bold: true);
        lblTargetInfo = Theme.MakeLabel("Click Capture or press hotkey", Theme.Dim);

        btnCapture = Theme.MakeAccentButton("Capture Window  (5s delay)", contentW);
        btnCapture.Click += (_, _) => StartCapture();

        chkCustomRes = Theme.MakeCheck("Resize to:");
        txtResW = Theme.MakeTextBox(Constants.DefaultResW.ToString());
        txtResH = Theme.MakeTextBox(Constants.DefaultResH.ToString());
        chkCenter = Theme.MakeCheck("Center on monitor"); chkCenter.Checked = true;
        chkClip = Theme.MakeCheck("Clip cursor to window"); chkClip.Checked = true;
        chkBorder = Theme.MakeCheck("Remove window border");
        chkBlackBg = Theme.MakeCheck("Black background (hide desktop)");
        chkMute = Theme.MakeCheck("Mute audio when in background");
        profileList = Theme.MakeListView(356, 95);
        profileList.Columns.Add("\u2605", 28);
        profileList.Columns.Add("Process", 140);
        profileList.Columns.Add("Settings", 180);
        profileList.DoubleClick += (_, _) => LoadSelectedProfile();

        var btnSave = Theme.MakeButton("Save Current"); btnSave.Click += (_, _) => SaveCurrentProfile();
        var btnFav = Theme.MakeButton("Favorite"); btnFav.Click += (_, _) => ToggleFavorite();
        var btnLoad = Theme.MakeButton("Load"); btnLoad.Click += (_, _) => LoadSelectedProfile();
        var btnDelete = Theme.MakeButton("Delete"); btnDelete.Click += (_, _) => DeleteSelectedProfile();
        var lblProfHint = Theme.MakeLabel("Favorited profiles auto-apply when game launches", Theme.Dim, 8);

        var lblHkCapture = Theme.MakeLabel("Capture + Apply:");
        lblHotkey = Theme.MakeLabel(hkLabel, Theme.Yellow, 11, bold: true);
        var btnChangeHk = Theme.MakeButton("Change", 80); btnChangeHk.Click += (_, _) => ChangeHotkey();
        var lblHkHint = Theme.MakeLabel("Press hotkey anywhere to start 5s capture + apply", Theme.Dim, 8);

        var btnRelease = Theme.MakeButton("Release", 118); btnRelease.Click += (_, _) => Release();

        var chkStartup = Theme.MakeCheck("Start with Windows");
        chkStartup.ForeColor = Theme.Dim;
        chkStartup.Font = Theme.Small;
        chkStartup.Checked = IsStartupEnabled();
        chkStartup.CheckedChanged += (_, _) => SetStartupEnabled(chkStartup.Checked);

        chkTrayMode = Theme.MakeCheck("Minimize to Tray");
        chkTrayMode.ForeColor = Theme.Dim;
        chkTrayMode.Font = Theme.Small;

        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => { if (trayIcon != null) trayIcon.Visible = false; Close(); });
        trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = Text,
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        // Virtual Gamepad
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

        lblStatus = new Label
        {
            Text = "Ready",
            Size = new Size(contentW, 26),
            BackColor = Theme.BG2,
            ForeColor = Theme.Green,
            Font = Theme.Normal,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };

        Flex.Apply(this, gap: 12, padding: new Padding(12),
            Flex.Group("Target Window", Theme.Bold, Theme.Accent, 4, contentW,
                lblTargetTitle,
                lblTargetInfo),
            btnCapture,
            Flex.Group("Options", Theme.Bold, Theme.Accent, 4, contentW,
                Flex.Row(8, chkCustomRes, txtResW, Theme.MakeLabel("x"), txtResH),
                chkCenter, chkClip, chkBorder, chkBlackBg, chkMute),
            GamepadPluginGroup(contentW),
            Flex.Group("Game Profiles", Theme.Bold, Theme.Accent, 8, contentW,
                profileList,
                Flex.Row(6, btnSave, btnFav, btnLoad, btnDelete),
                lblProfHint),
            Flex.Group("Hotkey", Theme.Bold, Theme.Accent, 4, contentW,
                Flex.Row(8, lblHkCapture, lblHotkey, btnChangeHk),
                lblHkHint),
            btnRelease,
            Flex.Row(8, chkStartup, chkTrayMode),
            lblStatus
        );

        ApplyUiSettings();
        RefreshProfileList();
        if (profiles.Count > 0)
            lblStatus.Text = $"Loaded {profiles.Count} profile(s)";
    }

    void Status(string text)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { Invoke(() => { if (!IsDisposed) lblStatus.Text = text; }); }
            catch (ObjectDisposedException) { }
        }
        else lblStatus.Text = text;
    }

    const string StartupRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string StartupValueName = "GameTools";

    static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
            return key?.GetValue(StartupValueName) != null;
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
                key.SetValue(StartupValueName, $"\"{Application.ExecutablePath}\"");
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
        if (s.TryGetValue("center", out var v)) chkCenter.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("clip", out v)) chkClip.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("remove_border", out v)) chkBorder.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("black_bg", out v)) chkBlackBg.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("mute_bg", out v)) chkMute.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("tray_mode", out v)) chkTrayMode.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("custom_res", out v)) chkCustomRes.Checked = Convert.ToBoolean(v);
        if (s.TryGetValue("res_w", out v)) txtResW.Text = v.ToString();
        if (s.TryGetValue("res_h", out v)) txtResH.Text = v.ToString();
        ApplyGamepadSettingsToUi(GamepadSettings.FromDict(s));
    }

    void SaveSettings()
    {
        SimpleJson.SaveSettings(SettingsPath, new Dictionary<string, object>
        {
            ["hk_mod"] = (int)hkMod, ["hk_vk"] = (int)hkVk, ["hk_label"] = hkLabel,
            ["center"] = chkCenter.Checked, ["clip"] = chkClip.Checked,
            ["remove_border"] = chkBorder.Checked, ["black_bg"] = chkBlackBg.Checked,
            ["mute_bg"] = chkMute.Checked,
            ["tray_mode"] = chkTrayMode.Checked,
            ["custom_res"] = chkCustomRes.Checked,
            ["res_w"] = txtResW.Text, ["res_h"] = txtResH.Text,
            ["gp_wasd"] = chkWasd.Checked, ["gp_ramp_up"] = trkRampUp.Value,
            ["gp_ramp_down"] = trkRampDown.Value, ["gp_mouse"] = chkMouse.Checked,
            ["gp_sensitivity"] = trkSensitivity.Value,
            ["gp_invert_rx"] = chkInvertRX.Checked, ["gp_invert_ry"] = chkInvertRY.Checked
        });
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

    void RefreshProfileList()
    {
        profileList.Items.Clear();
        foreach (var kv in profiles.OrderBy(p => p.Key))
        {
            var item = new ListViewItem(kv.Value.Favorite ? "\u2605" : "");
            item.SubItems.Add(kv.Key);
            item.SubItems.Add(kv.Value.Summary());
            item.Tag = kv.Key;
            profileList.Items.Add(item);
        }
    }

    GameProfile GetCurrentOpts() => new()
    {
        Center = chkCenter.Checked, Clip = chkClip.Checked, RemoveBorder = chkBorder.Checked,
        BlackBg = chkBlackBg.Checked, MuteBg = chkMute.Checked,
        CustomRes = chkCustomRes.Checked,
        ResW = int.TryParse(txtResW.Text, out int w) ? w : Constants.DefaultResW,
        ResH = int.TryParse(txtResH.Text, out int h) ? h : Constants.DefaultResH,
        Gamepad = GetGamepadSettings()
    };

    void ApplyProfileToUi(GameProfile p)
    {
        chkCenter.Checked = p.Center; chkClip.Checked = p.Clip; chkBorder.Checked = p.RemoveBorder;
        chkBlackBg.Checked = p.BlackBg; chkMute.Checked = p.MuteBg;
        chkCustomRes.Checked = p.CustomRes;
        txtResW.Text = p.ResW.ToString(); txtResH.Text = p.ResH.ToString();
        ApplyGamepadSettingsToUi(p.Gamepad);
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

    void SaveCurrentProfile()
    {
        if (string.IsNullOrEmpty(targetProcess)) { Status("Capture a window first"); return; }
        var p = GetCurrentOpts();
        if (profiles.TryGetValue(targetProcess, out var existing)) p.Favorite = existing.Favorite;
        profiles[targetProcess] = p;
        SaveProfiles(); RefreshProfileList();

        try
        {
            string saved = File.ReadAllText(ProfilesPath);
            Status(saved.Contains(targetProcess) ? $"Profile saved: {targetProcess}" : "Save failed - profile not found in file!");
        }
        catch (Exception ex) { Status("Save error: " + ex.Message); }
    }

    void ToggleFavorite()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        bool nowFav = profiles[exe].Favorite = !profiles[exe].Favorite;
        SaveProfiles(); RefreshProfileList();
        Status($"{exe} {(nowFav ? "favorited" : "unfavorited")}");

        // Auto-apply immediately if the favorited window is already open
        if (!nowFav) return;
        try
        {
            var win = WindowHelper.GetWindows()
                .Where(w => w.Width > Constants.MinWindowSize && w.Height > Constants.MinWindowSize)
                .FirstOrDefault(w => string.Equals(w.Process, exe, StringComparison.OrdinalIgnoreCase));
            if (win == null) return;
            lock (_appliedPidsLock) appliedPids.Add(win.Pid);
            ApplyProfileActions(win.Hwnd, profiles[exe]);
        }
        catch (Exception ex) { Debug.WriteLine("Favorite auto-apply: " + ex.Message); }
    }

    void LoadSelectedProfile()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        ApplyProfileToUi(profiles[exe]);
        Status($"Loaded settings from {exe}");
    }

    void DeleteSelectedProfile()
    {
        string? exe = SelectedExe(); if (exe == null) return;
        profiles.Remove(exe);
        SaveProfiles(); RefreshProfileList();
        Status($"Deleted: {exe}");
    }

    string? SelectedExe()
    {
        if (profileList.SelectedItems.Count == 0) { Status("Select a profile first"); return null; }
        return profileList.SelectedItems[0].Tag as string;
    }

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
            targetHwnd = hwnd;
            SaveOriginalState(hwnd);
            var info = WindowHelper.GetInfo(hwnd);
            targetProcess = info.Process;
            targetPid = info.Pid;
            UpdateTargetDisplay();
            if (profiles.TryGetValue(targetProcess, out var profile)) ApplyProfileToUi(profile);
            ApplyActions();
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
            lblTargetTitle.Text = "(no window selected)";
            lblTargetInfo.Text = "";
            return;
        }
        var info = WindowHelper.GetInfo(targetHwnd);
        var mi = WindowHelper.GetMonitor(targetHwnd);
        int mw = mi.rcMonitor.Right - mi.rcMonitor.Left, mh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        lblTargetTitle.Text = string.IsNullOrEmpty(info.Title) ? "(untitled)" : info.Title;
        lblTargetInfo.Text = $"{info.Process}  |  {info.Width}x{info.Height}  |  Monitor {mw}x{mh}";
    }

    void ApplyActions()
    {
        if (targetHwnd == IntPtr.Zero || !Win32.IsWindow(targetHwnd)) { Status("No target window"); return; }
        var actions = new List<string>();

        if (chkBorder.Checked) { WindowHelper.RemoveBorder(targetHwnd); Thread.Sleep(Constants.ActionSettleMs); actions.Add("border removed"); }

        if (chkCustomRes.Checked || chkCenter.Checked)
        {
            int? tw = null, th = null;
            if (chkCustomRes.Checked)
            {
                if (!int.TryParse(txtResW.Text, out int w2) || !int.TryParse(txtResH.Text, out int h2)) { Status("Invalid resolution"); return; }
                if (w2 <= 0 || h2 <= 0 || w2 > 15360 || h2 > 8640) { Status("Resolution out of range"); return; }
                tw = w2; th = h2;
            }
            WindowHelper.Center(targetHwnd, tw, th);
            actions.Add(chkCenter.Checked ? "centered" : "resized");
            Thread.Sleep(Constants.ActionSettleMs);
        }

        if (chkClip.Checked) { StartMonitoring(); actions.Add("clip"); }
        if (chkBlackBg.Checked) { ShowBlackBg(); actions.Add("blackbg"); }
        UpdateTargetDisplay();
        SaveSettings();

        if (!string.IsNullOrEmpty(targetProcess))
        {
            var p = GetCurrentOpts();
            if (profiles.TryGetValue(targetProcess, out var existing)) p.Favorite = existing.Favorite;
            profiles[targetProcess] = p;
            SaveProfiles(); RefreshProfileList();
            Status(actions.Count > 0
                ? $"Applied: {string.Join(", ", actions)} · profile saved: {targetProcess}"
                : $"Profile saved: {targetProcess}");
        }
        else
        {
            Status(actions.Count > 0 ? "Applied: " + string.Join(", ", actions) : "No actions selected");
        }
    }

    void ApplyProfileActions(IntPtr hwnd, GameProfile p)
    {
        if (InvokeRequired) { Invoke(() => ApplyProfileActions(hwnd, p)); return; }

        targetHwnd = hwnd;
        SaveOriginalState(hwnd);
        var info = WindowHelper.GetInfo(hwnd);
        targetProcess = info.Process;
        targetPid = info.Pid;
        ApplyProfileToUi(p);

        var actions = new List<string>();
        if (p.RemoveBorder) { WindowHelper.RemoveBorder(hwnd); Thread.Sleep(Constants.ActionSettleMs); actions.Add("border"); }

        int? tw = null, th = null;
        if (p.CustomRes) { tw = p.ResW; th = p.ResH; }
        if (p.Center) { WindowHelper.Center(hwnd, tw, th); actions.Add("centered"); Thread.Sleep(Constants.ActionSettleMs); }

        if (p.Clip) { StartMonitoring(); actions.Add("clip"); }
        if (p.BlackBg) { ShowBlackBg(); actions.Add("blackbg"); }
        if (p.Gamepad.Enabled && vigemAvailable) { chkGamepad.Checked = true; actions.Add("gamepad"); }
        UpdateTargetDisplay();
        Status($"Auto-applied: {info.Process} ({string.Join(", ", actions)})");
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
            UpdateTargetDisplay();
        }
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
        bool useBlack = chkBlackBg.Checked, useMute = chkMute.Checked;
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

            var favs = profiles.Where(p => p.Value.Favorite).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && chkTrayMode?.Checked == true)
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

    void ChangeHotkey()
    {
        Win32.UnregisterHotKey(Handle, HOTKEY_ID);

        using var dlg = new Form
        {
            Text = "Set Hotkey", Size = new Size(340, 140), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.BG, MaximizeBox = false, MinimizeBox = false
        };
        var lbl = new Label { Text = "Press a key combination...", ForeColor = Theme.FG, Font = new Font("Segoe UI", 13), Location = new Point(20, 15), AutoSize = true };
        var hint = new Label { Text = "(Ctrl / Alt / Shift + a key)", ForeColor = Theme.Dim, Font = Theme.Normal, Location = new Point(20, 50), AutoSize = true };
        dlg.Controls.Add(lbl); dlg.Controls.Add(hint);

        uint newMod = 0, newVk = 0; string? newLabel = null;
        dlg.KeyPreview = true;
        dlg.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            uint mod = 0; var parts = new List<string>();
            if (e.Control) { mod |= Win32.MOD_CONTROL; parts.Add("Ctrl"); }
            if (e.Alt) { mod |= Win32.MOD_ALT; parts.Add("Alt"); }
            if (e.Shift) { mod |= Win32.MOD_SHIFT; parts.Add("Shift"); }
            if (mod == 0) { hint.Text = "Need at least one modifier!"; hint.ForeColor = Color.FromArgb(243, 139, 168); return; }
            parts.Add(e.KeyCode.ToString());
            newMod = mod; newVk = (uint)e.KeyCode; newLabel = string.Join("+", parts);
            dlg.DialogResult = DialogResult.OK;
        };

        if (dlg.ShowDialog(this) == DialogResult.OK && newLabel != null)
        {
            hkMod = newMod; hkVk = newVk; hkLabel = newLabel;
            lblHotkey.Text = hkLabel;
            Status($"Hotkey changed to {hkLabel}");
            SaveSettings();
        }
        else Status("Hotkey change cancelled");

        Win32.RegisterHotKey(Handle, HOTKEY_ID, hkMod | Win32.MOD_NOREPEAT, hkVk);
    }

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
                    var d = new Dictionary<string, object>
                    {
                        ["center"] = chkCenter.Checked, ["clip"] = chkClip.Checked,
                        ["remove_border"] = chkBorder.Checked, ["black_bg"] = chkBlackBg.Checked,
                        ["mute_bg"] = chkMute.Checked, ["custom_res"] = chkCustomRes.Checked,
                        ["res_w"] = txtResW.Text, ["res_h"] = txtResH.Text,
                        ["hk_label"] = hkLabel
                    };
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
        if (dict.TryGetValue("center", out var v)) chkCenter.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("clip", out v)) chkClip.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("remove_border", out v)) chkBorder.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("black_bg", out v)) chkBlackBg.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("mute_bg", out v)) chkMute.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("custom_res", out v)) chkCustomRes.Checked = Convert.ToBoolean(v);
        if (dict.TryGetValue("res_w", out v)) txtResW.Text = v.ToString();
        if (dict.TryGetValue("res_h", out v)) txtResH.Text = v.ToString();
        SaveSettings();
        Status("Settings updated from remote");
    }

    void UpdateProfileFromRemote(string exe, GameProfile profile)
    {
        profiles[exe] = profile;
        SaveProfiles();
        RefreshProfileList();
        Status($"Profile updated from remote: {exe}");
    }

    void DeleteProfileFromRemote(string exe)
    {
        profiles.Remove(exe);
        SaveProfiles();
        RefreshProfileList();
        Status($"Profile deleted from remote: {exe}");
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
                if (chkBlackBg.Checked) { chkBlackBg.Checked = false; HideBlackBg(); }
                else { chkBlackBg.Checked = true; if (targetHwnd != IntPtr.Zero) ShowBlackBg(); }
                Status(chkBlackBg.Checked ? "Black BG on (remote)" : "Black BG off (remote)");
                break;
            case "remove_border":
                if (targetHwnd != IntPtr.Zero && Win32.IsWindow(targetHwnd))
                {
                    chkBorder.Checked = !chkBorder.Checked;
                    if (chkBorder.Checked) WindowHelper.RemoveBorder(targetHwnd);
                }
                Status(chkBorder.Checked ? "Border removed (remote)" : "Border toggle (remote)");
                break;
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

        try { clipRunning = false; clipThread?.Join(Constants.ThreadJoinMs); } catch (Exception ex) { Debug.WriteLine("Cleanup clip: " + ex.Message); }
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
        try { SaveSettings(); SaveProfiles(); } catch (Exception ex) { Debug.WriteLine("Cleanup save: " + ex.Message); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { Cleanup(); base.OnFormClosing(e); }
}
