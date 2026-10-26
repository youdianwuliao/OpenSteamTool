using System.Text.RegularExpressions;

namespace OpenSteamTool.Launcher;

internal sealed class GameDialog : Form
{
    public GameEntry Entry { get; }

    private readonly TextBox _appId;
    private readonly TextBox _key;
    private readonly TextBox _token;
    private readonly TextBox _stat;
    private readonly Label _ticketLabel;
    private readonly string? _sourceDir;

    public GameDialog(GameEntry? existing, string? sourceDir)
    {
        _sourceDir = sourceDir;
        Entry = existing?.Clone() ?? new GameEntry { Managed = true, SourceFile = LuaStore.ManagedFileName };

        Text = existing is null ? "添加游戏" : "编辑 AppId " + existing.AppId;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9f);
        ClientSize = new Size(560, 300);
        Padding = new Padding(20);

        var table = UiKit.Form();
        table.Dock = DockStyle.Fill;

        _appId = Theme.InputBox(Entry.AppId == 0 ? "" : Entry.AppId.ToString());
        _key = Theme.InputBox(Entry.DepotKey ?? "");
        _token = Theme.InputBox(Entry.Token ?? "");
        _stat = Theme.InputBox(Entry.Stat ?? "");

        _key.PlaceholderText = "64 位十六进制，可留空";
        _token.PlaceholderText = "addtoken 的值，可留空";
        _stat.PlaceholderText = "setStat 使用的 SteamID，可留空";

        _ticketLabel = Theme.TextLabel(Entry.HasTicket ? "已配置（保存时写入）" : "未配置", 8.5f, Theme.Muted);
        var extract = Theme.Ghost("提取票据");
        extract.Click += async (_, _) => await ExtractAsync();

        UiKit.Row(table, "AppId", _appId);
        UiKit.Row(table, "解密密钥", _key);
        UiKit.Row(table, "访问令牌", _token);
        UiKit.Row(table, "成就 SteamID", _stat);
        UiKit.Row(table, "票据", _ticketLabel, extract);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 12, 0, 0)
        };

        var cancel = Theme.Ghost("取消");
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var ok = Theme.Primary("确定");
        ok.Click += (_, _) => Confirm();

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Confirm()
    {
        if (!int.TryParse(_appId.Text.Trim(), out int appId) || appId <= 0)
        {
            MessageBox.Show(this, "AppId 必须是正整数。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var key = _key.Text.Trim();
        if (key.Length > 0 && !Regex.IsMatch(key, "^[0-9A-Fa-f]{64}$"))
        {
            MessageBox.Show(this, "解密密钥必须是 64 位十六进制字符。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var token = _token.Text.Trim();
        if (token.Length > 0 && !Regex.IsMatch(token, "^[0-9]+$"))
        {
            MessageBox.Show(this, "访问令牌通常是纯数字字符串。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Entry.AppId = appId;
        Entry.DepotKey = key.Length == 0 ? null : key.ToLowerInvariant();
        Entry.Token = token.Length == 0 ? null : token;
        Entry.Stat = _stat.Text.Trim().Length == 0 ? null : _stat.Text.Trim();
        Entry.Managed = true;
        Entry.SourceFile = LuaStore.ManagedFileName;

        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task ExtractAsync()
    {
        if (!int.TryParse(_appId.Text.Trim(), out int appId) || appId <= 0)
        {
            MessageBox.Show(this, "先填写 AppId。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var tool = TicketTool.Find(_sourceDir);
        if (tool is null)
        {
            MessageBox.Show(this, "未找到 extract_tickets.exe。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _ticketLabel.Text = "提取中…";
        var result = await Task.Run(() => TicketTool.Extract(tool, appId));

        if (!result.Ok)
        {
            _ticketLabel.Text = "提取失败";
            MessageBox.Show(this, result.Message, "提取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Entry.AppTicket = result.AppTicket;
        Entry.ETicket = result.ETicket;
        _ticketLabel.Text = result.Message;
        _ticketLabel.ForeColor = Theme.Ok;
    }
}

internal sealed class ManifestDialog : Form
{
    public ManifestPin Pin { get; }

    private readonly TextBox _depot;
    private readonly TextBox _gid;
    private readonly TextBox _size;

    public ManifestDialog(ManifestPin? existing)
    {
        Pin = existing?.Clone() ?? new ManifestPin { Managed = true, SourceFile = LuaStore.ManagedFileName };

        Text = existing is null ? "添加 Manifest 固定" : "编辑 DepotId " + existing.DepotId;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9f);
        ClientSize = new Size(560, 230);
        Padding = new Padding(20);

        var table = UiKit.Form();
        table.Dock = DockStyle.Fill;

        _depot = Theme.InputBox(Pin.DepotId == 0 ? "" : Pin.DepotId.ToString());
        _gid = Theme.InputBox(Pin.Gid == 0 ? "" : Pin.Gid.ToString());
        _size = Theme.InputBox(Pin.Size == 0 ? "" : Pin.Size.ToString());

        _gid.PlaceholderText = "Manifest Gid（十进制）";
        _size.PlaceholderText = "可选，0 表示沿用原大小";

        UiKit.Row(table, "DepotId", _depot);
        UiKit.Row(table, "Manifest Gid", _gid);
        UiKit.Row(table, "Size", _size);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 12, 0, 0)
        };

        var cancel = Theme.Ghost("取消");
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var ok = Theme.Primary("确定");
        ok.Click += (_, _) => Confirm();

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Confirm()
    {
        if (!uint.TryParse(_depot.Text.Trim(), out uint depotId) || depotId == 0)
        {
            MessageBox.Show(this, "DepotId 必须是正整数。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ulong.TryParse(_gid.Text.Trim(), out ulong gid) || gid == 0)
        {
            MessageBox.Show(this, "Manifest Gid 必须是十进制正整数。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ulong size = 0;
        var sizeText = _size.Text.Trim();
        if (sizeText.Length > 0 && !ulong.TryParse(sizeText, out size))
        {
            MessageBox.Show(this, "Size 必须是数字或留空。", "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Pin.DepotId = depotId;
        Pin.Gid = gid;
        Pin.Size = size;
        Pin.Managed = true;
        Pin.SourceFile = LuaStore.ManagedFileName;

        DialogResult = DialogResult.OK;
        Close();
    }
}
