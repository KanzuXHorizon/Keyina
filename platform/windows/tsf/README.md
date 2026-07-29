# Keyina TSF adapter

This directory contains the Windows Text Services Framework adapter.

## Current verified scope

- Builds as an x64 COM in-process DLL with MSVC and Windows SDK 10.0.26100.
- Exports `DllCanUnloadNow`, `DllGetClassObject`, `DllRegisterServer`, and `DllUnregisterServer`.
- Creates `ITfTextInputProcessorEx` and installs an `ITfKeyEventSink` outside secure mode.
- Does not install the key sink when activated with `TF_TMAE_SECUREMODE`.
- Converts code-point edits to validated UTF-16 edits, including surrogate pairs.
- Class factory, object lifetime, exports, and DLL unload behavior are covered by an integration smoke test.
- Registration uses the current `ITfInputProcessorProfileMgr` profile API and the keyboard TIP category.

## Deliberate safety boundary

The key-event methods currently pass through every key. They do not claim functional Vietnamese input until the edit-session/composition slice is implemented and tested. This prevents an incomplete developer DLL from swallowing input.

## Developer registration

Global TSF profile registration requires an elevated PowerShell session. The scripts do not select Keyina as the default keyboard.

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
.\scripts\windows\register-dev.ps1
# Test only on a disposable developer profile.
.\scripts\windows\unregister-dev.ps1
```

Build, unit tests, UTF-16 translation tests, and DLL smoke tests do not require elevation. Registration was intentionally not forced through a UAC prompt by automation.
