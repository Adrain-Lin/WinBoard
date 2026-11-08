# Capture smoke test

Build the app first, then run `powershell -ExecutionPolicy Bypass -File tests/Test-Capture.ps1` on an interactive Windows x64 desktop with the recording prerequisites installed.

This opens and records a dedicated test-pattern window for roughly nine seconds, validates capture settings and reversed/clamped selection math, writes a PNG and H.264 MP4, pauses, resumes and stops. Microphone and system audio capture are disabled. Temporary test artifacts are retained under the path printed by the script; no project configuration is used.

The UI integration was also checked locally for capture/power button visibility, audio options, countdown cancellation, native region pixel alignment, loopback AAC audio output, installer dependencies and data preservation. Those integration checks require an interactive desktop and are not part of this portable smoke test.
