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
    #region 私有字段

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

    #endregion

    #region 状态相关字段

    private OsuMemoryStatus _currentOsuStatus = OsuMemoryStatus.NotRunning;
    private BeatmapIdentifier? _currentBeatmap;
    private int _lastPlayTime;
    private int _currentMods;
    private int _currentCombo;
    private long _currentScore;
    private string _playerName = string.Empty;
    private bool _isReplay;

    #endregion

    #region 公共属性

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

    public MemoryReadObject MemoryReadObject => _memoryReadObject;

    #endregion

    #region 事件

    public event EventHandler<TosuConnectionState>? ConnectionStateChanged;

    #endregion

    #region 构造函数

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

    #endregion

    #region 公共方法

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

    #endregion

    #region 私有方法 - 初始化相关

    private void InitializeValueHandlers()
    {
        // 添加路径处理器，只需在构造函数中注册一次
        _valueHandlers.AddHandler("state.number", (ref Utf8JsonReader r, out OsuMemoryStatus value) =>
        {
            if (r.TokenType == JsonTokenType.Number)
            {
                value = ConvertStateToOsuStatus(r.GetInt64());
                return true;
            }

            value = OsuMemoryStatus.NotRunning;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _currentOsuStatus == value) return;
            _currentOsuStatus = value;
            _memoryReadObject.OsuStatus = value;
        });

        _valueHandlers.AddHandler("directPath.beatmapFolder", (ref Utf8JsonReader r, out string value) =>
        {
            if (r.TokenType == JsonTokenType.String)
            {
                value = r.GetString() ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }, (value, hasValue) =>
        {
            // 处理在beatmapFile处理程序中完成
        });

        _valueHandlers.AddHandler("directPath.beatmapFile", (ref Utf8JsonReader r, out string value) =>
        {
            if (r.TokenType == JsonTokenType.String)
            {
                string? fullPath = r.GetString();
                if (!string.IsNullOrEmpty(fullPath))
                {
                    value = Path.GetFileName(fullPath);
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || string.IsNullOrEmpty(value)) return;

            string folder = _valueHandlers.GetLastProcessedValue<string>("directPath.beatmapFolder");
            var newBeatmap = new BeatmapIdentifier(folder, value);
            if (newBeatmap.Equals(_currentBeatmap)) return;

            _currentBeatmap = newBeatmap;
            _memoryReadObject.BeatmapIdentifier = newBeatmap;
        });

        _valueHandlers.AddHandler("settings.replayUIVisible", (ref Utf8JsonReader r, out bool value) =>
        {
            if (r.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                value = r.GetBoolean();
                return true;
            }

            value = false;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _isReplay == value) return;
            _isReplay = value;
            _memoryReadObject.IsReplay = value;
        });

        _valueHandlers.AddHandler("play.playerName", (ref Utf8JsonReader r, out string value) =>
        {
            if (r.TokenType == JsonTokenType.String)
            {
                value = r.GetString() ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || string.IsNullOrEmpty(value) || _playerName == value) return;
            _playerName = value;
            _memoryReadObject.PlayerName = value;
        });

        _valueHandlers.AddHandler("play.score", (ref Utf8JsonReader r, out long value) =>
        {
            if (r.TokenType == JsonTokenType.Number)
            {
                value = r.GetInt64();
                return true;
            }

            value = 0;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _currentScore == value) return;
            _currentScore = value;
            _memoryReadObject.Score = value;
        });

        _valueHandlers.AddHandler("play.mods.number", (ref Utf8JsonReader r, out int value) =>
        {
            if (r.TokenType == JsonTokenType.Number)
            {
                value = (int)r.GetInt64();
                return true;
            }

            value = 0;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _currentMods == value) return;
            _currentMods = value;
            _memoryReadObject.Mods = (Mods)value;
        });

        _valueHandlers.AddHandler("play.combo.current", (ref Utf8JsonReader r, out int value) =>
        {
            if (r.TokenType == JsonTokenType.Number)
            {
                value = r.GetInt32();
                return true;
            }

            value = 0;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _currentCombo == value) return;
            _currentCombo = value;
            _memoryReadObject.Combo = value;
        });

        _valueHandlers.AddHandler("beatmap.time.live", (ref Utf8JsonReader r, out int value) =>
        {
            if (r.TokenType == JsonTokenType.Number)
            {
                value = r.GetInt32();
                return true;
            }

            value = 0;
            return false;
        }, (value, hasValue) =>
        {
            if (!hasValue || _lastPlayTime == value) return;
            _lastPlayTime = value;
            _memoryReadObject.PlayingTime = value;
        });
    }

    #endregion

    #region 私有方法 - 事件处理

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

    #endregion

    #region 私有方法 - 数据处理

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

    #endregion

    #region 测试相关方法

    internal void ParseAndProcessMessageForTest(ReadOnlyMemory<byte> messageData)
    {
        ParseAndProcessMessage(messageData);
    }

    internal (OsuMemoryStatus Status, string PlayerName, long Score, int Combo, int PlayTime, string? BeatmapFolder,
        string? BeatmapFile) GetCurrentValuesForTest()
    {
        string? folder = _currentBeatmap?.Folder;
        string? file = _currentBeatmap?.Filename;
        return (_currentOsuStatus, _playerName, _currentScore, _currentCombo, _lastPlayTime, folder, file);
    }

    #endregion

    #region IDisposable实现

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

    #endregion
}