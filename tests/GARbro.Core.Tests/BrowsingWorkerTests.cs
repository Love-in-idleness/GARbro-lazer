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
        await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);var limits=new WorkerLimits();
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
            await using var client=new ManagedWorkerClient("worker-not-needed");
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
            await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
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
        await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new(),cancellation:cancel.Token));
        await Assert.ThrowsAsync<WorkerLimitException>(()=>client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new(.001)));
        var listing=await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(Fixture("standard.xfl"))),new());
        Assert.Equal("XFL",listing.Tag);
    }    [Fact]
    public async Task CrashesAndIdleExpiryAreRecoveredWithoutRetainingTheOldProcess()
    {
        await using var browser=new BrowsingWorker(WorkerMarker.AssemblyPath,TimeSpan.FromMilliseconds(150));
        var request=new WorkerRequest("resource-list",null,Location:new(Fixture("standard.xfl")));
        var first=await browser.ExecuteAsync<BrowserListing>(request,new());int pid=browser.ProcessId.Value;
        using(var process=System.Diagnostics.Process.GetProcessById(pid)){process.Kill(true);await process.WaitForExitAsync();}
        var recovered=await browser.ExecuteAsync<BrowserListing>(request,new());Assert.Equal(first.Entries,recovered.Entries);
        Assert.NotEqual(pid,browser.ProcessId);
        for(int i=0;i<150 && browser.ProcessId!=null;i++)await Task.Delay(20);Assert.Null(browser.ProcessId);
        Assert.Equal("XFL",(await browser.ExecuteAsync<BrowserListing>(request,new())).Tag);
    }
    [Fact]
    public async Task ReturningToADirectoryReleasesTheArchiveFileHandle()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-release-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"source.xfl");File.Copy(Fixture("standard.xfl"),path);
            await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
            await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(path)),new());
            await client.ExecuteAsync<BrowserListing>(new("resource-list",null,Location:new(root)),new());
            using var exclusive=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            Assert.True(exclusive.Length>0);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task MaterializedEntriesRemainReadableAfterCacheEviction()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-cache-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"many.xfl");
            var payloads=Enumerable.Range(0,40).Select(i=>System.Text.Encoding.ASCII.GetBytes($"Resource {i:D2}")).ToArray();
            using(var writer=new BinaryWriter(File.Create(path)))
            {
                writer.Write(0x1424cu);writer.Write(40*40);writer.Write(40);uint offset=0;
                for(int i=0;i<40;i++)
                {
                    byte[] name=new byte[32];System.Text.Encoding.ASCII.GetBytes($"entry-{i:D2}.chunk").CopyTo(name,0);
                    writer.Write(name);writer.Write(offset);writer.Write(payloads[i].Length);offset+=(uint)payloads[i].Length;
                }
                foreach(var data in payloads)writer.Write(data);
            }
            await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
            var location=new ResourceLocation(path);
            for(int i=0;i<41;i++)
            {
                int index=i%40;
                var preview=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:location,Entry:$"entry-{index:D2}.chunk",ForcedType:"script"),new());
                Assert.Equal($"Resource {index:D2}",preview.Text);
            }
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task OrdinaryPreviewDoesNotRetainItsSourceDirectory()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-directory-release-"+Guid.NewGuid()),moved=root+"-moved";Directory.CreateDirectory(root);
        try
        {
            File.Copy(Fixture("colors-rgb.png"),Path.Combine(root,"image.png"));
            await using var client=new ManagedWorkerClient(WorkerMarker.AssemblyPath);
            var preview=await client.ExecuteAsync<ResourcePreview>(new("resource-preview",null,Location:new(root),Entry:"image.png"),new());
            Assert.Equal("image",preview.Kind);
            File.Move(Path.Combine(root,"image.png"),Path.Combine(root,"renamed.png"));
            Directory.Move(root,moved);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);if(Directory.Exists(moved))Directory.Delete(moved,true);}
    }

}
