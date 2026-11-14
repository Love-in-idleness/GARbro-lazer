# Wayland input-method integration

`tools/smoke-wayland.py` creates a private runtime directory and headless Sway
compositor with XWayland disabled. It removes the existing display/socket variables
before connecting the probe, so it never types into the user's desktop.

The helper creates a virtual keyboard and acts as an input-method-v2 client.
Sway relays its events through the application's actual text-input-v3 connection.
The native Avalonia window checks visible preedit without premature insertion,
Chinese commit, UTF-8 surrounding-text byte offsets, deletion and transfer to a
second editor. Artifacts retain the protocol trace and a rendered window.

The protocol XML files retain their upstream MIT-style notices. They were copied
from `swaywm/wlroots`'s `protocol/` directory. They are test inputs, not GARbro
runtime components. No real IME or game content is required for this test.
