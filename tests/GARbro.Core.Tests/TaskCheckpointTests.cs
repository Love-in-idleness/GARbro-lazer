using System;
using System.IO;
using GARbro.Application;
using Xunit;

public sealed class TaskCheckpointTests
{
    [Fact]
    public void InterruptedTaskRetainsCommittedOutputAndRejectsChangedSources()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-checkpoint-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string input=Path.Combine(root,"source.arc"),output=Path.Combine(root,"out"),journal=Path.Combine(root,"task.json");
            File.WriteAllText(input,"original");Directory.CreateDirectory(output);string completed=Path.Combine(output,"hello.txt");
            var request=new WorkerRequest("resource-extract",null,Location:new(input),Output:output,OptionsJson:"{\"Password\":\"not-persistent\"}");
            var first=TaskCheckpoint.Begin(journal,request);File.WriteAllText(completed,"data");first.Commit(completed);
            var resumed=TaskCheckpoint.Begin(journal,request);Assert.Contains(completed,resumed.CompletedPaths);
            Assert.DoesNotContain("not-persistent",File.ReadAllText(journal));resumed.SetState("interrupted");
            File.WriteAllText(input,"changed source");Assert.Throws<IOException>(()=>TaskCheckpoint.Begin(journal,request));
            Assert.Equal("data",File.ReadAllText(completed));
        }
        finally{Directory.Delete(root,true);}
    }
}
