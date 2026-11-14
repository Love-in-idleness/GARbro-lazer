using OperationCanceledException = System.OperationCanceledException;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using GARbro.Application;
using GARbro.Desktop;

namespace GARbro.Mobile;

public sealed class AndroidWorkerClient(Context context, AndroidStorageAccess storage) : IWorkerClient
{
    static readonly SemaphoreSlim gate = new(1, 1);
    public async Task<T> ExecuteAsync<T>(WorkerRequest request, WorkerLimits limits, IProgress<ExtractionProgress> progress = null, CancellationToken cancellation = default)
    {
        limits.Validate(); await gate.WaitAsync(cancellation);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        operation.CancelAfter(TimeSpan.FromSeconds(limits.TimeoutSeconds));
        bool background = request.Operation is "resource-extract" or "resource-create" or "resource-convert";
        int pid = 0; bool bound = false; DecoderConnection connection = null;
        string id = Guid.NewGuid().ToString("N"), registry = Path.Combine(context.CacheDir.AbsolutePath, "worker-" + id + ".jsonl");
        File.WriteAllText(registry, "");
        Action cancel = () => operation.Cancel();
        string journal = Path.Combine(context.FilesDir.AbsolutePath, "last-task.json");
        TaskCheckpoint checkpoint = null; string limitReason = null; var elapsed = Stopwatch.StartNew();
        try
        {
            if (background)
            {
                JobCoordinatorService.CancelRequested += cancel;
                context.StartForegroundService(new Intent(context, typeof(JobCoordinatorService)));

            }
            if (request.Operation == "resource-list" && request.Location != null)
            {
                var listing = await Task.Run(() => storage.TryList(request.Location), operation.Token);
                if (listing != null) return (T)(object)listing;
            }
            await Task.Run(() => storage.PrepareRequestAsync(request, operation.Token), operation.Token);
            if (background)
            {
                checkpoint = TaskCheckpoint.Begin(journal, request);
                request = request with { CompletedPaths = checkpoint.CompletedPaths };
            }
            var input = ParcelFileDescriptor.CreateReliablePipe(); var output = ParcelFileDescriptor.CreateReliablePipe();
            using var writeRequest = new ParcelFileDescriptor.AutoCloseOutputStream(input[1]);
            using var readReply = new ParcelFileDescriptor.AutoCloseInputStream(output[0]);
            using var managedReply = global::Android.Runtime.InputStreamInvoker.FromJniHandle(readReply.Handle, global::Android.Runtime.JniHandleOwnership.DoNotTransfer);
            using var managedInput = global::Android.Runtime.OutputStreamInvoker.FromJniHandle(writeRequest.Handle, global::Android.Runtime.JniHandleOwnership.DoNotTransfer);
            using var reader = new StreamReader(managedReply, new UTF8Encoding(false), false, 8192, true);
            connection = new DecoderConnection(input[0], output[1], registry, id);
            DesktopServices.Activity("decoder", "binding");
            bound = context.BindService(new Intent(context, typeof(DecoderService)), connection, Bind.AutoCreate | Bind.Important);
            if (!bound) throw new IOException("Cannot start the decoder service.");
            await connection.Connected.Task.WaitAsync(operation.Token);
            DesktopServices.Activity("decoder", "connected");
            byte[] data = Encoding.UTF8.GetBytes(WorkerProtocol.Serialize(request) + "\n");
            if (data.Length > 32 << 20) throw new InvalidDataException("Decoder request exceeds its size limit.");
            // Java pipe streams block. Dedicated threads keep pipe I/O out of the
            // managed thread pool, including on single-core devices/emulators.
            var writing = Task.Factory.StartNew(() => { managedInput.Write(data); managedInput.Close(); writeRequest.Close(); DesktopServices.Activity("decoder", "request sent"); }, operation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            WorkerReply result = null;
            using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
            var monitor = Task.Run(async () =>
            {
                while (!watchdog.IsCancellationRequested)
                {
                    if (pid > 0)
                    {
                        try
                        {
                            using var manager = (ActivityManager)context.GetSystemService(Context.ActivityService);
                            var usage = manager.GetProcessMemoryInfo(new[] { pid });
                            if (usage.Length > 0 && (long)usage[0].TotalPss * 1024 > limits.MaxMemoryBytes) { limitReason = "memory"; operation.Cancel(); break; }
                        }
                        catch (Java.Lang.SecurityException) { }
                    }
                    await Task.Delay(100, watchdog.Token);
                }
            });
            using var registration = operation.Token.Register(() => { if (pid > 0) global::Android.OS.Process.KillProcess(pid); readReply.Close(); });
            try
            {
                await Task.Factory.StartNew(() => WorkerSupervisor.ReadLines(reader, line =>
                {
                    var reply = WorkerProtocol.Deserialize<WorkerReply>(line) ?? throw new InvalidDataException("Invalid decoder reply.");
                    if (reply.Kind == "ready") { pid = reply.Data.Value.GetInt32(); DesktopServices.Activity("decoder", "ready"); return; }
                    if (reply.Kind == "progress") { checkpoint?.Commit(reply.Progress.OutputPath); progress?.Report(reply.Progress); if (background) JobCoordinatorService.Update(reply.Progress); }
                    else if (reply.Kind is "result" or "error")
                    { if (result != null) throw new InvalidDataException("Duplicate decoder result."); result = reply; }
                    else throw new InvalidDataException("Unknown decoder reply.");
                }), operation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                await writing; operation.Token.ThrowIfCancellationRequested();
            }
            finally { watchdog.Cancel(); try { await monitor; } catch (OperationCanceledException) { } }
            if (result?.Error is WorkerError error)
                throw new WorkerOperationException(error.Type, error.Message) { FormatId = error.FormatId, OptionsJson = error.OptionsJson, CompletedPaths = error.CompletedPaths, ContextKey = error.ContextKey };
            if (result?.Data is not JsonElement value) throw new IOException("The decoder process stopped. Completed outputs were retained; retry the task.");
            if (background && request.Output != null) await storage.CommitAsync(request.Output, request.Overwrite, operation.Token);
            checkpoint?.SetState("completed");
            return value.Deserialize<T>(WorkerProtocol.Json);
        }
        catch (Exception) when (operation.IsCancellationRequested)
        {
            checkpoint?.SetState("interrupted");
            if (limitReason == "memory") throw new WorkerLimitException("Decoder memory limit exceeded.", "memory");
            if (!cancellation.IsCancellationRequested && elapsed.Elapsed.TotalSeconds >= limits.TimeoutSeconds) throw new WorkerLimitException("Decoder time limit exceeded.", "time");
            throw new OperationCanceledException(operation.Token);
        }
        catch
        {
            checkpoint?.SetState("interrupted"); throw;
        }
        finally
        {
            if (pid > 0) global::Android.OS.Process.KillProcess(pid);
            if (bound) context.UnbindService(connection);
            connection?.Dispose(); WorkerTemporaryFiles.Cleanup(registry, id);
            if (background) { JobCoordinatorService.CancelRequested -= cancel; context.StopService(new Intent(context, typeof(JobCoordinatorService))); }
            gate.Release();
        }
    }
    sealed class DecoderConnection(ParcelFileDescriptor input, ParcelFileDescriptor output, string registry, string id) : Java.Lang.Object, IServiceConnection
    {
        public TaskCompletionSource<bool> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnServiceConnected(ComponentName name, IBinder binder)
        {
            try
            {
                using var messenger = new Messenger(binder); using var message = Message.Obtain(null, 1); var data = new Bundle();
                data.PutParcelable("input", input); data.PutParcelable("output", output); data.PutString("registry", registry); data.PutString("scope", id); message.Data = data;
                messenger.Send(message); input.Close(); output.Close(); Connected.TrySetResult(true);
            }
            catch (Exception e) { Connected.TrySetException(e); }
        }
        public void OnServiceDisconnected(ComponentName name) => Connected.TrySetException(new IOException("Decoder service disconnected."));
        public void OnBindingDied(ComponentName name) => Connected.TrySetException(new IOException("Decoder service stopped."));
        public void OnNullBinding(ComponentName name) => Connected.TrySetException(new IOException("Decoder service refused the connection."));
    }
}
