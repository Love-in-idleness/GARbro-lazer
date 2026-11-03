using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Media;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class MediaWorkflowTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath, request, new WorkerLimits(), null, default);
    static async Task<string> Archive(string root, string name, params string[] entries)
    {
        var writer=(await Request<ResourceFormatInfo[]>(new("resource-catalog",null))).Single(f=>f.Tag=="XFL"&&f.Type=="archive");
        string path=Path.Combine(root,name);
        await Request<string>(new("resource-create",root,Output:path,FormatId:writer.Id,Inputs:entries));return path;
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RecursiveCleanupRequiresSuccessAndPreservesEverySource(int failure)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-retention-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,"child.xfl"));
            string middle=await Archive(root,"middle.xfl","child.xfl"),outer=await Archive(root,"outer.xfl","middle.xfl"),output=Path.Combine(root,"out");
            byte[] before=File.ReadAllBytes(outer);
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(outer),Output:output,
                ResourceOptions:new(Recursive:true,MaxDepth:failure==1?1:8,DiscardChildArchives:true),Extraction:new(MaxEntries:failure==2?2:100)));
            Assert.Equal(before,File.ReadAllBytes(outer));Assert.True(File.Exists(middle));Assert.True(File.Exists(Path.Combine(root,"child.xfl")));
            if(failure==0)
            {
                Assert.Empty(result.Errors);Assert.False(File.Exists(Path.Combine(output,"middle.xfl")));
                Assert.False(File.Exists(Path.Combine(output,"middle.xfl.unpacked","child.xfl")));
                Assert.Equal("Hello, GARbro!\n",File.ReadAllText(Path.Combine(output,"middle.xfl.unpacked","child.xfl.unpacked","hello.txt")));
                Assert.DoesNotContain(result.Files,f=>f.Path.EndsWith(".xfl"));
                Assert.Equal(5,result.Entries);Assert.True(result.Bytes>result.Files.Sum(f=>f.Size));
            }
            else {Assert.NotEmpty(result.Errors);Assert.True(File.Exists(Path.Combine(output,"middle.xfl")));Assert.True(File.Exists(Path.Combine(output,"middle.xfl.unpacked","child.xfl")));}
        }
        finally {Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task ConversionCleanupOnlyRemovesCopiesWithANewSuccessfulConversion(bool discard,bool skip)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-convert-retention-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("colors-rgb.png"),Path.Combine(root,"colors.png"));File.Copy(Fixture("tone.aiff"),Path.Combine(root,"tone.aiff"));
            string archive=await Archive(root,"media.xfl","colors.png","tone.aiff"),output=Path.Combine(root,"out");Directory.CreateDirectory(output);
            if(skip)File.WriteAllText(Path.Combine(output,"colors.bmp"),"unrelated existing file");
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(archive),Output:output,Overwrite:skip?OverwritePolicy.Skip:OverwritePolicy.Fail,
                ResourceOptions:new(ImageFormat:"BMP",ConvertAudio:true,DiscardOriginalMedia:discard)));
            Assert.Empty(result.Errors);Assert.True(File.Exists(Path.Combine(output,"colors.bmp")));Assert.True(File.Exists(Path.Combine(output,"tone.wav")));
            Assert.Equal(!discard||skip,File.Exists(Path.Combine(output,"colors.png")));
            Assert.Equal(!discard,File.Exists(Path.Combine(output,"tone.aiff")));
            Assert.True(File.Exists(Path.Combine(root,"colors.png")));Assert.True(File.Exists(Path.Combine(root,"tone.aiff")));
            if(skip)Assert.Equal("unrelated existing file",File.ReadAllText(Path.Combine(output,"colors.bmp")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchConversionCanIncludeOriginalOutputWithoutDeletingGameInputs(bool discard)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-batch-retention-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string output=Path.Combine(root,"colors.bmp");
            var result=await Request<ResourceConversion>(new("resource-convert",null,Location:new(Path.GetDirectoryName(Fixture("colors-rgb.png"))),Entry:"colors-rgb.png",
                Output:output,FormatId:"BMP",ResourceOptions:new(DiscardOriginalMedia:discard)));
            Assert.True(result.Written);Assert.Equal(!discard,File.Exists(Path.Combine(root,"colors.png")));
            Assert.True(File.Exists(Fixture("colors-rgb.png")));
            await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceConversion>(new("resource-convert",null,
                Location:new(root),Entry:"colors.bmp",Output:output,FormatId:"BMP",Overwrite:OverwritePolicy.Replace)));
        }
        finally {Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("movie.mpg")]
    [InlineData("silent.mp4")]
    public async Task VideoPreviewAndArtifactsWorkThroughTheWorker(string filename)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro 视频 test "+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture(filename),Path.Combine(root,filename));
            var preview=await Request<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:filename));
            Assert.Equal("video",preview.Kind);Assert.Equal((byte)'B',preview.Image[0]);Assert.Contains("160 × 90",preview.Text);
            var artifact=await Request<VideoArtifact>(new("resource-video",null,Location:new(root),Entry:filename,Output:Path.Combine(root,"cache.media")));
            Assert.Equal(File.ReadAllBytes(Fixture(filename)),File.ReadAllBytes(artifact.Path));Assert.Equal(160,artifact.Info.Width);Assert.True(artifact.Info.Duration>0);
            File.Copy(Fixture(filename),Path.Combine(root,"renamed.dat"));
            var renamed=await Request<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"renamed.dat"));Assert.Equal("video",renamed.Kind);
            File.Copy(Fixture(filename),Path.Combine(root,"renamed.txt"));
            Assert.Equal("video",(await Request<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"renamed.txt"))).Kind);
            if(filename=="movie.mpg")
            {
                var wrapper=await Request<BrowserListing>(new("resource-list",null,Location:new(Path.Combine(root,filename))));Assert.Equal("media",wrapper.Tag);
                await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<BrowserListing>(new("resource-list",null,Location:wrapper.Location.EnterArchive(filename))));
            }
            string archive=await Archive(root,"media.xfl",filename,"renamed.dat");
            var extracted=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(archive),Output:Path.Combine(root,"out"),ResourceOptions:new(Recursive:true,DiscardChildArchives:true)));
            Assert.Empty(extracted.Errors);Assert.Equal(2,extracted.Files.Length);Assert.Empty(Directory.GetDirectories(Path.Combine(root,"out")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void MetadataBoundsAndSignaturesDoNotAcceptInvalidGeometryOrNonfiniteRates()
    {
        Assert.Equal(960,MediaInfo.Fit(3840,2160).Width);
        Assert.Throws<InvalidDataException>(()=>MediaInfo.Fit(int.MaxValue,1));
        Assert.Throws<InvalidDataException>(()=>MediaInfo.Fit(32768,32768));
        Assert.Throws<InvalidDataException>(()=>MediaInfo.Parse("{\"streams\":[]}"));
        var info=MediaInfo.Parse("{\"streams\":[{\"codec_type\":\"video\",\"width\":160,\"height\":90,\"avg_frame_rate\":\"NaN/1\",\"duration\":\"Infinity\"}]}");
        Assert.Equal(25,info.FrameRate);Assert.Equal(0,info.Duration);
        Assert.False(MediaInfo.HasVideoHeader("test.mpg",new byte[]{0,0,1}));Assert.True(MediaInfo.HasVideoHeader("test.dat",new byte[]{0,0,1,0xba}));
    }
}
