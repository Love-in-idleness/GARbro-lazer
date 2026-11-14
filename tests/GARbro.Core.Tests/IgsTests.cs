using System;
using System.IO;
using System.Linq;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class IgsTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void IgsUsesLiteralDatabasePathsAndValidatesAesPadding()
    {
        ArchiveService.Formats();
        string directory = Path.Combine(Path.GetTempPath(), "garbro-IGS % #; 日本語-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Fixture("igs-rc4-1024/index.db"), Path.Combine(directory,"index.db"));
            string path = Path.Combine(directory, "standard.dat");
            byte[] bytes = File.ReadAllBytes(Fixture("igs-rc4-1024/standard.dat"));
            bytes[271] ^= 0x80; // damage the final PKCS7 block of the first prefix
            File.WriteAllBytes(path,bytes);
            using var view = new ArcView(path);
            var format = new GameRes.Formats.CellWorks.IgsDatOpener();
            using var archive = format.TryOpen(view);
            Assert.NotNull(archive);
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => archive.OpenEntry(archive.Dir.First()));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void IgsRejectsIndexOffsetsOutsideTheArchive()
    {
        ArchiveService.Formats();
        string directory = Path.Combine(Path.GetTempPath(), "garbro-igs-bounds-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string database = Path.Combine(directory,"index.db");
            File.Copy(Fixture("igs-plain/index.db"), database);
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
                DataSource=database,Pooling=false
            }.ConnectionString))
            {
                connection.Open();
                using var command=connection.CreateCommand();
                command.CommandText="UPDATE file_infos SET offset=999999 WHERE id=1";
                command.ExecuteNonQuery();
            }
            string path=Path.Combine(directory,"standard.dat");
            File.Copy(Fixture("igs-plain/standard.dat"),path);
            using var view=new ArcView(path);
            var format=new GameRes.Formats.CellWorks.IgsDatOpener();
            Assert.Throws<InvalidDataException>(()=>format.TryOpen(view));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void IgsDoesNotReadAnActiveWalAsAFrozenIndex()
    {
        ArchiveService.Formats();
        string directory = Path.Combine(Path.GetTempPath(), "garbro-igs-wal-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Fixture("igs-rc4-4096/index.db"), Path.Combine(directory,"index.db"));
            File.WriteAllBytes(Path.Combine(directory,"index.db-wal"),new byte[] {1});
            string path = Path.Combine(directory,"standard.dat");
            File.Copy(Fixture("igs-rc4-4096/standard.dat"),path);
            using var view = new ArcView(path);
            var format = new GameRes.Formats.CellWorks.IgsDatOpener();
            Assert.Throws<NotSupportedException>(() => format.TryOpen(view));
        }
        finally { Directory.Delete(directory,true); }
    }
}
