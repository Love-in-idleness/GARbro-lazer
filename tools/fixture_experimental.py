"""Independent synthetic encoders for historical experimental providers."""
import struct


def next_key(key):
    return (key * 7 + 3) & 0xffffffff


def payload(data, key):
    result = bytearray()
    for offset in range(0, len(data), 4):
        result.extend(value ^ (key >> (i * 8) & 255) for i, value in enumerate(data[offset:offset+4]))
        key = next_key(key)
    return result


def rgss(files, version):
    header = b'RGSSAD\0' + bytes([version])
    if version == 1:
        result = bytearray(header)
        key = 0xdeadcafe
        for name, data in files:
            encoded = name.encode('utf-8')
            result.extend(struct.pack('<I', len(encoded) ^ key)); key = next_key(key)
            for value in encoded:
                result.append(value ^ (key & 255)); key = next_key(key)
            result.extend(struct.pack('<I', len(data) ^ key)); key = next_key(key)
            result.extend(payload(data, key))
        return result
    seed = 0x12345678
    key = (seed * 9 + 3) & 0xffffffff
    index_size = 12 + sum(16 + len(name.encode('utf-8')) for name, _ in files) + 4
    index = bytearray(header + struct.pack('<I', seed))
    body = bytearray()
    for number, (name, data) in enumerate(files):
        entry_key = (0x13579bdf + number * 0x11111111) & 0xffffffff
        raw = name.encode('utf-8')
        index.extend(struct.pack('<IIII', (index_size + len(body)) ^ key,
                                 len(data) ^ key, entry_key ^ key, len(raw) ^ key))
        index.extend(value ^ (key >> ((i % 4) * 8) & 255) for i, value in enumerate(raw))
        body.extend(payload(data, entry_key))
    return index + struct.pack('<I', key) + body


def iris(files):
    dirname = 'assets\0'.encode('utf-16le')
    directory = b'\0'*8 + struct.pack('<II', len(dirname), 0) + dirname
    directory += struct.pack('<IIII', 0, len(files), 0, 0)
    body = bytearray()
    for name, data in files:
        raw = (name + '\0').encode('utf-16le')
        padded = (len(data) + 15) // 16 * 16
        directory += struct.pack('<8I', 0, 0, len(data), padded, len(raw), 0, 0, 0) + raw
        body.extend(data + b'\0' * (padded - len(data)))
    return b'IRISPCK\0' + b'\0'*16 + directory + body


def hibiki(files, key):
    index = bytearray(struct.pack('<H',len(files)))
    body = bytearray()
    start = 2 + len(files)*266
    for name, data in files:
        index.extend(name.encode('cp932').ljust(256,b'\0') + struct.pack('<HII',0,len(data),start+len(body)))
        body.extend(value ^ (key[i] if i < len(key) else 0) for i,value in enumerate(data))
    return bytes(value ^ 0x80 for value in index) + body
