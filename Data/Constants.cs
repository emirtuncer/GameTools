namespace GameTools.Core;

public static class Constants
{
    // Timing
    public const int CaptureCountdownSec = 5;
    public const int CaptureIntervalMs = 1000;
    public const int ClipLoopMs = 500;
    public const int AutoDetectMs = 3000;
    public const int ActionSettleMs = 100;
    public const int ThreadJoinMs = 1000;

    // Layout
    public const int EdgePadding = 5;

    // Defaults
    public const int DefaultResW = 2560;
    public const int DefaultResH = 1440;

    // Window size thresholds
    public const int MinWindowSize = 100;

    // Gamepad emulation
    public const bool GamepadPluginEnabled = false; // flip to true to show Virtual Gamepad UI
    public const int GamepadPollMs = 8; // ~120Hz
    public const short StickMax = 32767;
    public const short StickMin = -32768;
    public const int DefaultRampUpMs = 150;
    public const int DefaultRampDownMs = 100;
    public const int DefaultMouseSensitivity = 50;
    public const int DefaultMouseDecayMs = 50;
}
