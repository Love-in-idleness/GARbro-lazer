using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Domain;
using GARbro.Imaging;
using GARbro.Worker;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class WebpWorkflowTests
{
    static string Fixture(string name)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    static Task<T> Request<T>(WorkerRequest request)=>WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath,request,new WorkerLimits(),null,default);
    [Theory]
    [InlineData(1,1)]
    [InlineData(3,2)]
    [InlineData(17,9)]
    public void WebpWriterRoundTripsTinyOddSizedAndTransparentPixels(int width,int height)
    {
        ArchiveService.Formats();
        byte[] pixels=Enumerable.Range(0,width*height).SelectMany(i=>new[]{(byte)(i*13+41),(byte)(i*7+83),(byte)(i*23+111),(byte)(i%3==0?0:i%3==1?87:255)}).ToArray();
        var image=ImageData.Create(new ImageMetaData {Width=(uint)width,Height=(uint)height,BPP=32},PixelFormats.Bgra32,null,pixels,width*4);
        var writer=ResourceService.ResolveImageOutput(".webp");Assert.True(writer.CanWrite);
        using var output=new MemoryStream();writer.Write(output,image);
        Assert.Equal("RIFF",System.Text.Encoding.ASCII.GetString(output.ToArray(),0,4));
        output.Position=0;using var input=new BinaryStream(output,"image.webp",true);
        var metadata=writer.ReadMetaData(input);Assert.NotNull(metadata);input.Position=0;
        Assert.Equal(pixels,writer.Read(input,metadata).Bitmap.ToBgra32());
    }
    [Fact]
    public async Task ExtractedWebpKeepsAlphaAndOnlyRemovesTheOutputOriginal()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-webp-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"image.png");File.Copy(Fixture("colors-rgba.png"),source);
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Selection:new[]{"image.png"},Output:Path.Combine(root,"out"),ResourceOptions:new(ImageFormat:"webp",DiscardOriginalMedia:true)));
            Assert.Empty(result.Errors);Assert.Single(result.Files);Assert.False(File.Exists(Path.Combine(root,"out","image.png")));
            Assert.Equal(File.ReadAllBytes(Fixture("colors-rgba.png")),File.ReadAllBytes(source));
            Assert.Equal(ArchiveService.ReadImage(source).Bitmap.ToBgra32(),ArchiveService.ReadImage(Path.Combine(root,"out","image.webp")).Bitmap.ToBgra32());
            var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));Assert.Contains(catalog,f=>f.Tag=="WEBP"&&f.CanWrite);
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task DiskRecursionCanRunWithoutExpandingNestedArchives()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-independent-recursion-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string disk=Path.Combine(root,"disk");Directory.CreateDirectory(Path.Combine(disk,"deep"));
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,"child.xfl"));File.Copy(Fixture("standard.xfl"),Path.Combine(disk,"deep","inside.xfl"));
            var writer=(await Request<ResourceFormatInfo[]>(new("resource-catalog",null))).Single(f=>f.Type=="archive"&&f.Tag=="XFL");
            await Request<string>(new("resource-create",root,Output:Path.Combine(disk,"outer.xfl"),FormatId:writer.Id,Inputs:new[]{"child.xfl"}));
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(disk),Output:Path.Combine(root,"out"),ResourceOptions:new(Recursive:false,RecurseDirectories:true)));
            Assert.Empty(result.Errors);Assert.True(File.Exists(Path.Combine(root,"out","deep","inside.xfl.unpacked","hello.txt")));
            Assert.True(File.Exists(Path.Combine(root,"out","outer.xfl.unpacked","child.xfl")));Assert.False(Directory.Exists(Path.Combine(root,"out","outer.xfl.unpacked","child.xfl.unpacked")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void CliParsingPreservesLiteralPathsAndBooleanOptions()
    {
        var parsed=CliArguments.Parse(new[]{"--timeout","10","extract","--image-format=webp","--discard-original-media","-o","out","--","-name.xfl"});
        Assert.Equal("extract",parsed.Command);Assert.Equal(new[]{"-name.xfl"},parsed.Inputs);Assert.Contains("--discard-original-media",parsed.Flags);
        Assert.Equal("webp",parsed.Values["--image-format"]);Assert.Equal("out",parsed.Values["--output"]);
        Assert.True(CliArguments.Parse(new[]{"help","convert"}).Help);Assert.True(CliArguments.Parse(new[]{"extract","--help"}).Help);
    }
    [Theory]
    [InlineData("convert --nested image.png -o out.png")]
    [InlineData("extract --discard-child-archives input.xfl -o out")]
    [InlineData("extract --discard-original-media input.xfl -o out")]
    [InlineData("extract input.xfl -o first --output second")]
    [InlineData("extract input.xfl -o --nested")]
    [InlineData("--image-format webp")]
    public void CliRejectsConflictingOrIgnoredOptions(string args)=>Assert.Throws<ArgumentException>(()=>CliArguments.Parse(args.Split(' ')));
}
