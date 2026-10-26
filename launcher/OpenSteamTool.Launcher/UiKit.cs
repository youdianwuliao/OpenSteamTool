using System.Drawing.Drawing2D;

namespace OpenSteamTool.Launcher;

/// <summary>带标题的卡片容器。</summary>
internal sealed class Card : Panel
{
    public Panel Body { get; }

    public Card(string title, int height)
    {
        Height = height;
        BackColor = Theme.Card;
        Margin = new Padding(0, 0, 0, 12);
        Padding = new Padding(16, 12, 16, 12);

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(16, 12),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.Font(10.5f, FontStyle.Bold)
        };

        Body = new Panel
        {
            Location = new Point(16, 40),
            BackColor = Color.Transparent,
            Size = new Size(Math.Max(10, Width - 32), Math.Max(10, Height - 52))
        };

        Controls.Add(Body);
        Controls.Add(titleLabel);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Body.Size = new Size(Math.Max(10, Width - 32), Math.Max(10, Height - 52));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>通用控件构建助手。</summary>
internal static class UiKit
{
    /// <summary>三列表单：标签 | 输入(自适应) | 按钮组。</summary>
    public static TableLayoutPanel Form()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 0,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    public static void Row(TableLayoutPanel table, string label, Control control, params Control[] actions)
    {
        int index = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = Theme.TextLabel(label, 9f, Theme.Muted);
        caption.Anchor = AnchorStyles.Left;
        caption.Margin = new Padding(0, 8, 8, 8);
        table.Controls.Add(caption, 0, index);

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 5, 8, 5);
        table.Controls.Add(control, 1, index);

        if (actions.Length > 0)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 5, 0, 5)
            };
            foreach (var action in actions)
            {
                action.Margin = new Padding(0, 0, 6, 0);
                flow.Controls.Add(action);
            }
            table.Controls.Add(flow, 2, index);
        }
    }

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Theme.Bg,
            BorderStyle = BorderStyle.None,
            GridColor = Theme.Border,
            EnableHeadersVisualStyles = false,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(31, 37, 47),
                ForeColor = Theme.Text,
                Font = Theme.Font(9f, FontStyle.Bold),
                SelectionBackColor = Color.FromArgb(31, 37, 47),
                SelectionForeColor = Theme.Text,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                SelectionBackColor = Color.FromArgb(38, 48, 62),
                SelectionForeColor = Theme.Text,
                Font = Theme.Font(9f),
                Padding = new Padding(6, 0, 0, 0)
            }
        };
        grid.RowTemplate.Height = 30;
        return grid;
    }
}
