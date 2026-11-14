"""Minimal MS-NRBF encodings for synthetic archive indexes; never runs a serializer."""
import struct


def string(value):
    raw = value.encode('utf-8')
    length = len(raw)
    prefix = bytearray()
    while length >= 128:
        prefix.append((length & 127) | 128)
        length >>= 7
    return bytes(prefix) + bytes([length]) + raw


def library(number, name):
    return b'\x0c' + struct.pack('<i', number) + string(name)


def record(number, name, members, kinds, extras, assembly):
    return b'\x05' + struct.pack('<i', number) + string(name) + struct.pack('<i', len(members)) \
        + b''.join(map(string, members)) + bytes(kinds) + extras + struct.pack('<i', assembly)


def class_array(number, name, assembly, count):
    return b'\x07' + struct.pack('<iBiiB', number, 0, 1, count, 4) + string(name) + struct.pack('<i', assembly)


HEADER = b'\0' + struct.pack('<iiii', 1, -1, 1, 0)
ENGINE = '[[UnityEngine.Texture2D, UnityEngine]]'


def gx4_index(files, root_name=None, array_count=None):
    root_name = root_name or 'GX4.PackageFile`1+PFHeaders' + ENGINE
    entry_name = 'GX4.PackageFile`1+PFHeader' + ENGINE
    data = HEADER + library(2, 'GX4Lib')
    data += record(1, root_name, ['headers'], [4], string(entry_name+'[]')+struct.pack('<i',2), 2)
    data += b'\x09' + struct.pack('<i',3)
    data += class_array(3, entry_name, 2, len(files) if array_count is None else array_count)
    if array_count is not None:
        return data + b'\x0e' + struct.pack('<i', array_count) + b'\x0b'
    data += b''.join(b'\x09'+struct.pack('<i',10+i)for i in range(len(files)))
    offset = 0
    for i, (name, payload) in enumerate(files):
        data += record(10+i, entry_name, ['FileName', 'readStartBytePos', 'ByteLength'], [1, 0, 0], b'\x09\x09', 2)
        data += b'\x06' + struct.pack('<i', 100+i) + string(name) + struct.pack('<qq', offset, len(payload))
        offset += len(payload)
    return data + b'\x0b'


def unity_index(files):
    assembly = 'mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'
    entry_name = 'SpVM.Library.LinkerInfo'
    data = HEADER + library(2, assembly) + library(3, 'Assembly-CSharp')
    data += record(1, 'System.Collections.Generic.List`1[['+entry_name+', Assembly-CSharp]]',
                   ['_items', '_size', '_version'], [4, 0, 0], string(entry_name+'[]')+struct.pack('<i',3)+b'\x08\x08', 2)
    data += b'\x09' + struct.pack('<iii',4,len(files),0)
    data += class_array(4, entry_name, 3, len(files)+1)
    data += b''.join(b'\x09'+struct.pack('<i',10+i)for i in range(len(files)))+b'\x0a'
    offset = 0
    for i, (name, payload) in enumerate(files):
        data += record(10+i, entry_name, ['<name>k__BackingField', '<offset>k__BackingField', '<size>k__BackingField'],
                       [1, 0, 0], b'\x08\x08', 3)
        data += b'\x06' + struct.pack('<i', 100+i) + string(name) + struct.pack('<ii', offset, len(payload))
        offset += len(payload)
    return data + b'\x0b'
