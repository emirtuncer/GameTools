using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTools.Core;

public static class AudioMuter
{
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr obj);

    static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");

    static IntPtr VTable(IntPtr ptr, int index)
    {
        var vtbl = Marshal.ReadIntPtr(ptr);
        return Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
    }

    delegate int GetDefaultAudioEndpointDel(IntPtr self, int dataFlow, int role, out IntPtr device);
    delegate int ActivateDel(IntPtr self, ref Guid iid, uint ctx, IntPtr param, out IntPtr iface);
    delegate int GetSessionEnumeratorDel(IntPtr self, out IntPtr enumerator);
    delegate int GetCountDel(IntPtr self, out int count);
    delegate int GetSessionDel(IntPtr self, int index, out IntPtr session);
    delegate int QueryInterfaceDel(IntPtr self, ref Guid iid, out IntPtr obj);
    delegate int GetProcessIdDel(IntPtr self, out uint pid);
    delegate int SetMuteDel(IntPtr self, int mute, ref Guid ctx);
    delegate int ReleaseDel(IntPtr self);

    static void SafeRelease(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            var release = (ReleaseDel)Marshal.GetDelegateForFunctionPointer(VTable(ptr, 2), typeof(ReleaseDel));
            release(ptr);
        }
    }

    public static bool SetMute(uint pid, bool mute)
    {
        if (pid == 0) return false;

        IntPtr enumerator = IntPtr.Zero, device = IntPtr.Zero, manager = IntPtr.Zero, sessionEnum = IntPtr.Zero;
        try
        {
            Guid clsid = CLSID_MMDeviceEnumerator, iid = IID_IMMDeviceEnumerator;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, 0x17, ref iid, out enumerator) != 0) return false;

            var getEndpoint = (GetDefaultAudioEndpointDel)Marshal.GetDelegateForFunctionPointer(VTable(enumerator, 4), typeof(GetDefaultAudioEndpointDel));
            if (getEndpoint(enumerator, 0, 0, out device) != 0) return false;

            Guid mgrIid = IID_IAudioSessionManager2;
            var activate = (ActivateDel)Marshal.GetDelegateForFunctionPointer(VTable(device, 3), typeof(ActivateDel));
            if (activate(device, ref mgrIid, 0x17, IntPtr.Zero, out manager) != 0) return false;

            var getEnum = (GetSessionEnumeratorDel)Marshal.GetDelegateForFunctionPointer(VTable(manager, 5), typeof(GetSessionEnumeratorDel));
            if (getEnum(manager, out sessionEnum) != 0) return false;

            var getCount = (GetCountDel)Marshal.GetDelegateForFunctionPointer(VTable(sessionEnum, 3), typeof(GetCountDel));
            getCount(sessionEnum, out int count);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                IntPtr session = IntPtr.Zero, session2 = IntPtr.Zero, volume = IntPtr.Zero;
                try
                {
                    var getSession = (GetSessionDel)Marshal.GetDelegateForFunctionPointer(VTable(sessionEnum, 4), typeof(GetSessionDel));
                    if (getSession(sessionEnum, i, out session) != 0) continue;

                    Guid sc2Iid = IID_IAudioSessionControl2;
                    var qi = (QueryInterfaceDel)Marshal.GetDelegateForFunctionPointer(VTable(session, 0), typeof(QueryInterfaceDel));
                    if (qi(session, ref sc2Iid, out session2) != 0) continue;

                    var getPid = (GetProcessIdDel)Marshal.GetDelegateForFunctionPointer(VTable(session2, 14), typeof(GetProcessIdDel));
                    if (getPid(session2, out uint sPid) != 0 || sPid != pid) continue;

                    Guid volIid = IID_ISimpleAudioVolume;
                    if (qi(session, ref volIid, out volume) != 0) continue;

                    Guid empty = Guid.Empty;
                    var setMute = (SetMuteDel)Marshal.GetDelegateForFunctionPointer(VTable(volume, 5), typeof(SetMuteDel));
                    setMute(volume, mute ? 1 : 0, ref empty);
                    found = true;
                }
                finally
                {
                    SafeRelease(volume);
                    SafeRelease(session2);
                    SafeRelease(session);
                }
            }
            return found;
        }
        catch (Exception ex) { Debug.WriteLine("AudioMuter.SetMute failed: " + ex.Message); return false; }
        finally
        {
            SafeRelease(sessionEnum);
            SafeRelease(manager);
            SafeRelease(device);
            SafeRelease(enumerator);
        }
    }
}
