using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using global::Android.Content;
using global::Android.Media;
using GARbro.Application;
using NAudio.Wave;

namespace GARbro.Mobile;

public sealed class AndroidAudioOutput : Java.Lang.Object, IAudioOutput, AudioManager.IOnAudioFocusChangeListener
{
    readonly AudioTrack track;
    readonly AudioManager manager;
    readonly AudioFocusRequestClass focus;
    readonly WaveFileReader input;
    readonly CancellationTokenSource lifetime = new();
    readonly Task feeder;
    readonly object gate = new();
    readonly Context context;
    readonly NoisyReceiver noisy;
    bool playing, disposed;
    double volume = 1, seek;
    public double Duration => input.TotalTime.TotalSeconds;
    public bool Playing => playing;
    public double Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); track.SetVolume((float)volume); } }
    public double Position
    {
        get => Math.Clamp(seek + (uint)track.PlaybackHeadPosition / (double)input.WaveFormat.SampleRate, 0, Duration);
        set
        {
            lock (gate)
            {
                bool resume = playing; track.Pause(); track.Flush(); seek = Math.Clamp(value, 0, Duration);
                input.Position = (long)(seek * input.WaveFormat.SampleRate) * input.WaveFormat.BlockAlign;
                // Flush resets the playback head for streaming tracks.
                if (resume) track.Play();
            }
        }
    }
    public AndroidAudioOutput(Context context, string path)
    {
        this.context = context; input = new WaveFileReader(path);
        if (input.WaveFormat.Encoding != WaveFormatEncoding.Pcm || input.WaveFormat.BitsPerSample != 16 || input.WaveFormat.Channels is < 1 or > 2)
        { input.Dispose(); throw new NotSupportedException("Android playback requires mono or stereo PCM16 audio."); }
        try
        {
            manager = (AudioManager)context.GetSystemService(Context.AudioService);
            var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media).SetContentType(AudioContentType.Music).Build();
            focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain).SetAudioAttributes(attributes).SetOnAudioFocusChangeListener(this).Build();
            var channels = input.WaveFormat.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
            int size = Math.Max(input.WaveFormat.AverageBytesPerSecond / 4, AudioTrack.GetMinBufferSize(input.WaveFormat.SampleRate, channels, global::Android.Media.Encoding.Pcm16bit));
            track = new AudioTrack.Builder().SetAudioAttributes(attributes).SetAudioFormat(new AudioFormat.Builder().SetEncoding(global::Android.Media.Encoding.Pcm16bit)
                .SetSampleRate(input.WaveFormat.SampleRate).SetChannelMask(channels).Build()).SetBufferSizeInBytes(size).SetTransferMode(AudioTrackMode.Stream).Build();
            if (track.State != AudioTrackState.Initialized) throw new IOException("No compatible audio output is available.");
            noisy = new NoisyReceiver(this);
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
                context.RegisterReceiver(noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy), ReceiverFlags.NotExported);
            else context.RegisterReceiver(noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
            feeder = Task.Run(Feed);
        }
        catch { input.Dispose(); track?.Release(); throw; }
    }
    void Feed()
    {
        byte[] bytes = new byte[4096 / input.WaveFormat.BlockAlign * input.WaveFormat.BlockAlign];
        while (!lifetime.IsCancellationRequested)
        {
            lock (gate)
            {
                if (playing)
                {
                    long before = input.Position; int read = input.Read(bytes, 0, bytes.Length);
                    if (read == 0) { if (Position >= Duration - .02) playing = false; }
                    else { int written = track.Write(bytes, 0, read, WriteMode.NonBlocking); if (written < 0) playing = false; input.Position = before + Math.Max(0, written); }
                }
            }
            lifetime.Token.WaitHandle.WaitOne(10);
        }
    }
    public void Play() { if (manager.RequestAudioFocus(focus) != AudioFocusRequest.Granted) throw new IOException("Audio output is in use."); if (Position >= Duration) Position = 0; playing = true; track.Play(); }
    public void Pause() { playing = false; track.Pause(); }
    public void Stop() { Pause(); Position = 0; }
    public void OnAudioFocusChange(AudioFocus focusChange) { if (focusChange != AudioFocus.Gain) Pause(); }
    void IDisposable.Dispose()
    {
        if (disposed) return; disposed = true; playing = false; lifetime.Cancel(); feeder.GetAwaiter().GetResult();
        track.Pause(); track.Flush(); track.Release(); manager.AbandonAudioFocusRequest(focus); context.UnregisterReceiver(noisy);
        input.Dispose(); lifetime.Dispose(); base.Dispose();
    }
    sealed class NoisyReceiver(AndroidAudioOutput owner) : BroadcastReceiver
    { public override void OnReceive(Context context, Intent intent) => owner.Pause(); }
}
