using System;
using System.Buffers.Binary;
using System.IO;

namespace GARbro.Application;

internal static class TjsScriptValidation
{
    internal static void Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28 || !data.StartsWith("TJS2100\0"u8) || BinaryPrimitives.ReadInt32LittleEndian(data[8..]) != data.Length)
            throw new InvalidDataException("Invalid TJS2100 bytecode header.");
        bool constants = false, objects = false;
        int position = 12;
        while (position < data.Length)
        {
            if (position > data.Length - 8) throw new InvalidDataException("Truncated TJS2100 section.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[(position + 4)..]);
            if (length < 8 || length > data.Length - position) throw new InvalidDataException("Invalid TJS2100 section size.");
            var tag = data.Slice(position, 4);
            if (tag.SequenceEqual("DATA"u8))
            { if (constants) throw new InvalidDataException("Duplicate TJS2100 section."); constants = true; }
            else if (tag.SequenceEqual("OBJS"u8))
            { if (objects || !constants) throw new InvalidDataException("Invalid TJS2100 section order."); objects = true; }
            else throw new NotSupportedException("Unsupported TJS2100 section.");
            position += length;
        }
        if (!constants || !objects) throw new InvalidDataException("Missing TJS2100 section.");
    }
}
