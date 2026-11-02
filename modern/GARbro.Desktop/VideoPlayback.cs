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
    PcmOutput sound;
    double offset, volume = 1;
    bool playing, disposed;
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
                if (resume) Begin();
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
            else { playing = true; clock.Start(); sound?.Pause(false); }
        }
    }
    public void Pause() { lock (gate) { playing = false; clock.Stop(); sound?.Pause(true); } }
    public void Stop() { lock (gate) { Halt(); offset = 0; } }
    void Begin()
    {
        var cancel = new CancellationTokenSource(); run = cancel; playing = true; Warning = null;
        PcmOutput output = null;
        if (artifact.Info.HasAudio)
        {
            try { output = new PcmOutput { Volume = volume }; }
            catch (Exception e) when (e is IOException or DllNotFoundException or TypeInitializationException) { Warning = "VideoMuted"; }
        }
        sound = output; clock.Restart();
        double start = offset;
        _ = Task.Run(() => Decode(cancel, output, start));
    }
    string[] Arguments(double start) => MediaTools.Input(artifact.Path).Take(2)
        .Concat(new[] { "-ss", start.ToString("0.######", CultureInfo.InvariantCulture) })
        .Concat(MediaTools.Input(artifact.Path).Skip(2)).ToArray();
    async Task Decode(CancellationTokenSource owner, PcmOutput output, double start)
    {
        Process movie = null, music = null;
        Task musicTask = Task.CompletedTask, watchdog = Task.CompletedTask;
        Task<string> movieErrors = Task.FromResult(""), musicErrors = Task.FromResult("");
        var token = owner.Token;
        try
        {
            var size = MediaInfo.Fit(artifact.Info.Width, artifact.Info.Height);
            double fps = Math.Clamp(artifact.Info.FrameRate, 1, 30);
            movie = MediaTools.Start("ffmpeg", Arguments(start).Concat(new[] { "-map", "0:v:0", "-an", "-vf",
                $"fps={fps.ToString(CultureInfo.InvariantCulture)},scale={size.Width}:{size.Height}", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1" }));
            movieErrors = MediaTools.ReadErrors(movie);
            if (output != null)
            {
                music = MediaTools.Start("ffmpeg", Arguments(start).Concat(new[] { "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "48000", "-f", "s16le", "pipe:1" }));
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
            byte[] pixels = new byte[checked(size.Width * size.Height * 4)];
            long index = 0;
            while (await ReadFrame(movie.StandardOutput.BaseStream, pixels, token))
            {
                double timestamp = start + index++ / fps;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    bool ready;
                    lock (gate) ready = playing && timestamp <= Current() + .015;
                    if (ready) break;
                    await Task.Delay(10, token);
                }
                await frame(pixels, size.Width, size.Height);
            }
            await movie.WaitForExitAsync(token);
            if (movie.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
            await musicTask;
            lock (gate) if (run == owner) { playing = false; clock.Stop(); }
        }
        catch (Exception e) when (token.IsCancellationRequested) { _ = e; }
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
                if (run == owner) { offset = Current(); clock.Reset(); run = null; video = audio = null; sound = null; playing = false; }
            }
            output?.Dispose(); movie?.Dispose(); music?.Dispose(); owner.Dispose();
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
                    if (process != null && !process.HasExited) { process.Refresh(); bytes += process.WorkingSet64; }
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
        }
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
        // Keep the audio clock advancing after the last sample, including short/silent tails.
        while (output.Queued != 0) await Task.Delay(10, token);
        lock (gate) if (run == owner) { offset += output.Position; clock.Reset(); if (playing) clock.Start(); sound = null; }
    }
    async Task FeedAudioOrMute(Process process, PcmOutput output, CancellationTokenSource owner)
    {
        try { await FeedAudio(process, output, owner); }
        catch (Exception error) when (!owner.IsCancellationRequested && error is IOException or InvalidOperationException)
        {
            lock (gate)
            {
                if (run != owner) return;
                offset = Current(); output.Pause(true); sound = null; clock.Reset(); if (playing) clock.Start();
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
        run?.Cancel(); run = null; playing = false; clock.Reset(); sound = null;
        Kill(video); Kill(audio); video = audio = null;
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; Halt(); } }

    sealed unsafe class PcmOutput : IDisposable
    {
        readonly Sdl sdl;
        readonly uint device;
        readonly object gate = new();
        long submitted;
        public double Volume { get; set; } = 1;
        public uint Queued { get { lock (gate) return sdl.GetQueuedAudioSize(device); } }
        public double Position { get { lock (gate) return Math.Max(0, submitted - sdl.GetQueuedAudioSize(device)) / 192000.0; } }
        public PcmOutput()
        {
            sdl = AudioPlayback.LoadApi();
            if (sdl.InitSubSystem(0x10) != 0) { sdl.Dispose(); throw new IOException("No compatible audio output is available."); }
            AudioSpec desired = new() { Freq = 48000, Channels = 2, Format = 0x8010, Samples = 1024 }; AudioSpec obtained;
            device = sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);
            if (device == 0) { sdl.QuitSubSystem(0x10); sdl.Dispose(); throw new IOException("No compatible audio output is available."); }
            sdl.PauseAudioDevice(device, 0);
        }
        public void Pause(bool paused) { lock (gate) sdl.PauseAudioDevice(device, paused ? 1 : 0); }
        public void Push(byte[] buffer, int count)
        {
            lock (gate)
            {
                for (int i = 0; i + 1 < count; i += 2)
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i, 2), (short)(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)) * Volume));
                fixed (byte* ptr = buffer) if (sdl.QueueAudio(device, ptr, (uint)count) != 0) throw new IOException("No compatible audio output is available.");
                submitted += count;
            }
        }
        public void Dispose() { lock (gate) { sdl.CloseAudioDevice(device); sdl.QuitSubSystem(0x10); sdl.Dispose(); } }
    }
}
