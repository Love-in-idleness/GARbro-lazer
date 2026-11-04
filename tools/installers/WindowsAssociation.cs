using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.ComponentModel;
using Microsoft.Win32;
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
    public static void Open(string path, string appUserModelId) {
        Complete(); failure=null;
        invoked=new ManualResetEventSlim(); completed=new ManualResetEventSlim();
        apartment=new Thread(()=>{
            try { OpenCore(path,appUserModelId); }
            catch(Exception error){failure=error;}
            finally {invoked.Set();}
        });
        apartment.IsBackground=true; apartment.SetApartmentState(ApartmentState.STA); apartment.Start();
        if(!invoked.Wait(10000))throw new TimeoutException("Shell handler invocation did not return.");
        if(failure!=null){Complete();throw new InvalidOperationException("Shell invocation failed.",failure);}
    }

    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ShellExecuteInfo {
        public uint Size,Mask;public IntPtr Window;
        public string Verb,File,Parameters,Directory;public int Show;
        public IntPtr Instance,Item;public string Class;public IntPtr ClassKey;
        public uint HotKey;public IntPtr Icon,Process;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
    static string RegisteredClass(string extension,string appUserModelId) {
        using(var candidates=Registry.ClassesRoot.OpenSubKey(extension+@"\OpenWithProgids")) {
            if(candidates!=null)foreach(string id in candidates.GetValueNames()) {
                using(var app=Registry.ClassesRoot.OpenSubKey(id+@"\Application")) {
                    if(string.Equals(app==null?null:app.GetValue("AppUserModelID")as string,appUserModelId,StringComparison.OrdinalIgnoreCase))return id;
                }
            }
        }
        throw new InvalidOperationException("The installed package has no registered Open With ProgID for "+extension);
    }
    static void OpenCore(string path,string appUserModelId) {
        IEnumAssocHandlers handlers=null;
        try {
            SHAssocEnumHandlers(System.IO.Path.GetExtension(path),0,out handlers);
            IAssocHandler handler;uint fetched;bool found=false;
            while(handlers.Next(1,out handler,out fetched)==0 && fetched==1) {
                try {
                    string name;handler.GetUIName(out name);
                    if(name.IndexOf("GARbro",StringComparison.OrdinalIgnoreCase)<0)continue;
                    string id;handler.GetName(out id);
                    Console.WriteLine("Open With candidate: "+name+" ["+id+"]");found=true;break;
                } finally {Marshal.ReleaseComObject(handler);}
            }
            if(!found)throw new InvalidOperationException("GARbro is missing from Windows Open With handlers.");
            string registered=RegisteredClass(System.IO.Path.GetExtension(path),appUserModelId);
            // Execute the registered file-class verb, as Open With does. No executable
            // path, command-line reconstruction, AUMID activation or default change.
            var info=new ShellExecuteInfo {
                Size=(uint)Marshal.SizeOf(typeof(ShellExecuteInfo)),Mask=0x1|0x40|0x100|0x400,
                Verb="open",File=path,Class=registered,Show=1
            };
            if(!ShellExecuteEx(ref info))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(info.Process!=IntPtr.Zero)CloseHandle(info.Process);
            Console.WriteLine("Shell registered file class opened: "+registered);
            invoked.Set();
            while(!completed.Wait(10)) {
                Message message;
                while(PeekMessage(out message,IntPtr.Zero,0,0,1)) {
                    TranslateMessage(ref message);DispatchMessage(ref message);
                }
            }
        } finally {if(handlers!=null)Marshal.ReleaseComObject(handlers);}
    }
}
