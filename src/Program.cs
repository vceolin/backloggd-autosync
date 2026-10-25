using System;
using System.Threading;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\BackloggdAutoSync_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Backloggd AutoSync is already running! Check your Windows System Tray (bottom-right corner).",
                        "Backloggd AutoSync",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
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
