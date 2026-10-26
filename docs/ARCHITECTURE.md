# OpenSteamTool 架构图

> Windows-only C++20 DLL，注入 Steam 进程，通过 hook `steamclient64.dll` / `steamui.dll` 伪造许可、清单、密钥与票据。

## 1. 分层结构

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Steam 进程 (steam.exe)                                                   │
│                                                                          │
│  ┌────────────────┐   ┌────────────────┐                                 │
│  │  dwmapi.dll    │   │  xinput1_4.dll │   加载劫持层（极薄）             │
│  │  src/dwmapi/   │   │ src/xinput1_4/ │                                 │
│  └───────┬────────┘   └───────┬────────┘                                 │
│          └──────────┬─────────┘                                          │
│                     ▼                                                    │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  OpenSteamTool.dll                                                 │  │
│  │                                                                    │  │
│  │  dllmain.cpp ──► detached 工作线程 (InitThread)                    │  │
│  │                                                                    │  │
│  │  ┌──────────────────────────────────────────────────────────────┐  │  │
│  │  │ Hook 层  src/Hook/                                           │  │  │
│  │  │  HookManager · HookMacros (Detours) · VehCommon (int3)        │  │  │
│  │  │  IPC │ Manifest │ Decryption │ Package │ NetPacket           │  │  │
│  │  │  CallBack │ SteamUI │ Misc │ KeyValues(未启用)               │  │  │
│  │  └──────────────────────────────────────────────────────────────┘  │  │
│  │  ┌─────────────────────┐  ┌───────────────────────────────────┐   │  │
│  │  │ Pipe 子系统         │  │ 功能服务层                        │   │  │
│  │  │ PipeManager         │  │ SteamMetadata (Pattern/IPC/Manifest│   │  │
│  │  │ ProcessInspector    │  │   /Stats/Diagnostics/RemoteToml)  │   │  │
│  │  │ DenuvoAuth          │  │ Config · LuaConfig · Watchers      │   │  │
│  │  │ Injection           │  │ Tickets · CloudRedirect · Logging  │   │  │
│  │  └─────────────────────┘  └───────────────────────────────────┘   │  │
│  │  ┌──────────────────────────────────────────────────────────────┐  │  │
│  │  │ OSTPlatform (静态库)   Memory Detour Http Process Hash PE    │  │  │
│  │  │ Trap Thread DynamicLibrary DirectoryWatch CredentialStore …  │  │  │
│  │  │ 公开头文件不暴露 Win32 类型；实现全在 Windows/*.cpp           │  │  │
│  │  └──────────────────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  steamclient64.dll            steamui.dll                                │
│  (被 hook 的主体)              (被 hook 的主体)                          │
└──────────────────────────────────────────────────────────────────────────┘

构建期依赖 / 代码生成
  tools/ipc_codegen  +  src/Steam/IPCMessages.steamd  →  IPCMessages.gen.h
  protoc             +  src/proto/steam_messages.proto →  steam_messages.pb.{h,cc}
  FetchContent 缓存  :  Lua 5.5 · protobuf 3.15.3 · spdlog · toml++ · Detours
```

## 2. 启动时序

```mermaid
flowchart TD
    A["DllMain / DLL_PROCESS_ATTACH"] --> B["Thread::StartDetached<br/>避免 loader lock"]
    B --> C["加载 steamclient64.dll / steamui.dll<br/>推导 Steam 根目录等路径"]
    C --> D["Config::Load(opensteamtool.toml)"]
    D --> E["Log::InitModules · SteamDiagnostics::Initialize"]
    E --> F["PatternLoader::Load(steamui) / (steamclient)"]
    F --> G["IPCLoader::Load(steamclient)"]
    G --> H["LuaConfig::ParseDirectory<br/>[lua].paths + config/lua"]
    H --> I["LuaFileWatcher::Start · ConfigFileWatcher::Start"]
    I --> J["SteamUI::CoreHook()"]
    J --> K["SteamClient::CoreHook()"]
    K --> L["PatternLoader::ReportMissingFunctions"]
    L --> M["CloudRedirectHost::Initialize<br/>仅 [cloud].enabled"]
```

## 3. 版本适配：元数据驱动

```mermaid
flowchart LR
    subgraph 磁盘
        DLL["steamclient64.dll / steamui.dll"]
    end
    DLL --> SHA["SHA-256"]
    SHA --> LOOK["RemoteToml::Fetch"]
    LOOK --> M1["自定义 [remote].url_template"]
    M1 -->|未配置/不可用| M2["GitHub raw"]
    M2 -->|不可达| M3["jsDelivr"]
    M3 -->|不可达| M4["本地缓存<br/>&lt;Steam&gt;/opensteamtool/..."]
    M2 -->|404| STOP["停止镜像链<br/>该模块 hook 全部跳过 + 弹窗一次"]
    M4 --> OK["解析 TOML"]
    M1 --> OK
    OK --> P["pattern 通道<br/>函数名 → RVA / 字节签名"]
    OK --> Q["ipc 通道<br/>vtable RVA + funcHash / fencepost / argc"]
    P --> PL["PatternLoader::FindPattern"]
    Q --> IL["IPCLoader::Find"]
```

## 4. Hook 装配

```mermaid
flowchart TD
    HM["HookManager.cpp<br/>SteamUI::CoreHook / SteamClient::CoreHook"] --> MAC["HookMacros.h"]
    MAC --> F1["HOOK_FUNC 声明 Name_t / oName / hkName"]
    MAC --> F2["INSTALL_HOOK_C|U → Detours::Attach"]
    MAC --> F3["RESOLVE_C|U → 仅取地址"]
    MAC --> F4["HOOK_BEGIN/END 事务"]
    W["需要序言前读参数"] --> VEH["VehCommon<br/>int3 + VEH 分发<br/>GetArg 读 RCX/RDX/R8/R9/栈"]
    VEH --> T["CAPTURE_THIS_FUNC / 单点 trap"]
```

## 5. 解锁数据流（addappid 之后发生了什么）

```mermaid
sequenceDiagram
    participant L as Lua 配置
    participant P as Hooks_Package
    participant S as Steam (steamclient64)
    participant M as Hooks_Manifest
    participant D as Hooks_Decryption
    participant I as Hooks_IPC

    L->>P: addappid(id, depotid, key) 写入内存表
    P->>S: 取 Package 0 (CPackageInfo)
    P->>P: 扩容 AppIdVec 并注入所有 depot id
    P->>S: MarkLicenseAsChanged + ProcessPendingLicenseUpdates
    S->>P: CheckAppOwnership(appId)
    P-->>S: 改写成 PackageId=0, bOwnsLicense=true
    S->>M: BuildDepotDependency(...)
    M-->>S: 覆写 ManifestGid / ManifestSize (setManifestid)
    S->>D: ConfigStore::GetBinary("...\<depot>\DecryptionKey")
    D-->>S: 返回 Lua 提供的 32 字节密钥
    S->>I: IPC InterfaceCall(IClientUser::GetSteamID)
    I-->>S: 返回伪造 SteamID
    S->>I: IPC InterfaceCall(GetAppOwnershipTicketExtendedData)
    I-->>S: 返回凭据库真票或伪造票
```

## 6. IPC 消息层

```mermaid
flowchart LR
    IDL["src/Steam/IPCMessages.steamd<br/>唯一事实来源"] --> CG["tools/ipc_codegen"]
    CG --> GEN["build/generated/&lt;Config&gt;/IPCMessages.gen.h<br/>Req / Resp 门面"]
    PIPE["游戏进程 pipe 消息"] --> H1["Hooks_IPC::Install<br/>拦截 pipe 消息"]
    H1 --> ENV["解析信封<br/>IPCRequest → IPCInterfaceCall"]
    ENV --> FIND["按 (interfaceID, funcHash) 查表"]
    GEN --> FIND
    MET["IPCLoader<br/>ipc 通道元数据"] --> FIND
    FIND --> PRE["HandlerPre_&lt;Iface&gt;_&lt;Method&gt;"]
    FIND --> POST["HandlerPost_&lt;Iface&gt;_&lt;Method&gt;"]
    PRE --> OUT["写回 CUtlBuffer"]
    POST --> OUT
    HS["EIPCCommand::Handshake"] --> PM["PipeManager::OnHandshake"]
    PM --> DN["DenuvoAuth::Apply"]
    PM --> INJ["Injection"]
```

## 7. 目标与依赖关系

```mermaid
flowchart TD
    subgraph 产物
        D1["dwmapi.dll"]
        D2["xinput1_4.dll"]
        D3["OpenSteamTool.dll"]
        T1["ipc_codegen (host)"]
        T2["extract_tickets (host)"]
    end
    D1 --> D3
    D2 --> D3
    D3 --> OS["OSTPlatform (static)"]
    D3 --> LU["lua_static"]
    D3 --> PB["Debug: libprotobuf<br/>Release: libprotobuf-lite"]
    D3 --> TP["tomlplusplus"]
    D3 --> SP["Debug: spdlog"]
    OS --> DT["detours"]
    OS --> WC["winhttp · Bcrypt"]
    T1 --> GEN["IPCMessages.gen.h"]
    GEN --> D3
    T2 -.->|"导出 AppTicket / ETicket hex"| D3
```
