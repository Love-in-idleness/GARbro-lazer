using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GARbro.Application
{
    public sealed record WorkerLimits(double TimeoutSeconds=300,long MaxMemoryBytes=1L<<30)
    {
        public void Validate()
        {
            if (!double.IsFinite(TimeoutSeconds) || TimeoutSeconds<=0 || TimeoutSeconds>86400
                || MaxMemoryBytes<64L<<20 || MaxMemoryBytes>1L<<40)
                throw new ArgumentOutOfRangeException(nameof(WorkerLimits),"Choose a positive time limit and at least 64 MiB of memory.");
        }
    }
    public sealed class WorkerLimitException(string message,string reason=null) : IOException(message)
    { public string Reason {get;}=reason; }
    public sealed record WorkerRunResult(int ExitCode,string StandardError);

    /// <summary>Keep decoder crashes and unresponsive operations outside the caller.</summary>
    public static class WorkerSupervisor
    {
        internal static long ChildMemoryLimit =>
            long.TryParse(Environment.GetEnvironmentVariable(MediaTools.MemoryVariable), out long limit) && limit >= 64L << 20
                ? limit : 1L << 30;

        [DllImport("kernel32.dll", ExactSpelling=true)]
        static extern int GetCurrentPackageFullName(ref uint length, IntPtr packageName);
        static bool HasPackageIdentity()
        {
            if (!OperatingSystem.IsWindows()) return false;
            uint length=0;
            return GetCurrentPackageFullName(ref length,IntPtr.Zero)==122;
        }
        public static long ResidentBytes(Process process)
        {
            // Refresh invalidates Process's shared metadata and exit state. The
            // request loop and watchdog must not clear it during another read.
            lock(process)
            {
                try {process.Refresh();return process.HasExited?0:process.WorkingSet64;}
                catch(InvalidOperationException)when(process.HasExited){return 0;}
                catch(System.ComponentModel.Win32Exception)when(process.HasExited){return 0;}
            }
        }

        public static string DotnetHost()
        {
            string host=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (host!=null && File.Exists(host)) return host;
            string root=Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (root!=null)
            {
                host=Path.Combine(root,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet");
                if (File.Exists(host)) return host;
            }
            // Apphosts can locate a registered runtime when a desktop launcher
            // supplies neither DOTNET_ROOT nor a PATH containing the CLI host.
            // Resolve the host beside this framework-dependent runtime first.
            host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",
                OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
            if(File.Exists(host))return host;
            return "dotnet";
        }

        internal static ProcessStartInfo StartInfo(string assembly)
        {
            if(!File.Exists(assembly)) throw new FileNotFoundException("Decoder worker is missing.",assembly);
            string full=Path.GetFullPath(assembly);
            string apphost=OperatingSystem.IsWindows()?Path.ChangeExtension(full,".exe"):Path.ChangeExtension(full,null);
            bool packaged=HasPackageIdentity();
            if(packaged && !File.Exists(apphost)) throw new FileNotFoundException("Packaged decoder apphost is missing.",apphost);
            bool native=File.Exists(apphost);
            var start=new ProcessStartInfo(native?apphost:DotnetHost()) {WorkingDirectory=Path.GetDirectoryName(full),UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            if(!native)start.ArgumentList.Add(full);
            return start;
        }

        public static async Task<WorkerRunResult> RunManagedAsync(string assembly,IReadOnlyList<string> arguments,
            WorkerLimits limits=null,string input=null,CancellationToken cancellation=default,
            Action<string> output=null,Action<string> error=null)
        {
            limits??=new(); limits.Validate();
            if(!File.Exists(assembly)) throw new FileNotFoundException("Decoder worker is missing.",assembly);
            cancellation.ThrowIfCancellationRequested();
            // An external dotnet host loses MSIX identity and cannot load the
            // packaged worker. The adjacent apphost keeps the package context.
            bool packaged=HasPackageIdentity();
            string apphost=OperatingSystem.IsWindows() ? Path.ChangeExtension(Path.GetFullPath(assembly),".exe") : Path.ChangeExtension(Path.GetFullPath(assembly),null);
            if(packaged && !File.Exists(apphost)) throw new FileNotFoundException("Packaged decoder apphost is missing.",apphost);
            bool useApphost = packaged || File.Exists(apphost);
            string id=Guid.NewGuid().ToString("N");
            string registry=Path.Combine(Path.GetTempPath(),"garbro-worker-"+id+".jsonl");
            var options=new FileStreamOptions {Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.Read};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using (new FileStream(registry,options)) { }
            var start=new ProcessStartInfo(useApphost?apphost:DotnetHost()) {UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            if(!useApphost) start.ArgumentList.Add(Path.GetFullPath(assembly));
            foreach(string argument in arguments) start.ArgumentList.Add(argument);
            start.Environment[WorkerTemporaryFiles.ScopeVariable]=id;
            start.Environment[WorkerTemporaryFiles.RegistryVariable]=registry;
            start.Environment[MediaTools.MemoryVariable]=limits.MaxMemoryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var process=new Process {StartInfo=start};
            Task write=null,stdout=null,stderr=null,exit=null;
            bool started=false;
            var errors=new StringBuilder();
            EventHandler onExit=(_,_)=>
            {
                if(!started)return;
                try
                {
                    if(!process.HasExited)process.Kill(entireProcessTree:true);
                    if(process.WaitForExit(2000))WorkerTemporaryFiles.Cleanup(registry,id);
                }
                catch(InvalidOperationException) { }
                catch(System.ComponentModel.Win32Exception) { }
            };
            // A desktop dispatcher can stop before pending async continuations
            // resume. Normal process exit must still reclaim the live worker.
            AppDomain.CurrentDomain.ProcessExit+=onExit;
            try
            {
                if (!process.Start()) throw new IOException("Cannot start the decoder worker.");
                started=true;
                var clock=Stopwatch.StartNew();
                write=Task.Run(async()=>
                {
                    try { if(input!=null) await process.StandardInput.WriteLineAsync(input); }
                    finally { process.StandardInput.Close(); }
                });
                stdout=Task.Run(()=>ReadLinesAsync(process.StandardOutput,output));
                stderr=Task.Run(()=>ReadLinesAsync(process.StandardError,line=>
                {
                    error?.Invoke(line);
                    if(errors.Length<1<<20) errors.AppendLine(line[..Math.Min(line.Length,(1<<20)-errors.Length)]);
                }));
                exit=process.WaitForExitAsync();
                while(!exit.IsCompleted)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(stdout.IsFaulted) await stdout;
                    if(stderr.IsFaulted) await stderr;
                    if(clock.Elapsed.TotalSeconds>limits.TimeoutSeconds)
                        throw new WorkerLimitException($"Operation exceeded {limits.TimeoutSeconds:g} seconds.","timeout");
                    if(ResidentBytes(process)>limits.MaxMemoryBytes)
                        throw new WorkerLimitException($"Operation exceeded the {limits.MaxMemoryBytes/(1<<20)} MiB memory limit.","memory");
                    await Task.WhenAny(exit,Task.Delay(100));
                }
                await Task.WhenAll(stdout,stderr,exit);
                try { await write; } catch(IOException) when(process.ExitCode!=0) { }
                cancellation.ThrowIfCancellationRequested();
                return new WorkerRunResult(process.ExitCode,errors.ToString());
            }
            finally
            {
                if(started)
                {
                    if(!process.HasExited)
                    {
                        try {process.Kill(entireProcessTree:true);} catch(InvalidOperationException) { }
                    }
                    await process.WaitForExitAsync();
                    foreach(var pending in new[]{write,stdout,stderr})
                        if(pending!=null) {try {await pending;} catch(Exception) { }}
                }
                try {WorkerTemporaryFiles.Cleanup(registry,id);}
                finally {AppDomain.CurrentDomain.ProcessExit-=onExit;}
            }
        }

        public static void ReadLines(TextReader reader,Action<string> receive,int maxCharacters=64<<20,bool stopAfterFirstLine=false)
        {
            var buffer=new char[4096];var line=new StringBuilder();int count;
            while((count=reader.Read(buffer,0,buffer.Length))>0)
                for(int i=0;i<count;i++)
                {
                    char value=buffer[i];
                    if(value=='\n'){receive?.Invoke(line.ToString().TrimEnd('\r'));line.Clear();if(stopAfterFirstLine)return;}
                    else {if(line.Length>=maxCharacters)throw new InvalidDataException("Worker response exceeds the size limit.");line.Append(value);}
                }
            if(line.Length>0)receive?.Invoke(line.ToString());
        }

        public static async Task ReadLinesAsync(TextReader reader,Action<string> receive,int maxCharacters=64<<20, bool stopAfterFirstLine=false)
        {
            var buffer=new char[4096];
            var line=new StringBuilder();
            int count;
            while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0)
            {
                for(int i=0;i<count;i++)
                {
                    char value=buffer[i];
                    if(value=='\n') {receive?.Invoke(line.ToString().TrimEnd('\r'));line.Clear();if(stopAfterFirstLine)return;}
                    else
                    {
                        if(line.Length>=maxCharacters) throw new InvalidDataException("Worker response exceeds the size limit.");
                        line.Append(value);
                    }
                }
            }
            if(line.Length>0) receive?.Invoke(line.ToString());
        }
    }
}
