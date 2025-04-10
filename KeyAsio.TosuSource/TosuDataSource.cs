using System.Text;
using System.Text.Json;
using KeyAsio.MemoryReading;
using KeyAsio.MemoryReading.Logging;
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
            var all = Encoding.UTF8.GetString(messageData.Span);
            var reader = new Utf8JsonReader(messageData.Span);

            OsuMemoryStatus? newStatus = null;
            string? beatmapFolder = null;
            string? beatmapFile = null;
            bool? isReplayMode = null;
            int? mods = null;
            string? playerName = null;
            long? score = null;
            int? combo = null;
            int? playTime = null;

            // 开始解析JSON
            while (reader.Read())
            {
                // 只处理属性名称
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                // 使用ValueSpan直接比较属性名，避免字符串分配
                ReadOnlySpan<byte> propertyName = reader.ValueSpan;

                // 跳到属性值
                reader.Read();

                if (propertyName.SequenceEqual("state"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // 处理state对象
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("number"u8))
                            {
                                reader.Read(); // 移到number值
                                if (reader.TokenType == JsonTokenType.Number)
                                {
                                    long stateNumber = reader.GetInt64();
                                    newStatus = ConvertStateToOsuStatus(stateNumber);
                                }
                            }
                        }
                    }
                }
                else if (propertyName.SequenceEqual("directPath"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // 处理directPath对象
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName)
                            {
                                ReadOnlySpan<byte> pathPropertyName = reader.ValueSpan;
                                reader.Read(); // 移到属性值

                                if (pathPropertyName.SequenceEqual("beatmapFolder"u8) && reader.TokenType == JsonTokenType.String)
                                {
                                    beatmapFolder = reader.GetString();
                                }
                                else if (pathPropertyName.SequenceEqual("beatmapFile"u8) && reader.TokenType == JsonTokenType.String)
                                {
                                    string fullPath = reader.GetString();
                                    if (!string.IsNullOrEmpty(fullPath))
                                    {
                                        beatmapFile = Path.GetFileName(fullPath);
                                    }
                                }
                            }
                        }
                    }
                }
                else if (propertyName.SequenceEqual("settings"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // 处理settings对象
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("replayUIVisible"u8))
                            {
                                reader.Read(); // 移到replayUIVisible值
                                if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                                {
                                    //isReplayMode = reader.GetBoolean();
                                }
                            }
                        }
                    }
                }
                else if (propertyName.SequenceEqual("play"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // 处理play对象
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName)
                            {
                                ReadOnlySpan<byte> playPropertyName = reader.ValueSpan;
                                reader.Read(); // 移到属性值

                                if (playPropertyName.SequenceEqual("mode"u8) && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理mode对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                    }
                                }
                                else if (playPropertyName.SequenceEqual("healthBar"u8) && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理healthBar对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                    }
                                }
                                else if (playPropertyName.SequenceEqual("hits"u8) && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理hits对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                    }
                                }
                                else if (playPropertyName.SequenceEqual("playerName"u8) && reader.TokenType == JsonTokenType.String)
                                {
                                    playerName = reader.GetString();
                                }
                                else if (playPropertyName.SequenceEqual("score"u8) && reader.TokenType == JsonTokenType.Number)
                                {
                                    score = reader.GetInt64();
                                }
                                else if (playPropertyName.SequenceEqual("mods"u8) && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理mods对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                        if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("number"u8))
                                        {
                                            reader.Read(); // 移到number值
                                            if (reader.TokenType == JsonTokenType.Number)
                                            {
                                                mods = (int)reader.GetInt64();
                                            }
                                        }
                                    }
                                }
                                else if (playPropertyName.SequenceEqual("combo"u8) && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理combo对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                        if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("current"u8))
                                        {
                                            reader.Read(); // 移到current值
                                            if (reader.TokenType == JsonTokenType.Number)
                                            {
                                                combo = reader.GetInt32();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (propertyName.SequenceEqual("beatmap"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // 处理beatmap对象
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("time"u8))
                            {
                                reader.Read(); // 移到time对象
                                if (reader.TokenType == JsonTokenType.StartObject)
                                {
                                    // 处理time对象
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                        if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.SequenceEqual("live"u8))
                                        {
                                            reader.Read(); // 移到live值
                                            if (reader.TokenType == JsonTokenType.Number)
                                            {
                                                playTime = reader.GetInt32();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 更新MemoryReadObject
            if (newStatus.HasValue && _currentOsuStatus != newStatus.Value)
            {
                _currentOsuStatus = newStatus.Value;
                _memoryReadObject.OsuStatus = newStatus.Value;
            }

            if (!string.IsNullOrEmpty(beatmapFolder) && !string.IsNullOrEmpty(beatmapFile))
            {
                var newBeatmap = new BeatmapIdentifier(beatmapFolder, beatmapFile);
                if (!newBeatmap.Equals(_currentBeatmap))
                {
                    _currentBeatmap = newBeatmap;
                    _memoryReadObject.BeatmapIdentifier = newBeatmap;
                }
            }

            if (isReplayMode.HasValue && _isReplay != isReplayMode.Value)
            {
                _isReplay = isReplayMode.Value;
                _memoryReadObject.IsReplay = isReplayMode.Value;
            }

            if (mods.HasValue && _currentMods != mods.Value)
            {
                _currentMods = mods.Value;
                _memoryReadObject.Mods = (Mods)_currentMods;
            }

            if (!string.IsNullOrEmpty(playerName) && _playerName != playerName)
            {
                _playerName = playerName;
                _memoryReadObject.PlayerName = _playerName;
            }

            if (score.HasValue && _currentScore != score.Value)
            {
                _currentScore = score.Value;
                _memoryReadObject.Score = _currentScore;
            }

            if (combo.HasValue && _currentCombo != combo.Value)
            {
                _currentCombo = combo.Value;
                _memoryReadObject.Combo = _currentCombo;
            }

            if (playTime.HasValue && _lastPlayTime != playTime.Value)
            {
                _lastPlayTime = playTime.Value;
                _memoryReadObject.PlayingTime = _lastPlayTime;
            }
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