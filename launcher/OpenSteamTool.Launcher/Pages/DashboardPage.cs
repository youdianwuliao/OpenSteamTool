using System.Diagnostics;

namespace OpenSteamTool.Launcher;

internal sealed class DashboardPage : PageBase
{
    private readonly TextBox _steamPath;
    private readonly TextBox _sourcePath;
    private readonly Label _stateLabel;
    private readonly Label _metaLabel;
    private readonly Label _toolLabel;
    private readonly TextBox _log;
    private readonly CheckBox _autoClose;

    public DashboardPage()
    {
        var steamCard = AddCard("Steam 目录", 132);
        var steamTable = UiKit.Form();
        steamCard.Body.Controls.Add(steamTable);

        _steamPath = Theme.InputBox(AppState.SteamRoot ?? "");
        var detect = Theme.Ghost("自动检测");
        detect.Click += (_, _) => DetectSteam();
        var browse = Theme.Ghost("浏览");
        browse.Click += (_, _) => BrowseInto(_steamPath, "选择 Steam 根目录");
        UiKit.Row(steamTable, "安装路径", _steamPath, detect, browse);

        _stateLabel = Theme.TextLabel("未检测", 9f, Theme.Muted);
        UiKit.Row(steamTable, "状态", _stateLabel);

        var sourceCard = AddCard("组件来源", 128);
        var sourceTable = UiKit.Form();
        sourceCard.Body.Controls.Add(sourceTable);

        _sourcePath = Theme.InputBox(AppState.SourceDir ?? "");
        var sourceBrowse = Theme.Ghost("浏览");
        sourceBrowse.Click += (_, _) => BrowseInto(_sourcePath, "选择包含 OpenSteamTool.dll 的目录");
        UiKit.Row(sourceTable, "DLL 目录", _sourcePath, sourceBrowse);

        _toolLabel = Theme.TextLabel("", 8.5f, Theme.Muted);
        UiKit.Row(sourceTable, "配套工具", _toolLabel);

        var actionCard = AddCard("操作", 118);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent
        };

        var install = Theme.Primary("安装 / 更新");
        install.Click += (_, _) => Install();

        var uninstall = Theme.DangerBtn("卸载并还原");
        uninstall.Click += (_, _) => Uninstall();

        var launch = Theme.Ghost("启动 Steam");
        launch.Click += (_, _) => LaunchSteam();

        var shutdown = Theme.Ghost("退出 Steam");
        shutdown.Click += (_, _) => ShutdownSteam();

        _autoClose = new CheckBox
        {
            Text = "安装前自动关闭 Steam",
            Checked = AppState.Settings.AutoCloseSteam,
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = Theme.Font(9f),
            Margin = new Padding(8, 10, 0, 0)
        };
        _autoClose.CheckedChanged += (_, _) =>
        {
            AppState.Settings.AutoCloseSteam = _autoClose.Checked;
            AppState.Settings.Save();
        };

        actions.Controls.Add(install);
        actions.Controls.Add(uninstall);
        actions.Controls.Add(launch);
        actions.Controls.Add(shutdown);
        actions.Controls.Add(_autoClose);
        actionCard.Body.Controls.Add(actions);

        var metaCard = AddCard("元数据缓存", 108);
        var metaTable = UiKit.Form();
        metaCard.Body.Controls.Add(metaTable);
        _metaLabel = Theme.TextLabel("", 8.5f, Theme.Muted);
        var openCache = Theme.Ghost("打开目录");
        openCache.Click += (_, _) => OpenFolder(Path.Combine(AppState.SteamRoot ?? "", "opensteamtool"));
        var releases = Theme.Ghost("检查更新");
        releases.Click += (_, _) => OpenUrl("https://github.com/OpenSteam001/OpenSteamTool/releases");
        UiKit.Row(metaTable, "缓存", _metaLabel, openCache, releases);

        var logCard = AddCard("输出", 176);
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        logCard.Body.Controls.Add(_log);

