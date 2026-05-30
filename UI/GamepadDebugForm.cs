using GameTools.Core;

namespace GameTools.UI;

public class GamepadDebugForm : Form
{
    readonly ListView deviceList;
    readonly TextBox txtCaps;
    readonly TextBox txtReport;
    readonly Button btnScan, btnReadReport, btnProbeAll;
    string? selectedDevicePath;
    volatile bool reading;
    Thread? readThread;

    static readonly Dictionary<(ushort page, ushort usage), string> UsageNames = new()
    {
        [(0x0001, 0x0002)] = "Mouse",
        [(0x0001, 0x0006)] = "Keyboard",
        [(0x0001, 0x0080)] = "System Control",
        [(0x000C, 0x0001)] = "Consumer Control",
    };

    static string DescribeUsage(ushort page, ushort usage)
    {
        if (UsageNames.TryGetValue((page, usage), out var name)) return name;
        if (page >= 0xFF00) return $"Vendor 0x{page:X4}";
        return $"0x{page:X4}/0x{usage:X4}";
    }

    public GamepadDebugForm()
    {
        Text = "Gamepad Debug \u2014 Apex Pro Actuation";
        Size = new Size(700, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.BG;
        ForeColor = Theme.FG;
        Font = Theme.Normal;

        var lblDevices = new Label { Text = "Apex Pro HID Interfaces:", Location = new Point(12, 10), AutoSize = true, ForeColor = Theme.Accent, Font = Theme.Bold };
        Controls.Add(lblDevices);

        deviceList = new ListView
        {
            Location = new Point(12, 32), Size = new Size(660, 110),
            View = View.Details, FullRowSelect = true,
            BackColor = Theme.BG2, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Font = Theme.Normal
        };
        deviceList.Columns.Add("PID", 55);
        deviceList.Columns.Add("Type", 110);
        deviceList.Columns.Add("Values", 50);
        deviceList.Columns.Add("Buttons", 55);
        deviceList.Columns.Add("Report", 50);
        deviceList.Columns.Add("Path", 330);
        deviceList.SelectedIndexChanged += (_, _) => OnDeviceSelected();
        Controls.Add(deviceList);

        btnScan = Theme.MakeButton("Scan", 80);
        btnScan.Location = new Point(12, 148);
        btnScan.Click += (_, _) => ScanDevices();
        Controls.Add(btnScan);

        btnProbeAll = Theme.MakeButton("Probe All", 90);
        btnProbeAll.Location = new Point(100, 148);
        btnProbeAll.Click += (_, _) => ProbeAll();
        Controls.Add(btnProbeAll);

        btnReadReport = Theme.MakeButton("Start Reading", 110);
        btnReadReport.Location = new Point(198, 148);
        btnReadReport.Enabled = false;
        btnReadReport.Click += (_, _) => ToggleReading();

        var btnDumpFeatures = Theme.MakeButton("Dump Features", 110);
        btnDumpFeatures.Location = new Point(316, 148);
        btnDumpFeatures.Click += (_, _) => DumpFeatureReports();
        Controls.Add(btnDumpFeatures);
        Controls.Add(btnReadReport);

        var lblCaps = new Label { Text = "Device Capabilities:", Location = new Point(12, 185), AutoSize = true, ForeColor = Theme.Accent, Font = Theme.Bold };
        Controls.Add(lblCaps);

        txtCaps = new TextBox
        {
            Location = new Point(12, 207), Size = new Size(660, 90), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Theme.BG2, ForeColor = Theme.FG,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f)
        };
        Controls.Add(txtCaps);

        var lblReport = new Label { Text = "Raw HID Input Reports (press keys while reading):", Location = new Point(12, 305), AutoSize = true, ForeColor = Theme.Accent, Font = Theme.Bold };
        Controls.Add(lblReport);

        txtReport = new TextBox
        {
            Location = new Point(12, 327), Size = new Size(660, 190), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Theme.BG2, ForeColor = Theme.Green,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f)
        };
        Controls.Add(txtReport);

