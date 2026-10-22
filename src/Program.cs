using System;
using System.Security.Principal;
using System.Windows.Forms;

namespace PrinterToolkit
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Native.SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show("An unexpected error occurred:\n\n" + e.Exception.Message,
                    "Printer Support Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}
