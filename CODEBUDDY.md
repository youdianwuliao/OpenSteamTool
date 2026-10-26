# CODEBUDDY.md

This file provides guidance to CodeBuddy Code when working with code in this repository.

## What this is

OpenSteamTool is a **Windows-only** C++20 DLL suite that is dropped into the Steam root directory and hooks `steamclient64.dll` / `steamui.dll` in-process. Three shipped artifacts:

- `OpenSteamTool.dll` — the real payload (the hook DLL).
- `dwmapi.dll`, `xinput1_4.dll` — tiny loader-hijack DLLs that cause the payload to be loaded by Steam. Their sources are `src/dwmapi/` and `src/xinput1_4/`.

The top-level CMake returns early on non-Windows (`if(NOT WIN32) return()`), so nothing builds on Linux/macOS.

## Build

```powershell
build.bat                     # configure + build Release and Debug, plus extract_tickets
```

Equivalent manual flow (what CI uses):

```powershell
cmake -S src -B build -G "Ninja Multi-Config"        # or "Visual Studio 17 2022" -A x64
cmake --build build --config Release                 # or Debug
```

- Configure from `src`, not the repo root.
- Release version string: `-DOPENSTEAMTOOL_VERSION=v1.2.3` (embedded via `src/cmake/OpenSteamToolBuildInfo.h.in`).
- Host tools (`ipc_codegen`, `extract_tickets`) are `EXCLUDE_FROM_ALL`; build them explicitly:
  `cmake --build build --config Release --target ipc_codegen extract_tickets` → `build/tools/<Config>/`.
- Artifacts land in `build/<Config>/` (Debug `OpenSteamTool.dll`, `dwmapi.dll`, `xinput1_4.dll`).

**There is no test suite and no linter.** CI (`.github/workflows/ci.yml`) only compiles both configurations on `windows-latest`; passing CI means "it compiles". Verify behavior manually in a live Steam install.

Third-party deps (Lua 5.5, protobuf 3.15.3, spdlog, toml++, Detours) come via FetchContent and are cached as source trees in `<repo>/.deps/<name>-src` (override with `-DOST_DEPS_DIR=...`). First configure needs network; later configures reuse the cache even after `rm -rf build`.

## Optional launcher (`launcher/`, C# WinForms / .NET 8)

`launcher/OpenSteamTool.Launcher` is a standalone GUI that deploys the three DLLs into the Steam root (with timestamped backups + SHA-256 verification), edits `opensteamtool.toml` line-wise, manages Lua entries, obtains tickets (optional remote ticket API, then an in-process `steamclient64.dll` vtable reader, finally the `extract_tickets` CLI), and hands the download to Steam via `steam://install/<appid>` — the launcher never downloads game content itself. It is **not** part of the CMake build, has no CI job, and ships nothing into `build/<Config>/` — publish it with `launcher/build-launcher.bat` (`dotnet publish -r win-x64 --self-contained`).

- Only writes `<Steam>/config/lua/opensteamtool_gui.lua`; other `.lua` files are parsed read-only.
- `Services/` holds all file/registry/process logic; `Pages/` + `MainForm.cs` are pure UI. Keep platform work in `Services/`.
- No test suite here either; changes are verified by running the exe against a real Steam install.

## Big-picture architecture

### Runtime boot sequence (`src/dllmain.cpp:59`)

`DllMain` does nothing but spawn a detached worker thread — all file I/O, module loading, pattern downloads and detour transactions must stay off the loader lock. The init thread, in order:

1. Load `steamclient64.dll` and `steamui.dll`, derive all runtime paths (Steam root, `config/lua`, `opensteamtool.toml`).
2. `Config::Load` (TOML) → `Log::InitModules` → `SteamDiagnostics::Initialize`.
3. `PatternLoader::Load` per module, `IPCLoader::Load` — these fetch per-build TOML metadata over HTTPS.
4. `LuaConfig::ParseDirectory` for configured dirs, then start `LuaFileWatcher` + `ConfigFileWatcher` (hot reload).
5. `SteamUI::CoreHook()`, `SteamClient::CoreHook()`, `PatternLoader::ReportMissingFunctions()`.
6. `CloudRedirectHost::Initialize` (no-op unless `[cloud].enabled`).

