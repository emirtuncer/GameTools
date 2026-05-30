namespace GameTools.Core;

public record WindowInfo(
    IntPtr Hwnd,
    string Title,
    string Process,
    uint Pid,
    int X,
    int Y,
    int Width,
    int Height);
