using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class UITheme
    {
        // Official Backloggd CSS Custom Properties (--back-*)
        public static readonly Color BgDark = Color.FromArgb(22, 24, 28);             // --back-primary: #16181c
        public static readonly Color BgCard = Color.FromArgb(36, 40, 50);             // --back-secondary: #242832
        public static readonly Color BgHeader = Color.FromArgb(39, 44, 55);           // --back-field-background: #272c37
        public static readonly Color BgInput = Color.FromArgb(48, 57, 76);            // --back-field: #30394c
        public static readonly Color BgHighlight = Color.FromArgb(63, 75, 100);       // --back-field-highlight: #3f4b64
        public static readonly Color BorderSubtle = Color.FromArgb(59, 65, 78);       // --back-field-border: #3b414e

        public static readonly Color AccentPink = Color.FromArgb(234, 55, 122);       // --back-pink-dk: #ea377a
        public static readonly Color AccentPinkLight = Color.FromArgb(252, 99, 153);  // --back-pink: #fc6399
        public static readonly Color AccentBlue = Color.FromArgb(74, 94, 141);        // --back-interact: #4a5e8d
        public static readonly Color AccentIndigo = Color.FromArgb(131, 157, 249);    // --back-interact-secondary: #839df9
        public static readonly Color AccentPlaying = Color.FromArgb(133, 111, 239);   // --back-playing: #856fef

        public static readonly Color TextWhite = Color.FromArgb(255, 255, 255);       // #ffffff
        public static readonly Color TextLight = Color.FromArgb(204, 219, 241);       // --back-text-review: #ccdbf1
        public static readonly Color TextPrimary = Color.FromArgb(203, 212, 220);     // --back-text: #cbd4dc
        public static readonly Color TextMuted = Color.FromArgb(143, 156, 167);       // --back-text-secondary: #8f9ca7

        public static readonly Color SuccessGreen = Color.FromArgb(67, 185, 79);      // --back-type-completed: #43b94f
        public static readonly Color WarnOrange = Color.FromArgb(230, 155, 62);       // --back-type-shelved: #e69b3e
        public static readonly Color DangerRed = Color.FromArgb(234, 71, 71);         // --back-type-abandoned: #ea4747

        public static Color AdjustBrightness(Color c, int delta)
        {
            int r = Math.Max(0, Math.Min(255, c.R + delta));
            int g = Math.Max(0, Math.Min(255, c.G + delta));
            int b = Math.Max(0, Math.Min(255, c.B + delta));
            return Color.FromArgb(c.A, r, g, b);
        }

        public static Button CreateStyledButton(string text, Color bgColor, int width, int height)
        {
            bool isFieldButton = (bgColor == BgInput || bgColor == BgCard);
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
                Margin = new Padding(6, 0, 0, 0),
                UseVisualStyleBackColor = false
            };
            btn.FlatAppearance.BorderSize = 1;
            if (bgColor == AccentPink)
            {
                btn.FlatAppearance.BorderColor = AccentPinkLight;
                btn.FlatAppearance.MouseOverBackColor = AccentPinkLight;
                btn.FlatAppearance.MouseDownBackColor = AdjustBrightness(AccentPink, -18);
            }
            else if (bgColor == AccentBlue)
            {
                btn.FlatAppearance.BorderColor = Color.FromArgb(92, 115, 168);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(88, 110, 162);
                btn.FlatAppearance.MouseDownBackColor = AdjustBrightness(AccentBlue, -14);
            }
            else if (isFieldButton)
            {
                btn.FlatAppearance.BorderColor = BorderSubtle;
                btn.FlatAppearance.MouseOverBackColor = BgHighlight;
                btn.FlatAppearance.MouseDownBackColor = BgHeader;
            }
            else
            {
                btn.FlatAppearance.BorderColor = AdjustBrightness(bgColor, 20);
                btn.FlatAppearance.MouseOverBackColor = AdjustBrightness(bgColor, 16);
                btn.FlatAppearance.MouseDownBackColor = AdjustBrightness(bgColor, -14);
            }
            return btn;
        }
    }

    /// <summary>
    /// Borderless TabControl that suppresses the legacy Win32 3D white border around TabPages.
    /// Navigation is driven by Backloggd-style top navbar buttons in MainForm.
    /// </summary>
    public class BorderlessTabControl : TabControl
    {
        public BorderlessTabControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Appearance = TabAppearance.FlatButtons;
            this.ItemSize = new Size(0, 1);
            this.SizeMode = TabSizeMode.Fixed;
        }

        protected override void WndProc(ref Message m)
        {
            // TCM_ADJUSTRECT = 0x1328: eliminate the 4px white 3D border around TabPages
            if (m.Msg == 0x1328 && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Backloggd-styled table view matching Backloggd's .library-entry-row and Journal tables
    /// (--back-secondary row surfaces, --back-field-border dividers, --back-pink selection indicator).
    /// </summary>
    public class DarkListView : ListView
    {
        private int[] _baseColumnWidths;
        private bool _isResizingColumns;
        private int _hoveredIndex = -1;

        public DarkListView()
        {
            this.DoubleBuffered = true;
            this.View = View.Details;
            this.FullRowSelect = true;
            this.GridLines = false;
            this.BackColor = UITheme.BgCard;
            this.ForeColor = UITheme.TextLight;
            this.BorderStyle = BorderStyle.None;
            this.OwnerDraw = true;
            this.HideSelection = false;

            // Increase row height slightly using a small image list (Backloggd spacious row feel)
            var rowHeightImg = new ImageList { ImageSize = new Size(1, 28) };
            this.SmallImageList = rowHeightImg;
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            int newHover = hit.Item != null ? hit.Item.Index : -1;
            if (newHover != _hoveredIndex)
            {
                int oldHover = _hoveredIndex;
                _hoveredIndex = newHover;
                if (oldHover >= 0 && oldHover < Items.Count) Invalidate(Items[oldHover].Bounds);
                if (_hoveredIndex >= 0 && _hoveredIndex < Items.Count) Invalidate(Items[_hoveredIndex].Bounds);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredIndex >= 0 && _hoveredIndex < Items.Count)
            {
                int old = _hoveredIndex;
                _hoveredIndex = -1;
                Invalidate(Items[old].Bounds);
            }
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
            }

            Rectangle textRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
            using (var headerFont = new Font("Segoe UI", 8.75f, FontStyle.Regular))
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
            bool isSelected = e.Item.Selected;
            bool isHovered = (e.ItemIndex == _hoveredIndex);

            Color rowBg = isSelected
                ? UITheme.BgInput
                : (isHovered ? Color.FromArgb(41, 47, 60) : UITheme.BgCard);

            using (var bgBrush = new SolidBrush(rowBg))
            {
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            }

            // Backloggd row divider: body tr+tr td { border-top: 1px solid var(--back-field-border) }
            using (var dividerPen = new Pen(UITheme.BorderSubtle))
            {
                e.Graphics.DrawLine(dividerPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            // Backloggd pink active bar on selected rows
            if (isSelected)
            {
                using (var pinkBrush = new SolidBrush(UITheme.AccentPinkLight))
                {
                    e.Graphics.FillRectangle(pinkBrush, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height - 1);
                }
            }
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            bool isFirstCol = (e.ColumnIndex == 0);
            Color textColor;

            if (isFirstCol)
            {
                textColor = UITheme.TextWhite;
            }
            else if (e.ColumnIndex == Columns.Count - 1 || e.ColumnIndex == Columns.Count - 2)
            {
                textColor = e.Item.ForeColor;
            }
            else
            {
                textColor = UITheme.TextPrimary;
            }

            Rectangle textRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
            Font fontToUse = isFirstCol
                ? new Font(this.Font.FontFamily, 9.5f, FontStyle.Bold)
                : this.Font;

            try
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    e.SubItem.Text ?? "",
                    fontToUse,
                    textRect,
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
            finally
            {
                if (isFirstCol && fontToUse != this.Font)
                {
                    fontToUse.Dispose();
                }
            }
        }
    }
}
