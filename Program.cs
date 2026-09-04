using System;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 检查是否已有实例在运行
            bool createdNew;
            var mutex = new Mutex(true, "DshTray_SingleInstance", out createdNew);
            if (!createdNew)
            {
                var result =MessageBox.Show("DshTray 已有一个实例在运行，是否需要打开窗口？", "提示", 
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result == DialogResult.OK)
                {
                    DshTrayApp.OpenBrowser();
                }
                Application.Exit();
                return;
            }

            Application.Run(new DshTrayApp());
        }
    }
}
