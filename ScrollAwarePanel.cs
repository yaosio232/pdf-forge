namespace Pdf_Merger;

internal sealed class ScrollAwarePanel : Panel
{
    private const int WmVScroll = 0x0115;
    private const int WmMouseWheel = 0x020A;

    public event EventHandler? ScrollChanged;

    public ScrollAwarePanel()
    {
        DoubleBuffered = true;
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is WmVScroll or WmMouseWheel)
        {
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
