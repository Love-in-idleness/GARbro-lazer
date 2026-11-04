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
public static class PackageActivation {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHParseDisplayName(string path, IntPtr context, out IntPtr item, uint mask, out uint attributes);
    [DllImport("shell32.dll")]
    static extern IntPtr ILFindLastID(IntPtr item);
    [DllImport("shell32.dll", PreserveSig=false)]
    static extern void SHCreateDataObject(IntPtr parent, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex=1)] IntPtr[] children, IntPtr inner, ref Guid iid, out IntPtr data);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);
    public static void Open(string path) {
        IntPtr item=IntPtr.Zero, parent=IntPtr.Zero, data=IntPtr.Zero;
        IEnumAssocHandlers handlers=null;
        try {
            // Pass a filesystem selection from its containing folder. Binding a ZIP
            // shell item itself may bind to the compressed-folder namespace instead.
            uint attributes;
            SHParseDisplayName(System.IO.Path.GetDirectoryName(path), IntPtr.Zero, out parent, 0, out attributes);
            SHParseDisplayName(path, IntPtr.Zero, out item, 0, out attributes);
            var dataId=new Guid("0000010e-0000-0000-C000-000000000046");
            SHCreateDataObject(parent, 1, new[]{ILFindLastID(item)}, IntPtr.Zero, ref dataId, out data);
            SHAssocEnumHandlers(System.IO.Path.GetExtension(path), 0, out handlers);
            IAssocHandler handler; uint fetched;
            while(handlers.Next(1,out handler,out fetched)==0 && fetched==1) {
                try {
                    string name; handler.GetUIName(out name);
                    if(name.IndexOf("GARbro",StringComparison.OrdinalIgnoreCase)>=0) {
                        string id; handler.GetName(out id);
                        Console.WriteLine("Open With candidate: " + name + " [" + id + "]");
                        handler.Invoke(data); return;
                    }
                } finally { Marshal.ReleaseComObject(handler); }
            }
            throw new InvalidOperationException("GARbro is missing from Windows Open With handlers.");
        } finally {
            if(handlers!=null)Marshal.ReleaseComObject(handlers);
            if(data!=IntPtr.Zero)Marshal.Release(data);
            if(parent!=IntPtr.Zero)Marshal.FreeCoTaskMem(parent);
            if(item!=IntPtr.Zero)Marshal.FreeCoTaskMem(item);
        }
    }
}
