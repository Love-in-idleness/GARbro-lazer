using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GARbro.Desktop;

/// <summary>One browsing window per user; later launches forward files over a private local pipe.</summary>
public sealed class DesktopInstance : IDisposable
{
    const int MaximumMessage=1<<20;
    readonly string pipeName;
    readonly FileStream ownership;
    readonly NamedPipeServerStream server;
    readonly CancellationTokenSource stop=new();
    readonly Queue<string[]> pending=new();
    readonly object gate=new();
    Action<string[]> receiver;
    public bool IsPrimary => ownership!=null && server!=null;

    public DesktopInstance(string identity="GARbro")
    {
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity+"\0"+Environment.UserName+"\0"+Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))[..16];
        string directory;
        if(OperatingSystem.IsWindows())directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GARbro","instance");
        else
        {
            string runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if(!string.IsNullOrEmpty(runtime) && Environment.GetEnvironmentVariable("FLATPAK_ID") is string appId)
                runtime=Path.Combine(runtime,"app",appId);
            directory=Path.Combine(string.IsNullOrEmpty(runtime)?Path.GetTempPath():runtime,"garbro-"+key);
        }
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        pipeName=OperatingSystem.IsWindows()?"garbro-desktop-"+key:Path.Combine(directory,"open");
        try
        {
            ownership=new FileStream(Path.Combine(directory,key+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        }
        catch(IOException){ownership?.Dispose();ownership=null;return;}
        _=Listen();
    }
    public void SetReceiver(Action<string[]> receive)
    {
        lock(gate)
        {
            receiver=receive;
            while(pending.TryDequeue(out var args))receiver(args);
        }
    }
    public async Task ForwardAsync(string[] args,CancellationToken cancellation=default)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(args);
        if(bytes.Length>MaximumMessage || args.Length>4096)throw new ArgumentException("Too many files to open.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        byte[] header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await pipe.WriteAsync(header,timeout.Token).ConfigureAwait(false);
        await pipe.WriteAsync(bytes,timeout.Token).ConfigureAwait(false);
        await pipe.ReadExactlyAsync(header.AsMemory(0,1),timeout.Token).ConfigureAwait(false);
        if(header[0]!=1)throw new InvalidDataException("File activation was not accepted.");
    }
    async Task Listen()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                byte[] header=new byte[4];await server.ReadExactlyAsync(header,timeout.Token).ConfigureAwait(false);
                int length=BinaryPrimitives.ReadInt32LittleEndian(header);
                if(length<1 || length>MaximumMessage)throw new InvalidDataException("Invalid file activation length.");
                byte[] bytes=new byte[length];await server.ReadExactlyAsync(bytes,timeout.Token).ConfigureAwait(false);
                string[] args=JsonSerializer.Deserialize<string[]>(bytes);
                if(args==null || args.Length>4096 || Array.Exists(args,a=>a==null))throw new InvalidDataException("Invalid file activation arguments.");
                lock(gate){if(receiver==null)pending.Enqueue(args);else receiver(args);}
                await server.WriteAsync(new byte[]{1},timeout.Token).ConfigureAwait(false);
            }
            catch(Exception error)when(error is IOException or InvalidDataException or JsonException or OperationCanceledException or ObjectDisposedException)
            {if(!stop.IsCancellationRequested)DesktopServices.Log(error);}
            finally {try{if(server.IsConnected)server.Disconnect();}catch(Exception error)when(error is IOException or ObjectDisposedException){} }
        }
    }
    public void Dispose()
    {
        stop.Cancel();server?.Dispose();ownership?.Dispose();
    }
}
