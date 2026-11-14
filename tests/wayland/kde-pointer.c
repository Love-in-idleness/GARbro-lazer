/* Test-only input injection into an owned, isolated KWin compositor. */
#include <stdio.h>
#include <string.h>
#include <wayland-client.h>
#include "fake-input.h"
static struct org_kde_kwin_fake_input *input;
static void global(void *data,struct wl_registry *registry,uint32_t name,const char *interface,uint32_t version)
{
    (void)data;
    if(!strcmp(interface,"org_kde_kwin_fake_input"))
        input=wl_registry_bind(registry,name,&org_kde_kwin_fake_input_interface,version<5?version:5);
}
static void removed(void *data,struct wl_registry *registry,uint32_t name){(void)data;(void)registry;(void)name;}
int main(void)
{
    struct wl_display *display=wl_display_connect(NULL);if(!display)return 1;
    struct wl_registry *registry=wl_display_get_registry(display);
    const struct wl_registry_listener listener={global,removed};
    wl_registry_add_listener(registry,&listener,NULL);wl_display_roundtrip(display);if(!input)return 2;
    org_kde_kwin_fake_input_authenticate(input,"GARbro cursor regression","Private compositor test");
    wl_display_roundtrip(display);puts("ready");fflush(stdout);
    char line[100];double x,y;unsigned state;
    while(fgets(line,sizeof(line),stdin))
    {
        if(sscanf(line,"move %lf %lf",&x,&y)==2)
            org_kde_kwin_fake_input_pointer_motion_absolute(input,wl_fixed_from_double(x),wl_fixed_from_double(y));
        else if(sscanf(line,"button %u",&state)==1)org_kde_kwin_fake_input_button(input,272,state);
        else if(!strncmp(line,"quit",4))break;
        else return 3;
        if(wl_display_roundtrip(display)<0)return 4;
        puts("ok");fflush(stdout);
    }
    org_kde_kwin_fake_input_destroy(input);wl_registry_destroy(registry);wl_display_disconnect(display);return 0;
}
