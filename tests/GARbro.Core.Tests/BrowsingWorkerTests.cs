using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class BrowsingWorkerTests
{
    static string Fixture(string name)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    [Fact]
    public async Task ReusedWorkerPreservesNestedNavigationAndIndependentPreviewEncoding()
    {
        using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);var limits=new WorkerLimits();
        var listing=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),limits);
        for(int n=0;n<5;n++)
        {
            var text=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:listing.Location,Entry:"hello.txt",PreviewEncoding:"utf-8"),limits);
            Assert.Equal("Hello, GARbro!\n",text.Text);
            var hex=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:listing.Location,Entry:"hello.txt",PreviewMode:"hex",Context:new(TextEncoding:"shift_jis")),limits);
            Assert.Equal("binary",hex.Kind);Assert.Contains("48 65 6C 6C 6F",hex.Text);Assert.Contains("Hello, GARbro!",hex.Text);
            var sub=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:listing.Location.EnterDirectory("sub")),limits);
            Assert.Contains(sub.Entries,e=>e.DisplayName=="日本語.bin");
        }
    }
    [Fact]
    public async Task PagingDoesNotCopyLargeFilesAndHexDoesNotResolveAnEncoding()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-hex-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"large.bin");
            using(var file=File.Create(path)){file.SetLength(3L<<30);file.Position=65536;file.Write(new byte[]{0x41,0x42,0x43,0x7f,0xff});}
            using var client=new ManagedWorkerClient("worker-not-needed");
            var preview=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"large.bin",PreviewMode:"hex",PreviewEncoding:"not-an-encoding",Offset:65536),new());
            Assert.Equal(65536,preview.Offset);Assert.True(preview.More);Assert.StartsWith("00010000  41 42 43 7F FF",preview.Text);Assert.Contains("| ABC..",preview.Text);
            File.WriteAllBytes(Path.Combine(root,"empty.png"),Array.Empty<byte>());
            var empty=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"empty.png",PreviewMode:"hex"),new());
            Assert.Equal("",empty.Text);Assert.False(empty.More);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task SourceMutationAndRefreshCannotReuseOldArchiveBytes()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-mutation-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"source.xfl");File.Copy(Fixture("standard.xfl"),path);
            using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
            var first=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(path)),new());
            File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddMinutes(1));
            await Assert.ThrowsAsync<WorkerOperationException>(()=>client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:first.Location,Entry:"hello.txt"),new()));
            var refreshed=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:first.Location with {Fingerprint=null}),new());
            Assert.NotEqual(first.Location.Fingerprint,refreshed.Location.Fingerprint);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task CancellationAndTimeoutRecycleTheSessionAndTheNextRequestSucceeds()
    {
        using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new(),cancellation:cancel.Token));
        await Assert.ThrowsAsync<WorkerLimitException>(()=>client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new(.001)));
        var listing=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new());
        Assert.Equal("XFL",listing.Tag);
    }    [Fact]
    public async Task CrashesAndIdleExpiryAreRecoveredWithoutRetainingTheOldProcess()
    {
        using var browser=new BrowsingWorker(WorkerMarker.AssemblyPath,TimeSpan.FromMilliseconds(150));
        var request=new WorkerRequest("resource-list",null,Location:new(Fixture("standard.xfl")));
        var first=await browser.ExecuteAsync<BrowserListing>(request,new());int pid=browser.ProcessId.Value;
        using(var process=System.Diagnostics.Process.GetProcessById(pid)){process.Kill(true);await process.WaitForExitAsync();}
        var recovered=await browser.ExecuteAsync<BrowserListing>(request,new());Assert.Equal(first.Entries,recovered.Entries);
        Assert.NotEqual(pid,browser.ProcessId);
        await Task.Delay(600);Assert.Null(browser.ProcessId);
        Assert.Equal("XFL",(await browser.ExecuteAsync<BrowserListing>(request,new())).Tag);
    }

}
