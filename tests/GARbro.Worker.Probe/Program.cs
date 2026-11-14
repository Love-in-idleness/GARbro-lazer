using System;
using System.IO;
using System.Threading.Tasks;
using GARbro.Application;

namespace GARbro.Worker.Probe
{
    public static class ProbeMarker {public static string AssemblyPath=>typeof(ProbeMarker).Assembly.Location;}
    static class Program
    {
        static async Task Main(string[] args)
        {
            if(args[0]=="exit-parent")
            {
                var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _=WorkerSupervisor.RunManagedAsync(ProbeMarker.AssemblyPath,new[]{"child",args[1]},output:line=>
                {
                    Console.WriteLine(line);
                    if(!line.StartsWith("pid:",StringComparison.Ordinal))ready.TrySetResult();
                });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Environment.Exit(0);
            }
            WorkerTemporaryFiles.InitializeFromEnvironment();
            if(args[0]=="child")Console.WriteLine("pid:"+Environment.ProcessId);
            string temporary=WorkerTemporaryFiles.Create(args[1]);
            File.WriteAllText(temporary,"incomplete output");
            Console.WriteLine(temporary);
            byte[] memory=null;
            if(args[0]=="memory")
            {
                memory=new byte[384<<20];
                for(int i=0;i<memory.Length;i+=4096)memory[i]=1;
            }
            await Task.Delay(30000);
            GC.KeepAlive(memory);
        }
    }
}
