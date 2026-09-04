using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    public class DshTrayApp : ApplicationContext
    {
        private readonly string _appTitle = getAppTitle();
        private NotifyIcon _notifyIcon;
        //private Process _dshProcess;

        public DshTrayApp()
        {
            CheckDshInstalled();
            InitializeComponent();
            StartDsh();
        }

        private static string getAppTitle()
        {
            object[] attrs = Assembly.GetExecutingAssembly()
                                     .GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
            if (attrs.Length > 0)
            {
                var titleAttr = (AssemblyTitleAttribute)attrs[0];
                string title = titleAttr.Title;  // 如 "DshTray"
                return title;
            }
            return "DeepSeek Harness Tray";
        }

        /// <summary>
        /// 检查 DSH 是否已本地安装
        /// </summary>
        private void CheckDshInstalled()
        {
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
            
            if (!isInstalled) {
                MessageBox.Show("未检测到 DeepSeek Harness 安装。\n\n请访问 https://github.com/deepseek-ai/deepseek-harness 获取安装说明。",
                        "DeepSeek Harness 未安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();
            }
        }

        private void InitializeComponent()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("打开窗口", null, (s, e) => OpenBrowser());
            contextMenu.Items.Add("重启服务", null, (s, e) => RestartDsh());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出 DshTray", null, (s, e) => ExitApp());

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = _appTitle,
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            _notifyIcon.DoubleClick += (s, e) => OpenBrowser();
            _notifyIcon.ShowBalloonTip(3000, _appTitle, 
                "服务已启动", ToolTipIcon.Info);
        }


        private Icon LoadIcon()
        {
            try
            {
                return new Icon(Assembly.GetExecutingAssembly()
                                        .GetManifestResourceStream("DshTray.dsh.ico"));
            }
            catch
            {
                return CreateDefaultIcon();
            }
        }
        private Icon CreateDefaultIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(31, 118, 210));
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("D", font, brush, new RectangleF(0, 0, 32, 32), sf);
                }
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }

        private void StartDsh()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    //FileName = "dsh",
                    //Arguments = $"web --no-open --port {Properties.Settings.Default.WebPort}",
                    FileName ="cmd",
                    Arguments = $"/c dsh web --no-open --port {Properties.Settings.Default.WebPort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                //_dshProcess = Process.Start(psi);
                Process.Start(psi);
            }
            catch (Win32Exception ex)
            {
                _notifyIcon.ShowBalloonTip(3000, _appTitle, 
                    "服务启动出错: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private void StopDsh()
        {
            // 通过端口号查找并关闭实际的 node.exe 进程
            int port = Properties.Settings.Default.WebPort;
            try
            {
                var psi = new ProcessStartInfo
                {
                    //FileName = "netstat",
                    //Arguments = "-ano",
                    FileName = "cmd",
                    Arguments = $"/c netstat -ano | findstr \"127.0.0.1:{port} \"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (var proc = Process.Start(psi))
                {
                    var lines = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    foreach (var line in lines.Split('\n'))
                    {
                        // 查找监听指定端口的进程，格式如:
                        //   TCP    127.0.0.1:53080        0.0.0.0:0              LISTENING       6072
                        //if (line.Contains($"127.0.0.1:{port}") && line.Contains("LISTENING"))
                        if(line.Contains("LISTENING"))
                        {
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            var pidStr = parts[parts.Length - 1].Trim();
                            if (int.TryParse(pidStr, out int pid))
                            {
                                try
                                {
                                    var procToKill = Process.GetProcessById(pid);
                                    procToKill.Kill();
                                    Debug.Print($"DBUG : {procToKill.ProcessName} 进程 {pid} 已被终止");
                                    procToKill.WaitForExit(3000);
                                }
                                catch (Exception ex)
                                {
                                    Debug.Print("WARN : " + ex.Message);
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 查找本地安装的 Microsoft Edge 浏览器路径
        /// </summary>
        /// <returns>Edge 可执行文件路径，如果未找到则返回 null</returns>
        private static string FindEdgeBrowser()
        {
            // Edge 常见的安装路径
            string[] edgePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                             @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             @"Microsoft\Edge\Application\msedge.exe"),
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            };

            foreach (var path in edgePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 尝试从注册表查找
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                {
                    var value = key?.GetValue("") as string;
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                        return value;
                }
            }
            catch { }

            // 尝试从注册表查找 (HKCU)
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                {
                    var value = key?.GetValue("") as string;
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                        return value;
                }
            }
            catch { }

            return null;
        }

        internal static void OpenBrowser()
        {
            var appUrl = $"http://127.0.0.1:{Properties.Settings.Default.WebPort}";
            // 优先尝试使用 Edge PWA 方式打开
            var edgePath = FindEdgeBrowser();
            if (!string.IsNullOrEmpty(edgePath))
            {
                try
                {
                    // Edge PWA 模式参数
                    var args = $"--app={appUrl} --app-id=pnnhogighjicmpdjhpopooneegkiocle";
                    var psi = new ProcessStartInfo
                    {
                        FileName = edgePath,
                        Arguments = args,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            // 回退到默认浏览器
            try
            {
                Process.Start(appUrl); 
            } 
            catch { }
        }

        private void RestartDsh()
        {
            StopDsh();
            Thread.Sleep(1000); // 等待一秒钟确保进程已终止

            StartDsh();
            _notifyIcon.ShowBalloonTip(2000, _appTitle, 
                "服务已重启", ToolTipIcon.Info);
        }

        private void ExitApp()
        {
            StopDsh();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _notifyIcon != null) _notifyIcon.Dispose();
            base.Dispose(disposing);
        }
    }
}
