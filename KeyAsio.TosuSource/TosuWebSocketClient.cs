using System.Net.WebSockets;
using System.Text.Json;
using KeyAsio.MemoryReading.Logging;
using Websocket.Client;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu WebSocket客户端
/// </summary>
internal class TosuWebSocketClient : IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly TosuDataSourceOptions _options;
    private WebsocketClient? _client;
    private Uri? _serverUri;
    private bool _isConnected;
    private bool _isReconnecting;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _reconnectAttempts = 0;

    /// <summary>
    /// 创建tosu WebSocket客户端
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">tosu数据源配置选项</param>
    public TosuWebSocketClient(ILogger logger, TosuDataSourceOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// 连接状态改变事件
    /// </summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// 消息接收事件
    /// </summary>
    public event EventHandler<string>? MessageReceived;

    /// <summary>
    /// 二进制消息接收事件
    /// </summary>
    public event EventHandler<ReadOnlyMemory<byte>>? BinaryMessageReceived;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// 是否正在重连
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// 连接到tosu服务器
    /// </summary>
    /// <param name="port">服务器端口</param>
    public async Task ConnectAsync(int port)
    {
        if (_client != null && _isConnected)
        {
            _logger.LogInformation("WebSocket客户端已连接");
            return;
        }

        _serverUri = new Uri($"ws://127.0.0.1:{port}/websocket/v2");
        _logger.LogInformation($"正在连接到tosu WebSocket服务器: {_serverUri}");

        try
        {
            await InitializeClientAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到tosu WebSocket服务器失败");
            throw;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_client == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("正在断开与tosu WebSocket服务器的连接");
            await _client.Stop(WebSocketCloseStatus.NormalClosure, "Client disconnecting");
            UpdateConnectionState(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开与tosu WebSocket服务器的连接时出错");
        }
    }

    private async Task InitializeClientAsync()
    {
        if (_serverUri == null)
        {
            throw new InvalidOperationException("服务器URI未设置");
        }

        _client?.Dispose();

        _client = new WebsocketClient(_serverUri)
        {
            IsReconnectionEnabled = false, // 我们将手动处理重连
            ErrorReconnectTimeout = TimeSpan.FromSeconds(5),
            IsTextMessageConversionEnabled = false
        };

        // 设置事件处理程序
        _client.ReconnectionHappened.Subscribe(OnReconnectionHappened);
        _client.DisconnectionHappened.Subscribe(OnDisconnectionHappened);
        _client.MessageReceived.Subscribe(OnMessageReceived);

        // 开始连接
        await _client.Start();
        UpdateConnectionState(_client.IsRunning);
    }

    private void OnReconnectionHappened(ReconnectionInfo info)
    {
        _logger.LogInformation($"WebSocket重连成功: {info.Type}");
        _reconnectAttempts = 0;
        _isReconnecting = false;
        UpdateConnectionState(true);
    }

    private void OnDisconnectionHappened(DisconnectionInfo info)
    {
        _logger.LogWarning($"WebSocket断开连接: {info.Type}, {info.CloseStatus}, {info.CloseStatusDescription}");
        UpdateConnectionState(false);

        // 如果不是正常关闭并且未取消，则尝试重连
        if (info.Type != DisconnectionType.Exit && !_cts.IsCancellationRequested)
        {
            Task.Run(async () => await ReconnectWithBackoffAsync());
        }
    }

    private void OnMessageReceived(ResponseMessage message)
    {
        if (message.MessageType == WebSocketMessageType.Text)
        {
            try
            {
                // 简单验证JSON格式
                using (JsonDocument.Parse(message.Text))
                {
                    MessageReceived?.Invoke(this, message.Text);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, $"收到格式错误的JSON消息: {message.Text}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理WebSocket消息时出错");
            }
        }
        else if (message.MessageType == WebSocketMessageType.Binary)
        {
            try
            {
                // 处理二进制消息
                if (message.Binary != null && BinaryMessageReceived != null)
                {
                    BinaryMessageReceived.Invoke(this, message.Binary);
                }
                else if (message.Binary != null && MessageReceived != null)
                {
                    // 如果没有注册二进制处理器，但有文本处理器，就转换为字符串
                    string text = System.Text.Encoding.UTF8.GetString(message.Binary.ToArray());
                    MessageReceived.Invoke(this, text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理WebSocket二进制消息时出错");
            }
        }
    }

    private async Task ReconnectWithBackoffAsync()
    {
        if (_serverUri == null || _cts.IsCancellationRequested)
        {
            return;
        }

        await _reconnectLock.WaitAsync();
        try
        {
            if (_isReconnecting || _isConnected || _cts.IsCancellationRequested)
            {
                return;
            }

            _isReconnecting = true;
            _reconnectAttempts++;

            if (_reconnectAttempts > _options.MaxConnectionRetries)
            {
                _logger.LogError($"重连尝试次数超过最大值 ({_options.MaxConnectionRetries})，停止重连");
                _isReconnecting = false;
                return;
            }

            int delayMs = _options.ReconnectIntervalMs * _reconnectAttempts;
            _logger.LogInformation($"尝试重连 ({_reconnectAttempts}/{_options.MaxConnectionRetries})，延迟 {delayMs}ms");

            await Task.Delay(delayMs, _cts.Token);
            await InitializeClientAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("重连操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重连时出错");
            _isReconnecting = false;
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private void UpdateConnectionState(bool connected)
    {
        if (_isConnected != connected)
        {
            _isConnected = connected;
            ConnectionChanged?.Invoke(this, connected);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _client?.Dispose();
        _cts.Dispose();
        _reconnectLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_client != null)
        {
            await _client.Stop(WebSocketCloseStatus.NormalClosure, "Client disposing");
            _client.Dispose();
        }
        _cts.Dispose();
        _reconnectLock.Dispose();
        GC.SuppressFinalize(this);
    }
}