        ScanDevices();
    }

    void ScanDevices()
    {
        StopReading();
        deviceList.Items.Clear();
        txtCaps.Clear();
        txtReport.Clear();
        selectedDevicePath = null;
        btnReadReport.Enabled = false;

        try
        {
            var allDevices = HidHelper.FindSteelSeriesDevices();
            // Show ALL Apex Pro interfaces (no filtering)
            var apexDevices = allDevices.Where(d =>
                d.Product.Contains("Apex Pro", StringComparison.OrdinalIgnoreCase) ||
                d.Pid == 0x1630 || d.Pid == 0x1610).ToList();

            if (apexDevices.Count == 0)
            {
                txtCaps.Text = allDevices.Count > 0
                    ? $"Found {allDevices.Count} SteelSeries device(s) but none matched Apex Pro.\r\nDevices: " +
                      string.Join(", ", allDevices.Select(d => $"{d.Product}(0x{d.Pid:X4})"))
                    : "No SteelSeries HID devices found. Make sure your Apex Pro is connected.";
                return;
            }

            foreach (var d in apexDevices)
            {
                var caps = HidHelper.GetDeviceCaps(d.Path);
                string type = caps != null ? DescribeUsage(caps.Caps.UsagePage, caps.Caps.Usage) : "? (no access)";
                int values = caps?.Caps.NumberInputValueCaps ?? 0;
                int buttons = caps?.Caps.NumberInputButtonCaps ?? 0;
                int reportSize = caps?.Caps.InputReportByteLength ?? 0;

                var item = new ListViewItem($"0x{d.Pid:X4}");
                item.SubItems.Add(type);
                item.SubItems.Add(values.ToString());
                item.SubItems.Add(buttons.ToString());
                item.SubItems.Add(reportSize.ToString());
                item.SubItems.Add(d.Path.Length > 70 ? "..." + d.Path[^67..] : d.Path);
                item.Tag = d.Path;

                // Color code by type
                if (type.StartsWith("Vendor")) item.ForeColor = Theme.Yellow;
                else if (type == "Keyboard") item.ForeColor = Theme.Green;
                else item.ForeColor = Theme.Dim;

                deviceList.Items.Add(item);
            }

            txtCaps.Text = $"Found {apexDevices.Count} Apex Pro interface(s).\r\n" +
                "Green=Keyboard  Yellow=Vendor-specific  Gray=Other\r\n" +
                "Click 'Probe All' to test which interfaces respond to key presses.";
        }
        catch (Exception ex)
        {
            txtCaps.Text = "Error scanning: " + ex.Message;
        }
    }

    /// <summary>Try reading from ALL interfaces simultaneously for 5 seconds to find which respond to keys.</summary>
    void ProbeAll()
    {
        StopReading();
        txtReport.Clear();
        txtCaps.Text = "Probing all interfaces for 5 seconds...\r\nPress WASD keys at different depths NOW!";
        btnProbeAll.Enabled = false;
        btnScan.Enabled = false;

        var paths = new List<(string path, string type, int idx)>();
        for (int i = 0; i < deviceList.Items.Count; i++)
        {
            string path = (deviceList.Items[i].Tag as string)!;
            string type = deviceList.Items[i].SubItems[1].Text;
            paths.Add((path, type, i));
        }

        reading = true;
        readThread = new Thread(() =>
        {
            var threads = new List<Thread>();
            var results = new System.Collections.Concurrent.ConcurrentDictionary<int, (int reports, int changes, string sample)>();

            foreach (var (path, type, idx) in paths)
            {
                var t = new Thread(() =>
                {
                    IntPtr handle = HidHelper.OpenDevice(path);
                    if (handle == IntPtr.Zero) { results[idx] = (0, 0, "Could not open"); return; }

                    try
                    {
                        var caps = HidHelper.GetDeviceCaps(path);
                        int bufSize = caps?.Caps.InputReportByteLength ?? 64;
                        byte[]? prev = null;
                        int reportCount = 0, changeCount = 0;
                        string lastSample = "";
                        var deadline = Environment.TickCount64 + 5000;

                        while (reading && Environment.TickCount64 < deadline)
                        {
                            var data = HidHelper.ReadReportFromHandle(handle, bufSize);
                            if (data == null) continue;
                            reportCount++;

                            if (prev != null && !data.AsSpan().SequenceEqual(prev))
                            {
                                changeCount++;
                                var changed = new List<string>();
                                for (int i = 0; i < data.Length && i < prev.Length; i++)
                                    if (data[i] != prev[i]) changed.Add($"[{i}] {prev[i]}->{data[i]}");
                                lastSample = string.Join(" ", changed.Take(8));
                            }
                            prev = (byte[])data.Clone();
                        }
                        results[idx] = (reportCount, changeCount, lastSample);
                    }
                    finally { HidHelper.CloseDevice(handle); }
                }) { IsBackground = true };
                t.Start();
                threads.Add(t);
            }

            // Wait for all probe threads (max 6 seconds)
            foreach (var t in threads) t.Join(6000);
            reading = false;

            try
            {
                BeginInvoke(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== Probe Results ===");
                    bool found = false;
                    foreach (var (path, type, idx) in paths)
                    {
                        if (!results.TryGetValue(idx, out var r)) { sb.AppendLine($"  [{idx}] {type}: no response"); continue; }
                        string status = r.changes > 0 ? "<<< KEY DATA FOUND" : "";
                        sb.AppendLine($"  [{idx}] {type}: {r.reports} reports, {r.changes} changes {status}");
                        if (r.changes > 0 && r.sample.Length > 0)
                        {
                            sb.AppendLine($"       Last change: {r.sample}");
                            found = true;
                        }
                    }
                    if (!found)
                        sb.AppendLine("\r\nNo interfaces responded to key presses.\r\nEnable 'Analog Input' in SteelSeries GG for the WASD keys.");

                    txtReport.Text = sb.ToString();
                    txtCaps.Text = found
                        ? "Found interface(s) with key data! Select the one marked <<< and click Start Reading."
                        : "No analog data detected. Analog mode may need to be enabled in SteelSeries GG.";
                    btnProbeAll.Enabled = true;
                    btnScan.Enabled = true;
                });
            }
            catch { }
        }) { IsBackground = true };
        readThread.Start();
    }

    /// <summary>Read Feature Reports from all vendor interfaces (non-blocking).</summary>
    void DumpFeatureReports()
    {
        StopReading();
        txtReport.Clear();

        var paths = new List<(string path, string type, int featSize)>();
        for (int i = 0; i < deviceList.Items.Count; i++)
        {
            string path = (deviceList.Items[i].Tag as string)!;
            string type = deviceList.Items[i].SubItems[1].Text;
            if (!type.StartsWith("Vendor")) continue;
            var caps = HidHelper.GetDeviceCaps(path);
            int featSize = caps?.Caps.FeatureReportByteLength ?? 0;
            paths.Add((path, type, featSize));
        }

        if (paths.Count == 0) { txtReport.Text = "No vendor interfaces found."; return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Feature Report Dump (non-blocking) ===\r\n");

        foreach (var (path, type, featSize) in paths)
        {
            sb.AppendLine($"--- {type}  FeatureReportSize={featSize} ---");

            if (featSize == 0)
            {
                sb.AppendLine("  No feature reports on this interface.\r\n");
                continue;
            }

            IntPtr handle = HidHelper.OpenDeviceRW(path);
            if (handle == IntPtr.Zero)
            {
                sb.AppendLine("  Could not open. Try closing SteelSeries GG first.\r\n");
                continue;
            }

            try
            {
                int found = 0;
                // Try all report IDs 0x00 - 0xFF
                for (int rid = 0; rid <= 0xFF; rid++)
                {
                    byte[] buf = new byte[featSize];
                    buf[0] = (byte)rid;
                    if (!HidHelper.GetFeature(handle, buf)) continue;

                    found++;
                    // Count non-zero bytes (skip byte 0 which is report ID)
                    int nonZeroCount = 0;
                    for (int i = 1; i < buf.Length; i++) if (buf[i] != 0) nonZeroCount++;

                    if (nonZeroCount == 0) continue; // skip empty reports

                    string hex = BitConverter.ToString(buf, 0, Math.Min(buf.Length, 40)).Replace("-", " ");
                    sb.AppendLine($"  ID=0x{rid:X2} ({nonZeroCount} non-zero bytes): {hex}...");

                    // Show non-zero byte positions and values
                    var nz = new List<string>();
                    for (int i = 1; i < buf.Length; i++)
                        if (buf[i] != 0) nz.Add($"[{i}]=0x{buf[i]:X2}({buf[i]})");
                    if (nz.Count <= 20)
                        sb.AppendLine($"    {string.Join("  ", nz)}");
                    else
                        sb.AppendLine($"    {string.Join("  ", nz.Take(20))}  ...+{nz.Count - 20} more");

                    // Check if it looks like ASCII text
                    bool isAscii = true;
                    for (int i = 1; i < buf.Length && i < 20; i++)
                        if (buf[i] != 0 && (buf[i] < 0x20 || buf[i] > 0x7E)) { isAscii = false; break; }
                    if (isAscii && nonZeroCount > 2)
                    {
                        string text = System.Text.Encoding.ASCII.GetString(buf, 1, Math.Min(buf.Length - 1, 40)).TrimEnd('\0');
                        if (text.Length > 1) sb.AppendLine($"    ASCII: \"{text}\"");
                    }
                }

                sb.AppendLine($"  Total readable report IDs: {found}\r\n");

                // Now read the SAME feature report twice while pressing a key
                // to see if any data changes
                sb.AppendLine("  Checking if feature reports change with key press...");
                sb.AppendLine("  (Reading report ID 0x00 fifty times)");
                byte[] baseline = new byte[featSize];
                baseline[0] = 0;
                HidHelper.GetFeature(handle, baseline);

                int changeDetected = 0;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    Thread.Sleep(20);
                    byte[] check = new byte[featSize];
                    check[0] = 0;
                    if (!HidHelper.GetFeature(handle, check)) continue;
                    for (int i = 1; i < check.Length; i++)
                    {
                        if (check[i] != baseline[i])
                        {
                            changeDetected++;
                            sb.AppendLine($"  CHANGE at attempt {attempt}: byte[{i}] {baseline[i]}->{check[i]}");
                            baseline = check;
                            break;
                        }
                    }
                }
                if (changeDetected == 0)
                    sb.AppendLine("  No changes detected in feature reports.");
                sb.AppendLine();
            }
            finally { HidHelper.CloseDevice(handle); }
        }

        sb.AppendLine("=== Done ===");
        sb.AppendLine("If 'Could not open' — close SteelSeries GG (system tray) and retry.");
        txtReport.Text = sb.ToString();
        txtCaps.Text = "Feature dump complete. Check results for non-empty reports.";
    }

    void OnDeviceSelected()
    {
        StopReading();
        if (deviceList.SelectedItems.Count == 0) return;
        selectedDevicePath = deviceList.SelectedItems[0].Tag as string;
        btnReadReport.Enabled = true;

        try
        {
            var caps = HidHelper.GetDeviceCaps(selectedDevicePath!);
            if (caps == null)
            {
                txtCaps.Text = "Could not read device capabilities (access denied or device busy).";
                return;
            }

            var c = caps.Caps;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Type: {DescribeUsage(c.UsagePage, c.Usage)}  (UsagePage=0x{c.UsagePage:X4}  Usage=0x{c.Usage:X4})");
            sb.AppendLine($"InputReportSize: {c.InputReportByteLength}  OutputReportSize: {c.OutputReportByteLength}  FeatureReportSize: {c.FeatureReportByteLength}");
            sb.AppendLine($"InputButtons: {c.NumberInputButtonCaps}  InputValues: {c.NumberInputValueCaps}  InputDataIndices: {c.NumberInputDataIndices}");

            if (caps.ValueCaps.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Input Value Capabilities:");
                foreach (var vc in caps.ValueCaps)
                {
                    string vpName = vc.UsagePage >= 0xFF00 ? "Vendor-specific" :
                        vc.UsagePage == 0x0001 ? "Generic Desktop" :
                        vc.UsagePage == 0x0006 ? "Generic Device" : $"Page 0x{vc.UsagePage:X4}";
                    sb.AppendLine($"  {vpName}  Usage=0x{vc.UsageMin:X4}-0x{vc.UsageMax:X4}  " +
                        $"Bits={vc.BitSize}  Range=[{vc.LogicalMin}..{vc.LogicalMax}]  Absolute={vc.IsAbsolute}");
                }
            }

            txtCaps.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            txtCaps.Text = "Error reading caps: " + ex.Message;
        }
    }

    void ToggleReading()
    {
        if (reading) { StopReading(); return; }
        if (selectedDevicePath == null) return;
        txtReport.Clear();
        reading = true;
        btnReadReport.Text = "Stop Reading";
        btnScan.Enabled = false;
        btnProbeAll.Enabled = false;
        readThread = new Thread(ReadLoop) { IsBackground = true, Name = "HidRead" };
        readThread.Start(selectedDevicePath);
    }

    void StopReading()
    {
        reading = false;
        readThread?.Join(500);
        readThread = null;
        btnReadReport.Text = "Start Reading";
        btnScan.Enabled = true;
        btnProbeAll.Enabled = true;
    }

    int reportCount;

    void ReadLoop(object? pathObj)
    {
        string path = (string)pathObj!;
        IntPtr handle = HidHelper.OpenDevice(path);
        if (handle == IntPtr.Zero)
        {
            try { BeginInvoke(() => AppendReport("ERROR: Could not open device for reading.")); } catch { }
            return;
        }

        try
        {
            var caps = HidHelper.GetDeviceCaps(path);
            int bufSize = caps?.Caps.InputReportByteLength ?? 64;
            byte[]? prevBuf = null;

            while (reading)
            {
                var data = HidHelper.ReadReportFromHandle(handle, bufSize);
                if (data == null) { Thread.Sleep(10); continue; }

                if (prevBuf != null && data.AsSpan().SequenceEqual(prevBuf))
                    continue;

                reportCount++;
                string hex = BitConverter.ToString(data).Replace("-", " ");

                var changed = new List<string>();
                var nonZero = new List<string>();
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] != 0) nonZero.Add($"[{i}]=0x{data[i]:X2}({data[i]})");
                    if (prevBuf != null && i < prevBuf.Length && data[i] != prevBuf[i])
                        changed.Add($"[{i}] {prevBuf[i]}->{data[i]}");
                }

                string line = $"#{reportCount:D4}  {hex}";
                if (changed.Count > 0)
                    line += $"\r\n  CHANGED: {string.Join("  ", changed)}";
                else if (nonZero.Count > 0)
                    line += $"\r\n  NonZero: {string.Join("  ", nonZero)}";

                prevBuf = (byte[])data.Clone();
                try { BeginInvoke(() => AppendReport(line)); } catch { break; }
            }
        }
        finally { HidHelper.CloseDevice(handle); }
    }

    void AppendReport(string line)
    {
        txtReport.AppendText(line + "\r\n");
        if (txtReport.Lines.Length > 300)
        {
            var lines = txtReport.Lines;
            txtReport.Text = string.Join("\r\n", lines[^200..]);
            txtReport.SelectionStart = txtReport.TextLength;
            txtReport.ScrollToCaret();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopReading();
        base.OnFormClosing(e);
    }
}
