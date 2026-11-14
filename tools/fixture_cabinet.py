"""Independent CFHEADER/CFFOLDER/CFFILE/CFDATA encodings from the CAB spec."""
import struct
import zlib


def checksum(data, seed=0):
    full = len(data) // 4 * 4
    for offset in range(0, full, 4):
        seed ^= int.from_bytes(data[offset:offset+4], 'little')
    return seed ^ int.from_bytes(data[full:], 'big')


def cabinet(files, mszip=False):
    records = bytearray()
    payload = bytearray()
    for name, data in files:
        records.extend(struct.pack('<IIHHHH', len(data), len(payload), 0, 0x5021, 0, 0x80))
        records.extend(name.encode('utf-8') + b'\0')
        payload.extend(data)
    blocks = bytearray()
    history = b''
    count = 0
    for offset in range(0, len(payload), 32768):
        raw = bytes(payload[offset:offset+32768])
        if mszip:
            codec = zlib.compressobj(level=6, wbits=-15, **({'zdict':history} if history else {}))
            packed = b'CK' + codec.compress(raw) + codec.flush()
            if history:
                # Ensure this fixture actually exercises references into the
                # preceding block instead of merely testing independent deflate.
                try:
                    zlib.decompress(packed[2:], wbits=-15)
                except zlib.error:
                    pass
                else:
                    raise AssertionError('Expected a dictionary-dependent MSZIP block')
        else:
            packed = raw
        fields = struct.pack('<HH',len(packed),len(raw)) + b'Rsv'
        blocks.extend(struct.pack('<I',checksum(fields,checksum(packed))) + fields + packed)
        history = (history + raw)[-32768:]
        count += 1
    reserved = struct.pack('<HBB',5,2,3) + b'HDR!?'
    files_offset = 36 + len(reserved) + 10
    start = files_offset + len(records)
    header = struct.pack('<4s5IBB5H',b'MSCF',0,start+len(blocks),0,files_offset,0,
                         3,1,1,len(files),4,7,0)
    folder = struct.pack('<IHH',start,count,int(mszip)) + b'FR'
    return header + reserved + folder + records + blocks
