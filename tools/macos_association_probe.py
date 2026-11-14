"""Read LaunchServices candidates/defaults without changing user choices."""
import ctypes

CF=ctypes.CDLL('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')
LS=ctypes.CDLL('/System/Library/Frameworks/CoreServices.framework/CoreServices')
CF.CFURLCreateFromFileSystemRepresentation.argtypes=[ctypes.c_void_p,ctypes.c_char_p,ctypes.c_long,ctypes.c_bool];CF.CFURLCreateFromFileSystemRepresentation.restype=ctypes.c_void_p
CF.CFURLCopyFileSystemPath.argtypes=[ctypes.c_void_p,ctypes.c_int];CF.CFURLCopyFileSystemPath.restype=ctypes.c_void_p
CF.CFStringCreateWithCString.argtypes=[ctypes.c_void_p,ctypes.c_char_p,ctypes.c_uint];CF.CFStringCreateWithCString.restype=ctypes.c_void_p
CF.CFStringGetCString.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_long,ctypes.c_uint];CF.CFStringGetCString.restype=ctypes.c_bool
CF.CFArrayGetCount.argtypes=[ctypes.c_void_p];CF.CFArrayGetCount.restype=ctypes.c_long
CF.CFArrayGetValueAtIndex.argtypes=[ctypes.c_void_p,ctypes.c_long];CF.CFArrayGetValueAtIndex.restype=ctypes.c_void_p
CF.CFRelease.argtypes=[ctypes.c_void_p]
LS.UTTypeCreateAllIdentifiersForTag.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p];LS.UTTypeCreateAllIdentifiersForTag.restype=ctypes.c_void_p
LS.LSCopyApplicationURLsForURL.argtypes=[ctypes.c_void_p,ctypes.c_uint];LS.LSCopyApplicationURLsForURL.restype=ctypes.c_void_p
LS.LSCopyDefaultRoleHandlerForContentType.argtypes=[ctypes.c_void_p,ctypes.c_uint];LS.LSCopyDefaultRoleHandlerForContentType.restype=ctypes.c_void_p

def string(value):
    if not value:return None
    buffer=ctypes.create_string_buffer(32768)
    assert CF.CFStringGetCString(value,buffer,len(buffer),0x08000100)
    return buffer.value.decode()

def handlers(path):
    data=str(path.resolve()).encode();url=CF.CFURLCreateFromFileSystemRepresentation(None,data,len(data),False)
    array=LS.LSCopyApplicationURLsForURL(url,2);result=[]
    try:
        for i in range(CF.CFArrayGetCount(array)if array else 0):
            name=CF.CFURLCopyFileSystemPath(CF.CFArrayGetValueAtIndex(array,i),0)
            try:result.append(string(name))
            finally:CF.CFRelease(name)
    finally:
        if array:CF.CFRelease(array)
        CF.CFRelease(url)
    return result

def default_handler(uti):
    name=CF.CFStringCreateWithCString(None,uti.encode(),0x08000100)
    value=LS.LSCopyDefaultRoleHandlerForContentType(name,0xFFFFFFFF)
    try:return string(value)
    finally:
        if value:CF.CFRelease(value)
        CF.CFRelease(name)


def filename_types(extension):
    """Existing system UTIs for this suffix, without registering or changing defaults."""
    tag=CF.CFStringCreateWithCString(None,b'public.filename-extension',0x08000100)
    value=CF.CFStringCreateWithCString(None,extension.encode(),0x08000100)
    array=LS.UTTypeCreateAllIdentifiersForTag(tag,value,None)
    try:
        return [string(CF.CFArrayGetValueAtIndex(array,i)) for i in range(CF.CFArrayGetCount(array)if array else 0)]
    finally:
        if array:CF.CFRelease(array)
        CF.CFRelease(value);CF.CFRelease(tag)
