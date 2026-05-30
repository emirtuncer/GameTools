using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using GameTools.Data;

namespace GameTools.Core;

public static class GamepadEmulator
{
    static ViGEmClient? _client;
    static IXbox360Controller? _controller;
    static volatile bool _running;
    static Thread? _pollThread;
    static GamepadSettings _settings = new();
    static bool _analogConnected;

    // WASD values [0..1] — from analog hardware or ramping
    static float _dValue, _aValue, _wValue, _sValue;

    // Mouse delta accumulator
    static int _mouseDeltaX, _mouseDeltaY;
    static readonly object _mouseLock = new();

    // Right stick state
    static float _rightX, _rightY;

    static long _lastTickMs;

    public static bool IsRunning => _running;
    public static bool IsAnalogConnected => _analogConnected;

    // Expose current values for UI visualization
    public static float CurrentW => _wValue;
    public static float CurrentA => _aValue;
    public static float CurrentS => _sValue;
    public static float CurrentD => _dValue;

    public static bool IsDriverAvailable()
    {
        try
        {
            using var test = new ViGEmClient();
            return true;
        }
        catch { return false; }
    }

    public static bool Start(GamepadSettings settings)
    {
        if (_running) return true;
        _settings = settings;

        try
        {
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("GamepadEmulator.Start failed: " + ex.Message);
            Cleanup();
            return false;
        }

        _dValue = _aValue = _wValue = _sValue = 0;
        _rightX = _rightY = 0;
        lock (_mouseLock) { _mouseDeltaX = _mouseDeltaY = 0; }
        _lastTickMs = Environment.TickCount64;

        _analogConnected = false;
        _running = true;
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "GamepadPoll" };
        _pollThread.Start();
        return true;
    }

    public static void Stop()
    {
        _running = false;
        _pollThread?.Join(Constants.ThreadJoinMs);
        _pollThread = null;
        Cleanup();
    }

    public static void OnRawMouseInput(int deltaX, int deltaY)
    {
        lock (_mouseLock)
        {
            _mouseDeltaX += deltaX;
            _mouseDeltaY += deltaY;
        }
    }

    static void Cleanup()
    {
        try { ApexProAnalog.Disconnect(); } catch { }
        _analogConnected = false;
        try { _controller?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _controller = null;
        _client = null;
    }

    static void PollLoop()
    {
        while (_running)
        {
            long now = Environment.TickCount64;
            float dtMs = Math.Max(1, now - _lastTickMs);
            _lastTickMs = now;

            if (_controller == null) break;

            if (_settings.WasdEnabled)
                UpdateLeftStick(dtMs);

            if (_settings.MouseEnabled)
                UpdateRightStick(dtMs);

            try
            {
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX,
                    ToStick(_dValue - _aValue));
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY,
                    ToStick(_wValue - _sValue));
                _controller.SetAxisValue(Xbox360Axis.RightThumbX,
                    ToStick(_rightX));
                _controller.SetAxisValue(Xbox360Axis.RightThumbY,
                    ToStick(_rightY));
                _controller.SubmitReport();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GamepadEmulator report: " + ex.Message);
                _running = false;
                break;
            }

            Thread.Sleep(Constants.GamepadPollMs);
        }
    }

    static void UpdateLeftStick(float dtMs)
    {
        float upRate = _settings.RampUpMs > 0 ? dtMs / _settings.RampUpMs : 1f;
        float downRate = _settings.RampDownMs > 0 ? dtMs / _settings.RampDownMs : 1f;

        _dValue = Ramp(_dValue, IsKeyDown(Win32.VK_D), upRate, downRate);
        _aValue = Ramp(_aValue, IsKeyDown(Win32.VK_A), upRate, downRate);
        _wValue = Ramp(_wValue, IsKeyDown(Win32.VK_W), upRate, downRate);
        _sValue = Ramp(_sValue, IsKeyDown(Win32.VK_S), upRate, downRate);
    }

    static float Ramp(float current, bool pressed, float upRate, float downRate)
    {
        if (pressed)
        {
            // Ease-in: starts slow, accelerates as you hold longer (like pressing a key deeper)
            float easedRate = upRate * (0.3f + 0.7f * current);
            return Math.Min(1f, current + Math.Max(upRate * 0.15f, easedRate));
        }
        else
        {
            // Ease-out: fast release initially, slows near zero
            float easedRate = downRate * (0.3f + 0.7f * current);
            return Math.Max(0f, current - Math.Max(downRate * 0.15f, easedRate));
        }
    }

    static void UpdateRightStick(float dtMs)
    {
        int dx, dy;
        lock (_mouseLock)
        {
            dx = _mouseDeltaX;
            dy = _mouseDeltaY;
            _mouseDeltaX = 0;
            _mouseDeltaY = 0;
        }

        float pixelsForFull = 40f - (_settings.MouseSensitivity * 0.35f);
        pixelsForFull = Math.Max(3f, pixelsForFull);

        // Smoothing factor: how fast the stick follows the mouse (0=frozen, 1=instant)
        float smooth = Math.Clamp(dtMs / 16f, 0.1f, 1f); // ~60% per frame at 120Hz

        if (dx != 0 || dy != 0)
        {
            float targetX = Math.Clamp(dx / pixelsForFull, -1f, 1f);
            float targetY = Math.Clamp(-dy / pixelsForFull, -1f, 1f);

            if (_settings.InvertRightX) targetX = -targetX;
            if (_settings.InvertRightY) targetY = -targetY;

            // Lerp toward target for smooth easing
            _rightX = Lerp(_rightX, targetX, smooth);
            _rightY = Lerp(_rightY, targetY, smooth);
        }
        else
        {
            // Ease back to center
            _rightX = Lerp(_rightX, 0, smooth * 0.5f);
            _rightY = Lerp(_rightY, 0, smooth * 0.5f);
            // Snap to zero when close enough
            if (MathF.Abs(_rightX) < 0.01f) _rightX = 0;
            if (MathF.Abs(_rightY) < 0.01f) _rightY = 0;
        }
    }

    static float Lerp(float from, float to, float t) => from + (to - from) * t;

    static short ToStick(float normalized)
    {
        int raw = (int)(normalized * Constants.StickMax);
        return (short)Math.Clamp(raw, Constants.StickMin, Constants.StickMax);
    }

    static bool IsKeyDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
}