### Two layers of version-compat metadata

Nothing is hardcoded as byte signatures in the DLL. Each launch hashes the DLL on disk (SHA-256) and downloads a matching TOML:

- **pattern** channel → function name → RVA or byte signature, consumed by `PatternLoader::FindPattern(module, "FuncName")`.
- **ipc** channel → per-interface vtable RVAs, and per-method `funcHash` / `fencepost` / `argc`, consumed by `IPCLoader`.

Lookup order (implemented in `Utils/SteamMetadata/RemoteToml.cpp`): custom `[remote] url_template` → GitHub raw → jsDelivr → local cache at `<Steam>/opensteamtool/<channel>/<component>/<sha256>.toml`. A 404 stops the mirror chain (the bot hasn't published that Steam build yet); hooks tied to that module are then skipped and a one-shot popup is shown while everything else keeps running.

### Hooking model (`src/Hook/`)

`HookManager.cpp` lists every hook module. `HookMacros.h` eliminates boilerplate:

- `HOOK_FUNC(Name, ret, args...)` declares `Name_t`, the `oName` trampoline, and `hkName`.
- `INSTALL_HOOK_C(Name)` / `INSTALL_HOOK_U(Name)` resolve via `PatternLoader` and attach a Detours hook; `RESOLVE_C/RESOLVE_U` only captures the address (no detour).
- All installs run inside `HOOK_BEGIN()`/`HOOK_END()` detour transactions; uninstalls inside `UNHOOK_*`.
- `_C` suffix = `client_hModule` (steamclient), `_U` = `ui_hModule` (steamui).

Modules, roughly by responsibility: `Hooks_IPC` (+ `Hooks_IPC_ISteamUser`, `Hooks_IPC_ISteamUtils` handler tables), `Hooks_Manifest`, `Hooks_Decryption`, `Hooks_Package`, `Hooks_NetPacket`, `Hooks_CallBack`, `Hooks_SteamUI`, `Hooks_Misc`, `Hooks_KeyValues` (currently not installed).

`Utils/HookSupport/VehCommon` provides a shared VEH dispatcher for **int3 prologue traps** — used where a Detours detour is unsuitable because arguments must be read before the prologue clobbers them (`VehCommon::GetArg` reads RCX/RDX/R8/R9 or stack slots). Trap sites are armed/disarmed per site; `SteamClient::CoreUnhook` calls `DisarmAll`/`RemoveHandler`.

### IPC message layer (the main feature surface)

`src/Steam/IPCMessages.steamd` is the **source of truth** for IPC enums, transport envelopes (`IPCRequestHeader`, `IPCInterfaceCallHeader`, `IPCResponseHeader`, the `protocol IPC` commands) and per-method request/response bodies. It is compiled by the host tool `tools/ipc_codegen` (driven by `tools/cmake/IPCCodegen.cmake`) into `build/generated/<Config>/IPCMessages.gen.h`, which emits Req/Resp facades that reproduce Steam's exact packed wire layout.

Flow: `Hooks_IPC` traps a pipe message, parses the envelope, looks up a handler by `(interfaceID, funcHash)` (hashes come from the ipc-channel metadata, not the IDL), then invokes a pre/post handler. Handlers are registered with:

```cpp
ADD_IPC_PRE_HANDLER(IClientUser, GetSteamID)
ADD_IPC_POST_HANDLER(IClientUtils, GetAPICallResult)
ADD_IPC_BOTH_HANDLER(...)
```

Adding an IPC feature touches three places: declare the method in `IPCMessages.steamd`, implement `HandlerPre_/HandlerPost_<Iface>_<Method>` in the matching `Hooks_IPC_*.cpp`, and add the entry to its `Register()` table.

Protobuf payloads (`src/proto/steam_messages.proto`) are generated per config: **Debug → full `libprotobuf`** (so `DebugString()` works), **Release → `libprotobuf-lite`**. Generated sources always go to a per-config directory — never a shared path — so switching configs doesn't ping-pong the build graph.

### Pipe subsystem (`src/Pipe/`)

`PipeManager::OnHandshake` (called from `Hooks_IPC`) resolves the calling process once (PID + creation time key, env-based AppId from `SteamAppId`/`SteamGameId`/`SteamOverlayGameId`) and hands a `PipeContext` to each feature:

- `PipeManager::DenuvoAuth` — protection scan + authorization state machine; `IsAuthorizedPipe` gates ticket/SteamID spoofing in the IPC handlers.
- `PipeManager::Injection` — optional game-process library injection from `[inject]`.

`ProcessInspector` owns the generic process/env inspection; `PipeTypes.h` holds the shared key/context structs. Keep feature state inside the feature module.

### Config and Lua

- TOML config: `<Steam>/opensteamtool.toml`, loaded once and hot-reloaded by `ConfigFileWatcher`; defaults apply when absent (never auto-created). `opensteamtool.example.toml` documents every key — keep it in sync when adding config.
- Lua dirs: `<Steam>/config/lua` (always loaded last so user files win) plus `[lua] paths`. `LuaFileWatcher` reloads on add/modify/delete.
- Bindings are registered through `register_func` in `Utils/Config/LuaConfig.cpp`, which also installs a `__index` metamethod on `_G` making **every** function callable in any case (`addAppId`, `SETManifestid`, ...). Current bindings: `addappid`, `addtoken`, `setmanifestid`, `setappticket`, `seteticket`, `setstat`, `http_get`, `http_post`; scripts may also define `fetch_manifest_code(gid)` / `fetch_manifest_code_ex(app_id, depot_id, gid)`, which override the configured manifest API.
- `pinApp` is intentionally disabled.

### Platform abstraction (`src/OSTPlatform/`)

`OSTPlatform` is a static lib exposing OS-agnostic headers in `include/` (`Memory`, `Detour`, `Http`, `Process`, `RemoteProcess`, `Hash`, `ByteSearch`, `PE`, `Trap`, `SteamCredentialStore`, `Thread`, `DynamicLibrary`, `DirectoryWatch`, ...). **Public headers must not leak Win32/WinHTTP/registry/BCrypt types** — those stay in `Windows/*.cpp`. Only Windows backends exist today (`src/OSTPlatform/Linux/` is an empty placeholder, and the credential store is registry-backed on Windows only).

Unit-testable logic belongs in `Encoding.cpp` / `Numbers.cpp` style platform-neutral code; everything else rides on these interfaces so callers stay portable.

### Logging

`Utils/Logging/LogModules.def` is the single registry. Adding `OST_MOD(Name, "name")` generates `LOG_NAME_TRACE..ERROR` macros through `cmake/LogMacros.cmake` at configure time (no need to touch `Log.h`). `LOG_*` is compiled in **only for Debug** (`OPENSTEAMTOOL_LOGGING_ENABLED`); in Release every macro is `((void)0)`. Debug logs are per-module files under `<Steam>/opensteamtool/`.

## Conventions worth knowing

- `src/dllmain.h` holds the global module handles (`client_hModule`, `ui_hModule`) and fixed-size runtime paths; hook macros resolve `_C`/`_U` from those.
- The whole tree is MSVC + static CRT (`MultiThreaded$<$<CONFIG:Debug>:Debug>`) and `BUILD_SHARED_LIBS OFF`, forced before any FetchContent so deps inherit it.
- Configurations are restricted to exactly `Debug;Release`.
- Prefer the existing macro/registry mechanisms (hook macros, `LogModules.def`, `IPCMessages.steamd`, `add_subdirectory` tool targets) over ad-hoc registration — several of them generate code at configure time and only re-run when CMake is reconfigured.
