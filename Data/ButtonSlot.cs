using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GameTools.Data;

public class ButtonSlot
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Type { get; set; } = "command"; // "command" or "hotkey"
    public string Target { get; set; } = "";      // action id for command, key combo for hotkey

    public string ToJson()
    {
        return $"{{\"id\":\"{Esc(Id)}\",\"label\":\"{Esc(Label)}\",\"icon\":\"{Esc(Icon)}\",\"type\":\"{Esc(Type)}\",\"target\":\"{Esc(Target)}\"}}";
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static List<ButtonSlot> ParseArray(string json)
    {
        var result = new List<ButtonSlot>();
        // Match each {...} object in the array
        foreach (Match m in Regex.Matches(json, @"\{([^}]*)\}"))
        {
            var slot = new ButtonSlot();
            var inner = m.Groups[1].Value;
            var id = ExtractVal(inner, "id"); if (id != null) slot.Id = id;
            var label = ExtractVal(inner, "label"); if (label != null) slot.Label = label;
            var icon = ExtractVal(inner, "icon"); if (icon != null) slot.Icon = icon;
            var type = ExtractVal(inner, "type"); if (type != null) slot.Type = type;
            var target = ExtractVal(inner, "target"); if (target != null) slot.Target = target;
            // Legacy: "keys" field maps to target for hotkey type
            var keys = ExtractVal(inner, "keys");
            if (keys != null) { slot.Target = keys; slot.Type = "hotkey"; }
            if (!string.IsNullOrEmpty(slot.Id)) result.Add(slot);
        }
        return result;
    }

    static string? ExtractVal(string json, string key)
    {
        var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
    }

    // Load all button profiles: { "default": [...], "GameExe.exe": [...] }
    public static Dictionary<string, List<ButtonSlot>> LoadAll(string path)
    {
        var result = new Dictionary<string, List<ButtonSlot>>(StringComparer.OrdinalIgnoreCase);
        if (!System.IO.File.Exists(path)) return result;
        try
        {
            string json = System.IO.File.ReadAllText(path);
            // Match top-level keys with array values
            foreach (Match m in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline))
            {
                string key = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                var slots = ParseArray(m.Groups[2].Value);
                if (slots.Count > 0) result[key] = slots;
            }
        }
        catch (Exception ex) { Debug.WriteLine("ButtonSlot.LoadAll failed: " + ex.Message); }
        return result;
    }

    public static void SaveAll(string path, Dictionary<string, List<ButtonSlot>> profiles)
    {
        try
        {
            var sb = new StringBuilder("{\n");
            int i = 0;
            foreach (var kv in profiles)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append($"  \"{Esc(kv.Key)}\": [\n");
                for (int j = 0; j < kv.Value.Count; j++)
                {
                    sb.Append("    " + kv.Value[j].ToJson());
                    if (j < kv.Value.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("  ]");
                i++;
            }
            sb.Append("\n}");
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex) { Debug.WriteLine("ButtonSlot.SaveAll failed: " + ex.Message); }
    }
}
