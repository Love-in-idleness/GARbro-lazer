/* Headless-compositor integration probe; never connects to the user's desktop.
 * Acts as an input-method-v2 client so the compositor relays real text-input-v3
 * composition and editing events to the Avalonia window. */
#define _POSIX_C_SOURCE 200809L
#include <wayland-client.h>
#include <xkbcommon/xkbcommon.h>
#include <poll.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include "input-method.h"
#include "virtual-keyboard.h"

static struct wl_seat *seat;
static struct zwp_input_method_manager_v2 *method_manager;
static struct zwp_virtual_keyboard_manager_v1 *keyboard_manager;
static uint32_t serial, cursor, anchor;
static int active, activations, deactivations, unavailable;
static char *surrounding;
static double last_done;
static double last_action;
static double seconds(void);

static void registry_global(void *data, struct wl_registry *registry,
        uint32_t name, const char *interface, uint32_t version) {
    (void)data;
    if (!strcmp(interface, wl_seat_interface.name) && !seat)
        seat=wl_registry_bind(registry,name,&wl_seat_interface,version<7?version:7);
    else if (!strcmp(interface,zwp_input_method_manager_v2_interface.name))
        method_manager=wl_registry_bind(registry,name,&zwp_input_method_manager_v2_interface,1);
    else if (!strcmp(interface,zwp_virtual_keyboard_manager_v1_interface.name))
        keyboard_manager=wl_registry_bind(registry,name,&zwp_virtual_keyboard_manager_v1_interface,1);
}
static void registry_remove(void *data,struct wl_registry *registry,uint32_t name)
    {(void)data;(void)registry;(void)name;}
static const struct wl_registry_listener registry_listener={registry_global,registry_remove};

static void activate(void *data,struct zwp_input_method_v2 *method)
    {(void)data;(void)method;active=1;activations++;fprintf(stderr,"activate %d\n",activations);}
static void deactivate(void *data,struct zwp_input_method_v2 *method)
    {(void)data;(void)method;active=0;deactivations++;fprintf(stderr,"deactivate %d\n",deactivations);}
static void surrounding_text(void *data,struct zwp_input_method_v2 *method,
        const char *text,uint32_t current,uint32_t selection) {
    (void)data;(void)method;
    free(surrounding);surrounding=strdup(text);cursor=current;anchor=selection;
    fprintf(stderr,"surrounding [%s] cursor=%u anchor=%u\n",text,cursor,anchor);
}
static void cause(void *data,struct zwp_input_method_v2 *method,uint32_t value)
    {(void)data;(void)method;(void)value;}
static void content(void *data,struct zwp_input_method_v2 *method,uint32_t hint,uint32_t purpose)
    {(void)data;(void)method;(void)hint;(void)purpose;}
static void done(void *data,struct zwp_input_method_v2 *method)
    {(void)data;(void)method;serial++;last_done=seconds();fprintf(stderr,"done %u\n",serial);}
static void not_available(void *data,struct zwp_input_method_v2 *method)
    {(void)data;(void)method;unavailable=1;}
static const struct zwp_input_method_v2_listener method_listener={
    .activate=activate,.deactivate=deactivate,.surrounding_text=surrounding_text,
    .text_change_cause=cause,.content_type=content,.done=done,.unavailable=not_available
};

static int flag(const char *directory,const char *name) {
    char path[4096];snprintf(path,sizeof path,"%s/%s",directory,name);
    return access(path,F_OK)==0;
}
static void ready(const char *directory) {
    char path[4096];snprintf(path,sizeof path,"%s/ime-helper-ready",directory);
    FILE *file=fopen(path,"w");if(!file){perror(path);exit(1);}fputs("ready\n",file);fclose(file);
}
static double seconds(void) {
    struct timespec now;clock_gettime(CLOCK_MONOTONIC,&now);
    return now.tv_sec+now.tv_nsec/1e9;
}

