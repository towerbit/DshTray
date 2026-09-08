using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    static class Program
    {
        // Release 下建议声明为静态变量，
        // 或者 GC.KeepAlive(x)，否则可
        // 能会被垃圾回收，导致互斥体失效
        private static Mutex _mutex; // 用于确保单实例运行

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            #region 检查 DSH 是否已本地安装
            bool isInstalled = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dsh",
                    Arguments = "--version",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (var process = Process.Start(psi))
                {
                    process.WaitForExit(3000);
                    isInstalled = process.ExitCode == 0;
                }
            }
            catch { }
            if (!isInstalled)
            {
                MessageBox.Show("未检测到 DeepSeek Harness 安装。\n\n请访问 https://github.com/deepseek-ai/deepseek-harness 获取安装说明。",
                        "DeepSeek Harness 未安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();
                return;
            }
            #endregion

            #region 检查是否已有实例在运行
            bool createdNew;
            _mutex = new Mutex(true, "DshTray_SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 如果已有实例在运行，则通过命名管道发送消息给已运行的实例，通知它打开窗口
                try
                {
                    using (var pipeClient = new NamedPipeClientStream(".", DshTrayApp.PIPE_NAME, PipeDirection.Out))
                    {
                        pipeClient.Connect(2000);
                        using (var writer = new StreamWriter(pipeClient) { AutoFlush = true })
                        {
                            writer.Write(DshTrayApp.OPEN_MESSAGE);
                        }
                    }
                }
                catch { }
                Application.Exit();
                return;
            }
            #endregion

            Application.Run(new DshTrayApp());
        }
    }
}
