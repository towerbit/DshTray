using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DshTray
{
    public class DshTrayApp : ApplicationContext
    {
        internal const string PIPE_NAME = "DshTray_OpenWindow_Pipe";
        internal const string OPEN_MESSAGE = "OPEN";

        private readonly string _appTitle = getAppTitle();
        private NotifyIcon _notifyIcon;
        private string _appUrl = string.Empty;
        private CancellationTokenSource _pipeServerCts;
        private NamedPipeServerStream _pipeServer;

        public DshTrayApp()
        {
            InitializeComponent();
            StartPipeServer();
            StopDsh();
            StartDsh();
            OpenBrowser();
        }

        private static string getAppTitle()
        {
            object[] attrs = Assembly.GetExecutingAssembly()
                                     .GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
            if (attrs.Length > 0)
            {
                var titleAttr = (AssemblyTitleAttribute)attrs[0];
                string title = titleAttr.Title;  // AssemblyInfo.cs 中的 AssemblyTitle
                return title;
            }
            return "DeepSeek Harness Tray";
        }

        private void InitializeComponent()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("打开窗口", null, (s, e) => OpenBrowser());
            contextMenu.Items.Add("重启服务", null, (s, e) => RestartDsh());
            contextMenu.Items.Add("清除缓存", null, (s, e) => ClearCache());
            contextMenu.Items.Add("关于", null, (s, e) => ShowAbout());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => ExitApp());

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                //Text = _appTitle,
                BalloonTipTitle = _appTitle,
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            _notifyIcon.DoubleClick += (s, e) => OpenBrowser();
        }

        private void ShowAbout()
        {
            var sbVersion = new System.Text.StringBuilder();

            // 1. 获取 DshTray 版本号
            var trayVersion = Assembly.GetExecutingAssembly().GetName().Version;
            sbVersion.AppendLine($"DshTray : {trayVersion}");

            // 2. dsh --version 获取主进程版本号
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = "/c dsh --version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    var error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(5000);

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        sbVersion.AppendLine($"DeepSeek Harness: {output.Trim()}");
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        sbVersion.AppendLine($"DeepSeek Harness: {error.Trim()}");
                    }
                    else
                    {
                        sbVersion.AppendLine("DeepSeek Harness: 未知版本");
                    }
                }
            }
            catch
            {
                sbVersion.AppendLine("DeepSeek Harness: 获取版本失败");
            }

            sbVersion.AppendLine();
            sbVersion.AppendLine("访问 https://github.com/towerbit/DshTray 了解更多信息");

            _notifyIcon.ShowBalloonTip(6000, "",
                 sbVersion.ToString().Trim(), ToolTipIcon.Info);
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
                // 故意设置一个较长的气泡提示时间，以便用户知道服务正在启动
                _notifyIcon.ShowBalloonTip(60000, "",
                    "dsh web 服务正在启动，请稍候...", ToolTipIcon.Info);
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c dsh web --no-open --port {Properties.Settings.Default.WebPort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var dshProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                dshProcess.Start();

                _appUrl = string.Empty;
                try
                {
                    var readTask = Task.Factory.StartNew((Action)(() =>
                    {
                        try
                        {
                            while (!dshProcess.HasExited)
                            {
                                var line = dshProcess.StandardOutput.ReadLine();
                                if (line == null)
                                {
                                    break;
                                }

                                Debug.Print("DSH: " + line);

                                if (line.Contains("http://127.0.0.1:") && line.Contains("?token="))
                                {
                                    var trimmed = line.Trim();
                                    var httpIndex = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
                                    if (httpIndex >= 0)
                                    {
                                        var urlPart = trimmed.Substring(httpIndex);
                                        var spaceIndex = urlPart.IndexOf(' ');
                                        if (spaceIndex >= 0)
                                        {
                                            urlPart = urlPart.Substring(0, spaceIndex);
                                        }

                                        _appUrl = urlPart;
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }), TaskCreationOptions.LongRunning);
                    while(string.IsNullOrEmpty(_appUrl) && !readTask.IsCompleted)
                    {
                        Application.DoEvents();
                    }
                    
                    // 通过隐藏和显示 NotifyIcon 间接关闭气泡提示
                    _notifyIcon.Visible = false;
                    Application.DoEvents();
                    _notifyIcon.Visible = true;
                }
                catch(Exception ex)
                {
                    Debug.Print(ex.Message);
                }
            }
            catch (Win32Exception ex)
            {
                _notifyIcon.ShowBalloonTip(3000, "",
                    "dsh web 服务启动出错: " + ex.Message, ToolTipIcon.Error);
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
                        if (line.Contains("LISTENING"))
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
                                    
                                    _notifyIcon.ShowBalloonTip(2000, "",
                                        "dsh web 服务已停止", ToolTipIcon.Info);
                                }
                                catch (Exception ex)
                                {
                                    _notifyIcon.ShowBalloonTip(2000, "",
                                        "dsh web 服务停止出错：" + ex.Message, ToolTipIcon.Error);
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

        /// <summary>
        /// 启动命名管道服务，接收第二个实例发来的 OPEN 消息
        /// </summary>
        private void StartPipeServer()
        {
            _pipeServerCts = new CancellationTokenSource();
            var token = _pipeServerCts.Token;
            Task.Run(() => PipeServerLoopAsync(token), token);
        }

        private async Task PipeServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                        PIPE_NAME, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        _pipeServer = server;
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                        string message;
                        using (var reader = new StreamReader(server))
                        {
                            message = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }

                        if (!string.IsNullOrWhiteSpace(message) &&
                            message.Trim().Equals(OPEN_MESSAGE, StringComparison.OrdinalIgnoreCase))
                        {
                            OpenBrowser();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 管道被中断或创建失败，短暂等待后重试
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    await Task.Delay(200)
                              .ConfigureAwait(false);
                }
            }
        }

        private const string APP_ID = "deepseek-harness-web";
        /// <summary>
        /// 单独的 Edge 用户数据目录，用于存储 PWA 的配置和状态，
        /// 主要用于查找和关闭窗口，避免影响其他的 Edge 浏览器实例
        /// </summary>
        private readonly string PROFILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            $"{Application.ProductName}\\Profile");

        private void OpenBrowser()
        {
            if (!string.IsNullOrEmpty(_appUrl))
            {
                // 优先尝试使用 Edge PWA 方式打开
                var edgePath = FindEdgeBrowser();
                if (!string.IsNullOrEmpty(edgePath))
                {
                    try
                    {
                        // Edge PWA 模式参数
                        var args = $"--app={_appUrl} --app-id={APP_ID} --user-data-dir=\"{PROFILE_PATH}\"";
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
                    Process.Start(_appUrl);
                }
                catch { }
            }
            else
            {
                Debug.Print("WARN : _appUrl IsNullOrEmpty ");
            }
        }

        /// <summary>
        /// 关闭 Edge PWA 窗口
        /// </summary>
        private void CloseBrowser()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='msedge.exe'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var cmd = obj["CommandLine"] as string ?? string.Empty;
                    Debug.Print(cmd);
                    if (cmd.Contains($"--app-id={APP_ID}") &&
                        cmd.Contains($"--user-data-dir=\"{PROFILE_PATH}\""))
                    {
                        if (int.TryParse(obj["ProcessId"]?.ToString(), out int pid))
                        {
                            try
                            {
                                var p = Process.GetProcessById(pid);
                                if (!p.HasExited)
                                    p.Kill();
                            }
                            catch 
                            { 
                                /* 忽略已退出或无权限 */ 
                                Debug.Print($"WARN : CloseBrowser 无法终止进程 {pid}");
                            }
                        }
                    }
                }
            }
            catch 
            { 
                /* WMI 不可用时就没办法了，忽略 */ 
                Debug.Print("WARN : CloseBrowser WMI 查询失败");
            }
        }

        private void ClearCache()
        {
            CloseBrowser();

            try
            {
                if (Directory.Exists(PROFILE_PATH))
                {
                    Directory.Delete(PROFILE_PATH, true);
                    _notifyIcon.ShowBalloonTip(2000, "",
                        "缓存已清除", ToolTipIcon.Info);
                }
                else
                {
                    _notifyIcon.ShowBalloonTip(2000, "",
                        "缓存目录不存在，无需清除", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                _notifyIcon.ShowBalloonTip(3000, "",
                    "清除缓存失败: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private void RestartDsh()
        {
            StopDsh();
            StartDsh();
        }

        private void ExitApp()
        {
            StopDsh();
            CloseBrowser();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            // 删除用户数据目录可能会导致下次启动 Edge 变慢，暂时不删除
            // try { Directory.Delete(PROFILE_PATH); } catch { }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pipeServerCts != null)
                {
                    _pipeServerCts.Cancel();
                    // net462 下 WaitForConnectionAsync 对取消令牌响应不可靠，
                    // 同时释放管道以中断等待
                    try { _pipeServer?.Dispose(); } catch { }
                }

                if (_notifyIcon != null) _notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
