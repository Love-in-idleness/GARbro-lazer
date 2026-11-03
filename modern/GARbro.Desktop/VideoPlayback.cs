using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Media;
using Silk.NET.SDL;
using GARbro.Domain;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace GARbro.Desktop;

/// <summary>Bounded raw frames and PCM from isolated codec processes; SDL owns audio only.</summary>
public sealed class VideoPlayback : IVideoOutput
{
    readonly VideoArtifact artifact;
    readonly Func<byte[], int, int, Task> frame;
    readonly Action<Exception> failed;
    readonly long memoryLimit;
    readonly object gate = new();
    readonly Stopwatch clock = new();
    CancellationTokenSource run;
    Process video, audio;
    PcmOutput sound, device;
    double offset, volume = 1;
    bool playing, disposed, primed;
    Task closing = Task.CompletedTask, decoding=Task.CompletedTask;
    public string Warning { get; private set; }
    public double Duration => artifact.Info.Duration;
    public bool Playing { get { lock (gate) return playing; } }
    public double Volume { get { lock (gate) return volume; } set { lock (gate) { volume = Math.Clamp(value, 0, 1); if (sound != null) sound.Volume = volume; } } }
    public double Position
    {
        get { lock (gate) return Current(); }
        set
        {
            lock (gate)
            {
                bool resume = playing; Halt(); offset = Math.Clamp(value, 0, Math.Max(0, Duration));
                Begin(!resume);
            }
        }
    }
    double Current() => Math.Clamp(offset + (sound?.Position ?? clock.Elapsed.TotalSeconds), 0, Duration > 0 ? Duration : double.MaxValue);
    public VideoPlayback(VideoArtifact artifact, Func<byte[], int, int, Task> frame, Action<Exception> failed, long memoryLimit)
    { this.artifact = artifact; this.frame = frame; this.failed = failed; this.memoryLimit = memoryLimit; }
    public void Play()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (playing) return;
            if (Duration > 0 && Current() >= Duration) { Halt(); offset = 0; }
            if (run == null) Begin();
            else { playing = true; if(primed){clock.Start();sound?.Pause(false);} }
        }
    }
    public void Pause() { lock (gate) { playing = false; clock.Stop(); sound?.Pause(true); } }
    public void Stop() { lock (gate) { Halt(); offset = 0; } }
    void Begin(bool paused = false)
    {
        var cancel=new CancellationTokenSource();run=cancel;playing=!paused;primed=false;Warning=null;clock.Reset();
        double start=offset;
        var previous=closing;
        decoding=Task.Run(async()=> {await previous;await Decode(cancel,start);});
    }
    string[] Arguments(double start) => MediaTools.Input(artifact.Path).Take(2)
        .Concat(new[] { "-ss", start.ToString("0.######", CultureInfo.InvariantCulture) })
        .Concat(MediaTools.Input(artifact.Path).Skip(2)).Concat(new[] { "-threads", "2" }).ToArray();
    async Task Decode(CancellationTokenSource owner, double start)
    {
        Process movie = null, music = null;
        PcmOutput output=null;bool ownsLease=false;Exception playbackFailure=null;
        Task musicTask = Task.CompletedTask, watchdog = Task.CompletedTask;
        Task<string> movieErrors = Task.FromResult(""), musicErrors = Task.FromResult("");
        var token = owner.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if(artifact.Info.HasAudio)
            {
                try {await AudioDeviceLease.Gate.WaitAsync(token);ownsLease=true;token.ThrowIfCancellationRequested();output=new PcmOutput {Volume=volume};output.Pause(true);}
                catch(Exception e)when(e is IOException or DllNotFoundException or TypeInitializationException){Warning="VideoMuted";}
            }
            lock(gate) {if(run!=owner){token.ThrowIfCancellationRequested();return;}sound=device=output;}
            var size = MediaInfo.Fit(artifact.Info.Width, artifact.Info.Height);
            double fps = Math.Clamp(artifact.Info.FrameRate, 1, 30);
            movie = MediaTools.Start("ffmpeg", Arguments(start).Concat(new[] { "-map", "0:V:0", "-an", "-vf",
                $"fps={fps.ToString(CultureInfo.InvariantCulture)}:start_time=0,scale={size.Width}:{size.Height}", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1" }));
            movieErrors = MediaTools.ReadErrors(movie);
            if (output != null)
            {
                music = MediaTools.Start("ffmpeg", Arguments(start).Concat(new[] { "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "48000", "-af", "aresample=async=1:first_pts=0", "-f", "s16le", "pipe:1" }));
                musicErrors = MediaTools.ReadErrors(music);
                musicTask = FeedAudioOrMute(music, output, owner);
            }
            lock (gate)
            {
                if (run != owner) { token.ThrowIfCancellationRequested(); return; }
                video = movie; audio = music;
            }
            using var registration = token.Register(() => { Kill(movie); Kill(music); });
            watchdog = Watch(movie, music, token);
            _ = watchdog.ContinueWith(task => { if (!token.IsCancellationRequested) { failed(task.Exception.GetBaseException()); owner.Cancel(); } },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // Two pending frames, one rendering frame, no unbounded dispatcher callbacks.
            var pending=Channel.CreateBounded<(byte[] Pixels,double Timestamp)>(new BoundedChannelOptions(2){SingleReader=true,SingleWriter=true});
            var free=new ConcurrentQueue<byte[]>();
            for(int i=0;i<4;i++)free.Enqueue(new byte[checked(size.Width*size.Height*4)]);
            var producer=Task.Run(async()=>
            {
                Exception error=null;long index=0;
                try
                {
                    while(true)
                    {
                        byte[] pixels;
                        while(!free.TryDequeue(out pixels))await Task.Delay(2,token);
                        if(!await ReadFrame(movie.StandardOutput.BaseStream,pixels,token)){free.Enqueue(pixels);break;}
                        double timestamp=start+index++/fps;
                        // Decode at most two frames ahead, including while paused.
                        while(index>1)
                        {
                            token.ThrowIfCancellationRequested();
                            bool ready;lock(gate)ready=playing && timestamp<=Current()+2/fps;
                            if(ready)break;await Task.Delay(5,token);
                        }
                        await pending.Writer.WriteAsync((pixels,timestamp),token);
                    }
                }
                catch(Exception e){error=e;}
                finally{pending.Writer.TryComplete(error);}
            },token);
            bool first=true;
            try
            {
                await foreach(var item in pending.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        while(!first)
                        {
                            token.ThrowIfCancellationRequested();bool ready;
                            lock(gate)ready=playing && FrameTiming.IsDue(item.Timestamp,Current());
                            if(ready)break;await Task.Delay(5,token);
                        }
                        bool late;lock(gate)late=!first && FrameTiming.IsLate(item.Timestamp,Current(),1/fps);
                        if(!late)await frame(item.Pixels,size.Width,size.Height);
                        if(first)lock(gate)
                        {
                            if(run==owner)
                            {
                                primed=true;
                                if(playing){if(sound!=null)sound.Pause(false);else clock.Restart();}
                            }
                        }
                        first=false;
                    }
                    finally{free.Enqueue(item.Pixels);}
                }
                await producer;
                if(first)throw new InvalidDataException("The video decoder could not read this media.");
            }
            catch(Exception error)
            {
                if(!token.IsCancellationRequested)playbackFailure=error;
                await owner.CancelAsync();try {await producer;}catch(Exception){}throw;
            }
            await movie.WaitForExitAsync(token);
            if (movie.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
            await musicTask;
            lock (gate) if (run == owner) { playing = false; clock.Stop(); }
        }
        catch (Exception e) when (token.IsCancellationRequested && playbackFailure==null) { _ = e; }
        catch (Exception e)
        {
            lock (gate) if (run == owner) { playing = false; clock.Stop(); }
            failed(e);
        }
        finally
        {
            owner.Cancel(); Kill(movie); Kill(music);
            try { await Task.WhenAll(musicTask, watchdog, movieErrors, musicErrors); } catch (Exception) { }
            lock (gate)
            {
                if (run == owner) { offset = Current(); clock.Reset(); output?.Dispose(); run = null; video = audio = null; sound = device = null; playing = false; }
            }
            output?.Dispose(); if(ownsLease)AudioDeviceLease.Gate.Release(); movie?.Dispose(); music?.Dispose(); owner.Dispose();
        }
    }
    async Task Watch(Process movie, Process music, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(250, token);
                long bytes = 0;
                foreach (var process in new[] { movie, music })
                    if (process != null) bytes += WorkerSupervisor.ResidentBytes(process);
                if (bytes > memoryLimit)
                { Kill(movie); Kill(music); throw new IOException("Video decoder memory limit exceeded."); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    async Task FeedAudio(Process process, PcmOutput output, CancellationTokenSource owner)
    {
        var token = owner.Token; byte[] samples = new byte[16384]; int read;
        while ((read = await process.StandardOutput.BaseStream.ReadAtLeastAsync(samples, samples.Length, false, token)) > 0)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool ready; lock (gate) ready = run == owner && playing && output.Queued < 96000;
                if (ready) break;
                await Task.Delay(10, token);
            }
            output.Push(samples, read);
            lock(gate) if(run==owner && playing && primed)output.Pause(false);
        }
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
        // Keep the audio clock advancing after the last sample, including short/silent tails.
        while (output.Queued != 0) await Task.Delay(10, token);
        lock (gate) if (run == owner) { offset += output.Position; clock.Reset(); if (playing && primed) clock.Start(); sound = null; }
    }
    async Task FeedAudioOrMute(Process process, PcmOutput output, CancellationTokenSource owner)
    {
        try { await FeedAudio(process, output, owner); }
        catch (Exception error) when (!owner.IsCancellationRequested && error is IOException or InvalidDataException or InvalidOperationException)
        {
            lock (gate)
            {
                if (run != owner) return;
                offset = Current(); output.Pause(true); sound = null; clock.Reset(); if (playing && primed) clock.Start();
                Warning = "VideoAudioUnavailable";
            }
            Kill(process);
        }
    }
    static async Task<bool> ReadFrame(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (n == 0) { if (offset != 0) throw new InvalidDataException("Incomplete video frame."); return false; }
            offset += n;
        }
        return true;
    }
    static void Kill(Process process) { try { if (process != null && !process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    void Halt()
    {
        var owner=run;var output=device;var movie=video;var music=audio;
        run=null;playing=false;primed=false;clock.Reset();sound=device=null;video=audio=null;
        var previous=closing;
        closing=Task.Run(async()=>
        {
            await previous;
            if(owner!=null) {try {await owner.CancelAsync();}catch(ObjectDisposedException){}}
            Kill(movie);Kill(music);output?.Dispose();
        });
    }
    public void Dispose()
    {
        Task close,decode;
        lock(gate){if(disposed)return;disposed=true;Halt();close=closing;decode=decoding;}
        close.GetAwaiter().GetResult();decode.GetAwaiter().GetResult();
    }

    sealed unsafe class PcmOutput : IDisposable
    {
        readonly Sdl sdl;
        readonly uint device;
        readonly object gate = new();
        long submitted;
        bool disposed;
        public double Volume { get; set; } = 1;
        public uint Queued { get { lock (gate) return disposed ? 0 : sdl.GetQueuedAudioSize(device); } }
        public double Position { get { lock (gate) return Math.Max(0, submitted - (disposed ? 0 : sdl.GetQueuedAudioSize(device))) / 192000.0; } }
        public PcmOutput()
        {
            sdl = AudioPlayback.LoadApi();
            if (sdl.InitSubSystem(0x10) != 0) { sdl.Dispose(); throw new IOException("No compatible audio output is available."); }
            AudioSpec desired = new() { Freq = 48000, Channels = 2, Format = 0x8010, Samples = 1024 }; AudioSpec obtained;
            device = sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);
            if (device == 0) { sdl.QuitSubSystem(0x10); sdl.Dispose(); throw new IOException("No compatible audio output is available."); }
            sdl.PauseAudioDevice(device, 0);
        }
        public void Pause(bool paused) { lock (gate) if (!disposed) sdl.PauseAudioDevice(device, paused ? 1 : 0); }
        public void Push(byte[] buffer, int count)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                for (int i = 0; i + 1 < count; i += 2)
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i, 2), (short)(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)) * Volume));
                fixed (byte* ptr = buffer) if (sdl.QueueAudio(device, ptr, (uint)count) != 0) throw new IOException("No compatible audio output is available.");
                submitted += count;
            }
        }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; sdl.CloseAudioDevice(device); sdl.QuitSubSystem(0x10); sdl.Dispose(); } }
    }
}
