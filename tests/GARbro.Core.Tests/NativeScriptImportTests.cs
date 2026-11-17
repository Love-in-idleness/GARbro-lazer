using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Domain;
using GARbro.Scripting;
using GARbro.Worker;
using GameRes;
using GameRes.Formats.Luca;
using Xunit;

[Collection("Decoder state")]
public sealed class NativeScriptImportTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeScripts",name));
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath,request,new WorkerLimits(),null,default);

    [Theory]
    [InlineData("rxdata")]
    [InlineData("rvdata")]
    [InlineData("rvdata2")]
    public void RgssPreservesIdsOrderAndUnchangedBytesAndWritesChangedSource(string extension)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] original=Fixture("scripts."+extension);
        string json=RgssScript.Export(original,"Scripts."+extension,null,false,default);
        Assert.Equal(original,RgssScript.Compile(json,null,default));
        var document=JsonNode.Parse(json);
        var scripts=document["scripts"].AsArray();
        Assert.Equal(new[]{7,42},scripts.Select(s=>s["id"].GetValue<int>()).ToArray());
        Assert.Contains("日本語",scripts[0]["source"].GetValue<string>());
        scripts[0]["source"]="puts '変更後'\n";
        byte[] changed=RgssScript.Compile(document.ToJsonString(),null,default);
        var restored=JsonNode.Parse(RgssScript.Export(changed,"Scripts."+extension,null,false,default))["scripts"].AsArray();
        Assert.Equal("puts '変更後'\n",restored[0]["source"].GetValue<string>());
        Assert.Equal(new[]{7,42},restored.Select(s=>s["id"].GetValue<int>()).ToArray());
        Assert.Equal("",restored[1]["source"].GetValue<string>());
        Assert.Equal("."+extension,RgssScript.OutputExtension(document.ToJsonString()));
    }

    [Fact]
    public void EditingOneSharedMarshalStringDoesNotChangeAnotherScript()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var source=JsonNode.Parse(RgssScript.Export(Fixture("scripts-shared.rvdata2"),"Scripts.rvdata2",null,false,default));
        source["scripts"][0]["source"]="puts 2\n";
        byte[] changed=RgssScript.Compile(source.ToJsonString(),null,default);
        var restored=JsonNode.Parse(RgssScript.Export(changed,"Scripts.rvdata2",null,false,default));
        Assert.Equal("puts 2\n",restored["scripts"][0]["source"].GetValue<string>());
        Assert.Equal("puts 1\n",restored["scripts"][1]["source"].GetValue<string>());
    }

    [Theory]
    [InlineData("renpy-rpc2.rpyc")]
    [InlineData("renpy-protocol2.rpyc")]
    public void RenpyReadsBothContainersAndEmitsDialogueScreensAndAtl(string fixture)
    {
        string text=RenpyScript.Export(Fixture(fixture),default);
        Assert.Contains("中文 日本語 Русский",text);
        Assert.Contains("screen native_sample(title=",text);
        Assert.Contains("textbutton \"OK\"",text);
        Assert.Contains("transform fade_in:",text);
        Assert.Contains("linear 0.25 alpha 1.0",text);
        Assert.Contains("call second from native_return",text);
    }

    [Theory]
    [InlineData("AIR")][InlineData("ButterflysPoisonBC")][InlineData("CartagraHD")]
    [InlineData("HARMONIA")][InlineData("KANON")][InlineData("LOOPERS")]
    [InlineData("LUNARiA")][InlineData("PlanetarianSG")][InlineData("SP")]
    [InlineData("SummerPocketsRB")][InlineData("LB_EN")]
    public void LucaDatabaseProfilesRelocateJumpsAfterLengthChanges(string scheme)
    {
        ArchiveService.Formats();
        var provider=FormatCatalog.Instance.ScriptFormats.OfType<LucaScriptFormat>().Single();
        var profile=provider.SelectProfile(scheme);
        byte[] original=Fixture("luca-"+scheme+".pak");
        string json=LucaScript.Export(original,scheme,profile.Json,null,default);
        Assert.Equal(original,LucaScript.Compile(json,profile.Json,null,default));
        var document=JsonNode.Parse(json);
        foreach(var entry in document["entries"].AsArray())
            foreach(var command in entry["commands"].AsArray())
                foreach(var parameter in command["parameters"].AsArray())
                    if(parameter["kind"].GetValue<string>() is "s:text" or "l:text")
                        parameter["value"]=parameter["value"].GetValue<string>()+"追加";
        byte[] changed=LucaScript.Compile(document.ToJsonString(),profile.Json,null,default);
        var reread=JsonNode.Parse(LucaScript.Export(changed,scheme,profile.Json,null,default));
        Assert.Contains("追加",reread.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        // Independently walk packet lengths and inspect the relocated branch words.
        var starts=new Dictionary<string,int>();
        var returns=new Dictionary<string,int>();
        for(int i=0;i<2;i++)
        {
            int start=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(40+i*8))*16);
            int length=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(44+i*8)));
            starts["scene"+i]=start;
            int position=0,last=0;
            while(position<length){last=position;int size=BinaryPrimitives.ReadUInt16LittleEndian(changed.AsSpan(start+position));Assert.True(size>=4);position+=size+(size&1);}
            Assert.Equal(length,position);returns["scene"+i]=last;
        }
        foreach(var entry in reread["entries"].AsArray())
            foreach(var command in entry["commands"].AsArray())
                foreach(var parameter in command["parameters"].AsArray())
                    if(parameter["kind"].GetValue<string>().StartsWith("j:",StringComparison.Ordinal))
                        Assert.Equal(returns[parameter["entry"].GetValue<string>()],parameter["value"].GetValue<int>());
        Assert.True(starts["scene1"]>starts["scene0"]);
    }

    [Fact]
    public async Task WorkerUsesNativeBackendsAndResourceScopedGameScheme()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string root=Path.Combine(Path.GetTempPath(),"garbro-native-import-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root,"SCRIPT.PAK"),Fixture("luca-LOOPERS.pak"));
            string output=Path.Combine(root,"output","SCRIPT.PAK.luca.json");
            var missing=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"SCRIPT.PAK",Output:output,FormatId:"LUCA-JSON")));
            Assert.Equal("MissingOptionsException",missing.RemoteType);
            Assert.Equal(FormatIdentity.Of(new LucaScriptFormat()),missing.FormatId);Assert.NotNull(missing.ContextKey);Assert.False(File.Exists(output));
            var result=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"SCRIPT.PAK",Output:output,FormatId:"LUCA-JSON",Scheme:"LOOPERS"));
            Assert.True(result.Written);Assert.Equal("LOOPERS",LucaScript.SchemeName(File.ReadAllText(output)));
            var restored=await Request<ResourceConversion>(new("resource-convert",null,Location:new(Path.GetDirectoryName(output)),Entry:Path.GetFileName(output),Output:Path.Combine(root,"rebuilt","SCRIPT.PAK"),FormatId:"LUCA"));
            Assert.Equal(Fixture("luca-LOOPERS.pak"),File.ReadAllBytes(restored.Path));
            File.WriteAllText(Path.Combine(root,"scripts.rgss.json"),RgssScript.Export(Fixture("scripts.rxdata"),"Scripts.rxdata",null,false,default));
            restored=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"scripts.rgss.json",Output:Path.Combine(root,"rebuilt","Scripts.rvdata2"),FormatId:"RGSS"));
            Assert.EndsWith("Scripts.rxdata",restored.Path);Assert.Equal(Fixture("scripts.rxdata"),File.ReadAllBytes(restored.Path));
            File.WriteAllBytes(Path.Combine(root,"script.rpyc"),Fixture("renpy-rpc2.rpyc"));
            result=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"script.rpyc",Output:Path.Combine(root,"output","script.rpy"),FormatId:"RPY"));
            Assert.Contains("中文 日本語 Русский",File.ReadAllText(result.Path));
            File.WriteAllBytes(Path.Combine(root,"script.tjs"),Fixture("tjs-imt-initialize.tjsbc"));
            result=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"script.tjs",Output:Path.Combine(root,"output","script.tjs"),FormatId:"TJS"));
            string text=File.ReadAllText(result.Path);Assert.Contains("class PoolWindow",text);Assert.Contains("function addArchive",text);
            var damaged=Fixture("tjs-imt-initialize.tjsbc");
            BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(20),int.MaxValue);
            File.WriteAllBytes(Path.Combine(root,"bad.tjs"),damaged);
            var invalid=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"bad.tjs",Output:result.Path,FormatId:"TJS",Overwrite:OverwritePolicy.Replace)));
            Assert.Equal("InvalidDataException",invalid.RemoteType);Assert.Equal(text,File.ReadAllText(result.Path));
            Assert.Equal(Fixture("luca-LOOPERS.pak"),File.ReadAllBytes(Path.Combine(root,"SCRIPT.PAK")));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void InvalidReferencesTruncationAndCancellationFailWithoutExecutingPickleConstructors()
    {
        Assert.Throws<InvalidDataException>(()=>RenpyScript.Export(new byte[]{0x78,0x9c,0},default));
        Assert.Throws<InvalidDataException>(()=>RgssScript.Export(new byte[]{4,8,(byte)'@',99},"Scripts.rvdata2",null,false,default));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RenpyScript.Export(Fixture("renpy-rpc2.rpyc"),cancelled.Token));
        var malicious=Fixture("renpy-unsafe-constructor.rpyc");
        Assert.Throws<InvalidDataException>(()=>RenpyScript.Export(malicious,default));
    }

    [Fact]
    public void MissingBranchLayoutsAndChangedConditionalFieldsCannotProduceCorruptLucaScripts()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ArchiveService.Formats();
        var profile=FormatCatalog.Instance.ScriptFormats.OfType<LucaScriptFormat>().Single().SelectProfile("LOOPERS");
        var layout=JsonNode.Parse(profile.Json);layout["Parameters"].AsObject().Remove("GOTO");
        byte[] data=Fixture("luca-LOOPERS.pak");
        var document=JsonNode.Parse(LucaScript.Export(data,"LOOPERS",layout.ToJsonString(),null,default));
        Assert.Equal(data,LucaScript.Compile(document.ToJsonString(),layout.ToJsonString(),null,default));
        var parameter=document["entries"][0]["commands"][0]["parameters"].AsArray().First(p=>p["kind"].GetValue<string>()=="l:text");
        parameter["value"]=parameter["value"].GetValue<string>()+"追加";
        Assert.Throws<InvalidDataException>(()=>LucaScript.Compile(document.ToJsonString(),layout.ToJsonString(),null,default));
        document=JsonNode.Parse(LucaScript.Export(data,"LOOPERS",profile.Json,null,default));
        parameter=document["entries"][0]["commands"][0]["parameters"].AsArray().First(p=>p["kind"].GetValue<string>()=="l:text");
        parameter["value"]="";
        Assert.Throws<InvalidDataException>(()=>LucaScript.Compile(document.ToJsonString(),profile.Json,null,default));
    }

    [Fact]
    public void TemplateRoutingKeepsAsbJsonAndDistinguishesRoundtripFromRubySource()
    {
        Assert.Equal("RGSS-JSON",ScriptOutputFormats.ForInput("Scripts.rvdata2"));
        Assert.True(ScriptOutputFormats.CanExport("Scripts.rxdata","RB"));
        Assert.Equal("LUCA-JSON",ScriptOutputFormats.ForInput("SCRIPT.PAK"));
        Assert.Equal("RGSS-JSON",ScriptOutputFormats.FromOutputName("Scripts.rvdata2.rgss.json"));
        Assert.Equal("LUCA-JSON",ScriptOutputFormats.FromOutputName("SCRIPT.PAK.luca.json"));
        Assert.Null(ScriptOutputFormats.FromOutputName("scene.json"));
        Assert.True(ScriptOutputFormats.CanRecompile("scene.json","ASB"));
        Assert.Equal("Scripts.rvdata2",ScriptOutputFormats.RecompileOutput("Scripts.rvdata2.rgss.json","RGSS"));
        Assert.Equal("SCRIPT.PAK",ScriptOutputFormats.RecompileOutput("SCRIPT.PAK.luca.json","LUCA"));
        Assert.Equal("module.rpym",ScriptOutputFormats.ExportOutput("module.rpymc","RPY"));
    }
}
