using OperationCanceledException = System.OperationCanceledException;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using GARbro.Application;
using GARbro.Desktop;

namespace GARbro.Mobile;

[Service(Name = "org.garbro.garbro.DecoderService", Process = ":decoder", Exported = false)]
public sealed class DecoderService : Service
{
    Messenger messenger;
    bool started;
    public override void OnCreate() { base.OnCreate(); messenger = new Messenger(new RequestHandler(this)); }
    public override IBinder OnBind(Intent intent) => messenger.Binder;
    public override void OnDestroy() { base.OnDestroy(); global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid()); }
    sealed class RequestHandler(DecoderService service) : Handler(Looper.MainLooper)
    {
        public override void HandleMessage(Message message)
        {
            if (message.What != 1 || service.started) return;
            service.started = true; var bundle = new Bundle(message.Data);
            DesktopServices.Activity("decoder", "received");
            _ = Task.Factory.StartNew(() =>
            {
                using var input = (ParcelFileDescriptor)bundle.GetParcelable("input"); using var output = (ParcelFileDescriptor)bundle.GetParcelable("output");
                using var read = new ParcelFileDescriptor.AutoCloseInputStream(input); using var write = new ParcelFileDescriptor.AutoCloseOutputStream(output);
                using var managedRead = global::Android.Runtime.InputStreamInvoker.FromJniHandle(read.Handle, global::Android.Runtime.JniHandleOwnership.DoNotTransfer);
                using var managedWrite = global::Android.Runtime.OutputStreamInvoker.FromJniHandle(write.Handle, global::Android.Runtime.JniHandleOwnership.DoNotTransfer);
                using var reader = new StreamReader(managedRead, new UTF8Encoding(false)); using var writer = new StreamWriter(managedWrite, new UTF8Encoding(false)) { AutoFlush = true };
                void Reply(WorkerReply reply) => writer.WriteLine(WorkerProtocol.Serialize(reply));
                try
                {
                    Reply(new("ready", JsonData(global::Android.OS.Process.MyPid())));
                    AndroidBootstrap.Initialize(service);
                    WorkerTemporaryFiles.Initialize(bundle.GetString("scope"), bundle.GetString("registry"));
                    string text = null;
                    WorkerSupervisor.ReadLines(reader, line => text = line, 32 << 20, stopAfterFirstLine: true);
                    if (text == null) throw new InvalidDataException("Decoder request is missing.");
                    var request = WorkerProtocol.Deserialize<WorkerRequest>(text) ?? throw new InvalidDataException("Decoder request is missing.");
                    DesktopServices.Activity("decoder", "executing");
                    WorkerExecutor.ExecuteAsync(request, Reply).GetAwaiter().GetResult();
                }
                catch (Exception e) { try { Reply(new("error", Error: new(e.GetType().Name, e.Message))); } catch (IOException) { } }
                finally { writer.Flush(); write.Close(); read.Close(); }
            }, System.Threading.CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        static System.Text.Json.JsonElement JsonData(int pid) => System.Text.Json.JsonSerializer.SerializeToElement(pid);
    }
}
