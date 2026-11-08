# Capture smoke test

Build the app first, then run `powershell -ExecutionPolicy Bypass -File tests/Test-Capture.ps1` on an interactive Windows x64 desktop with the recording prerequisites installed.

This opens and records a dedicated test-pattern window for roughly nine seconds, validates capture settings and reversed/clamped selection math, writes a PNG and H.264 MP4, pauses, resumes and stops. Microphone and system audio capture are disabled. Temporary test artifacts are retained under the path printed by the script; no project configuration is used.

The UI integration was also checked locally for capture/power button visibility, audio options, countdown cancellation, native region pixel alignment, loopback AAC audio output, installer dependencies and data preservation. Those integration checks require an interactive desktop and are not part of this portable smoke test.

## Dock visibility regression

Run `powershell -ExecutionPolicy Bypass -File tests/Test-DockVisibility.ps1` after building. This creates temporary native test windows without injecting keyboard/mouse input and checks normal/maximized/borderless-fullscreen/restored windows, full-screen popups with normal owners, desktop/minimized/self exclusions, physical geometry, and cross-monitor foreground behavior when a second monitor is available. It briefly displays test windows; run on an interactive desktop.
