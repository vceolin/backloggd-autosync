using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class UITheme
    {
        public static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        public static readonly Color BgCard = Color.FromArgb(31, 34, 43);
        public static readonly Color BgHeader = Color.FromArgb(37, 41, 52);
        public static readonly Color BgInput = Color.FromArgb(42, 46, 58);
        public static readonly Color BorderSubtle = Color.FromArgb(54, 60, 76);
        public static readonly Color AccentPink = Color.FromArgb(234, 55, 122);
        public static readonly Color AccentBlue = Color.FromArgb(72, 133, 237);
        public static readonly Color TextLight = Color.FromArgb(238, 240, 246);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 179);
        public static readonly Color SuccessGreen = Color.FromArgb(46, 204, 113);
        public static readonly Color WarnOrange = Color.FromArgb(243, 156, 18);

        public static Color AdjustBrightness(Color c, int delta)
        {
            int r = Math.Max(0, Math.Min(255, c.R + delta));
            int g = Math.Max(0, Math.Min(255, c.G + delta));
            int b = Math.Max(0, Math.Min(255, c.B + delta));
            return Color.FromArgb(c.A, r, g, b);
        }

        public static Button CreateStyledButton(string text, Color bgColor, int width, int height)
        {
            bool isNeutral = (bgColor == BgInput || bgColor == BgCard);
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 0, 0),
                UseVisualStyleBackColor = false
            };
            btn.FlatAppearance.BorderSize = isNeutral ? 1 : 0;
            if (isNeutral)
            {
                btn.FlatAppearance.BorderColor = BorderSubtle;
            }
            btn.FlatAppearance.MouseOverBackColor = AdjustBrightness(bgColor, 18);
            btn.FlatAppearance.MouseDownBackColor = AdjustBrightness(bgColor, -14);
            return btn;
        }

        public static void StyleTabControl(TabControl tabControl)
        {
            if (tabControl == null) return;
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.SizeMode = TabSizeMode.Fixed;
            tabControl.ItemSize = new Size(215, 34);
            tabControl.DrawItem += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                bool isSelected = (e.Index == tabControl.SelectedIndex);
                Rectangle tabRect = tabControl.GetTabRect(e.Index);
                string title = tabControl.TabPages[e.Index].Text;

                using (var bgBrush = new SolidBrush(isSelected ? BgCard : BgDark))
                {
                    g.FillRectangle(bgBrush, tabRect);
                }

                if (isSelected)
                {
                    using (var accentBrush = new SolidBrush(AccentPink))
                    {
                        g.FillRectangle(accentBrush, tabRect.Left + 8, tabRect.Bottom - 3, tabRect.Width - 16, 3);
                    }
                }

                using (var font = new Font("Segoe UI", 9.5f, isSelected ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(
                        g,
                        title,
                        font,
                        tabRect,
                        isSelected ? TextLight : TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            };
        }
    }

    public class DarkListView : ListView
    {
        private int[] _baseColumnWidths;
        private bool _isResizingColumns;

        public DarkListView()
        {
            this.DoubleBuffered = true;
            this.View = View.Details;
            this.FullRowSelect = true;
            this.GridLines = false;
            this.BackColor = UITheme.BgCard;
            this.ForeColor = UITheme.TextLight;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.OwnerDraw = true;
        }

        public void EnableProportionalColumns()
        {
            if (Columns.Count == 0) return;
            _baseColumnWidths = new int[Columns.Count];
            for (int i = 0; i < Columns.Count; i++)
            {
                _baseColumnWidths[i] = Math.Max(60, Columns[i].Width);
            }
            ApplyProportionalColumnWidths();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyProportionalColumnWidths();
        }

        private void ApplyProportionalColumnWidths()
        {
            if (_isResizingColumns || _baseColumnWidths == null || Columns.Count != _baseColumnWidths.Length) return;
            int availWidth = ClientSize.Width - 4;
            if (availWidth <= 100) return;

            int totalBase = 0;
            for (int i = 0; i < _baseColumnWidths.Length; i++)
            {
                totalBase += _baseColumnWidths[i];
            }
            if (totalBase <= 0) return;

            _isResizingColumns = true;
            try
            {
                int allocated = 0;
                for (int i = 0; i < Columns.Count; i++)
                {
                    if (i == Columns.Count - 1)
                    {
                        int lastWidth = Math.Max(_baseColumnWidths[i] / 2, availWidth - allocated);
                        Columns[i].Width = lastWidth;
                    }
                    else
                    {
                        int w = (int)Math.Round((double)_baseColumnWidths[i] / totalBase * availWidth);
                        w = Math.Max(70, w);
                        Columns[i].Width = w;
                        allocated += w;
                    }
                }
            }
            finally
            {
                _isResizingColumns = false;
            }
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var bgBrush = new SolidBrush(UITheme.BgHeader))
            {
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            }

            using (var borderPen = new Pen(UITheme.BorderSubtle))
            {
                e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            }

            Rectangle textRect = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            using (var headerFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Header.Text,
                    headerFont,
                    textRect,
                    UITheme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = true;
        }
    }
}
