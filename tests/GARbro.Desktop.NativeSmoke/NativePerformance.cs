using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using GARbro.Desktop;

static class NativePerformance
{
    public static async Task Check(MainWindow page)
    {
        if(Environment.GetEnvironmentVariable("GARBRO_BENCHMARKS")!="1")return;
        string root=Path.Combine(Program.Output,"performance-inputs"),directory=Path.Combine(root,"ten-thousand");Directory.CreateDirectory(directory);
        await Task.Run(()=> {for(int i=0;i<10000;i++)using(File.Create(Path.Combine(directory,$"entry-{i:D5}.txt"))){};});
        var delays=new List<double>();var tick=Stopwatch.StartNew();
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{delays.Add(Math.Max(0,tick.Elapsed.TotalMilliseconds-10));tick.Restart();};timer.Start();
        var clock=Stopwatch.StartNew();await page.LoadArchiveAsync(directory);
        double directoryMs=clock.Elapsed.TotalMilliseconds;
        if(page.VisibleEntryCount!=10000)throw new InvalidDataException("Large directory lost entries.");
        string archive=Path.Combine(root,"hundred-thousand.xfl");
        await Task.Run(()=>
        {
            using var file=new BinaryWriter(File.Create(archive));file.Write(0x1424cu);file.Write(100000*40);file.Write(100000);
            for(int i=0;i<100000;i++){byte[] name=new byte[32];Encoding.ASCII.GetBytes($"entry-{i:D6}.txt").CopyTo(name,0);file.Write(name);file.Write(0u);file.Write(0u);}
        });
        clock.Restart();await page.LoadArchiveAsync(archive);double archiveMs=clock.Elapsed.TotalMilliseconds;
        if(page.VisibleEntryCount!=100000)throw new InvalidDataException("Large archive lost entries.");
        var search=page.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Search");search.Text="entry-099999";
        for(int i=0;i<100&&page.VisibleEntryCount!=1;i++)await Task.Delay(20);
        if(page.VisibleEntryCount!=1)throw new InvalidDataException("Large archive filter failed.");search.Text="";
        var warm=new List<double>();
        for(int i=0;i<9;i++){clock.Restart();await page.LoadArchiveAsync(Program.Fixture);warm.Add(clock.Elapsed.TotalMilliseconds);}
        await page.LoadArchiveAsync(Path.Combine(Path.GetDirectoryName(Program.Fixture),"colors-rgb.png"));
        var image=page.GetLogicalDescendants().OfType<Image>().Single(c=>c.Name=="PreviewImage");
        for(int i=0;i<200&&image.Source==null;i++)await Task.Delay(10);
        if(image.Source==null)throw new InvalidDataException("Image preview did not render.");
        var encoding=page.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.Name=="PreviewEncoding");
        var cache=new List<double>();
        for(int i=0;i<7;i++)
        {
            encoding.SelectedIndex=1;
            for(int j=0;j<200&&image.IsEffectivelyVisible;j++)await Task.Delay(5);
            clock.Restart();encoding.SelectedIndex=0;
            for(int j=0;j<200&&!image.IsEffectivelyVisible;j++)await Task.Delay(5);
            cache.Add(clock.Elapsed.TotalMilliseconds);
        }
        cache.Sort();
        await page.LoadArchiveAsync(Program.Fixture);
        await Task.Delay(100);timer.Stop();
        warm.Sort();delays.Sort();double p95=delays[(int)Math.Floor((delays.Count-1)*.95)];
        var report=new{directory10000Ms=directoryMs,archive100000Ms=archiveMs,warmArchiveMedianMs=warm[warm.Count/2],uiDelayP95Ms=p95,warmSamplesMs=warm,imageCacheMedianMs=cache[cache.Count/2],imageCacheSamplesMs=cache,workerStages=(UiPlatform.Worker as GARbro.Application.ManagedWorkerClient)?.LastTiming};
        File.WriteAllText(Path.Combine(Program.Output,"performance.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        if(directoryMs>500 || warm[warm.Count/2]>250 || cache[cache.Count/2]>100 || p95>100)throw new InvalidDataException("Desktop performance budget exceeded: "+System.Text.Json.JsonSerializer.Serialize(report));
        Console.WriteLine($"Performance: 10k directory {directoryMs:F1}ms; warm archive {warm[warm.Count/2]:F1}ms; UI delay P95 {p95:F1}ms.");
    }
}
