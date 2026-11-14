using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GameRes.Cryptography;
using Microsoft.Data.Sqlite;

namespace GameRes.Formats.CellWorks
{
    /// <summary>Open historical RC4 databases using the MIT SQLite3 Multiple Ciphers backend.</summary>
    internal sealed class IgsDbReader(string archive) : IDisposable
    {
        SqliteConnection connection;
        static readonly byte[] Magic = "SQLite format 3\0"u8.ToArray();

        static int PageSize(byte[] header)
        {
            if (!header.AsSpan(0, 16).SequenceEqual(Magic)) return 0;
            int size = header[16] << 8 | header[17];
            if (size == 1) size = 65536;
            return size is >= 512 and <= 65536 && (size & (size - 1)) == 0 ? size : 0;
        }

        public bool GetArchiveId(string database, out int id)
        {
            id = -1;
            connection?.Dispose();
            connection = null;
            using var input = File.OpenRead(database);
            if (input.Length < 512 || input.Length > 256 << 20) return false;
            byte[] encrypted = new byte[32];
            input.ReadExactly(encrypted);
            // Never open an active journal as a frozen resource index.
            if (new FileInfo(database + "-wal") is { Exists: true, Length: > 0 }
                || new FileInfo(database + "-journal") is { Exists: true, Length: > 0 })
                throw new NotSupportedException("IGS database has an active journal; use a closed, checkpointed game database.");
            bool plaintext = PageSize(encrypted) != 0;
            foreach (string password in IgsDatOpener.KnownPasswords)
            {
                byte[] header = encrypted;
                if (!plaintext)
                {
                    byte[] key = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(password))[..16];
                    using var rc4 = new Rc4Transform(key);
                    header = rc4.TransformFinalBlock(encrypted, 0, encrypted.Length);
                }
                int page = PageSize(header);
                if (page == 0 || input.Length % page != 0) continue;
                string path = new Uri(Path.GetFullPath(database)).AbsoluteUri;
                if (!plaintext) path += "?cipher=rc4&legacy_page_size=" + page;
                var candidate = new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
                    Password = plaintext ? "" : password, DefaultTimeout = 3
                }.ConnectionString);
                try
                {
                    candidate.Open();
                    // Bound database-controlled strings, expressions and VM work.
                    SQLitePCL.raw.sqlite3_limit(candidate.Handle, SQLitePCL.raw.SQLITE_LIMIT_LENGTH, 1 << 20);
                    SQLitePCL.raw.sqlite3_limit(candidate.Handle, SQLitePCL.raw.SQLITE_LIMIT_SQL_LENGTH, 65536);
                    SQLitePCL.raw.sqlite3_limit(candidate.Handle, SQLitePCL.raw.SQLITE_LIMIT_EXPR_DEPTH, 64);
                    int checkpoints = 0;
                    SQLitePCL.raw.sqlite3_progress_handler(candidate.Handle, 1000, _ => ++checkpoints > 20000 ? 1 : 0, null);
                    using var command = candidate.CreateCommand();
                    command.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY;";
                    command.ExecuteNonQuery();
                    command.CommandText = "SELECT id FROM archives WHERE name=$name LIMIT 1";
                    command.Parameters.AddWithValue("$name", Path.GetFileNameWithoutExtension(archive));
                    object value = command.ExecuteScalar();
                    if (value == null || value == DBNull.Value) { candidate.Dispose(); return false; }
                    id = checked((int)(long)value);
                    connection = candidate;
                    return true;
                }
                catch (SqliteException) { candidate.Dispose(); }
                catch { candidate.Dispose(); throw; }
                if (plaintext) break;
            }
            return false;
        }

        public List<Entry> ReadIndex(int id)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT filepath,offset,size FROM file_infos WHERE archiveID=$id LIMIT 262144";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            var entries = new List<Entry>();
            long nameBytes = 0;
            while (reader.Read())
            {
                if (entries.Count >= 0x3FFFF) throw new InvalidDataException("IGS index exceeds the entry limit.");
                string name = reader.GetString(0);
                nameBytes += name.Length * 2L;
                long offset = reader.GetInt64(1), size = reader.GetInt64(2);
                if (string.IsNullOrWhiteSpace(name) || name.Length > 32768 || nameBytes > 64 << 20
                    || offset < 0 || size < 0 || size > uint.MaxValue)
                    throw new InvalidDataException("Invalid IGS entry metadata.");
                var entry = FormatCatalog.Instance.Create<Entry>(name);
                entry.Offset = offset; entry.Size = (uint)size;
                entries.Add(entry);
            }
            return entries;
        }

        public void Dispose() { connection?.Dispose(); connection = null; }
    }
}
