using System.Text.Json;
using KeyAsio.MemoryReading;
using KeyAsio.TosuSource.Models;
using Microsoft.Extensions.Logging;
using OsuMemoryDataProvider;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu数据源实现
/// </summary>
public class TosuDataSource : ITosuDataSource, IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly TosuDataSourceOptions _options;
    private readonly TosuProcessManager _processManager;
    private readonly TosuWebSocketClient _webSocketClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly MemoryReadObject _memoryReadObject = new();
    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private Task? _dataUpdateTask;
    private volatile TosuConnectionState _connectionState = TosuConnectionState.Disconnected;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private bool _disposedValue;
    private OsuMemoryStatus _currentOsuStatus = OsuMemoryStatus.NotRunning;
    private BeatmapIdentifier? _currentBeatmap;
    private int _lastPlayTime;
    private int _currentMods;
    private int _currentCombo;
    private long _currentScore;
    private string _playerName = string.Empty;

    /// <summary>
    /// 创建tosu数据源
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="logger">日志记录器</param>
    public TosuDataSource(TosuDataSourceOptions options, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrEmpty(_options.TosuExecutablePath) && _options.AutoStartTosuProcess)
        {
            throw new ArgumentException("当AutoStartTosuProcess为true时，TosuExecutablePath不能为空", nameof(options));
        }

        _processManager = new TosuProcessManager(_options.TosuExecutablePath, _logger);
        _webSocketClient = new TosuWebSocketClient(_logger);

        // 设置事件处理
        _processManager.ProcessReady += OnProcessReady;
        _processManager.ProcessExited += OnProcessExited;
        _webSocketClient.ConnectionChanged += OnWebSocketConnectionChanged;
        _webSocketClient.MessageReceived += OnWebSocketMessageReceived;
    }

    /// <summary>
    /// 连接状态
    /// </summary>
    public TosuConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                ConnectionStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// 连接状态改变事件
    /// </summary>
    public event EventHandler<TosuConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// 内存读取对象
    /// </summary>
    public MemoryReadObject MemoryReadObject => _memoryReadObject;

    /// <summary>
    /// 启动tosu数据源
    /// </summary>
    public async Task StartAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (ConnectionState != TosuConnectionState.Disconnected)
            {
                _logger.LogInformation("tosu数据源已经在运行中");
                return;
            }

            // 启动tosu进程
            if (_options.AutoStartTosuProcess)
            {
                ConnectionState = TosuConnectionState.StartingProcess;
                await _processManager.StartAsync();
                // 进程启动后，会触发ProcessReady事件，在那里建立WebSocket连接
            }
            else
            {
                // 不自动启动进程，直接尝试连接WebSocket
                // 假设tosu已经在运行，默认端口为24050
                ConnectionState = TosuConnectionState.Connecting;
                await _webSocketClient.ConnectAsync(24050);
            }
        }
        catch (Exception ex)
        {
            ConnectionState = TosuConnectionState.Error;
            _logger.LogError(ex, "启动tosu数据源时出错");
            throw;
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    /// <summary>
    /// 停止tosu数据源
    /// </summary>
    public async Task StopAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (ConnectionState == TosuConnectionState.Disconnected)
            {
                return;
            }

            _logger.LogInformation("正在停止tosu数据源");

            // 取消数据更新任务
            _cts.Cancel();
            if (_dataUpdateTask != null)
            {
                try
                {
                    await _dataUpdateTask;
                }
                catch (OperationCanceledException)
                {
                    // 预期的取消异常，忽略
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "取消数据更新任务时出错");
                }
            }

            // 断开WebSocket连接
            await _webSocketClient.DisconnectAsync();

            // 停止tosu进程
            if (_options.AutoStartTosuProcess)
            {
                await _processManager.StopAsync();
            }

            ConnectionState = TosuConnectionState.Disconnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止tosu数据源时出错");
            ConnectionState = TosuConnectionState.Error;
            throw;
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private async void OnProcessReady(object? sender, int port)
    {
        try
        {
            _logger.LogInformation($"tosu进程已准备就绪，WebSocket端口: {port}");
            ConnectionState = TosuConnectionState.WaitingForProcess;

            // 给tosu进程一些额外的启动时间
            await Task.Delay(500);

            // 连接到WebSocket
            ConnectionState = TosuConnectionState.Connecting;
            await _webSocketClient.ConnectAsync(port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到tosu WebSocket服务器失败");
            ConnectionState = TosuConnectionState.Error;
        }
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        if (ConnectionState == TosuConnectionState.Disconnected)
        {
            return;
        }

        _logger.LogWarning("tosu进程已退出");

        // 断开WebSocket连接
        await _webSocketClient.DisconnectAsync();

        if (_options.AutoRestartTosuProcess)
        {
            _logger.LogInformation("正在尝试重启tosu进程");
            ConnectionState = TosuConnectionState.StartingProcess;

            try
            {
                await _processManager.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重启tosu进程失败");
                ConnectionState = TosuConnectionState.Error;
            }
        }
        else
        {
            ConnectionState = TosuConnectionState.Disconnected;
        }
    }

    private void OnWebSocketConnectionChanged(object? sender, bool connected)
    {
        if (connected)
        {
            _logger.LogInformation("已连接到tosu WebSocket服务器");
            ConnectionState = TosuConnectionState.Connected;

            // 启动数据更新任务
            _dataUpdateTask = Task.Run(DataUpdateLoopAsync, _cts.Token);
        }
        else
        {
            _logger.LogWarning("与tosu WebSocket服务器的连接已断开");

            if (_webSocketClient.IsReconnecting)
            {
                ConnectionState = TosuConnectionState.Reconnecting;
            }
            else if (_options.AutoRestartTosuProcess && ConnectionState != TosuConnectionState.Disconnected)
            {
                // 如果自动重启选项已启用，尝试重启进程
                Task.Run(async () =>
                {
                    try
                    {
                        await _processManager.RestartAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "重启tosu进程失败");
                        ConnectionState = TosuConnectionState.Error;
                    }
                });
            }
            else
            {
                ConnectionState = TosuConnectionState.Disconnected;
            }
        }
    }

    private void OnWebSocketMessageReceived(object? sender, string message)
    {
        try
        {
            ParseAndProcessMessage(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理WebSocket消息时出错: {message}");
        }
    }

    private void ParseAndProcessMessage(string message)
    {
        try
        {
            // 解析为V2Response
            var response = JsonSerializer.Deserialize<V2Response>(message, _jsonOptions);
            if (response != null)
            {
                ProcessData(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"解析和处理JSON响应时出错");
        }
    }

    private void ProcessData(V2Response response)
    {
        // 处理osu状态更新
        if (response.State != null)
        {
            var newStatus = ConvertStateToOsuStatus(response.State.Number);
            if (_currentOsuStatus != newStatus)
            {
                var oldStatus = _currentOsuStatus;
                _currentOsuStatus = newStatus;
                _memoryReadObject.OsuStatus = newStatus;
            }
        }

        // 处理谱面信息
        if (response.Beatmap != null && response.DirectPath != null)
        {
            var beatmap = response.Beatmap;
            var directPath = response.DirectPath;

            string folder = directPath.BeatmapFolder ?? string.Empty;
            string file = directPath.BeatmapFile != null ? Path.GetFileName(directPath.BeatmapFile) : string.Empty;

            if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(file))
            {
                var newBeatmap = new BeatmapIdentifier(folder, file);

                if (!newBeatmap.Equals(_currentBeatmap))
                {
                    _currentBeatmap = newBeatmap;
                    _memoryReadObject.BeatmapIdentifier = newBeatmap;
                }
            }
        }

        // 处理MOD信息
        if (response.Play?.Mods?.Number != null)
        {
            var newMods = (int)response.Play.Mods.Number;
            if (_currentMods != newMods)
            {
                _currentMods = newMods;
                _memoryReadObject.Mods = (Mods)_currentMods;
            }
        }

        // 处理玩家信息
        if (response.Play != null)
        {
            // 处理玩家名称
            if (!string.IsNullOrEmpty(response.Play.PlayerName) && _playerName != response.Play.PlayerName)
            {
                _playerName = response.Play.PlayerName;
                _memoryReadObject.PlayerName = _playerName;
            }

            // 处理分数
            if (response.Play.Score != _currentScore)
            {
                _currentScore = response.Play.Score;
                _memoryReadObject.Score = _currentScore;
            }

            // 处理连击
            if (response.Play.Combo != null)
            {
                var combo = response.Play.Combo;

                if (combo.Current != _currentCombo)
                {
                    _currentCombo = combo.Current;
                    _memoryReadObject.Combo = _currentCombo;
                }
            }
        }

        // 处理时间
        if (response.Beatmap?.Time != null)
        {
            var time = response.Beatmap.Time;
            if (time.Live != _lastPlayTime)
            {
                _lastPlayTime = time.Live;
                _memoryReadObject.PlayingTime = _lastPlayTime;
            }
        }
    }

    private OsuMemoryStatus ConvertStateToOsuStatus(long stateNumber)
    {
        return stateNumber switch
        {
            0 => OsuMemoryStatus.MainMenu,
            1 => OsuMemoryStatus.EditingMap,
            2 => OsuMemoryStatus.Playing,
            5 => OsuMemoryStatus.SongSelect,
            7 => OsuMemoryStatus.ResultsScreen,
            _ => OsuMemoryStatus.NotRunning
        };
    }

    private async Task DataUpdateLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // 等待指定的更新间隔
                await Task.Delay(_options.UpdateIntervalMs, _cts.Token);

                // 此处不需要主动请求数据，因为tosu会主动推送数据
                // 我们只需要处理接收到的数据
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不需要处理
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据更新循环中发生错误");
            ConnectionState = TosuConnectionState.Error;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
                _startStopLock.Dispose();
                _processManager.ProcessReady -= OnProcessReady;
                _processManager.ProcessExited -= OnProcessExited;
                _webSocketClient.ConnectionChanged -= OnWebSocketConnectionChanged;
                _webSocketClient.MessageReceived -= OnWebSocketMessageReceived;
                _processManager.Dispose();
                _webSocketClient.Dispose();
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

        if (!_disposedValue)
        {
            _cts.Cancel();
            _cts.Dispose();
            _startStopLock.Dispose();
            _processManager.ProcessReady -= OnProcessReady;
            _processManager.ProcessExited -= OnProcessExited;
            _webSocketClient.ConnectionChanged -= OnWebSocketConnectionChanged;
            _webSocketClient.MessageReceived -= OnWebSocketMessageReceived;
            await _processManager.DisposeAsync();
            await _webSocketClient.DisposeAsync();

            _disposedValue = true;
        }

        GC.SuppressFinalize(this);
    }
}