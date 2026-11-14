using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Worker;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class ResourceBrowserTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath,
        request,new WorkerLimits(),null,default);

    [Fact]
    public async Task BrowserEntersDirectoriesAndArchivesWithoutExtractingThem()
    {
        var listing=await Request<BrowserListing>(new("resource-list",null,Location:new ResourceLocation(Fixture("standard.xfl"))));
        Assert.Equal("XFL",listing.Tag);
        Assert.Contains(listing.Entries,e=>e.IsDirectory && e.Name=="sub");
        var sub=await Request<BrowserListing>(new("resource-list",null,Location:listing.Location.EnterDirectory("sub")));
        Assert.Single(sub.Entries);
        Assert.Equal("日本語.bin",sub.Entries[0].DisplayName);
        var text=await Request<ResourcePreview>(new("resource-preview",null,Location:listing.Location,Entry:"hello.txt"));
        Assert.Contains("Hello, GARbro!",text.Text);
    }

    [Theory]
    [InlineData("child.unknown")]
    [InlineData("child.xfl")]
    public async Task NestedVfsBrowsingAndRecursiveExtractionKeepTheChildArchive(string childName)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-nested-"+Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,childName));
            var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            var xfl=catalog.Single(f=>f.Tag=="XFL" && f.Type=="archive");
            string outer=Path.Combine(root,"outer.xfl");
            await Request<string>(new("resource-create",root,Output:outer,FormatId:xfl.Id,Inputs:new[]{childName}));
            var nested=new ResourceLocation(outer).EnterArchive(childName);
            var listing=await Request<BrowserListing>(new("resource-list",null,Location:nested));
            Assert.Equal("XFL",listing.Tag);
            string output=Path.Combine(root,"output");
            var report=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(outer),Output:output,
                ResourceOptions:new(Recursive:true)));
            Assert.Empty(report.Errors);
            Assert.True(File.Exists(Path.Combine(output,childName)));
            Assert.Equal("Hello, GARbro!\n",File.ReadAllText(Path.Combine(output,childName+".unpacked","hello.txt")));
            string limited=Path.Combine(root,"limited");
            var constrained=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(outer),Output:limited,
                ResourceOptions:new(Recursive:true),Extraction:new(MaxEntries:1)));
            Assert.NotEmpty(constrained.Errors);
            Assert.True(File.Exists(Path.Combine(limited,childName)));
            Assert.False(File.Exists(Path.Combine(limited,childName+".unpacked","hello.txt")));
            Assert.DoesNotContain(Directory.EnumerateFiles(limited,"*",SearchOption.AllDirectories),p=>p.EndsWith(".tmp"));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task NestedParameterRetryKeepsCommittedOutputsAndCannotResetTheBudget()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-retry-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("standard.xp3"),Path.Combine(root,"child.xp3"));
            File.Copy(Fixture("standard.xp3"),Path.Combine(root,"second.xp3"));
            var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            var writer=catalog.Single(f=>f.Tag=="XFL"&&f.Type=="archive");
            var xp3=catalog.Single(f=>f.Tag=="XP3"&&f.Type=="archive");
            string outer=Path.Combine(root,"outer.xfl"),output=Path.Combine(root,"out");
            await Request<string>(new("resource-create",root,Output:outer,FormatId:writer.Id,Inputs:new[]{"child.xp3","second.xp3"}));
            var missing=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceJobReport>(new("resource-extract",null,
                Location:new(outer),Output:output,ResourceOptions:new(Recursive:true))));
            Assert.Equal(xp3.Id,missing.FormatId);Assert.Equal(2,missing.CompletedPaths.Length);Assert.Contains("child.xp3",missing.ContextKey);
            string retained=missing.CompletedPaths[0];var written=File.GetLastWriteTimeUtc(retained);
            var context=new DecoderContext(OptionsByFormat:new(){{missing.ContextKey,"{\"$scheme\":\"none\"}"}});
            var second=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceJobReport>(new("resource-extract",null,
                Location:new(outer),Output:output,ResourceOptions:new(Recursive:true),Context:context,CompletedPaths:missing.CompletedPaths)));
            Assert.Contains("second.xp3",second.ContextKey);Assert.NotEqual(missing.ContextKey,second.ContextKey);
            context.OptionsByFormat[second.ContextKey]="{\"$scheme\":\"none\"}";
            var resumed=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(outer),Output:output,
                ResourceOptions:new(Recursive:true),Context:context,CompletedPaths:second.CompletedPaths));
            Assert.Empty(resumed.Errors);Assert.Equal(written,File.GetLastWriteTimeUtc(retained));
            Assert.Equal("Hello, GARbro!\n",File.ReadAllText(Path.Combine(output,"child.xp3.unpacked","hello.txt")));
            await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceJobReport>(new("resource-extract",null,
                Location:new(outer),Output:output,Context:context,ResourceOptions:new(Recursive:true),CompletedPaths:missing.CompletedPaths,
                Extraction:new(MaxTotalBytes:1))));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task PreviewDecodesScriptsAndPagesBinaryWithoutReturningTheWholeResource()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-script-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            byte[] script=System.Text.Encoding.UTF8.GetBytes("*start\nHello decoded script\n");
            File.WriteAllBytes(Path.Combine(root,"nscript.dat"),script.Select(b=>(byte)(b^0x84)).ToArray());
            var text=await Request<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"nscript.dat"));
            Assert.Equal("text",text.Kind);Assert.Contains("Hello decoded script",text.Text);
            File.WriteAllText(Path.Combine(root,"unicode.txt"),new string('你',40000),System.Text.Encoding.Unicode);
            var next=await Request<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"unicode.txt",Offset:65536));
            Assert.Equal("text",next.Kind);Assert.StartsWith("你",next.Text);
        }
        finally {Directory.Delete(root,true);}
        var binary=await Request<ResourcePreview>(new("resource-preview",null,Location:new(Path.GetDirectoryName(Fixture("standard.xfl"))),
            Entry:"standard.xfl",ForcedType:"binary"));
        Assert.Equal("binary",binary.Kind);Assert.StartsWith("00000000",binary.Text);
        var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
        Assert.Contains(catalog,f=>f.Type=="script"&&f.Tag=="AMP/LEAF");
        Assert.Contains(catalog,f=>f.Tag=="XP3"&&f.Schemes.Contains("none"));
    }

    [Fact]
    public async Task AudioIsPreparedAsABoundedParentOwnedFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-preview-test-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string output=Path.Combine(root,"audio.wav");
            var audio=await Request<AudioArtifact>(new("resource-audio",null,Location:new(Path.GetDirectoryName(Fixture("tone.mp3"))),
                Entry:"tone.mp3",Output:output));
            Assert.Equal(22050,audio.SampleRate);
            Assert.True(audio.Duration>0);
            Assert.True(File.Exists(output));
            using var decoded=new NAudio.Wave.WaveFileReader(output);
            Assert.Equal(16,decoded.WaveFormat.BitsPerSample);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void ResourceEncodingChangesDefaultNameReadersWithoutChangingTheFixedCodepage()
    {
        var previous=Encodings.ResourceEncodingOverride;
        try
        {
            new DecoderContext("gbk").Apply();
            byte[] name=System.Text.Encoding.GetEncoding(936).GetBytes("中文\0");
            Assert.Equal("中文",GameRes.Utility.Binary.GetCString(name,0));
            Assert.Equal(932,Encodings.cp932.CodePage);
        }
        finally {Encodings.ResourceEncodingOverride=previous;}
    }

    [Fact]
    public void NamedSchemesKeepTheirTrustedConcreteCipherType()
    {
        ArchiveService.Formats();
        var warc=GameRes.FormatCatalog.Instance.ArcFormats.Single(f=>f.Tag=="WAR");
        string name=FormatParameters.Names(warc).First();
        var json=System.Text.Json.JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string,string>{{"$scheme",name}});
        var options=new JsonOptionsProvider(json).GetOptions(warc,"game");
        object cipher=options.GetType().GetProperty("Scheme").GetValue(options);
        Assert.Equal(name,cipher.GetType().GetProperty("Name").GetValue(cipher));
        Assert.NotEqual(typeof(object),cipher.GetType());
    }

    [Fact]
    public async Task EveryDeclaredArchiveWriterCreatesAnArchiveThatCanBeListed()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-writers-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var failures=new System.Collections.Generic.List<string>();
        string export=Environment.GetEnvironmentVariable("GARBRO_CREATED_ARCHIVES");
        var exported=new System.Collections.Generic.List<object>();
        if(!string.IsNullOrEmpty(export))Directory.CreateDirectory(export);
        try
        {
            File.WriteAllText(Path.Combine(root,"hello.txt"),"Hello writer!\n");
            File.WriteAllBytes(Path.Combine(root,"00000001.scr"),new byte[]{0x53,0x43,0x52,0,1,2,3,4});
            var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            var writers=catalog.Where(f=>f.Type=="archive"&&f.CanWrite).ToArray();Assert.Equal(22,writers.Length);
            int index=0;
            foreach(var writer in writers)
            {
                string output=Path.Combine(root,"archive-"+index+++"."+writer.Extensions.FirstOrDefault());
                try
                {
                    string parameters=writer.Tag=="NPK"?"{\"Key\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"}":writer.Tag=="YPF"?"{\"Key\":90,\"Version\":256,\"Scheme\":\"none\"}":null;
                    string filename=writer.Tag=="AMI"?"00000001.scr":"hello.txt";
                    await Request<string>(new("resource-create",root,Output:output,FormatId:writer.Id,Inputs:new[]{filename},OptionsJson:parameters));
                    var listing=await Request<BrowserListing>(new("resource-list",null,OptionsJson:parameters??(writer.OptionsJson=="null"?null:writer.OptionsJson),Location:new(output)));
                    Assert.Contains(listing.Entries,e=>e.DisplayName==filename);
                    var preview=await Request<ResourcePreview>(new("resource-preview",null,OptionsJson:parameters??(writer.OptionsJson=="null"?null:writer.OptionsJson),Location:new(output),Entry:filename,ForcedType:writer.Tag=="AMI"?"binary":"script"));
                    if(writer.Tag!="AMI")Assert.Contains("Hello writer!",preview.Text);
                    else Assert.Contains("53 43 52 00 01 02 03 04",preview.Text);
                    if(!string.IsNullOrEmpty(export))
                    {
                        string basename=Path.GetFileName(output);File.Copy(output,Path.Combine(export,basename),true);
                        byte[] data=File.ReadAllBytes(Path.Combine(root,filename));
                        exported.Add(new{file=basename,tag=writer.Tag,options=parameters??writer.OptionsJson,entry=filename,
                            size=data.Length,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant()});
                    }
                }
                catch(Exception error){failures.Add(writer.Tag+": "+error.Message);}
            }
            Assert.True(failures.Count==0,string.Join("\n",failures));
            if(!string.IsNullOrEmpty(export))File.WriteAllText(Path.Combine(export,"created-archives.json"),System.Text.Json.JsonSerializer.Serialize(exported));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task XflCanCreateAnArchiveContainingOnlyAnEmptyFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-empty-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root,"empty.txt"),Array.Empty<byte>());
            var catalog=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            var writer=catalog.Single(f=>f.Tag=="XFL"&&f.Type=="archive");
            string output=Path.Combine(root,"empty.xfl");
            await Request<string>(new("resource-create",root,Output:output,FormatId:writer.Id,Inputs:new[]{"empty.txt"}));
            var listing=await Request<BrowserListing>(new("resource-list",null,Location:new(output)));
            Assert.Single(listing.Entries);Assert.Equal(0,listing.Entries[0].Size);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task AssignedTypesAlsoControlExtractionFilters()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-types-"+Guid.NewGuid());
        try
        {
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(Fixture("standard.xfl")),Output:root,
                ResourceOptions:new(Types:new[]{"script"},TypeOverrides:new(){{"hello.txt","binary"}})));
            Assert.DoesNotContain(result.Files,f=>f.Path.EndsWith("hello.txt"));
            Assert.Contains(result.Files,f=>f.Path.EndsWith("empty.txt"));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryRecursionDoesNotReadItsOwnPreviousOutputs(bool inPlace)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-output-loop-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string output=inPlace?root:Path.Combine(root,"output");
        string prior=inPlace?Path.Combine(root,"old.xfl.unpacked"):output;Directory.CreateDirectory(prior);
        try
        {
            File.Copy(Fixture("standard.xfl"),Path.Combine(root,"source.xfl"));
            File.Copy(Fixture("standard.xfl"),Path.Combine(prior,"previous.xfl"));
            var result=await Request<ResourceJobReport>(new("resource-extract",null,Location:new(root),Output:output,ResourceOptions:new(Recursive:true)));
            Assert.Empty(result.Errors);Assert.Equal(3,result.Files.Length);
            Assert.True(File.Exists(Path.Combine(output,"source.xfl.unpacked","hello.txt")));
            Assert.False(Directory.Exists(Path.Combine(inPlace?prior:Path.Combine(output,"output"),"previous.xfl.unpacked")));
        }
        finally{Directory.Delete(root,true);}
    }
}
