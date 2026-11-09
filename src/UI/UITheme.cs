using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class UITheme
    {
        public static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        public static readonly Color BgCard = Color.FromArgb(31, 34, 43);
        public static readonly Color BgInput = Color.FromArgb(42, 46, 58);
        public static readonly Color AccentPink = Color.FromArgb(234, 55, 122);
        public static readonly Color AccentBlue = Color.FromArgb(72, 133, 237);
        public static readonly Color TextLight = Color.FromArgb(238, 240, 246);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 179);
        public static readonly Color SuccessGreen = Color.FromArgb(46, 204, 113);
        public static readonly Color WarnOrange = Color.FromArgb(243, 156, 18);

        public static Button CreateStyledButton(string text, Color bgColor, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
