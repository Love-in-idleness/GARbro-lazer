using System;
using System.Runtime.InteropServices;
[ComImport, Guid("F04061AC-1659-4a3f-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAssocHandler {
    void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
    [PreserveSig] int IsRecommended();
    void MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
    void Invoke(IntPtr data);
}
[ComImport, Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumAssocHandlers {
    [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
}
[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemArray {
    void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
}
public static class PackageActivation {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IntPtr item);
    [DllImport("shell32.dll", PreserveSig=false)]
    static extern void SHCreateShellItemArrayFromShellItem(IntPtr item, ref Guid iid, out IntPtr array);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);
    public static void Open(string path) {
        IntPtr item=IntPtr.Zero, array=IntPtr.Zero, data=IntPtr.Zero;
        IShellItemArray shellArray=null; IEnumAssocHandlers handlers=null;
        try {
            var itemId=new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            var arrayId=typeof(IShellItemArray).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item);
            SHCreateShellItemArrayFromShellItem(item, ref arrayId, out array);
            shellArray=(IShellItemArray)Marshal.GetObjectForIUnknown(array);
            var dataHandler=new Guid("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
            var dataId=new Guid("0000010e-0000-0000-C000-000000000046");
            shellArray.BindToHandler(IntPtr.Zero, ref dataHandler, ref dataId, out data);
            SHAssocEnumHandlers(System.IO.Path.GetExtension(path), 0, out handlers);
            IAssocHandler handler; uint fetched;
            while(handlers.Next(1,out handler,out fetched)==0 && fetched==1) {
                try {
                    string name; handler.GetUIName(out name);
                    if(name.IndexOf("GARbro",StringComparison.OrdinalIgnoreCase)>=0) {
                        handler.Invoke(data); return;
                    }
                } finally { Marshal.ReleaseComObject(handler); }
            }
            throw new InvalidOperationException("GARbro is missing from Windows Open With handlers.");
        } finally {
            if(handlers!=null)Marshal.ReleaseComObject(handlers);
            if(data!=IntPtr.Zero)Marshal.Release(data);
            if(shellArray!=null)Marshal.ReleaseComObject(shellArray);
            if(array!=IntPtr.Zero)Marshal.Release(array);
            if(item!=IntPtr.Zero)Marshal.Release(item);
        }
    }
}
