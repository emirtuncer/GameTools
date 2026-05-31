using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GameTools.Data;

namespace GameTools.Core;

public class WebServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly CancellationTokenSource _cts = new();
    Thread? _thread;
    Thread? _discoveryThread;
    UdpClient? _udp;

    // Callbacks wired by GameToolsForm
    public Func<Dictionary<string, object>>? GetSettings { get; set; }
    public Action<Dictionary<string, object>>? UpdateSettings { get; set; }
    public Func<Dictionary<string, GameProfile>>? GetProfiles { get; set; }
    public Action<string, GameProfile>? UpdateProfile { get; set; }
    public Action<string>? DeleteProfile { get; set; }
    public Func<string?>? GetCurrentGame { get; set; }
    public Func<bool>? IsTargetAlive { get; set; }
    public Action<string>? ExecuteAction { get; set; }
    public Func<Dictionary<string, List<ButtonSlot>>>? GetButtonProfiles { get; set; }
    public Action<string, List<ButtonSlot>>? UpdateButtonProfile { get; set; }

    public void Start(int port = 9876)
    {
        _listener.Prefixes.Add($"http://+:{port}/");
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Fallback to localhost only if no URL reservation
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
        }
        _thread = new Thread(Listen) { IsBackground = true, Name = "WebServer" };
        _thread.Start();
        _discoveryThread = new Thread(() => DiscoveryLoop(port)) { IsBackground = true, Name = "Discovery" };
        _discoveryThread.Start();
        Debug.WriteLine($"WebServer started on port {port}, discovery on UDP 47777");
    }

    void Listen()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var ctx = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Debug.WriteLine("WebServer error: " + ex.Message); }
        }
    }

    void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.ContentType = "application/json; charset=utf-8";
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            string path = req.Url?.AbsolutePath ?? "/";
            string method = req.HttpMethod;
            string body = "";
            if (req.HasEntityBody)
                using (var reader = new System.IO.StreamReader(req.InputStream, req.ContentEncoding))
                    body = reader.ReadToEnd();

            string json;

            switch (path)
            {
                case "/status":
                    json = HandleStatus();
                    break;
                case "/action" when method == "POST":
                    json = HandleAction(body);
                    break;
                case "/ui" when method == "GET":
                    json = HandleGetUI();
                    break;
                case "/settings" when method == "GET":
                    json = HandleGetSettings();
                    break;
                case "/settings" when method == "PUT":
                    json = HandleUpdateSettings(body);
                    break;
                case "/profiles" when method == "GET":
                    json = HandleGetProfiles();
                    break;
                case "/buttons" when method == "GET":
                    json = HandleGetButtons();
                    break;
                default:
                    // /profiles/{exe} and /buttons/{exe}
                    var profileMatch = Regex.Match(path, @"^/profiles/(.+)$");
                    var buttonMatch = Regex.Match(path, @"^/buttons/(.+)$");

                    if (profileMatch.Success)
                    {
                        string exe = Uri.UnescapeDataString(profileMatch.Groups[1].Value);
                        json = method switch
                        {
                            "PUT" => HandleUpdateProfile(exe, body),
                            "DELETE" => HandleDeleteProfile(exe),
                            _ => Error(405, "Method not allowed")
                        };
                    }
                    else if (buttonMatch.Success && method == "PUT")
                    {
                        string exe = Uri.UnescapeDataString(buttonMatch.Groups[1].Value);
                        json = HandleUpdateButtons(exe, body);
                    }
                    else
                    {
                        res.StatusCode = 404;
                        json = "{\"error\":\"not found\"}";
                    }
                    break;
            }

            byte[] buf = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("WebServer request error: " + ex.Message);
            try
            {
                res.StatusCode = 500;
                byte[] err = Encoding.UTF8.GetBytes($"{{\"error\":\"{Escape(ex.Message)}\"}}");
                res.ContentLength64 = err.Length;
                res.OutputStream.Write(err, 0, err.Length);
            }
            catch { }
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    string HandleStatus()
    {
        string? game = GetCurrentGame?.Invoke();
        bool alive = IsTargetAlive?.Invoke() ?? false;
        string hostname = System.Environment.MachineName;
        return $"{{\"connected\":true,\"capturing\":{(alive ? "true" : "false")},\"game\":{(game != null ? $"\"{Escape(game)}\"" : "null")},\"hostname\":\"{Escape(hostname)}\"}}";
    }

    string HandleGetUI()
    {
        string? game = GetCurrentGame?.Invoke();
        bool alive = IsTargetAlive?.Invoke() ?? false;
        var settings = GetSettings?.Invoke() ?? new Dictionary<string, object>();
        var allButtons = GetButtonProfiles?.Invoke();

        var sb = new StringBuilder();
        sb.Append($"{{\"game\":{(game != null ? $"\"{Escape(game)}\"" : "null")},\"sections\":[");

        int sectionIdx = 0;

        // Section 1: Status (only when capturing)
        if (game != null && alive)
        {
            if (sectionIdx > 0) sb.Append(',');
            sb.Append("{\"id\":\"status\",\"title\":\"Status\",\"icon\":\"info.circle.fill\",\"color\":\"green\",\"controls\":[");
            sb.Append($"{{\"id\":\"status_game\",\"type\":\"label\",\"label\":\"Captured\",\"icon\":\"gamecontroller.fill\",\"value\":\"{Escape(game)}\"}}");
            sb.Append("]}");
            sectionIdx++;
        }

        // Section 2: Game-specific buttons
        string buttonKey = game ?? "default";
        List<ButtonSlot>? buttons = null;
        if (allButtons != null)
        {
            if (!allButtons.TryGetValue(buttonKey, out buttons))
                allButtons.TryGetValue("default", out buttons);
        }
        if (buttons != null && buttons.Count > 0)
        {
            if (sectionIdx > 0) sb.Append(',');
            string title = game ?? "Actions";
            // Clean up exe name for display
            if (title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                title = title[..^4];
            foreach (var suffix in new[] { "-Win64-Shipping", "Steam-Win64", "Steam" })
                if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    title = title[..^suffix.Length];

            sb.Append($"{{\"id\":\"game_actions\",\"title\":\"{Escape(title)}\",\"icon\":\"gamecontroller.fill\",\"color\":\"blue\",\"columns\":2,\"controls\":[");
            for (int i = 0; i < buttons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var b = buttons[i];
                sb.Append($"{{\"id\":\"{Escape(b.Id)}\",\"type\":\"button\",\"label\":\"{Escape(b.Label)}\",\"icon\":\"{Escape(b.Icon)}\",\"action\":\"{Escape(b.Id)}\"}}");
            }
            sb.Append("]}");
            sectionIdx++;
        }

        // Section 3: Options (toggles from settings)
        if (sectionIdx > 0) sb.Append(',');
        sb.Append("{\"id\":\"options\",\"title\":\"Options\",\"icon\":\"gearshape.fill\",\"color\":\"mauve\",\"controls\":[");
        var toggles = new (string id, string label, string icon, string key)[]
        {
            ("opt_center", "Center Window", "arrow.up.left.and.arrow.down.right", "center"),
            ("opt_clip", "Clip Cursor", "cursorarrow.motionlines", "clip"),
            ("opt_border", "Remove Border", "rectangle.dashed", "remove_border"),
            ("opt_blackbg", "Black Background", "rectangle.fill", "black_bg"),
            ("opt_mute", "Mute on Background", "speaker.slash.fill", "mute_bg"),
        };
        for (int i = 0; i < toggles.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var t = toggles[i];
            bool val = settings.TryGetValue(t.key, out var v) && Convert.ToBoolean(v);
            sb.Append($"{{\"id\":\"{t.id}\",\"type\":\"toggle\",\"label\":\"{t.label}\",\"icon\":\"{t.icon}\",\"action\":\"{t.key}\",\"value\":{(val ? "true" : "false")}}}");
        }
        sb.Append("]}");
        sectionIdx++;

        // Section 4: Quick Actions
        if (sectionIdx > 0) sb.Append(',');
        sb.Append("{\"id\":\"quick\",\"title\":\"Quick Actions\",\"icon\":\"bolt.fill\",\"color\":\"green\",\"columns\":2,\"controls\":[");
        sb.Append("{\"id\":\"qa_capture\",\"type\":\"button\",\"label\":\"Capture\",\"icon\":\"camera.fill\",\"action\":\"capture\"},");
        sb.Append("{\"id\":\"qa_release\",\"type\":\"button\",\"label\":\"Release\",\"icon\":\"lock.open.fill\",\"action\":\"release\"}");
        sb.Append("]}");

        sb.Append("]}");
        return sb.ToString();
    }

    string HandleAction(string body)
    {
        // Support both "action" (new iOS app) and "id" (legacy/widget)
        string? id = ExtractString(body, "action") ?? ExtractString(body, "id");
        if (id == null) return Error(400, "missing action");
        ExecuteAction?.Invoke(id);
        return "{\"ok\":true}";
    }

    string HandleGetSettings()
    {
        var settings = GetSettings?.Invoke();
        if (settings == null) return "{}";
        return DictToJson(settings);
    }

    string HandleUpdateSettings(string body)
    {
        var dict = ParseFlatJson(body);
        if (dict.Count == 0) return Error(400, "empty body");
        UpdateSettings?.Invoke(dict);
        return "{\"ok\":true}";
    }

    string HandleGetProfiles()
    {
        var profiles = GetProfiles?.Invoke();
        if (profiles == null) return "{}";

        var sb = new StringBuilder("{");
        int i = 0;
        foreach (var kv in profiles)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"{Escape(kv.Key)}\":");
            sb.Append(DictToJson(kv.Value.ToDict()));
            i++;
        }
        sb.Append('}');
        return sb.ToString();
    }

    string HandleUpdateProfile(string exe, string body)
    {
        var dict = ParseFlatJson(body);
        if (dict.Count == 0) return Error(400, "empty body");
        var profile = GameProfile.FromDict(dict);
        UpdateProfile?.Invoke(exe, profile);
        return "{\"ok\":true}";
    }

    string HandleDeleteProfile(string exe)
    {
        DeleteProfile?.Invoke(exe);
        return "{\"ok\":true}";
    }

    string HandleGetButtons()
    {
        var allButtons = GetButtonProfiles?.Invoke();
        string? game = GetCurrentGame?.Invoke();
        if (allButtons == null) return "{\"game\":null,\"buttons\":[]}";

        string key = game ?? "default";
        if (!allButtons.TryGetValue(key, out var buttons))
            allButtons.TryGetValue("default", out buttons);

        var sb = new StringBuilder();
        sb.Append($"{{\"game\":{(game != null ? $"\"{Escape(game)}\"" : "null")},\"buttons\":[");
        if (buttons != null)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(buttons[i].ToJson());
            }
        }
        sb.Append("]}");
        return sb.ToString();
    }

    string HandleUpdateButtons(string exe, string body)
    {
        var slots = ButtonSlot.ParseArray(body);
        if (slots.Count == 0) return Error(400, "empty or invalid buttons array");
        UpdateButtonProfile?.Invoke(exe, slots);
        return "{\"ok\":true}";
    }

    // Helpers

    static string Error(int code, string msg) => $"{{\"error\":\"{Escape(msg)}\"}}";

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    static string? ExtractString(string json, string key)
    {
        var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
    }

    static Dictionary<string, object> ParseFlatJson(string json)
    {
        var d = new Dictionary<string, object>();
        json = json.Trim().TrimStart('{').TrimEnd('}');
        foreach (Match kv in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)"))
        {
            string k = kv.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            string v = kv.Groups[2].Value.Trim().Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (v == "true") d[k] = true;
            else if (v == "false") d[k] = false;
            else if (int.TryParse(v, out int n)) d[k] = n;
            else d[k] = v;
        }
        return d;
    }

    static string DictToJson(Dictionary<string, object> d)
    {
        var sb = new StringBuilder("{");
        int i = 0;
        foreach (var kv in d)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"{Escape(kv.Key)}\":");
            if (kv.Value is bool b) sb.Append(b ? "true" : "false");
            else if (kv.Value is int or long or float or double) sb.Append(kv.Value);
            else sb.Append($"\"{Escape(kv.Value?.ToString() ?? "")}\"");
            i++;
        }
        sb.Append('}');
        return sb.ToString();
    }

    void DiscoveryLoop(int httpPort)
    {
        try
        {
            using var udp = new UdpClient(47777);
            _udp = udp; // tracked so Dispose() can close it and unblock the blocking Receive() below
            udp.EnableBroadcast = true;
            var response = Encoding.UTF8.GetBytes($"GAMETOOLS_SERVER:{httpPort}");

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    var data = udp.Receive(ref remoteEP);
                    var msg = Encoding.UTF8.GetString(data);
                    if (msg == "GAMETOOLS_DISCOVER")
                    {
                        udp.Send(response, response.Length, remoteEP);
                        Debug.WriteLine($"Discovery: responded to {remoteEP}");
                    }
                }
                catch (SocketException) when (_cts.IsCancellationRequested) { break; }
                catch (Exception ex) { Debug.WriteLine($"Discovery error: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Discovery failed to start: {ex.Message}"); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _udp?.Close(); } catch { } // unblocks the discovery thread stuck in udp.Receive()
        // Both threads are now unblocked (listener via Stop/Close, discovery via udp.Close),
        // so these joins return in milliseconds instead of waiting out the timeout.
        _thread?.Join(200);
        _discoveryThread?.Join(200);
        try { _cts.Dispose(); } catch { }
    }
}
