using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Domain;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class ScriptRecompileTests
{
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(WorkerMarker.AssemblyPath, request, new WorkerLimits(), null, default);

    [Theory]
    [InlineData("NSCRIPT", "cp932", "日本語の台詞\r\n")]
    [InlineData("NSCRIPT", "gbk", "简体中文对白\r\n")]
    [InlineData("NSCRIPT", "utf-8", "Русский 中文 😀\r\n")]
    [InlineData("DSM", "auto", "name,text\r\nAlice,中文 日本語 😀\r\n")]
    public async Task NativeScriptsRecompileAndExportWithoutChangingSources(string format, string encoding, string script)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-script-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string input=Path.Combine(root,"edited.txt");byte[] original=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(script)).ToArray();File.WriteAllBytes(input,original);
            string output=Path.Combine(root,"output");
            var compiled=await Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:"edited.txt",Output:Path.Combine(output,"edited.txt"),FormatId:"MEDIA",
                Context:new DecoderContext(encoding),ResourceOptions:new(ConvertScripts:true,ScriptFormat:format,DiscardOriginalMedia:false)));
            Assert.True(compiled.Written);Assert.Equal(format=="NSCRIPT"?"nscript.dat":"data.dsm",Path.GetFileName(compiled.Path));
            Assert.Equal(original,File.ReadAllBytes(input));Assert.Equal(original,File.ReadAllBytes(Path.Combine(output,"edited.txt")));
            if(format=="NSCRIPT")
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Assert.Equal(new DecoderContext(encoding).ResolveEncoding().GetBytes(script).Select(b=>(byte)(b^0x84)).ToArray(),File.ReadAllBytes(compiled.Path));
            }
            string exported=Path.Combine(root,"restored.txt");
            await Request<ResourceConversion>(new("resource-convert",null,Location:new(output),Entry:Path.GetFileName(compiled.Path),Output:exported,FormatId:"TXT",Context:new DecoderContext(encoding)));
            Assert.Equal(Encoding.UTF8.GetBytes(script),File.ReadAllBytes(exported));
        }
        finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("encoding")]
    [InlineData("utf8")]
    [InlineData("limit")]
    [InlineData("string-table")]
    [InlineData("json-encoding")]
    public async Task FailedCompilationPreservesThePreviousOutput(string failure)
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-script-failure-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string name=failure=="string-table"?"script.bsd":failure=="json-encoding"?"script.json":"script.txt";
            string format=failure=="string-table"?"BGI":failure=="json-encoding"?"ASB":"NSCRIPT";
            byte[] data=failure switch
            {
                "utf8"=>new byte[]{0xe3,0x81},
                "string-table"=>Encoding.UTF8.GetBytes("#string_table \"../outside.strings\"\n"),
                "json-encoding"=>Encoding.UTF8.GetBytes("[\"\\uD83D\\uDE00\"]"),
                _=>Encoding.UTF8.GetBytes(failure=="encoding"?"😀":new string('x',256))
            };
            File.WriteAllBytes(Path.Combine(root,name),data);Directory.CreateDirectory(Path.Combine(root,"output"));string output=Path.Combine(root,"output","previous.dat");File.WriteAllText(output,"previous output");
            var error=await Assert.ThrowsAsync<WorkerOperationException>(()=>Request<ResourceConversion>(new("resource-convert",null,Location:new(root),Entry:name,Output:output,FormatId:format,Overwrite:OverwritePolicy.Replace,
                Context:new DecoderContext("cp932"),Extraction:failure=="limit"?new(MaxEntryBytes:10):null)));
            Assert.Contains(error.RemoteType,new[]{"InvalidDataException","NotSupportedException"});
            Assert.Equal("previous output",File.ReadAllText(output));Assert.Equal(data,File.ReadAllBytes(Path.Combine(root,name)));
            Assert.Equal(2,Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void CompilerRoutingUsesExplicitTargetsAndPreservesTheExportDefault()
    {
        foreach(var pair in new[]{("scene.tsc","GSC"),("nscript.txt","NSCRIPT"),("data.txt","DSM"),("scene.bsd","BGI"),("scene.bpd","BGI-BP"),("scene.json","ASB")})
        {Assert.True(ScriptOutputFormats.Supports(pair.Item2));Assert.True(ScriptOutputFormats.CanRecompile(pair.Item1,pair.Item2));}
        Assert.False(ScriptOutputFormats.CanRecompile("readme.txt","GSC"));Assert.False(ScriptOutputFormats.HasRecompileInput("game.rpy"));
        Assert.Null(ScriptOutputFormats.ForInput("edited.tsc"));Assert.Equal("TSC",ScriptOutputFormats.ForInput("script.gsc"));
        Assert.Equal("NSCRIPT",ScriptOutputFormats.FromOutputName("nscript.dat"));Assert.Equal("BGI-BP",ScriptOutputFormats.FromOutputName("scene._bp"));
        Assert.Equal("scene",ScriptOutputFormats.RecompileOutput("scene.bsd","BGI"));
        Assert.Equal(Path.Combine("sub","data.dsm"),ScriptOutputFormats.RecompileOutput(Path.Combine("sub","edited.txt"),"DSM"));
    }
}
