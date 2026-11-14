#!/usr/bin/env python3
"""Export the existing transparent artwork to Windows ICO (requires Pillow).

Small entries use BGRA bitmaps plus explicit 1-bit transparency masks for shell
readers that do not honour PNG alpha. The 256px entry remains a PNG.
"""
import io
import struct
from pathlib import Path
from PIL import Image

ASSETS=Path(__file__).resolve().parents[1]/'modern/GARbro.UI/Assets'


def export():
    sizes=(16,24,32,48,64,128,256)
    image24=ASSETS/'app-icon-24.png'
    if not image24.exists():
        with Image.open(ASSETS/'app-icon.png')as master:
            master.resize((24,24),Image.Resampling.LANCZOS).save(image24)
    entries=[]
    for size in sizes:
        with Image.open(ASSETS/f'app-icon-{size}.png')as source:
            image=source.convert('RGBA')
            if size==256:
                data=io.BytesIO();image.save(data,format='PNG');payload=data.getvalue()
            else:
                flipped=image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
                pixels=flipped.tobytes('raw','BGRA')
                stride=((size+31)//32)*4;mask=bytearray(stride*size)
                alpha=flipped.getchannel('A').tobytes()
                for y in range(size):
                    for x in range(size):
                        if alpha[y*size+x]==0:mask[y*stride+x//8]|=0x80>>(x%8)
                payload=struct.pack('<IiiHHIIiiII',40,size,size*2,1,32,0,len(pixels),0,0,0,0)+pixels+mask
            entries.append((size,payload))
    offset=6+16*len(entries);directory=[]
    for size,payload in entries:
        directory.append(struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(payload),offset));offset+=len(payload)
    (ASSETS/'app-icon.ico').write_bytes(struct.pack('<HHH',0,1,len(entries))+b''.join(directory)+b''.join(p for _,p in entries))


if __name__=='__main__':export()
