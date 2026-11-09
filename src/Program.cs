using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    static class Program
    {
        public const string AppShowMessageName = "BackloggdAutoSync_ShowMainWindow";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xffff;

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\BackloggdAutoSync_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    // Existing instance is running. Wake it up and restore its window to the foreground.
                    uint showMsg = RegisterWindowMessage(AppShowMessageName);
                    PostMessage((IntPtr)HWND_BROADCAST, showMsg, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                bool startMinimized = false;
                if (args != null)
                {
                    foreach (string a in args)
                    {
                        if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "-m", StringComparison.OrdinalIgnoreCase))
                        {
                            startMinimized = true;
                        }
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startMinimized));
            }
        }
    }
}
