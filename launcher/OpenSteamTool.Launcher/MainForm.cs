namespace OpenSteamTool.Launcher;

internal sealed class MainForm : Form
{
    private static readonly (string Key, string Title, string Caption)[] NavItems =
    {
        ("dashboard", "概览", "安装与运行状态"),
        ("library", "游戏库", "AppId、密钥、票据与 Manifest"),
        ("config",  "配置", "opensteamtool.toml"),
        ("logs",    "日志", "Debug 构建的诊断输出")
    };

    private readonly TableLayoutPanel _shell;
    private readonly Panel _body;
    private readonly Label _caption;
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();

    public MainForm()
    {
        Text = "OpenSteamTool Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 770);
        MinimumSize = new Size(1020, 680);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9f);
        DoubleBuffered = true;

        _shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Bg
        };
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _shell.Controls.Add(BuildSidebar(), 0, 0);

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(24, 0, 24, 20) };
        _caption = Theme.TextLabel("", 11f, Theme.Muted);
        _caption.Location = new Point(0, 22);

        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Bg };
        header.Controls.Add(_caption);

        _body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        content.Controls.Add(_body);
        content.Controls.Add(header);
        _shell.Controls.Add(content, 1, 0);
        Controls.Add(_shell);

        BuildPages();
        ShowPage(PageKey(AppState.Settings.LastPage));
    }

    private static string PageKey(int index)
        => index >= 0 && index < NavItems.Length ? NavItems[index].Key : "dashboard";

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(16, 24, 16, 16) };

        var brand = Theme.TextLabel("OpenSteamTool", 12.5f, Theme.Text, FontStyle.Bold);
        brand.Location = new Point(16, 24);

        var tagline = Theme.TextLabel("Windows · 注入式解锁", 8.5f, Theme.Muted);
        tagline.Location = new Point(16, 52);

        var navHost = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            BackColor = Theme.Sidebar,
            Location = new Point(16, 96),
            Size = new Size(176, NavItems.Length * 48 + 4)
        };

        foreach (var item in NavItems)
        {
            var button = new Button
            {
                Text = "   " + item.Title,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Sidebar,
                ForeColor = Theme.Muted,
                Font = Theme.Font(10f),
                Height = 42,
                Width = 176,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 0, 2),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 34, 43);
            button.Click += (_, _) => ShowPage(item.Key);
            _navButtons[item.Key] = button;
            navHost.Controls.Add(button);
        }

        var footer = Theme.TextLabel("v0.1 · 仅供研究", 8f, Theme.Muted);
        footer.Dock = DockStyle.Bottom;
        footer.Padding = new Padding(0, 0, 0, 6);

        sidebar.Controls.Add(navHost);
        sidebar.Controls.Add(brand);
        sidebar.Controls.Add(tagline);
        sidebar.Controls.Add(footer);
        return sidebar;
    }

    private void BuildPages()
    {
        AddPage("dashboard", new DashboardPage());
        AddPage("library", new LibraryPage());
        AddPage("config", new ConfigPage());
        AddPage("logs", new LogsPage());
    }

    private void AddPage(string key, Control page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[key] = page;
        _body.Controls.Add(page);
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;

        foreach (var pair in _pages) pair.Value.Visible = pair.Key == key;
        page.Visible = true;
        page.BringToFront();

        foreach (var pair in _navButtons)
        {
            bool active = pair.Key == key;
            pair.Value.BackColor = active ? Color.FromArgb(30, 42, 56) : Theme.Sidebar;
            pair.Value.ForeColor = active ? Theme.Accent : Theme.Muted;
        }

        _caption.Text = NavItems.First(n => n.Key == key).Caption;

        if (page is IRefreshable refreshable) refreshable.RefreshData();

        AppState.Settings.LastPage = Array.FindIndex(NavItems, n => n.Key == key);
        AppState.Settings.Save();
    }
}
