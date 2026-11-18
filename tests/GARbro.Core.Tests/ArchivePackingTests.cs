using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Domain;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class ArchivePackingTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath,request,new WorkerLimits(),null,default);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvertsSelectedMediaBeforePackingAndNeverChangesSources(bool discard)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-pack-"+Guid.NewGuid()),input=Path.Combine(root,"input"),folder=Path.Combine(input,"中文 Русский 日本語");Directory.CreateDirectory(folder);
        try
        {
            foreach(var pair in new[]{("colors-rgba.png","scene.png"),("tone.mp3","voice.mp3"),("movie.mpg","movie.mpg")})File.Copy(Fixture(pair.Item1),Path.Combine(folder,pair.Item2));
            File.WriteAllText(Path.Combine(folder,"notes.txt"),"keep me");File.WriteAllText(Path.Combine(input,"leave.txt"),"not selected");
            var originals=Directory.GetFiles(folder).ToDictionary(Path.GetFileName,File.ReadAllBytes);
            string output=Path.Combine(root,"output.zip");
            var result=await Request<ArchivePackingResult>(new("resource-pack",input,Output:output,Inputs:new[]{Path.GetFileName(folder)},PackingOptions:new("WEBP","WAV","MP4",discard)));
            Assert.True(result.Written);Assert.Equal(3,result.Converted);Assert.Equal(discard?4:7,result.Entries);
            var list=await Request<BrowserListing>(new("resource-list",null,Location:new ResourceLocation(output).EnterDirectory(Path.GetFileName(folder))));
            Assert.Equal(result.Entries,list.Entries.Length);Assert.Contains(list.Entries,e=>e.DisplayName=="scene.webp");Assert.Contains(list.Entries,e=>e.DisplayName=="voice.wav");Assert.Contains(list.Entries,e=>e.DisplayName=="movie.mp4");
            Assert.DoesNotContain(list.Entries,e=>e.DisplayName=="leave.txt");Assert.Equal(!discard,list.Entries.Any(e=>e.DisplayName=="scene.png"));
            var extracted=Path.Combine(root,"extracted");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(output),Output:extracted));Assert.Empty(report.Errors);
            string media=Path.Combine(extracted,Path.GetFileName(folder));
            Assert.Equal("RIFF",System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(media,"scene.webp")),0,4));
            using(var audio=new NAudio.Wave.WaveFileReader(Path.Combine(media,"voice.wav")))Assert.True(audio.Length>0);
            Assert.True(MediaTools.Probe(Path.Combine(media,"movie.mp4")).Width>0);
            foreach(var original in originals)Assert.Equal(original.Value,File.ReadAllBytes(Path.Combine(folder,original.Key)));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task PacksWritableGameImagesAndRecompiledScripts()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-pack-script-"+Guid.NewGuid());
        string input=Directory.CreateDirectory(Path.Combine(root,"input")).FullName;
        try
        {
            string script=Path.Combine(AppContext.BaseDirectory,"NativeScripts","modern.tsc");
            File.Copy(script,Path.Combine(input,"scene.tsc"));
            File.Copy(Fixture("colors-rgba.png"),Path.Combine(input,"image.png"));
            File.WriteAllText(Path.Combine(input,"notes.txt"),"keep me");
            string output=Path.Combine(root,"out.xfl");
            var result=await Request<ArchivePackingResult>(new("resource-pack",input,Output:output,
                PackingOptions:new(ImageFormat:"WCG",DiscardOriginalMedia:true,ConvertScripts:true,ScriptFormat:"GSC")));
            Assert.True(result.Written);Assert.Equal(2,result.Converted);Assert.Equal(3,result.Entries);
            string extracted=Path.Combine(root,"extracted");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(output),Output:extracted));
            Assert.Empty(report.Errors);
            Assert.Equal(new[]{"image.wcg","notes.txt","scene.gsc"},Directory.GetFiles(extracted).Select(Path.GetFileName).OrderBy(n=>n).ToArray());
            Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeScripts","modern.gsc")),File.ReadAllBytes(Path.Combine(extracted,"scene.gsc")));
            var preview=await Request<ResourcePreview>(new("resource-preview",null,Location:new(output),Entry:"image.wcg"));
            Assert.Equal("image",preview.Kind);
            Assert.Equal(File.ReadAllBytes(script),File.ReadAllBytes(Path.Combine(input,"scene.tsc")));
            Assert.Equal(File.ReadAllBytes(Fixture("colors-rgba.png")),File.ReadAllBytes(Path.Combine(input,"image.png")));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task AutoAudioPassthroughIsPackedOnceAndSkipIsReportedTruthfully()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-pack-auto-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("tone.wav"),Path.Combine(root,"tone.wav"));string output=Path.Combine(root,"out.xfl");
            var result=await Request<ArchivePackingResult>(new("resource-pack",root,Output:output,Inputs:new[]{"tone.wav"},PackingOptions:new(AudioFormat:"AUTO")));
            Assert.Equal(1,result.Entries);Assert.Equal(0,result.Converted);
            var skipped=await Request<ArchivePackingResult>(new("resource-pack",root,Output:output,Inputs:new[]{"tone.wav"},Overwrite:OverwritePolicy.Skip));
            Assert.False(skipped.Written);Assert.Equal(0,skipped.Entries);
        }
        finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedConversionsAndNameCollisionsPreserveAnExistingOutput(bool collision)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-pack-fail-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            if(collision){File.Copy(Fixture("colors-rgb.png"),Path.Combine(root,"scene.png"));File.Copy(Fixture("top-down.bmp"),Path.Combine(root,"scene.bmp"));}
            else File.WriteAllText(Path.Combine(root,"scene.png"),"not a decodable image");
            string output=Path.Combine(root,"keep.xfl");File.WriteAllText(output,"previous archive");
            string[] inputs=collision?new[]{"scene.png","scene.bmp"}:new[]{"scene.png"};
            var error=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ArchivePackingResult>(new("resource-pack",root,Output:output,Inputs:inputs,Overwrite:OverwritePolicy.Replace,PackingOptions:new(ImageFormat:"BMP",DiscardOriginalMedia:true))));
            Assert.Contains(collision?"Colliding input paths":"Packing conversion failed",error.Message);
            Assert.Equal("previous archive",File.ReadAllText(output));Assert.Equal(inputs.Length+1,Directory.GetFiles(root).Length);
        }
        finally {Directory.Delete(root,true);}
    }

    sealed class Progress(Action<ExtractionProgress> action) : IProgress<ExtractionProgress> {public void Report(ExtractionProgress value)=>action(value);}
    [Fact]
    public void CancellationAndLimitsNeverReplaceThePreviousArchive()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-pack-cancel-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"input.txt"),new string('a',4096));string output=Path.Combine(root,"keep.xfl");File.WriteAllText(output,"previous");
            Assert.Throws<InvalidDataException>(()=>ResourceService.PackArchive(root,new[]{"input.txt"},output,"XFL",overwrite:OverwritePolicy.Replace,limits:new(MaxEntryBytes:10)));
            using var cancel=new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(()=>ResourceService.PackArchive(root,new[]{"input.txt"},output,"XFL",overwrite:OverwritePolicy.Replace,
                progress:new Progress(p=>{if(p.Phase=="write")cancel.Cancel();}),cancellation:cancel.Token));
            Assert.Equal("previous",File.ReadAllText(output));Assert.Equal(2,Directory.GetFiles(root).Length);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void InputRootsAndCliOptionsAreExplicitAndRelativeNamesArePortable()
    {
        string root=Path.Combine(Path.GetTempPath(),"pack-root");
        Assert.Equal(root,ArchivePackingPlan.CommonRoot(new[]{Path.Combine(root,"child"),Path.Combine(root,"other.txt")},new[]{true,false}));
        Assert.Equal(root,ArchivePackingPlan.CommonRoot(new[]{root},new[]{true}));
        Assert.Equal("sub/é.txt",ArchivePackingPlan.EntryName("sub\\e\u0301.txt",true));Assert.Equal("file.txt",ArchivePackingPlan.EntryName("sub/file.txt",false));
        var parsed=CliArguments.Parse(new[]{"create","--format","XFL","--image-format","webp","--discard-original-media","-o","out.xfl","--","-input.txt"});
        Assert.Equal("create",parsed.Command);Assert.Equal(new[]{"-input.txt"},parsed.Inputs);
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(new[]{"create","in","--nested"}));
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(new[]{"create","in","--discard-original-media"}));
    }
}
