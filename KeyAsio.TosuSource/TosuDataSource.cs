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

            // 创建值处理器映射
            var valueHandlers = new ValueHandlerMapping();
            
            // 添加路径处理器
            valueHandlers.AddHandler("state.number", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.Number)
                {
                    long stateNumber = r.GetInt64();
                    newStatus = ConvertStateToOsuStatus(stateNumber);
                }
                return true;
            });
            
            valueHandlers.AddHandler("directPath.beatmapFolder", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.String)
                {
                    beatmapFolder = r.GetString();
                }
                return true;
            });
            
            valueHandlers.AddHandler("directPath.beatmapFile", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.String)
                {
                    string fullPath = r.GetString();
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        beatmapFile = Path.GetFileName(fullPath);
                    }
                }
                return true;
            });
            
            valueHandlers.AddHandler("settings.replayUIVisible", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.True || r.TokenType == JsonTokenType.False)
                {
                    isReplayMode = r.GetBoolean();
                }
                return true;
            });
            
            valueHandlers.AddHandler("play.playerName", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.String)
                {
                    playerName = r.GetString();
                }
                return true;
            });
            
            valueHandlers.AddHandler("play.score", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.Number)
                {
                    score = r.GetInt64();
                }
                return true;
            });
            
            valueHandlers.AddHandler("play.mods.number", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.Number)
                {
                    mods = (int)r.GetInt64();
                }
                return true;
            });
            
            valueHandlers.AddHandler("play.combo.current", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.Number)
                {
                    combo = r.GetInt32();
                }
                return true;
            });
            
            valueHandlers.AddHandler("beatmap.time.live", (ref Utf8JsonReader r) => {
                if (r.TokenType == JsonTokenType.Number)
                {
                    playTime = r.GetInt32();
                }
                return true;
            });
            
            // 解析JSON并处理注册的路径
            JsonHelpers.ParseDocument(ref reader, valueHandlers);

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

    /// <summary>
    /// JSON辅助类，提供处理JSON的静态方法
    /// </summary>
    private static class JsonHelpers
    {
        /// <summary>
        /// 委托定义：处理指定JSON路径的值
        /// </summary>
        /// <param name="reader">JSON读取器，定位在值上</param>
        /// <returns>如果处理成功返回true</returns>
        public delegate bool ValueHandler(ref Utf8JsonReader reader);

        /// <summary>
        /// 解析整个JSON文档，将值分派给相应的处理器
        /// </summary>
        /// <param name="reader">JSON读取器</param>
        /// <param name="handlers">值处理器映射</param>
        public static void ParseDocument(ref Utf8JsonReader reader, ValueHandlerMapping handlers)
        {
            // 创建路径追踪器
            var pathTracker = new JsonPathTracker();
            ParseElement(ref reader, handlers, pathTracker, false);
        }

        /// <summary>
        /// 解析JSON元素（对象或数组）
        /// </summary>
        /// <param name="reader">JSON读取器</param>
        /// <param name="handlers">值处理器映射</param>
        /// <param name="pathTracker">JSON路径追踪器</param>
        /// <param name="skipStartToken">是否跳过起始标记（对嵌套调用有用）</param>
        private static void ParseElement(ref Utf8JsonReader reader, ValueHandlerMapping handlers, JsonPathTracker pathTracker, bool skipStartToken)
        {
            if (!skipStartToken && !reader.Read())
                return;

            // 确定是否为对象（否则为数组）
            bool isObject = reader.TokenType == JsonTokenType.StartObject;
            
            // 数组索引（仅用于数组）
            int arrayIndex = 0;
            
            // 当前属性名（仅用于对象）
            ReadOnlySpan<byte> currentProperty = default;

            int depth = 1; // 开始深度为1（已经在对象/数组内部）
            while (depth > 0 && reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        currentProperty = reader.ValueSpan;
                        if (isObject)
                        {
                            pathTracker.PushProperty(currentProperty);
                        }
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        depth++;
                        if (isObject && reader.TokenType == JsonTokenType.StartObject)
                        {
                            // 只对对象内的对象递归解析
                            ParseElement(ref reader, handlers, pathTracker, true);
                            depth--; // 减少深度，因为递归已经处理了这个对象
                        }
                        else if (isObject && reader.TokenType == JsonTokenType.StartArray)
                        {
                            // 跳过数组，暂时不处理
                            SkipValue(ref reader);
                            depth--; // 减少深度，因为SkipValue已经处理了这个数组
                        }
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        depth--;
                        if (depth == 0 && isObject)
                        {
                            pathTracker.Pop(); // 退出当前对象，弹出最后一个属性
                        }
                        break;

                    default:
                        // 处理值
                        if (isObject)
                        {
                            // 尝试查找处理器并处理当前路径
                            if (handlers.TryGetHandler(pathTracker, out var handler))
                            {
                                handler(ref reader);
                            }
                            pathTracker.Pop(); // 值处理完后弹出属性
                        }
                        else
                        {
                            // 数组元素处理（如果需要）
                            arrayIndex++;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 跳过任意JSON值（对象、数组或基本类型）
        /// </summary>
        /// <param name="reader">JSON读取器</param>
        public static void SkipValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
            {
                int depth = 1;
                
                while (depth > 0 && reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                    {
                        depth++;
                    }
                    else if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
                    {
                        depth--;
                    }
                }
            }
            // 原始值类型不需要额外跳过操作
        }
    }

    /// <summary>
    /// JSON路径追踪器，用于高效追踪当前的JSON路径而不创建字符串
    /// </summary>
    private class JsonPathTracker
    {
        // 最大支持的路径深度
        private const int MaxDepth = 16;
        
        // 存储路径部分的数组，使用字节数组而不是ReadOnlyMemory
        private readonly byte[][] _pathParts = new byte[MaxDepth][];
        
        // 存储每个路径部分的长度
        private readonly int[] _pathLengths = new int[MaxDepth];
        
        // 预分配的临时缓冲区，用于复制属性名
        private readonly byte[] _tempBuffer = new byte[256]; // 假设大多数属性名不会超过256字节
        
        // 当前路径深度
        private int _depth = 0;

        /// <summary>
        /// 将属性名推入路径栈
        /// </summary>
        /// <param name="propertyName">属性名</param>
        public void PushProperty(ReadOnlySpan<byte> propertyName)
        {
            if (_depth < MaxDepth)
            {
                // 确保我们有足够的空间来存储这个属性名
                int length = propertyName.Length;
                if (length <= _tempBuffer.Length)
                {
                    // 使用预分配的临时缓冲区
                    propertyName.CopyTo(_tempBuffer);
                    
                    // 如果当前深度没有分配数组，或者数组太小，则分配新数组
                    if (_pathParts[_depth] == null || _pathParts[_depth].Length < length)
                    {
                        _pathParts[_depth] = new byte[length];
                    }
                    
                    // 复制到存储数组
                    propertyName.CopyTo(_pathParts[_depth].AsSpan(0, length));
                    _pathLengths[_depth] = length;
                    _depth++;
                }
            }
        }

        /// <summary>
        /// 弹出最后一个路径部分
        /// </summary>
        public void Pop()
        {
            if (_depth > 0)
            {
                _depth--;
            }
        }

        /// <summary>
        /// 获取当前路径的深度
        /// </summary>
        public int Depth => _depth;

        /// <summary>
        /// 获取指定索引处的路径部分
        /// </summary>
        /// <param name="index">索引</param>
        /// <returns>路径部分</returns>
        public ReadOnlySpan<byte> this[int index] => 
            index < _depth && _pathParts[index] != null ? 
            new ReadOnlySpan<byte>(_pathParts[index], 0, _pathLengths[index]) : 
            default;
    }

    /// <summary>
    /// 值处理器映射，高效存储和检索路径处理器
    /// </summary>
    private class ValueHandlerMapping
    {
        // 路径部分的最大长度
        private const int MaxPathPartLength = 64;
        
        // 存储路径处理器的数组
        private readonly List<PathHandlerEntry> _handlers = new List<PathHandlerEntry>();
        
        // 预分配的缓冲区，用于转换路径部分
        private readonly byte[] _byteBuffer = new byte[MaxPathPartLength];

        /// <summary>
        /// 添加路径处理器
        /// </summary>
        /// <param name="path">点分隔的路径，如 "state.number"</param>
        /// <param name="handler">处理器</param>
        public void AddHandler(string path, JsonHelpers.ValueHandler handler)
        {
            if (string.IsNullOrEmpty(path) || handler == null)
                return;

            // 解析路径字符串为路径部分
            var parts = path.Split('.');
            var entry = new PathHandlerEntry
            {
                PathParts = new byte[parts.Length][],
                PathLengths = new int[parts.Length],
                Handler = handler
            };
            
            for (int i = 0; i < parts.Length; i++)
            {
                // 获取部分的UTF8字节表示
                int byteCount = Encoding.UTF8.GetBytes(parts[i], _byteBuffer);
                
                // 分配并复制到目标数组
                entry.PathParts[i] = new byte[byteCount];
                Buffer.BlockCopy(_byteBuffer, 0, entry.PathParts[i], 0, byteCount);
                entry.PathLengths[i] = byteCount;
            }
            
            _handlers.Add(entry);
        }

        /// <summary>
        /// 尝试获取指定路径的处理器
        /// </summary>
        /// <param name="pathTracker">路径追踪器</param>
        /// <param name="handler">找到的处理器</param>
        /// <returns>如果找到处理器则返回true</returns>
        public bool TryGetHandler(JsonPathTracker pathTracker, out JsonHelpers.ValueHandler handler)
        {
            handler = null;
            
            foreach (var entry in _handlers)
            {
                if (entry.PathParts.Length == pathTracker.Depth)
                {
                    bool isMatch = true;
                    
                    for (int i = 0; i < entry.PathParts.Length; i++)
                    {
                        ReadOnlySpan<byte> trackerPart = pathTracker[i];
                        ReadOnlySpan<byte> entryPart = new ReadOnlySpan<byte>(entry.PathParts[i], 0, entry.PathLengths[i]);
                        
                        if (!trackerPart.SequenceEqual(entryPart))
                        {
                            isMatch = false;
                            break;
                        }
                    }
                    
                    if (isMatch)
                    {
                        handler = entry.Handler;
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 路径处理器条目
        /// </summary>
        private class PathHandlerEntry
        {
            public byte[][] PathParts { get; set; }
            public int[] PathLengths { get; set; }
            public JsonHelpers.ValueHandler Handler { get; set; }
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