using System.Runtime.InteropServices;

namespace GameTools.Core;

public static class HidHelper
{
    // setupapi.dll
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // hid.dll
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(ushort reportType, [Out] HIDP_VALUE_CAPS[] valueCaps,
        ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);
    [DllImport("hid.dll")] static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);
    [DllImport("hid.dll")] static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    // kernel32.dll
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(IntPtr hFile, byte[] buffer, uint numberOfBytesToRead,
        out uint numberOfBytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr hObject);

    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3;
    static readonly IntPtr INVALID_HANDLE = new(-1);
    const int HIDP_STATUS_SUCCESS = 0x00110000;
    const ushort HidP_Input = 0;

    const ushort STEELSERIES_VID = 0x1038;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage, ReportID;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
        public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public bool IsRange, IsStringRange, IsDesignatorRange, IsAbsolute;
        [MarshalAs(UnmanagedType.U1)] public bool HasNull;
        public byte Reserved;
        public ushort BitSize, ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
        public uint UnitsExp, Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        // Range/NotRange union — we use Range fields
        public ushort UsageMin, UsageMax;
        public ushort StringMin, StringMax;
        public ushort DesignatorMin, DesignatorMax;
        public ushort DataIndexMin, DataIndexMax;
    }

    public record HidDeviceInfo(string Path, ushort Vid, ushort Pid, string Product);

    public record HidDeviceCaps(
        HIDP_CAPS Caps,
        HIDP_VALUE_CAPS[] ValueCaps);

    /// <summary>Find all SteelSeries HID devices.</summary>
    public static List<HidDeviceInfo> FindSteelSeriesDevices()
    {
        var results = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devInfo == INVALID_HANDLE) return results;

        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            for (uint i = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
            {
                SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out uint reqSize, IntPtr.Zero);
                IntPtr detailBuf = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    // First 4 bytes = cbSize (6 on x64 due to alignment)
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuf, reqSize, out _, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(detailBuf + 4)!;
                    IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle == INVALID_HANDLE) continue;

                    try
                    {
                        var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (HidD_GetAttributes(handle, ref attrs) && attrs.VendorID == STEELSERIES_VID)
                        {
                            byte[] prodBuf = new byte[256];
                            string product = "";
                            if (HidD_GetProductString(handle, prodBuf, (uint)prodBuf.Length))
                                product = System.Text.Encoding.Unicode.GetString(prodBuf).TrimEnd('\0');

                            results.Add(new HidDeviceInfo(path, attrs.VendorID, attrs.ProductID, product));
                        }
                    }
                    finally { CloseHandle(handle); }
                }
                finally { Marshal.FreeHGlobal(detailBuf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devInfo); }

        return results;
    }

    /// <summary>Get HID capabilities and value caps for a device.</summary>
    public static HidDeviceCaps? GetDeviceCaps(string devicePath)
    {
        IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE) return null;

        try
        {
            if (!HidD_GetPreparsedData(handle, out IntPtr ppd)) return null;
            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS) return null;

                var valueCaps = new HIDP_VALUE_CAPS[Math.Max(caps.NumberInputValueCaps, (ushort)1)];
                ushort numValueCaps = caps.NumberInputValueCaps;
                if (numValueCaps > 0)
                    HidP_GetValueCaps(HidP_Input, valueCaps, ref numValueCaps, ppd);

                return new HidDeviceCaps(caps, valueCaps[..numValueCaps]);
            }
            finally { HidD_FreePreparsedData(ppd); }
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>Open a device handle for reading.</summary>
    public static IntPtr OpenDevice(string devicePath)
    {
        IntPtr handle = CreateFile(devicePath, GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        return handle == INVALID_HANDLE ? IntPtr.Zero : handle;
    }

    /// <summary>Open a device handle for read+write (needed for feature/output reports).</summary>
    public static IntPtr OpenDeviceRW(string devicePath)
    {
        IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        return handle == INVALID_HANDLE ? IntPtr.Zero : handle;
    }

    /// <summary>Get a feature report. reportBuffer[0] = report ID.</summary>
    public static bool GetFeature(IntPtr handle, byte[] buffer) =>
        HidD_GetFeature(handle, buffer, (uint)buffer.Length);

    /// <summary>Set a feature report. reportBuffer[0] = report ID.</summary>
    public static bool SetFeature(IntPtr handle, byte[] buffer) =>
        HidD_SetFeature(handle, buffer, (uint)buffer.Length);

    /// <summary>Send an output report. reportBuffer[0] = report ID.</summary>
    public static bool SendOutput(IntPtr handle, byte[] buffer) =>
        HidD_SetOutputReport(handle, buffer, (uint)buffer.Length);

    /// <summary>Read a single report from an already-open handle. Blocks until data arrives.</summary>
    public static byte[]? ReadReportFromHandle(IntPtr handle, int reportSize)
    {
        byte[] buf = new byte[reportSize];
        if (ReadFile(handle, buf, (uint)buf.Length, out uint read, IntPtr.Zero) && read > 0)
            return buf[..(int)read];
        return null;
    }

    /// <summary>Close a device handle.</summary>
    public static void CloseDevice(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }
}
