using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class MonitorChoice
    {
        public string Id, DeviceName, Name; public int Adapter, Output, Width, Height, Rotation; public bool Primary, CanCapture;
        public override string ToString() { return Name + " · " + Width + "×" + Height + (Primary ? " · основной" : "") + (!CanCapture ? " · не NVIDIA" : ""); }
    }
    static class MonitorCatalog
    {
        [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct AdapterDesc
        { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint Vendor, Device, SubSys, Revision; public UIntPtr VideoMemory, SystemMemory, SharedMemory; public Luid Luid; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct OutputDesc
        { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; public Rect Desktop; public int Attached, Rotation; public IntPtr Monitor; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumItems(IntPtr instance, uint index, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AdapterDescription(IntPtr instance, out AdapterDesc desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutputDescription(IntPtr instance, out OutputDesc desc);
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        static T Method<T>(IntPtr instance, int slot) where T : class
        { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T)) as T; }

        [StructLayout(LayoutKind.Sequential)] struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
        [StructLayout(LayoutKind.Sequential)] struct SourcePath { public Luid Adapter; public uint Id, ModeIndex, Status; }
        [StructLayout(LayoutKind.Sequential)] struct TargetPath { public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, Scanline; public int Available; public uint Status; }
        [StructLayout(LayoutKind.Sequential)] struct PathInfo { public SourcePath Source; public TargetPath Target; public uint Flags; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SourceName
        { public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TargetName
        { public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath; }
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName name);
        static Dictionary<string, string[]> FriendlyNames()
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint paths, modes; if (GetDisplayConfigBufferSizes(2, out paths, out modes) != 0 || paths > 128 || modes > 512) return result;
                int pathSize = Marshal.SizeOf(typeof(PathInfo));
                IntPtr pathArray = Marshal.AllocHGlobal((int)Math.Max(1, paths) * pathSize), modeArray = Marshal.AllocHGlobal((int)Math.Max(1, modes) * 64);
                try
                {
                    int status = QueryDisplayConfig(2, ref paths, pathArray, ref modes, modeArray, IntPtr.Zero);
                    if (status == 122) continue; if (status != 0) return result;
                    for (int i = 0; i < paths; i++)
                    {
                        var path = (PathInfo)Marshal.PtrToStructure(IntPtr.Add(pathArray, i * pathSize), typeof(PathInfo));
                        var source = new SourceName { Header = new DeviceHeader { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = path.Source.Adapter, Id = path.Source.Id } };
                        var target = new TargetName { Header = new DeviceHeader { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                        if (GetSourceName(ref source) == 0 && GetTargetName(ref target) == 0 && !string.IsNullOrEmpty(source.DeviceName)) result[source.DeviceName] = new[] { target.FriendlyName, target.DevicePath };
                    }
                    return result;
                }
                finally { Marshal.FreeHGlobal(pathArray); Marshal.FreeHGlobal(modeArray); }
            }
            return result;
        }
        public static List<MonitorChoice> Enumerate()
        {
            var result = new List<MonitorChoice>(); var names = FriendlyNames(); IntPtr factory = IntPtr.Zero;
            try
            {
                var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out factory));
                var adapters = Method<EnumItems>(factory, 7);
                for (uint a = 0; a < 32; a++)
                {
                    IntPtr adapter; if (adapters(factory, a, out adapter) < 0) break;
                    try
                    {
                        AdapterDesc adapterDesc; if (Method<AdapterDescription>(adapter, 8)(adapter, out adapterDesc) < 0) continue;
                        var outputs = Method<EnumItems>(adapter, 7);
                        for (uint o = 0; o < 64; o++)
                        {
                            IntPtr output; if (outputs(adapter, o, out output) < 0) break;
                            try
                            {
                                OutputDesc desc; if (Method<OutputDescription>(output, 7)(output, out desc) < 0 || desc.Attached == 0) continue;
                                string[] friendly; names.TryGetValue(desc.DeviceName, out friendly);
                                string number = desc.DeviceName.Replace(@"\\.\DISPLAY", "");
                                result.Add(new MonitorChoice { Adapter = (int)a, Output = (int)o, Rotation = desc.Rotation == 2 ? -90 : desc.Rotation == 3 ? 180 : desc.Rotation == 4 ? 90 : 0, DeviceName = desc.DeviceName, Id = friendly != null && !string.IsNullOrEmpty(friendly[1]) ? friendly[1] : desc.DeviceName, Name = friendly != null && !string.IsNullOrWhiteSpace(friendly[0]) ? friendly[0] : "Монитор " + number, Width = desc.Desktop.Right - desc.Desktop.Left, Height = desc.Desktop.Bottom - desc.Desktop.Top, Primary = desc.Desktop.Left == 0 && desc.Desktop.Top == 0, CanCapture = adapterDesc.Vendor == 0x10de });
                            }
                            finally { Marshal.Release(output); }
                        }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            catch (COMException) { }
            finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
            if (result.Count == 0) foreach (var screen in Screen.AllScreens) result.Add(new MonitorChoice { DeviceName = screen.DeviceName, Id = screen.DeviceName, Name = "Монитор " + screen.DeviceName.Replace(@"\\.\DISPLAY", ""), Width = screen.Bounds.Width, Height = screen.Bounds.Height, Primary = screen.Primary, CanCapture = false });
            return result.OrderByDescending(m => m.Primary).ThenBy(m => m.DeviceName).ToList();
        }
    }
}
