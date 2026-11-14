using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Domain;
using GARbro.Media;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class ExtractionWorkflowTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath,request,new WorkerLimits(),null,default);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedArchivesNeverWidenToTheirSiblings(bool discard)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-selected-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            foreach(string name in new[]{"一.xfl","二.xfl","untouched.xfl"})File.Copy(Fixture("standard.xfl"),Path.Combine(root,name));
            Directory.CreateDirectory(Path.Combine(root,"outside"));File.Copy(Fixture("standard.xfl"),Path.Combine(root,"outside","other.xfl"));
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),
                Selection:new[]{"一.xfl","二.xfl"},ResourceOptions:new(Recursive:true,DiscardChildArchives:discard,DiscardOriginalMedia:true)));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Equal(6,report.Files.Length);Assert.Equal(6,report.Entries);
            foreach(string name in new[]{"一.xfl","二.xfl"})Assert.Equal("Hello, GARbro!\n",File.ReadAllText(Path.Combine(root,"out",name+".unpacked","hello.txt")));
            Assert.False(Directory.Exists(Path.Combine(root,"out","untouched.xfl.unpacked")));Assert.False(Directory.Exists(Path.Combine(root,"out","outside")));
            foreach(string name in new[]{"一.xfl","二.xfl","untouched.xfl"})Assert.Equal(File.ReadAllBytes(Fixture("standard.xfl")),File.ReadAllBytes(Path.Combine(root,name)));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ExtractAllInsideAnArchiveDirectoryDoesNotExtractOutsideIt()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-folder-scope-"+Guid.NewGuid());
        try
        {
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new ResourceLocation(Fixture("standard.xfl")).EnterDirectory("sub"),Output:root,
                ResourceOptions:new(Recursive:true,DiscardChildArchives:true)));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Single(report.Files);Assert.True(File.Exists(Path.Combine(root,"日本語.bin")));
            Assert.False(File.Exists(Path.Combine(root,"hello.txt")));Assert.False(Directory.Exists(Path.Combine(root,"sub")));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task SelectedMediaFromADirectoryIsCopiedAndConvertedWithoutTouchingOtherFiles()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-selected-media-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("tone.mp3"),Path.Combine(root,"声.mp3"));File.Copy(Fixture("movie.mpg"),Path.Combine(root,"画.mpg"));File.Copy(Fixture("colors-rgb.png"),Path.Combine(root,"leave.png"));
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"声.mp3","画.mpg"},
                ResourceOptions:new(Recursive:true,AudioFormat:"WAV",VideoFormat:"MP4",DiscardOriginalMedia:true)));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Equal(2,report.Files.Length);Assert.All(report.Files,f=>Assert.True(File.Exists(f.Path)));
            using var audio=new NAudio.Wave.WaveFileReader(Path.Combine(root,"out","声.wav"));Assert.True(audio.Length>0);
            var video=MediaTools.Probe(Path.Combine(root,"out","画.mp4"));Assert.True(video.HasAudio);Assert.Equal("mpeg4",video.VideoCodec);Assert.True(video.Duration>0);
            Assert.False(File.Exists(Path.Combine(root,"out","声.mp3")));Assert.False(File.Exists(Path.Combine(root,"out","画.mpg")));Assert.False(File.Exists(Path.Combine(root,"out","leave.png")));
            Assert.Equal(File.ReadAllBytes(Fixture("tone.mp3")),File.ReadAllBytes(Path.Combine(root,"声.mp3")));Assert.Equal(File.ReadAllBytes(Fixture("movie.mpg")),File.ReadAllBytes(Path.Combine(root,"画.mpg")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task UnrecognizedSelectedArchiveIsReportedInsteadOfCompletingWithZeroFiles()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-bad-selection-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"broken.xfl"),"not an XFL archive");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"broken.xfl"},ResourceOptions:new(Recursive:true,DiscardChildArchives:true)));
            Assert.Empty(report.Files);Assert.NotEmpty(report.Errors);Assert.Contains("broken.xfl",report.Errors[0]);Assert.True(File.Exists(Path.Combine(root,"broken.xfl")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("WAV","wav")]
    [InlineData("FLAC","flac")]
    [InlineData("M4A","m4a")]
    public async Task SelectedAudioFormatsAreRealDecodableOutputs(string format,string extension)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-audio-format-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"voice.aiff");File.Copy(Fixture("tone.aiff"),source);
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"voice.aiff"},
                ResourceOptions:new(AudioFormat:format,DiscardOriginalMedia:true)));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Single(report.Files);
            string output=Path.Combine(root,"out","voice."+extension);Assert.True(File.Exists(output));
            byte[] pcm=MediaTools.Run("ffmpeg",MediaTools.Input(output).Concat(new[]{"-map","0:a:0","-f","s16le","-c:a","pcm_s16le","pipe:1"}),4L<<20);
            Assert.NotEmpty(pcm);Assert.Equal(File.ReadAllBytes(Fixture("tone.aiff")),File.ReadAllBytes(source));
            var preview=await Request<ResourcePreview>(new("resource-preview",null,Location:new(Path.GetDirectoryName(output)),Entry:Path.GetFileName(output)));Assert.Equal("audio",preview.Kind);
            var prepared=await Request<AudioArtifact>(new("resource-audio",null,Location:new(Path.GetDirectoryName(output)),Entry:Path.GetFileName(output),Output:Path.Combine(root,"preview.wav")));
            Assert.True(prepared.Bytes>44);Assert.True(prepared.Duration>0);using var wave=new NAudio.Wave.WaveFileReader(prepared.Path);Assert.True(wave.Length>0);
        }
        finally {Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("MP4","mp4")]
    [InlineData("MKV","mkv")]
    public async Task SilentVideoConvertsInsideAnArchiveWithNoAudioRequirement(string format,string extension)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-video-format-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("silent.mp4"),Path.Combine(root,"scene.mp4"));
            var writer=(await Request<ResourceFormatInfo[]>(new("resource-catalog",null))).Single(f=>f.Type=="archive"&&f.Tag=="XFL");
            string archive=Path.Combine(root,"media.xfl");await Request<string>(new("resource-create",root,Output:archive,FormatId:writer.Id,Inputs:new[]{"scene.mp4"}));
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(archive),Output:Path.Combine(root,"out"),Selection:new[]{"scene.mp4"},
                ResourceOptions:new(VideoFormat:format,DiscardOriginalMedia:true)));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Single(report.Files);
            var video=MediaTools.Probe(Path.Combine(root,"out","scene."+extension));Assert.False(video.HasAudio);Assert.Equal(160,video.Width);Assert.True(video.Duration>0);
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task FailedConversionRetainsTheOutputCopyAndReportsTheFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-bad-media-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"bad.mp3"),"not audio");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"bad.mp3"},
                ResourceOptions:new(AudioFormat:"WAV",DiscardOriginalMedia:true)));
            Assert.NotEmpty(report.Errors);Assert.True(File.Exists(Path.Combine(root,"out","bad.mp3")));Assert.False(File.Exists(Path.Combine(root,"out","bad.wav")));
            Assert.DoesNotContain(Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories),path=>path.EndsWith(".tmp"));
            Assert.Equal("not audio",File.ReadAllText(Path.Combine(root,"bad.mp3")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task AnOriginalCopyConflictDoesNotRetryOrMisreportASuccessfulConversion()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-retain-conflict-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("tone.mp3"),Path.Combine(root,"voice.mp3"));Directory.CreateDirectory(Path.Combine(root,"out"));
            string original=Path.Combine(root,"out","voice.mp3");File.WriteAllText(original,"existing output");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"voice.mp3"},
                ResourceOptions:new(AudioFormat:"WAV")));
            Assert.Single(report.Errors);Assert.Single(report.Files);Assert.Equal("converted",report.Files[0].Status);
            Assert.True(File.Exists(Path.Combine(root,"out","voice.wav")));Assert.Equal("existing output",File.ReadAllText(original));
            Assert.Equal(File.ReadAllBytes(Fixture("tone.mp3")),File.ReadAllBytes(Path.Combine(root,"voice.mp3")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ConversionCannotOverwriteAnotherOriginalInTheSourceDirectory()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-protect-sources-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("tone.mp3"),Path.Combine(root,"voice.mp3"));File.WriteAllText(Path.Combine(root,"voice.wav"),"keep this original");
            await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"voice.mp3",Output:Path.Combine(root,"voice.wav"),FormatId:"WAV",Overwrite:OverwritePolicy.Replace)));
            Assert.Equal("keep this original",File.ReadAllText(Path.Combine(root,"voice.wav")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task EmptyTypeFilterCannotWidenIntoExtractingEverything()
    {
        string output=Path.Combine(Path.GetTempPath(),"garbro-no-types-"+Guid.NewGuid());
        await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceJobReport>(new("resource-extract",null,Location:new(Fixture("standard.xfl")),Output:output,ResourceOptions:new(Recursive:true,Types:Array.Empty<string>()))));
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public async Task ForcedBinaryCopiesTheSelectedArchiveWithoutOpeningItsContents()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-forced-binary-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,"data.xfl"));
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"data.xfl"},
                ResourceOptions:new(Recursive:true,DiscardChildArchives:true,TypeOverrides:new(){{"data.xfl","binary"}})));
            Assert.True(report.Errors.Length==0,string.Join(Environment.NewLine,report.Errors));Assert.Single(report.Files);
            Assert.Equal(File.ReadAllBytes(Fixture("standard.xfl")),File.ReadAllBytes(Path.Combine(root,"out","data.xfl")));Assert.False(Directory.Exists(Path.Combine(root,"out","data.xfl.unpacked")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task BatchConversionUsesTheChosenFormatForEachMediaKind()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-mixed-batch-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var settings=new ResourceExtractionOptions(ImageFormat:"BMP",AudioFormat:"WAV",VideoFormat:"MP4",DiscardOriginalMedia:true);
            foreach(var item in new[]{("colors-rgb.png","scene.png","scene.bmp"),("tone.mp3","voice.mp3","voice.wav"),("movie.mpg","video.mpg","video.mp4")})
            {
                File.Copy(Fixture(item.Item1),Path.Combine(root,item.Item2));
                var result=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:item.Item2,Output:Path.Combine(root,"out",item.Item2),FormatId:"MEDIA",ResourceOptions:settings));
                Assert.Equal(Path.Combine(root,"out",item.Item3),result.Path);Assert.True(result.Written);Assert.True(File.Exists(result.Path));Assert.True(File.Exists(Path.Combine(root,item.Item2)));
            }
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ASelectedDirectoryCannotBeMixedIntoAnArchivesOutputNamespace()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-output-conflict-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,"data.xfl"));Directory.CreateDirectory(Path.Combine(root,"data.xfl.unpacked"));File.WriteAllText(Path.Combine(root,"data.xfl.unpacked","foreign.txt"),"keep separate");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:Path.Combine(root,"out"),Selection:new[]{"data.xfl","data.xfl.unpacked"},ResourceOptions:new(Recursive:true)));
            Assert.NotEmpty(report.Errors);Assert.False(File.Exists(Path.Combine(root,"out","data.xfl.unpacked","foreign.txt")));Assert.True(File.Exists(Path.Combine(root,"data.xfl.unpacked","foreign.txt")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void DirectoryAndSelectionMustBothMatch()
    {
        Assert.True(ExtractionScope.Includes("sub/",new[]{"sub/深"},"sub\\深\\a.bin"));
        Assert.False(ExtractionScope.Includes("sub/",null,"outside/a.bin"));
        Assert.False(ExtractionScope.Includes("sub/",Array.Empty<string>(),"sub/a.bin"));
        Assert.False(ExtractionScope.Includes("sub/",new[]{"sub/a.bin"},"sub/a.bin2"));
    }
}
