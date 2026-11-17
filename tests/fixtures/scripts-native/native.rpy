define actor = Character("Example")
default choice = 0
image example = "example.png"
transform fade_in:
    alpha 0.0
    linear 0.25 alpha 1.0
screen native_sample(title="Example"):
    frame:
        vbox:
            text title
            textbutton "OK" action Return()
label start:
    actor "中文 日本語 Русский"
    if choice == 0:
        $ choice += 1
    else:
        pass
    menu:
        "First":
            jump first
        "Second":
            jump second
label first:
    call second from native_return
    return
label second:
    show example at fade_in
    actor "Second"
    return
