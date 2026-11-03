# Linux desktop

Audio playback uses the system SDL2 library (`libsdl2-2.0-0 >= 2.0.22` on
Debian/Ubuntu). The DEB declares this dependency. Linux tarballs do not bundle
SDL; install it through the distribution package manager.

GARbro prefers the native Wayland backend. If it cannot connect to a usable
Wayland compositor, it falls back to X11. `GARBRO_LINUX_BACKEND=wayland` requires
native Wayland and reports initialization failure; `GARBRO_LINUX_BACKEND=x11`
selects X11 explicitly. The default is `auto`. The Wayland app ID is
`org.garbro.GARbro`.

The Avalonia 12.1.4 Wayland backend binds `zwp_text_input_v3` when the compositor
advertises it. GARbro's editable fields support preedit, committed text,
surrounding text, selection and caret geometry. Input-method setup belongs to the
desktop compositor/IME. No XIM environment-variable workaround is required for
native text-input-v3.

Avalonia 12.1.4 enables the text-input session before its initial surrounding
text is populated. On wlroots this omits the surrounding-text capability from
the enabled session. GARbro forwards the standard text client through a small
adapter and requests a reset after focus initialization, using public Avalonia
APIs. This re-enables the cached state. The behavior is covered by a real protocol
test, not by directly assigning text to simulate input.

The integration test uses an owned headless Sway compositor with XWayland
disabled. It checks preedit without premature insertion, Chinese commit,
UTF-8 byte offsets and deletion, then Japanese commit into a second field after
focus transfer. Native window rendering and extraction also run in this session.
The local reference is Sway 1.10.1 / wlroots 0.18.2; CI retains the protocol trace
and screenshot. Native portal file-picker dialogs and every desktop IME are not
covered by that test.

Linux needs the .NET 10 runtime, Wayland client/EGL/GLES, xkbcommon and fontconfig
libraries. On Debian/Ubuntu, the native Wayland dependencies include
`libwayland-client0 libwayland-cursor0 libwayland-egl1 libegl1 libgles2 libxkbcommon0 libfontconfig1`.
X11 libraries are also needed for the fallback. A compositor-appropriate
`xdg-desktop-portal` backend supplies file-picker integration.

For the regression test, install `sway libwayland-dev libxkbcommon-dev libgles2`
and a C compiler, build the solution, then run:

```sh
python3 tools/smoke-wayland.py
```

The test creates its own runtime directory and seat; it does not send input to
the user's active desktop. Protocol XML notices are retained under
`tests/wayland/protocols`; NWayland's MIT notice is in `docs/licenses`.

Native Wayland requires a compositor advertising xdg_wm_base version 3 or newer.
Older compositors, including Ubuntu 24.04's Sway package, use the X11 fallback.
Ubuntu 24.04 GNOME is supported; CI uses a newer headless Sway and separately tests
the DEB on Ubuntu 24.04 and Debian 13 for both CPU architectures.

原生 Wayland 需要合成器提供 xdg_wm_base 第 3 版或更高版本。Ubuntu 24.04 自带的旧 Sway 等环境会回退到 X11；Ubuntu 24.04 的 GNOME 不受此限制。CI 使用较新的无头 Sway 测试输入法，并另行在 Ubuntu 24.04、Debian 13 上安装测试双架构 DEB。

Standard cursors use the compositor's `wp_cursor_shape_v1` protocol when it is
available. On KDE 6, KWin supplies the user's cursor theme, size and animation,
including changes made while GARbro is running. GARbro installs no cursor theme
and has no custom cursor factory. On older compositors, libwayland-cursor reads
`XCURSOR_THEME` / `XCURSOR_SIZE` and its system default theme. Windows, X11 and
macOS continue using their existing platform backends.

在 KDE 6 原生 Wayland 下，光标外观、大小与动画由 KWin 按系统设置提供，运行中修改也会跟随。
GARbro 不安装或绘制自己的光标。缺少新协议的旧合成器使用系统 libwayland-cursor，遵循
`XCURSOR_THEME`、`XCURSOR_SIZE`；Windows、X11 和 macOS 保留原有系统后端。

For a reproducible KDE regression, install `kwin-wayland kwin-common
breeze-cursor-theme plasma-wayland-protocols python3-dbus libwayland-dev gcc
xvfb`, then build the native smoke project and run:

```sh
dbus-run-session -- /usr/bin/python3 tools/smoke-kde-cursors.py --output artifacts/kde-cursors
```

This launches a private nested KWin session. Permission-check overrides apply
only to that disposable compositor, for test input and screenshots. It never
injects input into the user's desktop. The test checks protocol messages,
actual caption hover/pressed screenshots, live system theme/size changes and
popup/dialog pointer enters. A compositor without cursor-shape can be checked
with `--expect-fallback --cursor-size 24` and `--cursor-size 48` in separate runs.
