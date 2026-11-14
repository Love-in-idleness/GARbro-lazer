using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;
using System.Linq;
using GameRes.Formats.Unity;
using GameRes.Gx4Lib;

namespace GameRes.Formats
{
    /// <summary>Read fixed archive-index schemas as data, without loading serialized CLR types.</summary>
    internal static class PortableSerializedIndexes
    {
        const int MaxIndexBytes = 64 << 20;

        static ClassRecord ReadRoot(Stream input)
        {
            if (input.CanSeek && input.Length - input.Position > MaxIndexBytes)
                throw new InvalidDataException("Serialized index exceeds the input limit.");
            using var limited = new LimitStream(input, MaxIndexBytes, true);
            return NrbfDecoder.DecodeClassRecord(limited, leaveOpen: true);
        }

        static ClassRecord[] ReadArray(ArrayRecord array)
        {
            if (array == null || array.Rank != 1 || !ArchiveFormat.IsSaneCount(array.Lengths[0]))
                throw new InvalidDataException("Invalid serialized index array length.");
            return array switch
            {
                SZArrayRecord<ClassRecord> classes => classes.GetArray(allowNulls: true),
                SZArrayRecord<SerializationRecord> objects => objects.GetArray(allowNulls: true)
                    .Select(record => record == null ? null : record as ClassRecord
                        ?? throw new InvalidDataException("Expected an index entry record.")).ToArray(),
                _ => throw new InvalidDataException("Unsupported serialized index array.")
            };
        }

        static string GenericArgument(ClassRecord record, string definition)
        {
            var name = record.TypeName;
            if (!name.IsConstructedGenericType || name.GetGenericTypeDefinition().FullName != definition
                || name.GetGenericArguments().Length != 1)
                throw new InvalidDataException("Unexpected serialized index type.");
            return name.GetGenericArguments()[0].FullName;
        }

        public static PFHeaders ReadGx4(Stream input)
        {
            var root = ReadRoot(input);
            if (root.TypeName.AssemblyName?.Name != "GX4Lib")
                throw new InvalidDataException("Unexpected GX4 index assembly.");
            string argument = GenericArgument(root, "GX4.PackageFile`1+PFHeaders");
            var records = ReadArray(root.GetArrayRecord("headers"));
            PFHeaders result = argument == "UnityEngine.Texture2D" ? new PFImageHeaders()
                : argument == "UnityEngine.AudioClip" ? new PFAudioHeaders() : new PFHeaders();
            result.headers = records.Select(record =>
            {
                if (record == null || record.TypeName.AssemblyName?.Name != "GX4Lib"
                    || GenericArgument(record, "GX4.PackageFile`1+PFHeader") != argument)
                    throw new InvalidDataException("Unexpected GX4 entry type.");
                string name = record.GetString("FileName");
                long offset = record.GetInt64("readStartBytePos"), size = record.GetInt64("ByteLength");
                if (string.IsNullOrWhiteSpace(name) || offset < 0 || size < 0 || size > uint.MaxValue)
                    throw new InvalidDataException("Invalid GX4 entry bounds.");
                return new PFHeader { FileName = name, readStartBytePos = offset, ByteLength = size };
            }).ToArray();
            return result;
        }

        public static List<LinkerInfo> ReadUnity(Stream input)
        {
            var root = ReadRoot(input);
            if (root.TypeName.AssemblyName?.Name is not (null or "mscorlib" or "System.Private.CoreLib")
                || GenericArgument(root, "System.Collections.Generic.List`1") != "SpVM.Library.LinkerInfo")
                throw new InvalidDataException("Unexpected Unity index element type.");
            int count = root.GetInt32("_size");
            var records = ReadArray(root.GetArrayRecord("_items"));
            if (!ArchiveFormat.IsSaneCount(count) || count > records.Length)
                throw new InvalidDataException("Invalid Unity index count.");
            return records.Take(count).Select(record =>
            {
                if (record == null || record.TypeName.AssemblyName?.Name != "Assembly-CSharp"
                    || record.TypeName.FullName != "SpVM.Library.LinkerInfo")
                    throw new InvalidDataException("Unexpected Unity index entry type.");
                string name = record.GetString("<name>k__BackingField");
                int offset = record.GetInt32("<offset>k__BackingField"), size = record.GetInt32("<size>k__BackingField");
                if (string.IsNullOrWhiteSpace(name) || offset < 0 || size < 0)
                    throw new InvalidDataException("Invalid Unity entry bounds.");
                return new LinkerInfo { name = name, offset = offset, size = size };
            }).ToList();
        }
    }
}
