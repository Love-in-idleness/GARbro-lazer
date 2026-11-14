using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GARbro.Application
{
    /// <summary>Register only this worker's temporary outputs before creating them.</summary>
    public static class WorkerTemporaryFiles
    {
        internal const string ScopeVariable="GARBRO_WORKER_TEMP_SCOPE";
        internal const string RegistryVariable="GARBRO_WORKER_TEMP_REGISTRY";
        sealed record Scope(string Id,string Registry);
        static readonly AsyncLocal<Scope> current=new();

        public static void InitializeFromEnvironment()
        {
            string id=Environment.GetEnvironmentVariable(ScopeVariable);
            string registry=Environment.GetEnvironmentVariable(RegistryVariable);
            if (id==null && registry==null) return;
            Initialize(id,registry);
        }

        public static void Initialize(string id,string registry)
        {
            if (!Guid.TryParseExact(id,"N",out _) || registry==null || !File.Exists(registry))
                throw new InvalidDataException("Invalid worker temporary-file scope.");
            current.Value=new Scope(id,registry);
        }

        public static string Create(string directory)
        {
            var scope=current.Value;
            string name=".garbro-"+(scope==null?"":scope.Id+"-")+Guid.NewGuid().ToString("N")+".tmp";
            string path=Path.GetFullPath(Path.Combine(directory,name));
            if (scope!=null)
                File.AppendAllText(scope.Registry,JsonSerializer.Serialize(path)+"\n");
            return path;
        }

        public static string CreateDirectory(string directory)
        {
            string path = Create(directory);
            System.IO.Directory.CreateDirectory(path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }

        public static void Cleanup(string registry,string id)
        {
            string prefix=".garbro-"+id+"-";
            try
            {
                foreach(string line in File.ReadLines(registry))
                {
                    if (line.Length>1<<20) continue;
                    string path;
                    try { path=JsonSerializer.Deserialize<string>(line); }
                    catch(JsonException) { continue; }
                    if (path==null || !Path.IsPathFullyQualified(path)) continue;
                    string name=Path.GetFileName(path);
                    if (name.StartsWith(prefix,StringComparison.Ordinal) && name.EndsWith(".tmp",StringComparison.Ordinal)
                        && Guid.TryParseExact(name.AsSpan(prefix.Length,name.Length-prefix.Length-4),"N",out _))
                    {
                        try
                        {
                            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true);
                            else File.Delete(path);
                        }
                        catch(IOException) { } catch(UnauthorizedAccessException) { }
                    }
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(registry); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
        }
    }
}
