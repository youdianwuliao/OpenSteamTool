namespace OpenSteamTool.Launcher;

internal sealed class LogsPage : UserControl, IRefreshable
{
    private readonly ComboBox _files;
    private readonly CheckBox _auto;
    private readonly TextBox _view;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;

    private string[] _paths = Array.Empty<string>();

    public LogsPage()
    {
        BackColor = Theme.Bg;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 8)
        };

        _files = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Width = 240,
            Margin = new Padding(0, 0, 8, 0)
        };
        _files.SelectedIndexChanged += (_, _) => ShowSelected();

        var refresh = Theme.Ghost("刷新");
        refresh.Click += (_, _) => RefreshData();

        var openDir = Theme.Ghost("打开目录");
        openDir.Click += (_, _) =>
        {
            var dir = AppState.LogDir;
            if (dir is null || !Directory.Exists(dir)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true }); }
            catch { }
        };

        _auto = new CheckBox
        {
            Text = "自动刷新（2 秒）",
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = Theme.Font(9f),
            Margin = new Padding(8, 10, 0, 0)
        };

        toolbar.Controls.Add(_files);
        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(openDir);
        toolbar.Controls.Add(_auto);

        _view = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };

        _status = Theme.TextLabel("", 8.5f, Theme.Muted);
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_view, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) =>
        {
            if (!_auto.Checked) return;
            ShowSelected();
        };
        _timer.Start();
    }

    public void RefreshData()
    {
        var root = AppState.SteamRoot;
        _paths = LogFiles.List(root);

        var previous = _files.SelectedItem?.ToString();
        _files.Items.Clear();

        if (_paths.Length == 0)
        {
            _view.Text = root is null
                ? "请先在“概览”里设置 Steam 目录。"
                : "没有日志文件。日志只由 Debug 构建产生：" + Path.Combine(root, "opensteamtool") + "\\*.log";
            _status.Text = "";
            return;
        }

        foreach (var path in _paths) _files.Items.Add(Path.GetFileName(path));

        int index = previous is null ? 0 : Array.FindIndex(_paths, p => Path.GetFileName(p) == previous);
        _files.SelectedIndex = index < 0 ? 0 : index;
        ShowSelected();
    }

    private void ShowSelected()
    {
        int index = _files.SelectedIndex;
        if (index < 0 || index >= _paths.Length) return;

        _view.Text = LogFiles.ReadTail(_paths[index]);
        _view.SelectionStart = _view.TextLength;
        _view.ScrollToCaret();

        var info = new FileInfo(_paths[index]);
        _status.Text = info.Exists
            ? $"{info.Name} · {info.Length / 1024} KB · 最后写入 {info.LastWriteTime:HH:mm:ss}"
            : info.Name;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
