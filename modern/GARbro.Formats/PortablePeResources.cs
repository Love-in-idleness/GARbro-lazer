using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GameRes.Formats
{
    public partial class ExeFile
    {
        /// <summary>Read PE resources from bounded file mappings without loading an executable.</summary>
        public sealed class ResourceAccessor : IDisposable
        {
            readonly ArcView file;
            readonly ExeFile image;
            readonly Dictionary<string, Dictionary<string, SortedDictionary<int, ResourceData>>> resources = new(StringComparer.Ordinal);
            readonly Dictionary<uint, string> strings = new();
            uint rootRva, rootSize;
            int visits, stringBytes;
            bool disposed;
            readonly record struct ResourceData(long Offset, uint Size);
            readonly record struct Node(string Name, uint Target, bool Directory);

            public ResourceAccessor(string filename)
            {
                file = new ArcView(filename);
                try
                {
                    image = new ExeFile(file);
                    if (file.MaxOffset < 0x40) throw Invalid();
                    long pe = file.View.ReadUInt32(0x3C);
                    if (pe > file.MaxOffset - 24 || !file.View.AsciiEqual(pe, "PE\0\0")) throw Invalid();
                    int optionalSize = file.View.ReadUInt16(pe + 20);
                    long optional = pe + 24L;
                    int magic = file.View.ReadUInt16(optional);
                    int directories = magic == 0x10B ? 96 : magic == 0x20B ? 112 : throw Invalid();
                    if (optionalSize < directories + 24 || optionalSize > file.MaxOffset - optional) throw Invalid();
                    if (file.View.ReadUInt32(optional + directories - 4) < 3) return;
                    rootRva = file.View.ReadUInt32(optional + directories + 16);
                    rootSize = file.View.ReadUInt32(optional + directories + 20);
                    if (rootRva == 0 || rootSize == 0) return;
                    _ = image.Sections; // Validate and materialize the section table.
                    foreach (var type in Directory(0))
                    {
                        if (!type.Directory) throw Invalid();
                        var names = new Dictionary<string, SortedDictionary<int, ResourceData>>(StringComparer.Ordinal);
                        if (!resources.TryAdd(type.Name, names)) throw Invalid();
                        foreach (var name in Directory(type.Target))
                        {
                            if (!name.Directory) throw Invalid();
                            var languages = new SortedDictionary<int, ResourceData>();
                            if (!names.TryAdd(name.Name, languages)) throw Invalid();
                            foreach (var language in Directory(name.Target))
                            {
                                if (language.Directory || !language.Name.StartsWith('#')
                                    || !int.TryParse(language.Name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                                    throw Invalid();
                                long data = Position(language.Target, 16);
                                uint rva = file.View.ReadUInt32(data), size = file.View.ReadUInt32(data + 4);
                                if (!languages.TryAdd(id, new ResourceData(size == 0 ? 0 : Map(rva, size), size))) throw Invalid();
                            }
                        }
                    }
                }
                catch { file.Dispose(); throw; }
            }

            static BadImageFormatException Invalid() => new("Invalid PE resource table.");

            long Map(uint rva, uint size)
            {
                foreach (var section in image.m_section_list)
                {
                    long offset = (long)rva - section.VirtualAddress;
                    if (offset < 0 || offset > section.SizeOfRawData || size > section.SizeOfRawData - offset)
                        continue;
                    long position = section.PointerToRawData + offset;
                    if (position <= file.MaxOffset && size <= file.MaxOffset - position) return position;
                }
                throw Invalid();
            }

            long Position(uint relative, uint size)
            {
                if (relative > rootSize || size > rootSize - relative || (ulong)rootRva + relative > uint.MaxValue)
                    throw Invalid();
                return Map(rootRva + relative, size);
            }

            string Identifier(uint value)
            {
                if ((value & 0x80000000) == 0)
                {
                    if (value > ushort.MaxValue) throw Invalid();
                    return "#" + value.ToString(CultureInfo.InvariantCulture);
                }
                uint offset = value & 0x7FFFFFFF;
                if (strings.TryGetValue(offset, out var cached)) return cached;
                int count = file.View.ReadUInt16(Position(offset, 2));
                int bytes = count * 2;
                stringBytes = checked(stringBytes + bytes);
                if (stringBytes > 16 << 20) throw Invalid();
                var data = file.View.ReadBytes(Position(checked(offset + 2), (uint)bytes), (uint)bytes);
                string text = Encoding.Unicode.GetString(data);
                int end = text.IndexOf('\0');
                if (end >= 0) text = text[..end];
                strings.Add(offset, text);
                return text;
            }

            IEnumerable<Node> Directory(uint relative)
            {
                long position = Position(relative, 16);
                int count = file.View.ReadUInt16(position + 12) + file.View.ReadUInt16(position + 14);
                visits = checked(visits + count);
                if (visits >= 0x40000) throw Invalid();
                _ = Position(checked(relative + 16), checked((uint)count * 8));
                for (int i = 0; i < count; i++)
                {
                    long entry = position + 16 + i * 8;
                    uint name = file.View.ReadUInt32(entry), target = file.View.ReadUInt32(entry + 4);
                    yield return new Node(Identifier(name), target & 0x7FFFFFFF, (target & 0x80000000) != 0);
                }
            }

            void EnsureOpen()
            {
                if (disposed) throw new ObjectDisposedException(nameof(ResourceAccessor));
            }

            ResourceData? Find(string name, string type)
            {
                EnsureOpen();
                if (!resources.TryGetValue(type, out var names) || !names.TryGetValue(name, out var languages)
                    || languages.Count == 0) return null;
                int current = CultureInfo.CurrentUICulture.LCID;
                foreach (int id in new[] { current, current & 0x3FF, 0, 1033 })
                    if (languages.TryGetValue(id, out var data)) return data;
                return languages.First().Value;
            }

            public byte[] GetResource(string name, string type)
                => Find(name, type) is ResourceData data ? file.View.ReadBytes(data.Offset, data.Size) : null;

            public int ReadResource(string name, string type, byte[] destination, int offset)
            {
                ArgumentNullException.ThrowIfNull(destination);
                if (offset < 0 || offset > destination.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                if (Find(name, type) is not ResourceData data) return 0;
                int count = (int)Math.Min(data.Size, (uint)(destination.Length - offset));
                return checked((int)file.View.Read(data.Offset, destination, offset, (uint)count));
            }

            public uint GetResourceSize(string name, string type) => Find(name, type)?.Size ?? 0;

            public IEnumerable<string> EnumTypes()
            {
                EnsureOpen();
                return resources.Keys.ToArray();
            }

            public IEnumerable<string> EnumNames(string type)
            {
                EnsureOpen();
                return resources.TryGetValue(type, out var names) ? names.Keys.ToArray() : Array.Empty<string>();
            }

            public void Dispose()
            {
                if (!disposed) { file.Dispose(); disposed = true; }
            }
        }

        public sealed class VersionAccessor
        {
            public ushort[] FileVersions;
            public ushort[] ProductVersions;

            public VersionAccessor(string filename)
            {
                using var resources = new ResourceAccessor(filename);
                string name = resources.EnumNames("#16").FirstOrDefault();
                var data = name == null ? null : resources.GetResource(name, "#16");
                const int valueOffset = 40;
                if (data == null || data.Length < valueOffset + 52
                    || BitConverter.ToUInt16(data, 0) < valueOffset + 52 || BitConverter.ToUInt16(data, 0) > data.Length
                    || BitConverter.ToUInt16(data, 2) < 52
                    || Encoding.Unicode.GetString(data, 6, 32) != "VS_VERSION_INFO\0"
                    || BitConverter.ToUInt32(data, valueOffset) != 0xFEEF04BD)
                    throw new InvalidFormatException("File has no valid fixed version resource.");
                static ushort[] Version(byte[] bytes, int offset)
                {
                    uint high = BitConverter.ToUInt32(bytes, offset), low = BitConverter.ToUInt32(bytes, offset + 4);
                    return new[] { (ushort)(high >> 16), (ushort)high, (ushort)(low >> 16), (ushort)low };
                }
                FileVersions = Version(data, valueOffset + 8);
                ProductVersions = Version(data, valueOffset + 16);
            }
        }
    }
}
