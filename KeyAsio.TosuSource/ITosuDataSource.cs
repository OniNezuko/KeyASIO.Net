using KeyAsio.MemoryReading;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu 数据源接口
/// </summary>
public interface ITosuDataSource
{
    /// <summary>
    /// 连接状态
    /// </summary>
    TosuConnectionState ConnectionState { get; }

    /// <summary>
    /// 启动数据源
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// 停止数据源
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 连接状态发生变化时触发
    /// </summary>
    event EventHandler<TosuConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// 获取内存读取对象
    /// </summary>
    MemoryReadObject MemoryReadObject { get; }
}

/// <summary>
/// tosu连接状态
/// </summary>
public enum TosuConnectionState
{
    /// <summary>
    /// 未连接
    /// </summary>
    Disconnected,
    
    /// <summary>
    /// 正在启动子进程
    /// </summary>
    StartingProcess,
    
    /// <summary>
    /// 等待子进程准备好
    /// </summary>
    WaitingForProcess,
    
    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,
    
    /// <summary>
    /// 已连接
    /// </summary>
    Connected,
    
    /// <summary>
    /// 正在重连
    /// </summary>
    Reconnecting,
    
    /// <summary>
    /// 出错
    /// </summary>
    Error
} 