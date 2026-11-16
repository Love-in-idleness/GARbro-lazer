using System;
using System.IO;
using System.Text;
using System.Threading;
using GARbro.Scripting;
using Xunit;

public sealed class NativeScriptCodecTests
{
    static Encoding Cp932 { get { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(932); } }
    static string Fixture(string name)=>Path.Combine(AppContext.BaseDirectory,"NativeScripts",name);

    [Theory]
    [InlineData("modern")]
    [InlineData("rscript19")]
    [InlineData("rscript18")]
    [InlineData("early")]
    [InlineData("early-short-select")]
    [InlineData("pre-codex")]
    public void TscMatchesIndependentCompilerAndRemainsEditable(string schema)
    {
        string text=File.ReadAllText(Fixture(schema+".tsc"));byte[] expected=File.ReadAllBytes(Fixture(schema+".gsc"));
        Assert.Equal(expected,GscScript.Compile(text,Cp932,default));
        string exported=GscScript.Export(expected,Cp932,default);
        Assert.DoesNotContain(";@gsc-raw-v1",exported);Assert.Contains("日本語",exported);
        byte[] edited=GscScript.Compile(exported.Replace("日本語","変更後"),Cp932,default);
        Assert.Contains("変更後",GscScript.Export(edited,Cp932,default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BgiMatchesIndependentAssemblerAndRetargetsLabels(bool bp)
    {
        string source=File.ReadAllText(Fixture(bp?"scene.bpd":"scene.bsd"));
        byte[] expected=File.ReadAllBytes(Fixture(bp?"scene._bp":"scene.bgi"));
        Assert.Equal(expected,BgiScript.Compile(source,bp,Cp932,default));
        string text=bp?BgiScript.ExportBp(expected,Cp932,default):BgiScript.Export(expected,Cp932,default);
        byte[] edited=BgiScript.Compile(text.Replace("日本語","長さの変わる文章"),bp,Cp932,default);
        string restored=bp?BgiScript.ExportBp(edited,Cp932,default):BgiScript.Export(edited,Cp932,default);
        Assert.Contains("長さの変わる文章",restored);Assert.Contains("ret();",restored);
    }

    [Fact]
    public void AsbPreservesTheExistingStructuredJson()
    {
        byte[] expected=File.ReadAllBytes(Fixture("scene.asb"));
        Assert.Equal(expected,AsbScript.Compile(File.ReadAllText(Fixture("scene.json")),Cp932,default));
        string source=AsbScript.Export(expected,Cp932,default);
        Assert.Equal(expected,AsbScript.Compile(source,Cp932,default));
        string edited=AsbScript.Export(AsbScript.Compile(source.Replace("return 1","return 2"),Cp932,default),Cp932,default);
        Assert.Contains("return 2",edited);
    }

    [Fact]
    public void RejectsMalformedSourcesAndCancellation()
    {
        Assert.Throws<InvalidDataException>(()=>GscScript.Compile("*end",Cp932,default));
        Assert.Throws<InvalidDataException>(()=>BgiScript.Compile("#header \"BurikoCompiledScriptVer1.00\"\npush_offset(missing);",false,Cp932,default));
        Assert.Throws<InvalidDataException>(()=>AsbScript.Compile("[\"😀\"]",Cp932,default));
        Assert.Throws<InvalidDataException>(()=>GscScript.Compile(File.ReadAllText(Fixture("modern.tsc")),Encoding.Unicode,default));
        Assert.Throws<InvalidDataException>(()=>BgiScript.Compile(File.ReadAllText(Fixture("scene.bsd")),false,Encoding.Unicode,default));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>GscScript.Compile(File.ReadAllText(Fixture("modern.tsc")),Cp932,cancellation.Token));
        var broken=File.ReadAllBytes(Fixture("scene.asb"));Array.Resize(ref broken,broken.Length-1);
        Assert.ThrowsAny<Exception>(()=>AsbScript.Export(broken,Cp932,default));
    }
}
