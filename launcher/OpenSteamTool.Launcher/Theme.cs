using System.Drawing.Drawing2D;

namespace OpenSteamTool.Launcher;

/// <summary>全局配色与控件工厂，模仿 Steam 客户端的深色风格。</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(15, 17, 21);
    public static readonly Color Sidebar = Color.FromArgb(20, 24, 30);
    public static readonly Color Card = Color.FromArgb(27, 32, 41);
    public static readonly Color Input = Color.FromArgb(18, 22, 28);
    public static readonly Color Border = Color.FromArgb(42, 48, 60);
    public static readonly Color Accent = Color.FromArgb(102, 192, 244);
    public static readonly Color Text = Color.FromArgb(199, 213, 224);
    public static readonly Color Muted = Color.FromArgb(143, 152, 160);
    public static readonly Color Ok = Color.FromArgb(164, 208, 7);
    public static readonly Color Danger = Color.FromArgb(224, 108, 117);
    public static readonly Color Warn = Color.FromArgb(230, 176, 84);

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
        => new("Microsoft YaHei UI", size, style);

    public static Button Primary(string text) => MakeButton(text, Accent, Color.FromArgb(12, 18, 24), true);
    public static Button Ghost(string text) => MakeButton(text, Color.FromArgb(35, 42, 53), Text, false);
    public static Button DangerBtn(string text) => MakeButton(text, Color.FromArgb(56, 28, 33), Danger, false);

    private static Button MakeButton(string text, Color back, Color fore, bool bold)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(126, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = Font(9.5f, bold ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 0, 8, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.18f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.10f);
        return b;
    }

    public static Label TextLabel(string text, float size = 9f, Color? color = null, FontStyle style = FontStyle.Regular)
        => new()
        {
            Text = text,
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = color ?? Text,
            Font = Font(size, style)
        };

    public static TextBox InputBox(string text = "")
        => new()
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Input,
            ForeColor = Text,
            Font = Font(9f),
            Dock = DockStyle.Fill
        };
}
