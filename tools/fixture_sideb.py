"""Side-B fixture encoded independently with py3rijndael 0.3.3 (generator only)."""
import struct
import zlib


def sideb(files):
    from py3rijndael import RijndaelCbc, ZeroPadding
    key = b'OTG'.ljust(32,b'\0')
    iv = bytes(range(32))
    info = ''.join(f'U_{i}:{name}\t{len(data)}\t\r\n' for i,(name,data) in enumerate(files)).encode('utf-8')
    header_size = (len(info)+31)//32*32
    index = RijndaelCbc(key,iv,ZeroPadding(32),block_size=32).encrypt(info.ljust(header_size+32,b'\0'))
    body = RijndaelCbc(key,index[-32:],ZeroPadding(32),block_size=32).encrypt(zlib.compress(b''.join(d for _,d in files)))
    header = bytearray(64)
    struct.pack_into('<I',header,0,0x00030006)
    struct.pack_into('<I',header,0x1c,header_size)
    header[0x20:0x40] = iv
    return header+index+body