        Append("就绪。先确认 Steam 目录与组件来源，再点击“安装 / 更新”。");
    }

    public override void RefreshData()
    {
        if (string.IsNullOrWhiteSpace(_steamPath.Text) && AppState.SteamRoot is not null)
            _steamPath.Text = AppState.SteamRoot;
        if (string.IsNullOrWhiteSpace(_sourcePath.Text))
        {
            var probed = AppState.ProbeSourceDir();
            if (probed is not null) _sourcePath.Text = probed;
        }

        var steamRoot = CurrentSteamRoot();
        bool installed = Installation.IsInstalled(steamRoot);
        var backup = steamRoot is null ? null : Installation.LatestBackup(steamRoot);

        _stateLabel.Text = steamRoot is null
            ? "未设置 Steam 目录"
            : (SteamLocator.IsSteamRunning() ? "Steam 运行中" : "Steam 未运行")
              + "　·　组件" + (installed ? "已安装" : "未安装")
              + "　·　备份" + (backup is null ? "无" : Path.GetFileName(backup));
        _stateLabel.ForeColor = steamRoot is null ? Theme.Danger : (installed ? Theme.Ok : Theme.Muted);

        _metaLabel.Text = Installation.DescribeMetadata(steamRoot);

        var tool = TicketTool.Find(CurrentSourceDir());
        _toolLabel.Text = tool is null
            ? "extract_tickets.exe 未找到（构建 tools 目标后可提取票据）"
            : tool;
    }

    private void DetectSteam()
    {
        var detected = SteamLocator.Detect();
        if (detected is null)
        {
            Append("注册表里没有找到 Steam，请手动选择安装目录。");
            return;
        }
        _steamPath.Text = detected;
        Persist();
        Append("已检测到 Steam：" + detected);
    }

    private void Install()
    {
        Persist();
        var result = Installation.Install(CurrentSteamRoot(), CurrentSourceDir(), Append, _autoClose.Checked);
        Append((result.Ok ? "[成功] " : "[失败] ") + result.Message);
        RefreshData();
    }

    private void Uninstall()
    {
        Persist();
        var confirm = MessageBox.Show(this, "将删除/还原 Steam 根目录下的三个 DLL，继续？", "确认卸载",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        var result = Installation.Uninstall(CurrentSteamRoot(), Append, _autoClose.Checked);
        Append((result.Ok ? "[成功] " : "[失败] ") + result.Message);
        RefreshData();
    }

    private void LaunchSteam()
    {
        var root = CurrentSteamRoot();
        if (root is null) { Append("请先设置 Steam 目录。"); return; }
        Append(SteamLocator.Launch(root) ? "已请求启动 Steam。" : "启动失败，请检查路径。");
    }

    private void ShutdownSteam()
    {
        var root = CurrentSteamRoot();
        if (root is null) { Append("请先设置 Steam 目录。"); return; }
        Append(SteamLocator.Shutdown(root, TimeSpan.FromSeconds(25)) ? "Steam 已退出。" : "Steam 仍在运行。");
        RefreshData();
    }

    private void Persist()
    {
        var steamRoot = _steamPath.Text.Trim();
        var sourceDir = _sourcePath.Text.Trim();
        try
        {
            AppState.Settings.SteamRoot = steamRoot.Length == 0 ? null : SteamLocator.Normalize(steamRoot);
            AppState.Settings.SourceDir = sourceDir.Length == 0 ? null : Path.GetFullPath(sourceDir);
            AppState.Settings.Save();
        }
        catch (Exception ex)
        {
            Append("路径无效：" + ex.Message);
        }
        RefreshData();
    }

    private string? CurrentSteamRoot()
    {
        var text = _steamPath.Text.Trim();
        if (text.Length == 0) return AppState.SteamRoot;
        try
        {
            return SteamLocator.Normalize(text);
        }
        catch
        {
            return null;
        }
    }

    private string? CurrentSourceDir()
    {
        var text = _sourcePath.Text.Trim();
        if (text.Length == 0) return AppState.SourceDir;
        try
        {
            return Path.GetFullPath(text);
        }
        catch
        {
            return null;
        }
    }

    private void BrowseInto(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
            Persist();
        }
    }

    private void Append(string message)
    {
        if (_log.IsDisposed) return;
        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(new Action<string>(Append), message);
            return;
        }
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) { Append("目录不存在：" + path); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Append("打开目录失败：" + ex.Message);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Append("打开链接失败：" + ex.Message);
        }
    }
}
