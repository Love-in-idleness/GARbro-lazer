"""Python SQLite + independent historical RC4 pages and OpenSSL AES prefixes."""
import hashlib
import sqlite3
import subprocess
import tempfile
from pathlib import Path


def rc4(key, source):
    state = list(range(256))
    j = 0
    for i in range(256):
        j = (j + state[i] + key[i % len(key)]) & 255
        state[i],state[j] = state[j],state[i]
    i = j = 0
    output = bytearray()
    for value in source:
        i = (i+1) & 255
        j = (j+state[i]) & 255
        state[i],state[j] = state[j],state[i]
        output.append(value ^ state[(state[i]+state[j]) & 255])
    return output


def name_key(name, length):
    data = name.encode('utf-8')
    key = bytearray(data[:length].ljust(length,b'\0'))
    for i in range(length,len(data)):
        key[i % length] ^= data[i]
    return key.hex()


def igs(files, page_size, password, full_prefix=True):
    body = bytearray()
    offsets = []
    for name,data in files:
        offsets.append(len(body))
        encrypted = subprocess.run(['openssl','enc','-aes-256-cbc','-nosalt',
            '-K',name_key(name,32),'-iv',name_key(name,16)],
            input=data[:256].ljust(256,b'\0') if full_prefix else data[:256],stdout=subprocess.PIPE,check=True).stdout
        assert len(encrypted) == 272 if full_prefix else len(encrypted) == (min(256,len(data))//16+1)*16
        body.extend(encrypted + data[256:])
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder)/'index.db'
        with sqlite3.connect(path) as db:
            db.execute(f'PRAGMA page_size={page_size}')
            db.execute('CREATE TABLE archives (id INTEGER PRIMARY KEY, name TEXT)')
            db.execute('CREATE TABLE file_infos (id INTEGER PRIMARY KEY, name TEXT, filepath TEXT, size INTEGER, offset INTEGER, typeID INTEGER, archiveID INTEGER)')
            db.execute('INSERT INTO archives VALUES (1,?)',('standard',))
            for i,((name,data),offset) in enumerate(zip(files,offsets)):
                db.execute('INSERT INTO file_infos VALUES (?,?,?,?,?,?,1)',(i+1,name,name,len(data),offset,0))
        plain = path.read_bytes()
    if password is None:
        return bytes(body),plain
    key = hashlib.sha1(password.encode('utf-8')).digest()[:16]
    encrypted = b''.join(rc4(key,plain[p:p+page_size]) for p in range(0,len(plain),page_size))
    return bytes(body),encrypted
