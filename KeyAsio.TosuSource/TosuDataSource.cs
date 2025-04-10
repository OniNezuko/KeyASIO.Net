using System.Text;
using System.Text.Json;
using KeyAsio.MemoryReading;
using KeyAsio.MemoryReading.Logging;
using KeyAsio.TosuSource.Json;
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
    private readonly ValueHandlerMapping _valueHandlers;
    private Task? _dataUpdateTask;
    private volatile TosuConnectionState _connectionState = TosuConnectionState.Disconnected;
    private bool _disposedValue;
    private OsuMemoryStatus _currentOsuStatus = OsuMemoryStatus.NotRunning;
    private BeatmapIdentifier? _currentBeatmap;
    private int _lastPlayTime;
    private int _currentMods;
    private int _currentCombo;
    private long _currentScore;
    private string _playerName = string.Empty;
    private bool _isReplay;

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
        _webSocketClient = new TosuWebSocketClient(_logger, _options);

        // 设置事件处理
        _processManager.ProcessReady += OnProcessReady;
        _processManager.ProcessExited += OnProcessExited;
        _webSocketClient.ConnectionChanged += OnWebSocketConnectionChanged;
        _webSocketClient.BinaryMessageReceived += OnWebSocketMessageReceived;
        
        // 初始化JSON值处理器映射
        _valueHandlers = new ValueHandlerMapping();
        InitializeValueHandlers();
    }
    
    /// <summary>
    /// 初始化JSON路径处理器
    /// </summary>
    private void InitializeValueHandlers()
    {
        // 添加路径处理器，只需在构造函数中注册一次
        _valueHandlers.AddHandler("state.number", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.Number)
            {
                long stateNumber = r.GetInt64();
                return (value: ConvertStateToOsuStatus(stateNumber), hasValue: true);
            }
            return (value: OsuMemoryStatus.NotRunning, hasValue: false);
        }, value => {
            if (value.hasValue && _currentOsuStatus != value.value)
            {
                _currentOsuStatus = value.value;
                _memoryReadObject.OsuStatus = value.value;
            }
        });
        
        _valueHandlers.AddHandler("directPath.beatmapFolder", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.String)
            {
                return (value: r.GetString(), hasValue: true);
            }
            return (value: null, hasValue: false);
        }, value => {
            // 处理在beatmapFile处理程序中完成
        });
        
        _valueHandlers.AddHandler("directPath.beatmapFile", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.String)
            {
                string fullPath = r.GetString();
                if (!string.IsNullOrEmpty(fullPath))
                {
                    return (value: Path.GetFileName(fullPath), hasValue: true);
                }
            }
            return (value: null, hasValue: false);
        }, value => {
            // 从已处理的值中获取文件夹和文件名
            string folder = _valueHandlers.GetLastProcessedValue<string>("directPath.beatmapFolder");
            string file = value.value;
            
            if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(file))
            {
                var newBeatmap = new BeatmapIdentifier(folder, file);
                if (!newBeatmap.Equals(_currentBeatmap))
                {
                    _currentBeatmap = newBeatmap;
                    _memoryReadObject.BeatmapIdentifier = newBeatmap;
                }
            }
        });
        
        _valueHandlers.AddHandler("settings.replayUIVisible", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.True || r.TokenType == JsonTokenType.False)
            {
                return (value: r.GetBoolean(), hasValue: true);
            }
            return (value: false, hasValue: false);
        }, value => {
            if (value.hasValue && _isReplay != value.value)
            {
                _isReplay = value.value;
                _memoryReadObject.IsReplay = value.value;
            }
        });
        
        _valueHandlers.AddHandler("play.playerName", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.String)
            {
                return (value: r.GetString(), hasValue: true);
            }
            return (value: null, hasValue: false);
        }, value => {
            if (value.hasValue && !string.IsNullOrEmpty(value.value) && _playerName != value.value)
            {
                _playerName = value.value;
                _memoryReadObject.PlayerName = _playerName;
            }
        });
        
        _valueHandlers.AddHandler("play.score", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.Number)
            {
                return (value: r.GetInt64(), hasValue: true);
            }
            return (value: 0L, hasValue: false);
        }, value => {
            if (value.hasValue && _currentScore != value.value)
            {
                _currentScore = value.value;
                _memoryReadObject.Score = _currentScore;
            }
        });
        
        _valueHandlers.AddHandler("play.mods.number", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.Number)
            {
                return (value: (int)r.GetInt64(), hasValue: true);
            }
            return (value: 0, hasValue: false);
        }, value => {
            if (value.hasValue && _currentMods != value.value)
            {
                _currentMods = value.value;
                _memoryReadObject.Mods = (Mods)_currentMods;
            }
        });
        
        _valueHandlers.AddHandler("play.combo.current", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.Number)
            {
                return (value: r.GetInt32(), hasValue: true);
            }
            return (value: 0, hasValue: false);
        }, value => {
            if (value.hasValue && _currentCombo != value.value)
            {
                _currentCombo = value.value;
                _memoryReadObject.Combo = _currentCombo;
            }
        });
        
        _valueHandlers.AddHandler("beatmap.time.live", (ref Utf8JsonReader r) => {
            if (r.TokenType == JsonTokenType.Number)
            {
                return (value: r.GetInt32(), hasValue: true);
            }
            return (value: 0, hasValue: false);
        }, value => {
            if (value.hasValue && _lastPlayTime != value.value)
            {
                _lastPlayTime = value.value;
                _memoryReadObject.PlayingTime = _lastPlayTime;
            }
        });
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

    private void OnWebSocketMessageReceived(object? sender, ReadOnlyMemory<byte> messageData)
    {
        try
        {
            ParseAndProcessMessage(messageData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理WebSocket二进制消息时出错");
        }
    }

    /// <summary>
    /// 供测试使用的方法，解析并处理WebSocket消息
    /// </summary>
    /// <param name="messageData">二进制消息数据</param>
    internal void ParseAndProcessMessageForTest(ReadOnlyMemory<byte> messageData)
    {
        ParseAndProcessMessage(messageData);
    }

    /// <summary>
    /// 供测试使用的方法，获取当前的值
    /// </summary>
    /// <returns>当前的状态值元组</returns>
    internal (OsuMemoryStatus Status, string PlayerName, long Score, int Combo, int PlayTime, string? BeatmapFolder, string? BeatmapFile) GetCurrentValuesForTest()
    {
        string? folder = _currentBeatmap?.Folder;
        string? file = _currentBeatmap?.Filename;
        return (_currentOsuStatus, _playerName, _currentScore, _currentCombo, _lastPlayTime, folder, file);
    }

    private void ParseAndProcessMessage(ReadOnlyMemory<byte> messageData)
    {
        try
        {
            var reader = new Utf8JsonReader(messageData.Span);

            // 重置处理器值，准备新一轮解析
            _valueHandlers.ResetProcessedValues();
            
            // 解析JSON并处理注册的路径
            JsonHelpers.ParseDocument(ref reader, _valueHandlers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析和处理二进制JSON响应时出错");
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
                _webSocketClient.BinaryMessageReceived -= OnWebSocketMessageReceived;
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
            _webSocketClient.BinaryMessageReceived -= OnWebSocketMessageReceived;
            await _processManager.DisposeAsync();
            await _webSocketClient.DisposeAsync();

            _disposedValue = true;
        }

        GC.SuppressFinalize(this);
    }
}