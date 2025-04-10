using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu进程管理器
/// </summary>
internal class TosuProcessManager : IDisposable, IAsyncDisposable
{
    private const string ServerStartedPattern = @"\[server\] Dashboard started on http://127\.0\.0\.1:(\d+)";
    
    private Process? _process;
    private readonly string _tosuPath;
    private readonly ILogger _logger;
    private bool _isReady;
    private int? _serverPort;
    private bool _disposedValue;

    /// <summary>
    /// 创建一个tosu进程管理器
    /// </summary>
    /// <param name="tosuPath">tosu可执行文件路径</param>
    /// <param name="logger">日志记录器</param>
    public TosuProcessManager(string tosuPath, ILogger logger)
    {
        _tosuPath = tosuPath;
        _logger = logger;
    }

    /// <summary>
    /// 进程准备就绪事件
    /// </summary>
    public event EventHandler<int>? ProcessReady;

    /// <summary>
    /// 进程退出事件
    /// </summary>
    public event EventHandler? ProcessExited;

    /// <summary>
    /// 启动tosu进程
    /// </summary>
    public Task StartAsync()
    {
        if (_process != null)
        {
            _logger.LogInformation("tosu进程已经在运行");
            return Task.CompletedTask;
        }

        _isReady = false;
        _serverPort = null;

        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _tosuPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += OnProcessOutputDataReceived;
            _process.ErrorDataReceived += OnProcessErrorDataReceived;
            _process.Exited += OnProcessExited;

            _logger.LogInformation($"正在启动tosu进程，路径: {_tosuPath}");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动tosu进程失败");
            throw;
        }
    }

    /// <summary>
    /// 停止tosu进程
    /// </summary>
    public async Task StopAsync()
    {
        if (_process == null || _process.HasExited)
        {
            return;
        }

        try
        {
            _logger.LogInformation("正在停止tosu进程");
            
            // 尝试正常关闭
            _process.CloseMainWindow();
            
            // 给进程一些时间来正常关闭
            await Task.Delay(1000);
            
            // 如果进程仍在运行，则强制终止
            if (!_process.HasExited)
            {
                _process.Kill();
            }
            
            await Task.Delay(500); // 等待进程完全退出
            
            _isReady = false;
            _serverPort = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止tosu进程时出错");
        }
    }

    /// <summary>
    /// 重启tosu进程
    /// </summary>
    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    private void OnProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
        {
            return;
        }

        _logger.LogDebug($"[tosu] {e.Data}");

        // 检查服务器是否已启动
        var match = Regex.Match(e.Data, ServerStartedPattern);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int port))
        {
            _serverPort = port;
            _isReady = true;
            _logger.LogInformation($"tosu服务器已启动，端口: {port}");
            ProcessReady?.Invoke(this, port);
        }
    }

    private void OnProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            _logger.LogError($"[tosu-error] {e.Data}");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogInformation("tosu进程已退出");
        _isReady = false;
        _serverPort = null;
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.Kill();
                        }
                        catch
                        {
                            // 忽略关闭时的错误
                        }
                    }

                    _process.OutputDataReceived -= OnProcessOutputDataReceived;
                    _process.ErrorDataReceived -= OnProcessErrorDataReceived;
                    _process.Exited -= OnProcessExited;
                    _process.Dispose();
                    _process = null;
                }
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Dispose(true);
        GC.SuppressFinalize(this);
    }
} 