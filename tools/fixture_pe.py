"""Independent minimal PE/COFF resources; contains no executable machine code."""
import struct


def fixed_version():
    key = 'VS_VERSION_INFO\0'.encode('utf-16le')
    header = struct.pack('<HHH', 92, 52, 0) + key
    return header.ljust(40, b'\0') + struct.pack('<13I', 0xfeef04bd, 0x10000,
        0x00010002, 0x00030004, 0x00050006, 0x00070008, 0x3f, 0, 4, 1, 0, 0, 0)


def executable(is64=False):
    tree = {'CUSTOM': {'日本語': {1033: b'Unicode resource\n'}},
            10: {1: {1033: b'Hello, PE resources!\n'}, 2: {1033: b''}},
            16: {1: {1033: fixed_version()}}}
    directory = bytearray()
    leaves = []
    names = []

    def node(items):
        ordered = sorted(items, key=lambda key: (not isinstance(key, str), key))
        offset = len(directory)
        directory.extend(struct.pack('<IIIHH', 0, 0, 0, sum(isinstance(k,str) for k in ordered),
                                     sum(isinstance(k,int) for k in ordered)))
        directory.extend(b'\0'*(len(ordered)*8))
        for i, key in enumerate(ordered):
            entry = offset + 16 + i*8
            if isinstance(key, str):
                names.append((entry, key))
            else:
                struct.pack_into('<I', directory, entry, key)
            value = items[key]
            if isinstance(value, dict):
                target = node(value) | 0x80000000
            else:
                target = len(directory)
                directory.extend(b'\0'*16)
                leaves.append((target, value))
            struct.pack_into('<I', directory, entry+4, target)
        return offset

    assert node(tree) == 0
    for entry, name in names:
        offset = len(directory)
        encoded = name.encode('utf-16le')
        directory.extend(struct.pack('<H',len(encoded)//2)+encoded)
        struct.pack_into('<I',directory,entry,offset|0x80000000)
    while len(directory)%4:directory.append(0)
    for entry, value in leaves:
        rva = 0x1000+len(directory) if value else 0
        struct.pack_into('<IIII',directory,entry,rva,len(value),0,0)
        directory.extend(value)
        while len(directory)%4:directory.append(0)
    resource_size = len(directory)
    directory.extend(b'\0' * ((-len(directory))%512))
    header = bytearray(512)
    header[:2] = b'MZ';struct.pack_into('<I',header,0x3c,0x80)
    header[0x80:0x84] = b'PE\0\0'
    optional_size = 240 if is64 else 224
    struct.pack_into('<HHIIIHH',header,0x84,0x8664 if is64 else 0x14c,1,0,0,0,
                     optional_size,0x22 if is64 else 0x102)
    optional = 0x98
    struct.pack_into('<HBBIII',header,optional,0x20b if is64 else 0x10b,0,0,0,len(directory),0)
    if is64:
        struct.pack_into('<Q',header,optional+24,0x140000000)
    else:
        struct.pack_into('<I',header,optional+28,0x400000)
    struct.pack_into('<II',header,optional+32,0x1000,0x200)
    struct.pack_into('<HH',header,optional+40,4,0)
    struct.pack_into('<HH',header,optional+48,4,0)
    struct.pack_into('<II',header,optional+56,0x2000,0x200)
    struct.pack_into('<H',header,optional+68,3)
    if is64:
        struct.pack_into('<QQQQII',header,optional+72,0x100000,0x1000,0x100000,0x1000,0,16)
    else:
        struct.pack_into('<IIIIII',header,optional+72,0x100000,0x1000,0x100000,0x1000,0,16)
    directories = optional + (112 if is64 else 96)
    struct.pack_into('<II',header,directories+16,0x1000,resource_size)
    section = optional+optional_size
    struct.pack_into('<8sIIIIIIHHI',header,section,b'.rsrc\0\0\0',resource_size,0x1000,
                     len(directory),0x200,0,0,0,0,0x40000040)
    return header+directory
