namespace KeyAsio.TosuSource;

/// <summary>
/// tosu数据源配置选项
/// </summary>
public class TosuDataSourceOptions
{
    /// <summary>
    /// tosu可执行文件路径
    /// </summary>
    public string TosuExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否自动启动tosu进程
    /// </summary>
    public bool AutoStartTosuProcess { get; set; } = true;

    /// <summary>
    /// 是否自动重启tosu进程
    /// </summary>
    public bool AutoRestartTosuProcess { get; set; } = true;

    /// <summary>
    /// WebSocket数据更新间隔(毫秒)
    /// </summary>
    public int UpdateIntervalMs { get; set; } = 100;

    /// <summary>
    /// 当连接断开时，重试间隔(毫秒)
    /// </summary>
    public int ReconnectIntervalMs { get; set; } = 2000;
} 