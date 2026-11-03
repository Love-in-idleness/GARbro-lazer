using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GARbro.Application;
using NAudio.Wave;
using Silk.NET.SDL;

namespace GARbro.Desktop;

/// <summary>Audio-device output only. Original game formats are decoded in the worker.</summary>
internal static class AudioDeviceLease { internal static readonly SemaphoreSlim Gate=new(1,1); }

public sealed unsafe class AudioPlayback : IAudioOutput
{
    public static Sdl LoadApi()
    {
        if (OperatingSystem.IsLinux())
            return new Sdl(Sdl.CreateDefaultContext(new[] { "libSDL2-2.0.so.0", "libSDL2-2.0.so", "libSDL2.so" }));
        string name = OperatingSystem.IsWindows() ? "SDL2.dll" : OperatingSystem.IsMacOS() ? "libSDL2-2.0.dylib" : "libSDL2-2.0.so";
        string rid = OperatingSystem.IsMacOS() ? "osx" : (OperatingSystem.IsWindows() ? "win-" : "linux-") + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string path = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name);
        if (!File.Exists(path)) throw new FileNotFoundException("The bundled SDL audio library is missing.", name);
        return new Sdl(Sdl.CreateDefaultContext(new[] { path }));
    }
    readonly Sdl sdl;
    readonly uint device;
    readonly WaveFileReader input;
    readonly object gate = new();
    readonly CancellationTokenSource cancel = new();
    readonly Task feeder;
    bool playing;
    int disposed;
    float volume = 1;
    public double Duration => input.TotalTime.TotalSeconds;
    public bool Playing => playing;
    public double Volume
    {
        get => volume;
        set
        {
            lock (gate)
            {
                long current = input.Position - sdl.GetQueuedAudioSize(device);
                sdl.ClearQueuedAudio(device); input.Position = Math.Max(0, current);
                volume = (float)Math.Clamp(value, 0, 1);
            }
        }
    }
    public double Position
    {
        get { lock (gate) return Math.Clamp((input.Position - sdl.GetQueuedAudioSize(device)) / (double)input.WaveFormat.AverageBytesPerSecond, 0, Duration); }
        set { lock (gate) { sdl.ClearQueuedAudio(device); input.Position = (long)(Math.Clamp(value, 0, Duration) * input.WaveFormat.SampleRate) * input.WaveFormat.BlockAlign; } }
    }
    public AudioPlayback(string path)
    {
        input = new WaveFileReader(path);
        if (input.WaveFormat.Encoding != WaveFormatEncoding.Pcm || input.WaveFormat.BitsPerSample != 16) { input.Dispose(); throw new InvalidDataException("Expected PCM16 audio."); }
        AudioDeviceLease.Gate.Wait();
        try
        {
        try { sdl = LoadApi(); }
        catch { input.Dispose(); throw; }
        if (sdl.InitSubSystem(0x10) != 0) { string error = sdl.GetErrorS(); input.Dispose(); sdl.Dispose(); throw new IOException(error); }
        AudioSpec desired = new() { Freq = input.WaveFormat.SampleRate, Channels = (byte)input.WaveFormat.Channels, Format = 0x8010, Samples = 2048 };
        AudioSpec obtained;
        device = sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);
        if (device == 0) { string error = sdl.GetErrorS(); input.Dispose(); sdl.QuitSubSystem(0x10); sdl.Dispose(); throw new IOException(error); }
        feeder = Task.Run(Feed);
        }
        catch {AudioDeviceLease.Gate.Release();throw;}
    }
    void Feed()
    {
        byte[] buffer = new byte[16384 / input.WaveFormat.BlockAlign * input.WaveFormat.BlockAlign];
        while (!cancel.IsCancellationRequested)
        {
            lock (gate)
            {
                if (playing && sdl.GetQueuedAudioSize(device) < input.WaveFormat.AverageBytesPerSecond)
                {
                    int read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0) { if (sdl.GetQueuedAudioSize(device) == 0) playing = false; }
                    else
                    {
                        float gain = volume;
                        for (int i = 0; i + 1 < read; i += 2)
                            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i, 2), (short)(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)) * gain));
                        fixed (byte* pointer = buffer) if (sdl.QueueAudio(device, pointer, (uint)read) != 0) { playing = false; }
                    }
                }
            }
            cancel.Token.WaitHandle.WaitOne(20);
        }
    }
    public void Play() { lock (gate) { if (input.Position == input.Length && sdl.GetQueuedAudioSize(device) == 0) input.Position = 0; playing = true; sdl.PauseAudioDevice(device, 0); } }
    public void Pause() { lock (gate) { playing = false; sdl.PauseAudioDevice(device, 1); } }
    public void Stop() { Pause(); Position = 0; }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        cancel.Cancel(); feeder.GetAwaiter().GetResult(); sdl.CloseAudioDevice(device); sdl.QuitSubSystem(0x10); input.Dispose(); cancel.Dispose(); sdl.Dispose();AudioDeviceLease.Gate.Release();
    }
}
