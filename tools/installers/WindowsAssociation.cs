using System;
using System.Runtime.InteropServices;
using System.Threading;
[ComImport, Guid("F04061AC-1659-4a3f-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAssocHandler {
    void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
    [PreserveSig] int IsRecommended();
    void MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
    [PreserveSig] int Invoke(IntPtr data);
}
[ComImport, Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumAssocHandlers {
    [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
}
public static class PackageActivation {
    // Shell COM objects require an STA with a message loop. PowerShell's worker
    // apartment does not provide the Explorer message pump for deferred invokes.
    static Thread apartment;
    static ManualResetEventSlim invoked, completed;
    static Exception failure;
    [StructLayout(LayoutKind.Sequential)]
    struct Message {
        public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam;
        public uint Time; public int X, Y; public uint Private;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PeekMessage(out Message message, IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr DispatchMessage(ref Message message);
    public static void Complete() {
        if(apartment==null)return;
        completed.Set();
        if(!apartment.Join(5000))throw new TimeoutException("Shell activation apartment did not stop.");
        apartment=null; invoked.Dispose(); completed.Dispose();
    }
    public static void Open(string path) {
        Complete(); failure=null;
        invoked=new ManualResetEventSlim(); completed=new ManualResetEventSlim();
        apartment=new Thread(()=>{
            try { OpenCore(path); }
            catch(Exception error){failure=error;}
            finally {invoked.Set();}
        });
        apartment.IsBackground=true; apartment.SetApartmentState(ApartmentState.STA); apartment.Start();
        if(!invoked.Wait(10000))throw new TimeoutException("Shell handler invocation did not return.");
        if(failure!=null){Complete();throw new InvalidOperationException("Shell invocation failed.",failure);}
    }

    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHParseDisplayName(string path, IntPtr context, out IntPtr item, uint mask, out uint attributes);
    [DllImport("shell32.dll")]
    static extern IntPtr ILFindLastID(IntPtr item);
    [DllImport("shell32.dll", PreserveSig=false)]
    static extern void SHCreateDataObject(IntPtr parent, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex=1)] IntPtr[] children, IntPtr inner, ref Guid iid, out IntPtr data);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);
    static void OpenCore(string path) {
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
                        int result=handler.Invoke(data);
                        Console.WriteLine("Shell Invoke HRESULT: 0x"+result.ToString("X8")+"; apartment="+Thread.CurrentThread.GetApartmentState());
                        Marshal.ThrowExceptionForHR(result);
                        invoked.Set();
                        while(!completed.Wait(10)) {
                            Message message;
                            while(PeekMessage(out message,IntPtr.Zero,0,0,1)) {
                                TranslateMessage(ref message);DispatchMessage(ref message);
                            }
                        }
                        return;
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
