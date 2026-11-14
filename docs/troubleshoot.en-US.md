# Troubleshooting

## Opening resources
Open the parent folder when a format needs an auxiliary index or key file. Choose the correct game scheme in the decoder parameters. Passwords and keys remain in the current session.

If names or text look wrong, change the encoding in Settings → Decoding. Fixed encodings required by a format or encryption algorithm keep their original meaning.

Some Fate/hollow ataraxia language and voice packs wrap XP3 in MoleBox. Download demoleition VS from **Help → Tool downloads**. Copy the pack and its companion `FateFD.exe` into an empty folder. Open the copied executable in demoleition VS and accept its hidden-filenames prompt. Rename the resulting `namemd5_….bin` with an XP3 header to `.xp3`, open it in GARbro and select the **Fate/hollow ataraxia** scheme.

## Android storage
Select files or folders with the system picker. If access has expired, select the source again. Optional access to shared storage can reduce temporary copying for large local folders; Android still protects other apps' private directories.

Choose another destination when a document provider cannot safely replace an existing file. Completed output is retained after an interrupted task. Reopen the same source and retry; a changed source requires a different output directory.

## Background tasks and audio
Android extraction and conversion continue with a cancellable progress notification. The system may stop the app under memory pressure or impose its background-service time limit. Interrupted jobs are reported on the next launch.

Audio starts only when Play is pressed. Android pauses playback when audio focus is lost or headphones disconnect. Linux uses system SDL2; check the audio device and volume if playback is unavailable.

## Desktop installation
Portable and preview desktop installers require the matching .NET 10 Runtime and CPU architecture. On Windows, trust the public certificate from the same self-signed MSIX release before installing it. Microsoft Store packages use their separate registered identity.

Linux prefers native Wayland with X11 fallback. GARBRO_LINUX_BACKEND=x11 selects X11 for diagnosis. Keep the input-method service enabled for Chinese or Japanese input.

## Diagnostics
Use Help → Export logs to save a local diagnostic ZIP. Paths and key material are redacted. Logs are not uploaded automatically; review them before attaching them to a report.
