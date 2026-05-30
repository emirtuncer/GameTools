namespace GameTools.Data;

public class GameProfile
{
    public bool Favorite { get; set; }
    public bool Center { get; set; } = true;
    public bool Clip { get; set; } = true;
    public bool RemoveBorder { get; set; }
    public bool BlackBg { get; set; }
    public bool MuteBg { get; set; }
    public bool CustomRes { get; set; }
    public int ResW { get; set; } = GameTools.Core.Constants.DefaultResW;
    public int ResH { get; set; } = GameTools.Core.Constants.DefaultResH;
    public GamepadSettings Gamepad { get; set; } = new();

    public string Summary()
    {
        var p = new List<string>();
        if (CustomRes) p.Add($"{ResW}x{ResH}");
        if (Center) p.Add("center");
        if (Clip) p.Add("clip");
        if (RemoveBorder) p.Add("noborder");
        if (BlackBg) p.Add("blackbg");
        if (MuteBg) p.Add("mute");
        if (Gamepad.Enabled) p.Add("gamepad");
        return p.Count > 0 ? string.Join(", ", p) : "default";
    }

    public Dictionary<string, object> ToDict()
    {
        var d = new Dictionary<string, object>
        {
            ["favorite"] = Favorite, ["center"] = Center, ["clip"] = Clip,
            ["remove_border"] = RemoveBorder, ["black_bg"] = BlackBg, ["mute_bg"] = MuteBg,
            ["custom_res"] = CustomRes, ["res_w"] = ResW, ["res_h"] = ResH
        };
        foreach (var kv in Gamepad.ToDict()) d[kv.Key] = kv.Value;
        return d;
    }

    public static GameProfile FromDict(Dictionary<string, object> d)
    {
        var p = new GameProfile();
        if (d.TryGetValue("favorite", out var v)) p.Favorite = Convert.ToBoolean(v);
        if (d.TryGetValue("center", out v)) p.Center = Convert.ToBoolean(v);
        if (d.TryGetValue("clip", out v)) p.Clip = Convert.ToBoolean(v);
        if (d.TryGetValue("remove_border", out v)) p.RemoveBorder = Convert.ToBoolean(v);
        if (d.TryGetValue("black_bg", out v)) p.BlackBg = Convert.ToBoolean(v);
        if (d.TryGetValue("mute_bg", out v)) p.MuteBg = Convert.ToBoolean(v);
        if (d.TryGetValue("custom_res", out v)) p.CustomRes = Convert.ToBoolean(v);
        if (d.TryGetValue("res_w", out v)) p.ResW = Convert.ToInt32(v);
        if (d.TryGetValue("res_h", out v)) p.ResH = Convert.ToInt32(v);
        p.Gamepad = GamepadSettings.FromDict(d);
        return p;
    }
}
