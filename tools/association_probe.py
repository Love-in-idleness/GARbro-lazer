"""Query GIO's real Open With candidates, including inherited MIME handlers."""
import ctypes
import ctypes.util
import os
import subprocess


def handlers(mime):
    gio=ctypes.CDLL(ctypes.util.find_library('gio-2.0'))
    glib=ctypes.CDLL(ctypes.util.find_library('glib-2.0'))
    gobject=ctypes.CDLL(ctypes.util.find_library('gobject-2.0'))
    class GList(ctypes.Structure):pass
    GList._fields_=[('data',ctypes.c_void_p),('next',ctypes.POINTER(GList)),('prev',ctypes.POINTER(GList))]
    gio.g_app_info_get_all_for_type.argtypes=[ctypes.c_char_p]
    gio.g_app_info_get_all_for_type.restype=ctypes.POINTER(GList)
    gio.g_app_info_get_id.argtypes=[ctypes.c_void_p];gio.g_app_info_get_id.restype=ctypes.c_char_p
    gobject.g_object_unref.argtypes=[ctypes.c_void_p]
    glib.g_list_free.argtypes=[ctypes.c_void_p]
    head=gio.g_app_info_get_all_for_type(mime.encode());node=head;result=[]
    try:
        while node:
            value=gio.g_app_info_get_id(node.contents.data)
            if value:result.append(value.decode())
            gobject.g_object_unref(node.contents.data);node=node.contents.next
    finally:glib.g_list_free(head)
    return result


def content_type(path):
    output=subprocess.check_output(['gio','info','-a','standard::content-type',str(path)],text=True,env=dict(os.environ,LC_ALL='C'))
    return next(line.split(': ',1)[1].strip() for line in output.splitlines() if 'standard::content-type:'in line)


def verify_candidate(path,application):
    mime=content_type(path);available=handlers(mime)
    assert application in available,f'{application} is missing from Open With for {path}: {mime}, {available}'
    print('Open With candidate:',path.name,mime,application)
    return mime
