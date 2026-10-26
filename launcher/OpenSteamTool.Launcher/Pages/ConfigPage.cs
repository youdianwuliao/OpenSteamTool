namespace OpenSteamTool.Launcher;

internal sealed class ConfigPage : PageBase
{
    private readonly ComboBox _level;
    private readonly ComboBox _manifestUrl;
    private readonly NumericUpDown _timeoutResolve;
    private readonly NumericUpDown _timeoutConnect;
    private readonly NumericUpDown _timeoutSend;
    private readonly NumericUpDown _timeoutRecv;
    private readonly CheckBox _statsApi;
    private readonly TextBox _luaPaths;
    private readonly CheckBox _injectEnabled;
    private readonly TextBox _injectX64;
    private readonly TextBox _injectX86;
    private readonly CheckBox _cloudEnabled;
    private readonly TextBox _cloudLibrary;
    private readonly TextBox _remoteTemplate;
    private readonly Label _status;
    private readonly TextBox _ticketApi;
    private readonly CheckBox _autoPrepare;

    public ConfigPage()
    {
        var logCard = AddCard("日志", 84);
        var logTable = UiKit.Form();
        logCard.Body.Controls.Add(logTable);
        _level = Combo("trace", "debug", "info", "warn", "error");
        UiKit.Row(logTable, "级别", _level);

        var manifestCard = AddCard("Manifest", 214);
        var manifestTable = UiKit.Form();
        manifestCard.Body.Controls.Add(manifestTable);
        _manifestUrl = Combo("opensteamtool", "steamrun", "wudrm");
        _timeoutResolve = Number(5000);
        _timeoutConnect = Number(5000);
        _timeoutSend = Number(10000);
        _timeoutRecv = Number(10000);
        UiKit.Row(manifestTable, "上游 API", _manifestUrl);
        UiKit.Row(manifestTable, "解析超时", _timeoutResolve);
        UiKit.Row(manifestTable, "连接超时", _timeoutConnect);
        UiKit.Row(manifestTable, "发送超时", _timeoutSend);
        UiKit.Row(manifestTable, "接收超时", _timeoutRecv);

        var statsCard = AddCard("成就统计", 88);
        var statsTable = UiKit.Form();
        statsCard.Body.Controls.Add(statsTable);
        _statsApi = Check("未配置 setStat 时查询 stats API");
        UiKit.Row(statsTable, "启用", _statsApi);

        var luaCard = AddCard("Lua 目录", 150);
        var luaTable = UiKit.Form();
        luaCard.Body.Controls.Add(luaTable);
        _luaPaths = Theme.InputBox();
        _luaPaths.Multiline = true;
        _luaPaths.Height = 72;
        _luaPaths.MinimumSize = new Size(0, 72);
        _luaPaths.ScrollBars = ScrollBars.Vertical;
        _luaPaths.PlaceholderText = "每行一个目录；config/lua 始终最后加载";
        UiKit.Row(luaTable, "额外路径", _luaPaths);

        var injectCard = AddCard("进程注入", 170);
        var injectTable = UiKit.Form();
        injectCard.Body.Controls.Add(injectTable);
        _injectEnabled = Check("向游戏进程注入 DLL");
        _injectX64 = Theme.InputBox();
        _injectX86 = Theme.InputBox();
        _injectX64.PlaceholderText = "相对 Steam 目录或绝对路径";
        _injectX86.PlaceholderText = "相对 Steam 目录或绝对路径";
        UiKit.Row(injectTable, "启用", _injectEnabled);
        UiKit.Row(injectTable, "x64 库", _injectX64);
        UiKit.Row(injectTable, "x86 库", _injectX86);

        var cloudCard = AddCard("云存档重定向", 170);
        var cloudTable = UiKit.Form();
        cloudCard.Body.Controls.Add(cloudTable);
        _cloudEnabled = Check("加载 cloud_redirect.dll 接管云存档 RPC");
        _cloudLibrary = Theme.InputBox();
        _cloudLibrary.PlaceholderText = "默认 <Steam>/cloud_redirect.dll";
        UiKit.Row(cloudTable, "启用", _cloudEnabled);
        UiKit.Row(cloudTable, "库路径", _cloudLibrary);

        var remoteCard = AddCard("元数据镜像", 140);
        var remoteTable = UiKit.Form();
        remoteCard.Body.Controls.Add(remoteTable);
        _remoteTemplate = Theme.InputBox();
        _remoteTemplate.PlaceholderText = "https://host/{channel}/{component}/{sha256}.toml";
        UiKit.Row(remoteTable, "URL 模板", _remoteTemplate);

        var ticketCard = AddCard("启动器 / 票据", 156);
        var ticketTable = UiKit.Form();
        ticketCard.Body.Controls.Add(ticketTable);
        _ticketApi = Theme.InputBox();
        _ticketApi.PlaceholderText = "https://your.server/ticket/{appid}（留空则只用本机提取）";
        _autoPrepare = Check("交给 Steam 下载前自动准备 AppTicket / ETicket");
        UiKit.Row(ticketTable, "票据接口", _ticketApi);
        UiKit.Row(ticketTable, "自动准备", _autoPrepare);

        var saveCard = AddCard("保存", 104);
        var saveTable = UiKit.Form();
        saveCard.Body.Controls.Add(saveTable);
        _status = Theme.TextLabel("", 8.5f, Theme.Muted);
        var save = Theme.Primary("保存");
        save.Click += (_, _) => Save();
        var reload = Theme.Ghost("重新加载");
        reload.Click += (_, _) => RefreshData();
        var openFile = Theme.Ghost("打开目录");
        openFile.Click += (_, _) => OpenConfigFolder();
        UiKit.Row(saveTable, "状态", _status, save, reload, openFile);
    }

