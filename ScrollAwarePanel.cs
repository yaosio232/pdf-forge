namespace Pdf_Merger;

internal sealed class ScrollAwarePanel : Panel
{
    private const int WmVScroll = 0x0115;
    private const int WmMouseWheel = 0x020A;

    public event EventHandler? ScrollChanged;

    private bool isMiddleMousePanning;
    private Point panPointerOrigin;
    private Point panScrollOrigin;

    public ScrollAwarePanel()
    {
        DoubleBuffered = true;
    }

    internal static Point CalculatePanPosition(Point scrollOrigin, Point pointerOrigin, Point pointer) =>
        new(Math.Max(0, scrollOrigin.X - (pointer.X - pointerOrigin.X)),
            Math.Max(0, scrollOrigin.Y - (pointer.Y - pointerOrigin.Y)));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Middle || !AutoScroll)
        {
            return;
        }

        isMiddleMousePanning = true;
        panPointerOrigin = e.Location;
        panScrollOrigin = new Point(
            Math.Abs(AutoScrollPosition.X), Math.Abs(AutoScrollPosition.Y));
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!isMiddleMousePanning)
        {
            return;
        }

        AutoScrollPosition = CalculatePanPosition(panScrollOrigin, panPointerOrigin, e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle)
        {
            EndMiddleMousePan();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            EndMiddleMousePan();
        }
    }

    private void EndMiddleMousePan()
    {
        isMiddleMousePanning = false;
        Capture = false;
        Cursor = Cursors.Default;
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
