using System.Diagnostics;

namespace GameTools.Core;

/// <summary>
/// Reads real-time per-key analog depth from SteelSeries Apex Pro keyboards
/// via HID Feature Reports on the Vendor 0xFFC0 interface.
///
/// Report structure (642 bytes, Report ID 0x00):
///   [0]    = Report ID (0x00 for live data)
///   [1]    = 0x61 (marker)
///   [2]    = 0xFF (marker)
///   [3..N] = Repeating groups of 4 bytes per key:
///            [USB_scancode] [analog_depth_0_255] [cal1] [cal2]
///
/// USB HID scancodes: A=0x04, B=0x05, C=0x06, D=0x07, ..., W=0x1A, S=0x16
/// </summary>
public static class ApexProAnalog
{
    const ushort STEELSERIES_VID = 0x1038;
    const ushort APEX_PRO_PID = 0x1630;
    const int FEATURE_REPORT_SIZE = 642;
    const byte LIVE_REPORT_ID = 0x00;

    // USB HID scancodes for WASD
    const byte SC_A = 0x04, SC_B = 0x05, SC_C = 0x06, SC_D = 0x07;
    const byte SC_S = 0x16, SC_W = 0x1A;

    static IntPtr _handle;
    static string? _devicePath;
    static byte[] _reportBuf = new byte[FEATURE_REPORT_SIZE];

    public static bool IsConnected => _handle != IntPtr.Zero;

    /// <summary>Find and open the Apex Pro Vendor 0xFFC0 interface.</summary>
    public static bool TryConnect()
    {
        if (_handle != IntPtr.Zero) return true;

        var devices = HidHelper.FindSteelSeriesDevices();
        foreach (var d in devices)
        {
            if (d.Pid != APEX_PRO_PID) continue;
            if (!d.Product.Contains("Apex Pro", StringComparison.OrdinalIgnoreCase) && d.Product.Length > 0) continue;

            var caps = HidHelper.GetDeviceCaps(d.Path);
            if (caps == null) continue;

            // Look for Vendor 0xFFC0 with 642-byte feature reports
            if (caps.Caps.UsagePage != 0xFFC0) continue;
            if (caps.Caps.FeatureReportByteLength < FEATURE_REPORT_SIZE) continue;

            IntPtr h = HidHelper.OpenDeviceRW(d.Path);
            if (h == IntPtr.Zero) continue;

            // Verify we can read a feature report
            byte[] test = new byte[FEATURE_REPORT_SIZE];
            test[0] = LIVE_REPORT_ID;
            if (HidHelper.GetFeature(h, test) && test[1] == 0x61 && test[2] == 0xFF)
            {
                _handle = h;
                _devicePath = d.Path;
                Debug.WriteLine($"ApexProAnalog: Connected to {d.Product} at {d.Path}");
                return true;
            }

            HidHelper.CloseDevice(h);
        }

        return false;
    }

    public static void Disconnect()
    {
        if (_handle != IntPtr.Zero)
        {
            HidHelper.CloseDevice(_handle);
            _handle = IntPtr.Zero;
        }
        _devicePath = null;
    }

    /// <summary>
    /// Read analog depth for a key by USB scancode.
    /// Returns 0.0 (not pressed) to 1.0 (fully pressed), or -1 if read failed.
    /// </summary>
    public static float ReadKeyDepth(byte scancode)
    {
        if (_handle == IntPtr.Zero) return -1;

        _reportBuf[0] = LIVE_REPORT_ID;
        if (!HidHelper.GetFeature(_handle, _reportBuf))
        {
            // Device disconnected or error
            Disconnect();
            return -1;
        }

        int offset = GetKeyOffset(scancode);
        if (offset < 0 || offset >= _reportBuf.Length) return -1;

        return _reportBuf[offset] / 255f;
    }

    /// <summary>
    /// Read WASD depths in one call (single feature report read).
    /// Returns (W, A, S, D) as 0.0-1.0, or all -1 if failed.
    /// </summary>
    public static (float w, float a, float s, float d) ReadWASD()
    {
        if (_handle == IntPtr.Zero) return (-1, -1, -1, -1);

        _reportBuf[0] = LIVE_REPORT_ID;
        if (!HidHelper.GetFeature(_handle, _reportBuf))
        {
            Disconnect();
            return (-1, -1, -1, -1);
        }

        float w = GetDepthAt(SC_W);
        float a = GetDepthAt(SC_A);
        float s = GetDepthAt(SC_S);
        float d = GetDepthAt(SC_D);

        return (w, a, s, d);
    }

    /// <summary>Get the byte offset in the feature report for a given scancode.</summary>
    static int GetKeyOffset(byte scancode)
    {
        if (scancode < 0x04) return -1;
        // Structure: bytes[3..] = groups of 4: [scancode][depth][cal1][cal2]
        // Depth byte is at: 3 + (scancode - 0x04) * 4 + 1 = 4 + (scancode - 0x04) * 4
        return 4 + (scancode - 0x04) * 4;
    }

    static float GetDepthAt(byte scancode)
    {
        int offset = GetKeyOffset(scancode);
        if (offset < 0 || offset >= _reportBuf.Length) return 0;
        return _reportBuf[offset] / 255f;
    }
}
