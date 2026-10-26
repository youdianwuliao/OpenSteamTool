namespace OpenSteamTool.Launcher;

internal interface IRefreshable
{
    void RefreshData();
}

/// <summary>可滚动页面基类：自上而下堆叠卡片。</summary>
internal abstract class PageBase : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel _flow;

    protected PageBase()
    {
        BackColor = Theme.Bg;
        AutoScroll = true;

        _flow = new FlowLayoutPanel
        {
            Location = new Point(0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Bg,
            Padding = new Padding(0)
        };

        Controls.Add(_flow);
        Resize += (_, _) => ResizeCards();
    }

    protected Card AddCard(string title, int height)
    {
        var card = new Card(title, height);
        _flow.Controls.Add(card);
        ResizeCards();
        return card;
    }

    public virtual void RefreshData()
    {
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ResizeCards();
    }

    private void ResizeCards()
    {
        int width = Math.Max(320, ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        _flow.Width = width;
        foreach (Control control in _flow.Controls) control.Width = width;
    }
}
