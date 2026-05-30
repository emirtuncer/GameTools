using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GameTools.Data;

public static class SimpleJson
{
    public static Dictionary<string, Dictionary<string, object>> LoadProfiles(string path)
    {
        var result = new Dictionary<string, Dictionary<string, object>>();
        if (!File.Exists(path)) return result;
        try
        {
            string json = File.ReadAllText(path).Trim().TrimStart('{').TrimEnd('}').Trim();
            if (string.IsNullOrEmpty(json)) return result;

            foreach (Match m in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\{([^}]*)\\}"))
            {
                string key = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                var dict = new Dictionary<string, object>();
                foreach (Match kv in Regex.Matches(m.Groups[2].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)"))
                {
                    string k = kv.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    string v = kv.Groups[2].Value.Trim().Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (v == "true") dict[k] = true;
                    else if (v == "false") dict[k] = false;
                    else if (int.TryParse(v, out int n)) dict[k] = n;
                    else dict[k] = v;
                }
                result[key] = dict;
            }
        }
        catch (Exception ex) { Debug.WriteLine("LoadProfiles failed: " + ex.Message); }
        return result;
    }

    public static void SaveProfiles(string path, Dictionary<string, GameProfile> profiles)
    {
        try
        {
            var lines = new List<string> { "{" };
            int i = 0;
            foreach (var kv in profiles)
            {
                var props = new List<string>();
                foreach (var p in kv.Value.ToDict())
                {
                    string v = p.Value is bool b ? b.ToString().ToLower() : p.Value.ToString()!;
                    if (p.Value is string) v = "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    props.Add($"    \"{p.Key}\": {v}");
                }
                string comma = (++i < profiles.Count) ? "," : "";
                lines.Add($"  \"{kv.Key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\": {{");
                lines.Add(string.Join(",\n", props));
                lines.Add("  }" + comma);
            }
            lines.Add("}");
            File.WriteAllText(path, string.Join("\n", lines));
        }
        catch (Exception ex) { Debug.WriteLine("SaveProfiles failed: " + ex.Message); }
    }

    public static Dictionary<string, object> LoadSettings(string path)
    {
        var result = new Dictionary<string, object>();
        if (!File.Exists(path)) return result;
        try
        {
            string json = File.ReadAllText(path).Trim().TrimStart('{').TrimEnd('}');
            foreach (Match kv in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)"))
            {
                string k = kv.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                string v = kv.Groups[2].Value.Trim().Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                if (v == "true") result[k] = true;
                else if (v == "false") result[k] = false;
                else if (int.TryParse(v, out int n)) result[k] = n;
                else result[k] = v;
            }
        }
        catch (Exception ex) { Debug.WriteLine("LoadSettings failed: " + ex.Message); }
        return result;
    }

    public static void SaveSettings(string path, Dictionary<string, object> d)
    {
        try
        {
            var lines = new List<string> { "{" };
            int i = 0;
            foreach (var kv in d)
            {
                string v = kv.Value is bool b ? b.ToString().ToLower() : kv.Value.ToString()!;
                if (kv.Value is string) v = "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                string comma = (++i < d.Count) ? "," : "";
                lines.Add($"  \"{kv.Key}\": {v}{comma}");
            }
            lines.Add("}");
            File.WriteAllText(path, string.Join("\n", lines));
        }
        catch (Exception ex) { Debug.WriteLine("SaveSettings failed: " + ex.Message); }
    }
}
