using System.Drawing;
using System.Windows.Forms;

namespace QingQi
{
    public sealed class BufferedTablePanel : TableLayoutPanel
    {
        public BufferedTablePanel()
        {
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Background;
        }
    }
    public sealed class BufferedFlowPanel : FlowLayoutPanel
    {
        public BufferedFlowPanel()
        {
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Background;
        }
    }
    public sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Background;
        }
    }
    public sealed class BufferedGrid : DataGridView
    {
        public BufferedGrid() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