    private static ComboBox Combo(params string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = Theme.Font(9f),
            Dock = DockStyle.Fill
        };
        combo.Items.AddRange(items);
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    private static NumericUpDown Number(int value) => new()
    {
        Minimum = 0,
        Maximum = 600000,
        Increment = 500,
        Value = value,
        BackColor = Theme.Input,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Font(9f),
        Dock = DockStyle.Fill,
        ThousandsSeparator = false
    };

    private static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        BackColor = Color.Transparent,
        Font = Theme.Font(9f),
        Margin = new Padding(0, 8, 0, 8)
    };

    public override void RefreshData()
    {
        _ticketApi.Text = AppState.Settings.TicketApiTemplate ?? "";
        _autoPrepare.Checked = AppState.Settings.AutoPrepareTickets;

        var path = AppState.ConfigPath;
        if (path is null)
        {
            _status.Text = "请先在“概览”设置 Steam 目录";
            _status.ForeColor = Theme.Danger;
            return;
        }

        var toml = TomlConfig.Load(path);
        SelectCombo(_level, toml.GetString("log", "level") ?? "info");
        SelectCombo(_manifestUrl, toml.GetString("manifest", "url") ?? "opensteamtool");
        _timeoutResolve.Value = Clamp(toml.GetInt("manifest", "timeout_resolve_ms", 5000));
        _timeoutConnect.Value = Clamp(toml.GetInt("manifest", "timeout_connect_ms", 5000));
        _timeoutSend.Value = Clamp(toml.GetInt("manifest", "timeout_send_ms", 10000));
        _timeoutRecv.Value = Clamp(toml.GetInt("manifest", "timeout_recv_ms", 10000));
        _statsApi.Checked = toml.GetBool("stats", "enable_api", true);
        _luaPaths.Text = string.Join(Environment.NewLine, toml.GetStringArray("lua", "paths"));
        _injectEnabled.Checked = toml.GetBool("inject", "enabled", false);
        _injectX64.Text = toml.GetString("inject", "library_x64") ?? "";
        _injectX86.Text = toml.GetString("inject", "library_x86") ?? "";
        _cloudEnabled.Checked = toml.GetBool("cloud", "enabled", false);
        _cloudLibrary.Text = toml.GetString("cloud", "library") ?? "";
        _remoteTemplate.Text = toml.GetString("remote", "url_template") ?? "";

        _status.Text = toml.Exists ? "已加载 " + path : "配置文件不存在，保存后创建：" + path;
        _status.ForeColor = toml.Exists ? Theme.Muted : Theme.Accent;
    }

    private void Save()
    {
        var api = _ticketApi.Text.Trim();
        AppState.Settings.TicketApiTemplate = api.Length == 0 ? null : api;
        AppState.Settings.AutoPrepareTickets = _autoPrepare.Checked;
        AppState.Settings.Save();

        var path = AppState.ConfigPath;
        if (path is null)
        {
            _status.Text = "启动器设置已保存；opensteamtool.toml 需要先设置 Steam 目录";
            _status.ForeColor = Theme.Warn;
            return;
        }

        try
        {
            var toml = TomlConfig.Load(path);
            toml.SetString("log", "level", _level.SelectedItem?.ToString() ?? "info");
            toml.SetString("manifest", "url", _manifestUrl.SelectedItem?.ToString() ?? "opensteamtool");
            toml.SetInt("manifest", "timeout_resolve_ms", (int)_timeoutResolve.Value);
            toml.SetInt("manifest", "timeout_connect_ms", (int)_timeoutConnect.Value);
            toml.SetInt("manifest", "timeout_send_ms", (int)_timeoutSend.Value);
            toml.SetInt("manifest", "timeout_recv_ms", (int)_timeoutRecv.Value);
            toml.SetBool("stats", "enable_api", _statsApi.Checked);

            var paths = _luaPaths.Lines
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
            toml.SetStringArray("lua", "paths", paths);

            toml.SetBool("inject", "enabled", _injectEnabled.Checked);
            if (_injectX64.Text.Trim().Length > 0) toml.SetString("inject", "library_x64", _injectX64.Text.Trim());
            if (_injectX86.Text.Trim().Length > 0) toml.SetString("inject", "library_x86", _injectX86.Text.Trim());

            toml.SetBool("cloud", "enabled", _cloudEnabled.Checked);
            if (_cloudLibrary.Text.Trim().Length > 0) toml.SetString("cloud", "library", _cloudLibrary.Text.Trim());

            var remote = _remoteTemplate.Text.Trim();
            if (remote.Length > 0) toml.SetString("remote", "url_template", remote);

            toml.Save();
            _status.Text = "已保存，Steam 运行中会自动热重载";
            _status.ForeColor = Theme.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = "没有写入权限，请以管理员身份运行";
            _status.ForeColor = Theme.Danger;
        }
        catch (Exception ex)
        {
            _status.Text = "保存失败：" + ex.Message;
            _status.ForeColor = Theme.Danger;
        }
    }

    private void OpenConfigFolder()
    {
        var root = AppState.SteamRoot;
        if (root is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true });
        }
        catch
        {
            // 忽略
        }
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        int index = combo.Items.IndexOf(value);
        if (index < 0)
        {
            combo.Items.Add(value);
            index = combo.Items.Count - 1;
        }
        combo.SelectedIndex = index;
    }

    private static decimal Clamp(int value)
        => Math.Clamp(value, 0, 600000);
}
