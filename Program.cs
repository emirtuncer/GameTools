using GameTools.Core;
using GameTools.UI;

bool created;
using var mutex = new Mutex(true, @"Global\GameTools_SingleInstance", out created);

if (!created)
{
    uint wm = Win32.RegisterWindowMessage("GameTools_ShowMe");
    Win32.PostMessage(Win32.HWND_BROADCAST, wm, IntPtr.Zero, IntPtr.Zero);
    return;
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

bool startMinimized = args.Any(a =>
    a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

try
{
    Application.Run(new GameToolsForm(startMinimized));
}
catch (Exception ex)
{
    try { Win32.ClipCursor(IntPtr.Zero); } catch { }
    MessageBox.Show("Fatal error: " + ex.Message, "GameTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
