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
                MessageBox.Show("DSH Web 已有一个实例在运行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Exit();
                return;
            }

            Application.Run(new DshTrayApp());
        }
    }
}
