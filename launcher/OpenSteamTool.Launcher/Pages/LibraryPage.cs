using System.Diagnostics;

namespace OpenSteamTool.Launcher;

internal sealed class LibraryPage : UserControl, IRefreshable
{
    private readonly DataGridView _gamesGrid;
    private readonly DataGridView _pinsGrid;
    private readonly TextBox _search;
    private readonly Label _summary;

    private LuaSnapshot _snapshot = new();
    private List<GameEntry> _games = new();
    private List<ManifestPin> _pins = new();

    public LibraryPage()
    {
        BackColor = Theme.Bg;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 8)
        };

        var addGame = Theme.Primary("添加游戏");
        addGame.Click += (_, _) => AddGame();

        var addPin = Theme.Ghost("添加 Manifest");
        addPin.Click += (_, _) => AddPin();

        var tickets = Theme.Ghost("提取票据");
        tickets.Click += (_, _) => ExtractSelectedTicket();

        var prepare = Theme.Ghost("批量准备票据");
        prepare.Click += async (_, _) => await PrepareTicketsAsync();

        var refresh = Theme.Ghost("刷新");
        refresh.Click += (_, _) => RefreshData();

        var openDir = Theme.Ghost("打开 Lua 目录");
        openDir.Click += (_, _) => OpenLuaDir();

        _search = Theme.InputBox();
        _search.Dock = DockStyle.None;
        _search.Size = new Size(220, 26);
        _search.Margin = new Padding(12, 0, 0, 0);
        _search.PlaceholderText = "搜索 AppId / DepotId";
        _search.TextChanged += (_, _) => ApplyFilter();

        toolbar.Controls.Add(addGame);
        toolbar.Controls.Add(addPin);
        toolbar.Controls.Add(tickets);
        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(openDir);
        toolbar.Controls.Add(_search);

        var gridHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg };
        gridHost.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        gridHost.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        _gamesGrid = UiKit.Grid();
        BuildGameColumns();
        _gamesGrid.CellContentClick += OnGameCellClick;
        _gamesGrid.CellDoubleClick += OnGameCellDoubleClick;

        _pinsGrid = UiKit.Grid();
        BuildPinColumns();
        _pinsGrid.CellContentClick += OnPinCellClick;
        _pinsGrid.CellDoubleClick += OnPinCellDoubleClick;

        gridHost.Controls.Add(Wrap("游戏（addappid / addtoken / 票据 / 成就）", _gamesGrid), 0, 0);
        gridHost.Controls.Add(Wrap("Manifest 固定（setManifestid）", _pinsGrid), 0, 1);

        _summary = Theme.TextLabel("", 8.5f, Theme.Muted);
        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(gridHost, 0, 1);
        root.Controls.Add(_summary, 0, 2);
        Controls.Add(root);
    }

    private static Panel Wrap(string title, Control grid)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, 8) };
        var label = Theme.TextLabel(title, 9.5f, Theme.Text, FontStyle.Bold);
        label.Dock = DockStyle.Top;
        label.Height = 26;
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        panel.Controls.Add(label);
        return panel;
    }

    private void BuildGameColumns()
    {
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "appid", HeaderText = "AppId", FillWeight = 55 });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "key", HeaderText = "解密密钥", FillWeight = 90 });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "token", HeaderText = "访问令牌", FillWeight = 90 });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stat", HeaderText = "成就 SteamID", FillWeight = 95 });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ticket", HeaderText = "票据", FillWeight = 55 });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "来源", FillWeight = 80 });
        _gamesGrid.Columns.Add(ButtonColumn("install", "下载", 50));
        _gamesGrid.Columns.Add(ButtonColumn("delete", "删除", 50));
    }

    private void BuildPinColumns()
    {
        _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "depot", HeaderText = "DepotId", FillWeight = 50 });
        _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "gid", HeaderText = "Manifest Gid", FillWeight = 120 });
        _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "Size", FillWeight = 60 });
        _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "来源", FillWeight = 90 });
        _pinsGrid.Columns.Add(ButtonColumn("delete", "删除", 50));
    }

    private static DataGridViewButtonColumn ButtonColumn(string name, string text, int weight) => new()
    {
        Name = name,
        HeaderText = "",
        Text = text,
        UseColumnTextForButtonValue = true,
        FlatStyle = FlatStyle.Flat,
        FillWeight = weight,
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(35, 42, 53),
            ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(45, 54, 68),
            SelectionForeColor = Theme.Text,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        }
    };

    public void RefreshData()
    {
        var luaDir = AppState.LuaDir;
        if (luaDir is null)
        {
            _snapshot = new LuaSnapshot();
            ApplyFilter();
            _summary.Text = "请先在“概览”里设置 Steam 目录";
            return;
        }

        _snapshot = LuaStore.Load(luaDir);
        ApplyFilter();

        var managed = _snapshot.Games.Count(g => g.Managed);
        _summary.Text = $"共 {_snapshot.Games.Count} 个 AppId（GUI 管理 {managed}）· {_snapshot.Pins.Count} 条 Manifest 固定 · {luaDir}";
    }

    private void ApplyFilter()
    {
        var query = _search.Text.Trim();
        bool Match(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);

        _games = _snapshot.Games
            .Where(g => Match(g.AppId.ToString()) || Match(g.DepotKey ?? "") || Match(g.Token ?? ""))
            .ToList();

        _pins = _snapshot.Pins
            .Where(p => Match(p.DepotId.ToString()) || Match(p.Gid.ToString()))
            .ToList();

        _gamesGrid.Rows.Clear();
        foreach (var game in _games)
        {
            _gamesGrid.Rows.Add(
                game.AppId,
                Shorten(game.DepotKey),
                Shorten(game.Token),
                game.Stat ?? "",
                game.HasTicket ? "已配置" : "—",
                game.Managed ? "GUI" : game.SourceFile,
                "下载",
                game.Managed ? "删除" : "—");
        }

        _pinsGrid.Rows.Clear();
        foreach (var pin in _pins)
        {
            _pinsGrid.Rows.Add(
                pin.DepotId,
                pin.Gid,
                pin.Size == 0 ? "0" : pin.Size.ToString(),
                pin.Managed ? "GUI" : pin.SourceFile,
                pin.Managed ? "删除" : "—");
        }
    }

    private static string Shorten(string? value)
        => string.IsNullOrEmpty(value) ? "—" : (value.Length > 16 ? value[..8] + "…" + value[^4..] : value);

    private void OnGameCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _games.Count) return;
        var entry = _games[e.RowIndex];
        switch (_gamesGrid.Columns[e.ColumnIndex].Name)
        {
            case "install":
                DownloadGame(entry);
                break;
            case "delete":
                DeleteGame(entry);
                break;
        }
    }

    private void OnGameCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _games.Count) return;
        var entry = _games[e.RowIndex];
        if (!entry.Managed)
        {
            MessageBox.Show(this, $"该条目来自 {entry.SourceFile}，请在对应文件里修改。", "只读",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        EditGame(entry);
    }

    private void OnPinCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _pins.Count) return;
        if (_pinsGrid.Columns[e.ColumnIndex].Name != "delete") return;
        DeletePin(_pins[e.RowIndex]);
    }

    private void OnPinCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _pins.Count) return;
        var pin = _pins[e.RowIndex];
        if (!pin.Managed)
        {
            MessageBox.Show(this, $"该条目来自 {pin.SourceFile}，请在对应文件里修改。", "只读",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        EditPin(pin);
    }

    private void AddGame()
    {
        if (AppState.LuaDir is null) { WarnNoSteam(); return; }
        using var dialog = new GameDialog(null, AppState.SourceDir);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var existing = _snapshot.Games.FirstOrDefault(g => g.AppId == dialog.Entry.AppId);
        if (existing is null)
        {
            _snapshot.Games.Add(dialog.Entry);
        }
        else
        {
            existing.DepotKey = dialog.Entry.DepotKey;
            existing.Token = dialog.Entry.Token;
            existing.Stat = dialog.Entry.Stat;
            existing.AppTicket = dialog.Entry.AppTicket;
            existing.ETicket = dialog.Entry.ETicket;
            existing.Managed = true;
            existing.SourceFile = LuaStore.ManagedFileName;
        }

        Save();
    }

    private void EditGame(GameEntry entry)
    {
        using var dialog = new GameDialog(entry, AppState.SourceDir);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        entry.DepotKey = dialog.Entry.DepotKey;
        entry.Token = dialog.Entry.Token;
        entry.Stat = dialog.Entry.Stat;
        entry.AppTicket = dialog.Entry.AppTicket;
        entry.ETicket = dialog.Entry.ETicket;
        entry.Managed = true;
        Save();
    }

    private void DeleteGame(GameEntry entry)
    {
        if (!entry.Managed)
        {
            MessageBox.Show(this, $"该条目来自 {entry.SourceFile}，请在对应文件里删除。", "只读",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"从 GUI 配置中移除 AppId {entry.AppId}？", "确认",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        _snapshot.Games.Remove(entry);
        Save();
    }

    private void AddPin()
    {
        if (AppState.LuaDir is null) { WarnNoSteam(); return; }
        using var dialog = new ManifestDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var existing = _snapshot.Pins.FirstOrDefault(p => p.DepotId == dialog.Pin.DepotId);
        if (existing is null) _snapshot.Pins.Add(dialog.Pin);
        else
        {
            existing.Gid = dialog.Pin.Gid;
            existing.Size = dialog.Pin.Size;
            existing.Managed = true;
            existing.SourceFile = LuaStore.ManagedFileName;
        }
        Save();
    }

    private void EditPin(ManifestPin pin)
    {
        using var dialog = new ManifestDialog(pin);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        pin.Gid = dialog.Pin.Gid;
        pin.Size = dialog.Pin.Size;
        pin.Managed = true;
        Save();
    }

    private void DeletePin(ManifestPin pin)
    {
        if (!pin.Managed)
        {
            MessageBox.Show(this, $"该条目来自 {pin.SourceFile}，请在对应文件里删除。", "只读",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"移除 DepotId {pin.DepotId} 的 Manifest 固定？", "确认",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        _snapshot.Pins.Remove(pin);
        Save();
    }

    private void ExtractSelectedTicket()
    {
        if (_gamesGrid.CurrentRow is null || _gamesGrid.CurrentRow.Index < 0 || _gamesGrid.CurrentRow.Index >= _games.Count)
        {
            MessageBox.Show(this, "请先在列表里选中一个游戏。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = _games[_gamesGrid.CurrentRow.Index];
        var result = TicketProvider.Acquire(entry.AppId, AppState.SteamRoot, AppState.SourceDir,
                                            AppState.Settings.TicketApiTemplate);
        if (!result.Ok)
        {
            MessageBox.Show(this, result.Message, "提取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var target = _snapshot.Games.FirstOrDefault(g => g.AppId == entry.AppId);
        if (target is null)
        {
            target = entry.Clone();
            target.Managed = true;
            target.SourceFile = LuaStore.ManagedFileName;
            _snapshot.Games.Add(target);
        }

        target.AppTicket = result.AppTicket;
        target.ETicket = result.ETicket;
        target.Managed = true;
        Save();

        MessageBox.Show(this, result.Message, "已提取", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>把外部 lua 条目复制一份到 GUI 管理的文件，便于追加票据。</summary>
    private GameEntry EnsureManaged(GameEntry entry)
    {
        if (entry.Managed) return entry;

        var copy = entry.Clone();
        copy.Managed = true;
        copy.SourceFile = LuaStore.ManagedFileName;
        _snapshot.Games.Add(copy);
        return copy;
    }

    /// <summary>准备票据 -> 写入 lua -> 交给 Steam 下载。</summary>
    private async void DownloadGame(GameEntry entry)
    {
        var target = EnsureManaged(entry);

        bool needTickets = AppState.Settings.AutoPrepareTickets && !target.HasTicket;

        // 取票要求 Steam 在线。离线时先把 Steam 拉起来，让用户登录后再点一次。
        if (needTickets && AppState.SteamRoot is not null && !SteamLocator.IsSteamRunning())
        {
            Save();
            SteamLocator.Launch(AppState.SteamRoot);
            _summary.Text = "取票需要 Steam 在线：已启动 Steam，登录后再点一次「下载」。";
            return;
        }

        if (needTickets)
        {
            _summary.Text = $"正在为 AppId {target.AppId} 准备票据…";
            var result = await Task.Run(() =>
                TicketProvider.Acquire(target.AppId, AppState.SteamRoot, AppState.SourceDir, AppState.Settings.TicketApiTemplate));

            if (result.Ok)
            {
                target.AppTicket = result.AppTicket;
                target.ETicket = result.ETicket;
            }
            else
            {
                _summary.Text = $"AppId {target.AppId} 未取到票据（仍会交给 Steam 下载）：{result.Message}";
            }
        }

        Save();

        try
        {
            Process.Start(new ProcessStartInfo($"steam://install/{target.AppId}") { UseShellExecute = true });
            _summary.Text = $"AppId {target.AppId} 已交给 Steam 下载；票据写入后由 DLL 在运行期接管";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "调用 Steam 失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>批量为 GUI 管理的条目准备票据（只补缺，不覆盖已有）。</summary>
    private async Task PrepareTicketsAsync()
    {
        if (AppState.LuaDir is null) { WarnNoSteam(); return; }

        var targets = _snapshot.Games.Where(g => g.Managed && !g.HasTicket).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "GUI 管理的条目都已有票据，没有需要准备的内容。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int ok = 0;
        int failed = 0;

        foreach (var game in targets)
        {
            _summary.Text = $"准备票据 {ok + failed + 1}/{targets.Count}：AppId {game.AppId}…";
            var result = await Task.Run(() =>
                TicketProvider.Acquire(game.AppId, AppState.SteamRoot, AppState.SourceDir, AppState.Settings.TicketApiTemplate));

            if (result.Ok)
            {
                game.AppTicket = result.AppTicket;
                game.ETicket = result.ETicket;
                ok++;
            }
            else
            {
                failed++;
            }
        }

        Save();

        MessageBox.Show(this,
            $"完成：成功 {ok}，失败 {failed}。\n\n票据已写入 {LuaStore.ManagedFileName}；" +
            "Steam 处于运行状态时会在配置热重载后写入本地凭据库。",
            "准备票据", MessageBoxButtons.OK,
            failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void Save()
    {
        var luaDir = AppState.LuaDir;
        if (luaDir is null) { WarnNoSteam(); return; }

        try
        {
            LuaStore.SaveManaged(luaDir, _snapshot.Games.Where(g => g.Managed), _snapshot.Pins.Where(p => p.Managed));
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "写入 Lua 失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLuaDir()
    {
        var luaDir = AppState.LuaDir;
        if (luaDir is null) { WarnNoSteam(); return; }
        try
        {
            Directory.CreateDirectory(luaDir);
            Process.Start(new ProcessStartInfo(luaDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WarnNoSteam()
        => MessageBox.Show(this, "请先在“概览”里设置 Steam 目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
