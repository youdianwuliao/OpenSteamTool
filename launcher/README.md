# OpenSteamTool Launcher

可选的 Windows 图形界面，用来把 OpenSteamTool 装进 Steam、管理 Lua 配置、按 AppId 触发安装，并调用
`extract_tickets` 导出 Denuvo / SteamStub 需要的票据。

它**不是**主 DLL 的一部分，也不参与 CMake 构建：主项目仍然是 `src/` 下的 C++20 工程。

## 构建

前置条件：

1. 先按仓库根目录的 `build.bat` 构建出 `build/Release/*.dll`。
2. 需要 .NET 8 SDK（仅构建期需要，产物可 self-contained）。

```powershell
launcher\build-launcher.bat
# 输出：build\launcher\OpenSteamTool.Launcher.exe（单文件、免安装运行时）
```

也可以直接：

```powershell
dotnet run --project launcher\OpenSteamTool.Launcher
```

## 功能

| 页面 | 内容 |
|------|------|
| 概览 | 自动检测 Steam 目录（注册表）、选择组件目录、备份并覆盖 3 个 DLL、卸载还原、优雅退出/启动 Steam、元数据缓存状态 |
| 游戏库 | AppId / depot key / token / setStat / AppTicket / ETicket 的增删改，`setManifestid` 固定；行内「下载」= 准备票据 → 写 lua → 交给 Steam 下载；另有「批量准备票据」 |
| 配置 | `opensteamtool.toml` 的可视化编辑（日志级别、manifest 上游、超时、stats API、Lua 路径、注入、云存档、镜像模板） |
| 日志 | 读取 `<Steam>/opensteamtool/*.log`，支持自动刷新（仅 Debug 构建有日志） |

## 验证票据通路（建议先做）

内置提取要真正取到票，必须有 Windows + 运行中的 Steam + 一个**账号确实拥有**的 AppId。
先跑探针确认链路，再回界面点「下载」：

```powershell
# 方式一：已发布（免 SDK）
build\launcher\ost-ticket-probe.exe 1361510
build\launcher\ost-ticket-probe.exe 1361510 "C:\Program Files (x86)\Steam"

# 方式二：源码直接跑（需要 .NET 8 SDK）
launcher\probe-tickets.bat 1361510
```

正常输出形如：

```
==================================================================
 Steam 目录 : C:\Program Files (x86)\Steam
 Steam 运行 : True
 进程位数   : x64
 AppId      : 1361510
==================================================================
  · 加载 C:\Program Files (x86)\Steam\steamclient64.dll
  · steamclient64.dll 句柄 0x7FF...
  · CreateInterface(SteamClient023) → 0x...
  · CreateSteamPipe → 1
  · ConnectToGlobalUser → 1
  · STEAMAPPTICKET_INTERFACE_VERSION001 → 0x...
  · AppTicket 184 字节
  · ISteamUser 0x... · ISteamUtils 0x...
  · RequestEncryptedAppTicket → hCall=0x...，等待回调…
  · ETicket 143 字节
  · 完成 AppTicket 184 字节 / ETicket 143 字节
------------------------------------------------------------------
 结果     : 成功 — 内置提取成功：AppTicket 184 字节 / ETicket 143 字节
```

失败对照：

| 输出 | 含义 / 处理 |
|------|-------------|
| `LoadLibraryEx 失败（Win32 126）` | 缺 `tier0_s64.dll` / `vstdlib_s64.dll`，校验 Steam 目录完整性 |
| `CreateInterface(SteamClient023) 返回空（code=-1）` | Steam 改了接口版本，需要更新工具 |
| `CreateSteamPipe 返回 0` | Steam 没在运行 |
| `ConnectToGlobalUser 返回 0` | Steam 未登录 |
| `GetAppOwnershipTicketData 返回 0 字节` | 当前账号不拥有该 AppId（换拥有它的账号） |
| `等待 EncryptedAppTicketResponse 超时` | 客户端繁忙/网络问题，重启 Steam 重试 |
| `EncryptedAppTicketResponse 结果码 N` | 服务端拒绝，N 为 EResult |

探针与界面走同一个 `SteamTicketReader.Read`，探针通过即界面可用。

## 票据与下载

启动器只负责**把票据落到本地**，游戏本体始终由 Steam 客户端下载（界面只发 `steam://install/<appid>`）。

票据获取顺序：

1. **远程票据接口**（可选，在「配置 → 启动器 / 票据」里填模板，支持 `{appid}`）。
   响应可以是 JSON（`appticket` / `eticket`，大小写不敏感），也可以是 `appticket=...` 行文本。
2. **内置提取**（默认，免安装）：启动器直接把 `steamclient64.dll` 载入自己的进程，
   走 `CreateInterface(SteamClient023) → CreateSteamPipe → ConnectToGlobalUser →
   ISteamAppTicket / ISteamUser + ISteamUtils` 取票，**不需要构建或运行任何命令行工具**。
   要求 Steam 正在运行、已登录，且当前账号真正拥有该 AppId。
3. **命令行回退**：仅当内置提取失败且能找到 `extract_tickets.exe` 时才使用。

拿到的 AppTicket / ETicket 写入 `<Steam>/config/lua/opensteamtool_gui.lua` 的 `setAppTicket` / `setETicket`；
DLL 下次解析该文件时会把它们写进本机凭据库（`HKCU\Software\Valve\Steam\Apps\<AppId>`）。
Steam 正在运行时是配置热重载，无需重启。

> Denuvo 票据有约 30 分钟有效期，过期需重新准备。

## 行为约定

- **安装**：先把 Steam 根目录下已存在的同名 DLL 备份到 `<Steam>/opensteamtool/backup/<时间戳>/`，
  再复制并做 SHA-256 校验。Steam 正在运行时默认拒绝写入，可勾选“安装前自动关闭 Steam”。
- **卸载**：从最近一次备份还原；没有备份则删除三个文件。
- **Lua 写入**：只写 `<Steam>/config/lua/opensteamtool_gui.lua`。
  其它 `.lua` 文件只读解析（列表里会显示来源文件名），绝不修改。
- **TOML 写入**：行级修改，保留已有注释与未知键；文件不存在时会在保存时才创建。
- **提权**：默认 asInvoker。Steam 装在 `Program Files` 时请以管理员身份运行。

## 安全提醒

- `[inject]` 与 `[cloud]` 会把配置里的任意 DLL 加载进游戏 / Steam 进程；不使用时保持关闭。
- `[remote]` 建议只使用 https 模板，下载的元数据没有签名校验。
- 启动器本身不联网，唯一的外部动作是“检查更新”按钮打开 GitHub Releases 页面。
