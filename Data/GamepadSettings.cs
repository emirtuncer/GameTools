using GameTools.Core;

namespace GameTools.Data;

public class GamepadSettings
{
    public bool Enabled { get; set; }
    public bool WasdEnabled { get; set; } = true;
    public int RampUpMs { get; set; } = Constants.DefaultRampUpMs;
    public int RampDownMs { get; set; } = Constants.DefaultRampDownMs;
    public bool MouseEnabled { get; set; } = true;
    public int MouseSensitivity { get; set; } = Constants.DefaultMouseSensitivity;
    public int MouseDecayMs { get; set; } = Constants.DefaultMouseDecayMs;
    public bool InvertRightX { get; set; }
    public bool InvertRightY { get; set; }

    public Dictionary<string, object> ToDict() => new()
    {
        ["gp_enabled"] = Enabled,
        ["gp_wasd"] = WasdEnabled,
        ["gp_ramp_up"] = RampUpMs,
        ["gp_ramp_down"] = RampDownMs,
        ["gp_mouse"] = MouseEnabled,
        ["gp_sensitivity"] = MouseSensitivity,
        ["gp_decay"] = MouseDecayMs,
        ["gp_invert_rx"] = InvertRightX,
        ["gp_invert_ry"] = InvertRightY,
    };

    public static GamepadSettings FromDict(Dictionary<string, object> d)
    {
        var s = new GamepadSettings();
        if (d.TryGetValue("gp_enabled", out var v)) s.Enabled = Convert.ToBoolean(v);
        if (d.TryGetValue("gp_wasd", out v)) s.WasdEnabled = Convert.ToBoolean(v);
        if (d.TryGetValue("gp_ramp_up", out v)) s.RampUpMs = Convert.ToInt32(v);
        if (d.TryGetValue("gp_ramp_down", out v)) s.RampDownMs = Convert.ToInt32(v);
        if (d.TryGetValue("gp_mouse", out v)) s.MouseEnabled = Convert.ToBoolean(v);
        if (d.TryGetValue("gp_sensitivity", out v)) s.MouseSensitivity = Convert.ToInt32(v);
        if (d.TryGetValue("gp_decay", out v)) s.MouseDecayMs = Convert.ToInt32(v);
        if (d.TryGetValue("gp_invert_rx", out v)) s.InvertRightX = Convert.ToBoolean(v);
        if (d.TryGetValue("gp_invert_ry", out v)) s.InvertRightY = Convert.ToBoolean(v);
        return s;
    }
}
