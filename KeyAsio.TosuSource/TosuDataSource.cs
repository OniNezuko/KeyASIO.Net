using System.Text.Json;
using KeyAsio.MemoryReading;
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
    private int _maxCombo;
    private int _currentScore;
    private ushort _hit300;
    private ushort _hit100;
    private ushort _hit50;
    private ushort _hitMiss;
    private ushort _hitGeki;
    private ushort _hitKatu;
    private double _hp;
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
            // 尝试解析为菜单响应
            var menuResponse = JsonSerializer.Deserialize<TosuMenuResponse>(message, _jsonOptions);
            if (menuResponse?.Menu != null)
            {
                ProcessMenuData(menuResponse.Menu);
                return;
            }
        }
        catch { /* 如果不是菜单数据，继续尝试其他格式 */ }

        try
        {
            // 尝试解析为游戏数据响应
            var playResponse = JsonSerializer.Deserialize<TosuPlayResponse>(message, _jsonOptions);
            if (playResponse?.Gameplay != null)
            {
                ProcessGameplayData(playResponse.Gameplay);
                return;
            }
        }
        catch { /* 如果不是游戏数据，忽略 */ }
    }

    private void ProcessMenuData(TosuMenuData menuData)
    {
        // 处理osu状态更新
        var newStatus = TosuDataConverter.ConvertMenuStateToOsuStatus(menuData.State);
        if (_currentOsuStatus != newStatus)
        {
            var oldStatus = _currentOsuStatus;
            _currentOsuStatus = newStatus;
            _memoryReadObject.OsuStatus = newStatus;
        }

        // 处理谱面信息
        if (menuData.Beatmap != null)
        {
            var bm = menuData.Beatmap;
            var newBeatmap = new BeatmapIdentifier
            {
                //SongTitle = bm.Title ?? string.Empty,
                //SongArtist = bm.Artist ?? string.Empty,
                //Difficulty = bm.Difficulty ?? string.Empty,
                //BeatmapId = bm.Id,
                //BeatmapSetId = bm.SetId,
                //Md5 = bm.Md5 ?? string.Empty,
                //MapFolderName = bm.Path?.Folder ?? string.Empty,
                //MapFileName = bm.Path?.File ?? string.Empty,
                //Ar = bm.Ar,
                //Cs = bm.Cs,
                //Hp = bm.Hp,
                //Od = bm.Od
            };


            if (!newBeatmap.Equals(_currentBeatmap))
            {
                _currentBeatmap = newBeatmap;
                _memoryReadObject.BeatmapIdentifier = newBeatmap;
            }
        }

        // 处理MOD信息
        if (menuData.Mods != null)
        {
            var newMods = menuData.Mods.Num;
            if (_currentMods != newMods)
            {
                _currentMods = newMods;
                _memoryReadObject.Mods = TosuDataConverter.ConvertToMods(_currentMods);
            }
        }
    }

    private void ProcessGameplayData(TosuGameplayData gameplayData)
    {
        // 处理玩家名称
        if (gameplayData.PlayerName != null && _playerName != gameplayData.PlayerName)
        {
            _playerName = gameplayData.PlayerName;
            _memoryReadObject.PlayerName = _playerName;
        }

        // 处理分数
        if (gameplayData.Score != _currentScore)
        {
            _currentScore = gameplayData.Score;
            _memoryReadObject.Score = _currentScore;
        }

        // 处理连击
        if (gameplayData.Combo != null)
        {
            var combo = gameplayData.Combo;

            if (combo.Current != _currentCombo)
            {
                _currentCombo = combo.Current;
                _memoryReadObject.Combo = _currentCombo;
            }

            if (combo.Max != _maxCombo)
            {
                _maxCombo = combo.Max;
                //_memoryReadObject.UpdateMaxCombo(_maxCombo);
            }
        }

        // 处理生命值
        if (gameplayData.Hp != null)
        {
            var newHp = gameplayData.Hp.Normal;
            if (Math.Abs(_hp - newHp) > 0.01)
            {
                _hp = newHp;
                //_memoryReadObject.UpdateHp(_hp);
            }
        }

        // 处理打击数据
        if (gameplayData.Hits != null)
        {
            var hits = gameplayData.Hits;

            if (hits.Hit300 != _hit300)
            {
                _hit300 = (ushort)hits.Hit300;
                //_memoryReadObject.UpdateHit300(_hit300);
            }

            if (hits.Hit100 != _hit100)
            {
                _hit100 = (ushort)hits.Hit100;
                //_memoryReadObject.UpdateHit100(_hit100);
            }

            if (hits.Hit50 != _hit50)
            {
                _hit50 = (ushort)hits.Hit50;
                //_memoryReadObject.UpdateHit50(_hit50);
            }

            if (hits.HitMiss != _hitMiss)
            {
                _hitMiss = (ushort)hits.HitMiss;
                //_memoryReadObject.UpdateHitMiss(_hitMiss);
            }

            if (hits.HitGeki != _hitGeki)
            {
                _hitGeki = (ushort)hits.HitGeki;
                //_memoryReadObject.UpdateHitGeki(_hitGeki);
            }

            if (hits.HitKatu != _hitKatu)
            {
                _hitKatu = (ushort)hits.HitKatu;
                //_memoryReadObject.UpdateHitKatu(_hitKatu);
            }
        }

        // 处理时间
        if (gameplayData.Time != null)
        {
            var time = gameplayData.Time;
            if (time.Current != _lastPlayTime)
            {
                _lastPlayTime = time.Current;
                _memoryReadObject.PlayingTime = _lastPlayTime;
            }
        }

        // 基于回放数据，我们可能无法直接知道是否是回放，但可以基于其他信息推断
        // 简单实现：假设这里我们不改变它的值
        _memoryReadObject.IsReplay = _isReplay;
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