int main(int argc,char **argv) {
    if(argc!=2){fprintf(stderr,"Expected a private probe directory.\n");return 2;}
    struct wl_display *display=wl_display_connect(NULL);
    if(!display){fprintf(stderr,"Cannot connect to the private compositor.\n");return 1;}
    struct wl_registry *registry=wl_display_get_registry(display);
    wl_registry_add_listener(registry,&registry_listener,NULL);
    if(wl_display_roundtrip(display)<0 || !seat || !method_manager || !keyboard_manager) {
        fprintf(stderr,"Compositor must expose a seat, input-method-v2 and virtual-keyboard-v1.\n");return 1;
    }
    struct zwp_virtual_keyboard_v1 *keyboard=zwp_virtual_keyboard_manager_v1_create_virtual_keyboard(keyboard_manager,seat);
    struct xkb_context *context=xkb_context_new(XKB_CONTEXT_NO_FLAGS);
    struct xkb_rule_names names={.layout="us"};
    struct xkb_keymap *keymap=xkb_keymap_new_from_names(context,&names,XKB_KEYMAP_COMPILE_NO_FLAGS);
    if(!keymap){fprintf(stderr,"Cannot build test keymap.\n");return 1;}
    char *text=xkb_keymap_get_as_string(keymap,XKB_KEYMAP_FORMAT_TEXT_V1);
    char path[4096];snprintf(path,sizeof path,"%s/keymap-XXXXXX",argv[1]);
    int fd=mkstemp(path);if(fd<0){perror("keymap");return 1;}unlink(path);
    size_t length=strlen(text)+1;
    if(write(fd,text,length)!=(ssize_t)length){perror("write keymap");return 1;}
    zwp_virtual_keyboard_v1_keymap(keyboard,WL_KEYBOARD_KEYMAP_FORMAT_XKB_V1,fd,length);
    close(fd);free(text);xkb_keymap_unref(keymap);xkb_context_unref(context);
    struct zwp_input_method_v2 *method=zwp_input_method_manager_v2_get_input_method(method_manager,seat);
    zwp_input_method_v2_add_listener(method,&method_listener,NULL);
    if(wl_display_roundtrip(display)<0)return 1;
    ready(argv[1]);
    int phase=0, previous_activations=0, previous_deactivations=0;uint32_t preedit_serial=0;double start=seconds();
    while(seconds()-start<35 && !unavailable) {
        if(wl_display_roundtrip(display)<0)break;
        /* Wait for compositor acknowledgments after layout/caret changes before
         * committing an IME batch; sending an old done serial is invalid. */
        int settled=seconds()-last_done>.75 && seconds()-last_action>.75;
        if(phase==0 && active && settled) {
            zwp_input_method_v2_set_preedit_string(method,"ni",2,2);
            zwp_input_method_v2_commit(method,serial);phase=1;preedit_serial=serial;
            fprintf(stderr,"sent preedit serial=%u\n",serial);
            last_action=seconds();
        } else if(phase==1 && active && settled && !flag(argv[1],"preedit-ok") && serial!=preedit_serial) {
            zwp_input_method_v2_set_preedit_string(method,"ni",2,2);
            zwp_input_method_v2_commit(method,serial);preedit_serial=serial;last_action=seconds();
            fprintf(stderr,"renewed preedit serial=%u after caret/layout update\n",serial);
        } else if(phase==1 && settled && flag(argv[1],"preedit-ok")) {
            zwp_input_method_v2_set_preedit_string(method,"",0,0);
            zwp_input_method_v2_commit_string(method,"你好");
            zwp_input_method_v2_commit(method,serial);phase=2;
            fprintf(stderr,"sent commit serial=%u\n",serial);
            last_action=seconds();
        } else if(phase==2 && settled && flag(argv[1],"commit-ok") && surrounding
                && !strcmp(surrounding,"你好") && cursor==6 && anchor==6) {
            /* Lengths are UTF-8 bytes, not UTF-16 indexes: remove one CJK character. */
            zwp_input_method_v2_delete_surrounding_text(method,3,0);
            zwp_input_method_v2_commit_string(method,"世界");
            zwp_input_method_v2_commit(method,serial);phase=3;
            fprintf(stderr,"sent deletion serial=%u\n",serial);
            previous_activations=activations;previous_deactivations=deactivations;
            last_action=seconds();
        } else if(phase==3 && settled && flag(argv[1],"delete-ok") && active
                && activations>previous_activations && deactivations>previous_deactivations) {
            zwp_input_method_v2_commit_string(method,"かな");
            zwp_input_method_v2_commit(method,serial);phase=4;
            fprintf(stderr,"sent focus commit serial=%u\n",serial);
            last_action=seconds();
        } else if(phase==4 && flag(argv[1],"focus-ok")) {
            puts("IME_OK preedit, commit, UTF-8 surrounding/deletion and focus transfer");
            wl_display_flush(display);
            wl_display_disconnect(display);free(surrounding);return 0;
        }
        wl_display_flush(display);
        struct pollfd wait={.fd=wl_display_get_fd(display),.events=POLLIN};
        if(poll(&wait,1,20)>0 && wl_display_dispatch(display)<0)break;
    }
    fprintf(stderr,"IME probe failed: phase=%d active=%d activations=%d deactivations=%d cursor=%u anchor=%u surrounding=%s\n",
        phase,active,activations,deactivations,cursor,anchor,surrounding?surrounding:"(null)");
    return 1;
}
