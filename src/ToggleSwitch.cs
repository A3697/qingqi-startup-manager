using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QingQi
{
    public sealed class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false; Dock = DockStyle.Fill; Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float scale = e.Graphics.DpiX / 96F;
            var track = new Rectangle(0, (Height - (int)(24 * scale)) / 2, (int)(46 * scale), (int)(24 * scale));
            PaintTrack(e.Graphics, track, Checked, Enabled, false);
            var caption = new Rectangle((int)(56 * scale), 0, Width - (int)(56 * scale), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, caption, Enabled ? Theme.Ink : SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
        internal static void PaintTrack(Graphics graphics, Rectangle bounds, bool on, bool enabled, bool selected)
        {
            GraphicsState state = graphics.Save();
            try
            {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !enabled ? Color.FromArgb(205, 210, 218) : on ? (selected ? Color.FromArgb(18, 50, 133) : Theme.Blue) : Color.FromArgb(119, 132, 152);
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(selected ? Color.White : Color.FromArgb(89, 106, 132), Math.Max(1F, graphics.DpiX / 96F)))
            {
                int diameter = bounds.Height - 1;
                path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
                path.AddArc(bounds.Right - diameter - 1, bounds.Y, diameter, diameter, 270, 180);
                path.CloseFigure(); graphics.FillPath(brush, path); graphics.DrawPath(pen, path);
            }
            int inset = Math.Max(3, bounds.Height / 8), thumb = bounds.Height - 2 * inset;
            using (var brush = new SolidBrush(Color.White))
                graphics.FillEllipse(brush, on ? bounds.Right - inset - thumb : bounds.X + inset, bounds.Y + inset, thumb, thumb);
            }
            finally { graphics.Restore(state); }
        }
    }

    public sealed class ToggleCell : DataGridViewCheckBoxCell
    {
        protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle style, int rowIndex)
        {
            Size size = GetSize(rowIndex); float scale = graphics.DpiX / 96F;
            int width = (int)(44 * scale), height = (int)(23 * scale);
            return new Rectangle((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        }
        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
            DataGridViewElementStates state, object value, object formattedValue, string errorText,
            DataGridViewCellStyle style, DataGridViewAdvancedBorderStyle borderStyle, DataGridViewPaintParts parts)
        {
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, state, value, formattedValue, errorText, style, borderStyle,
                parts & ~DataGridViewPaintParts.ContentForeground);
            float scale = graphics.DpiX / 96F;
            int width = (int)(44 * scale), height = (int)(23 * scale);
            var bounds = new Rectangle(cellBounds.X + (cellBounds.Width - width) / 2, cellBounds.Y + (cellBounds.Height - height) / 2, width, height);
            ToggleSwitch.PaintTrack(graphics, bounds, value is bool && (bool)value, !ReadOnly,
                (state & DataGridViewElementStates.Selected) != 0);
        }
    }
}
