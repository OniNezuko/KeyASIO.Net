using Microsoft.Extensions.Logging;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu数据源工厂
/// </summary>
public static class TosuDataSourceFactory
{
    private static ITosuDataSource? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 创建一个tosu数据源实例
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>tosu数据源实例</returns>
    public static ITosuDataSource Create(TosuDataSourceOptions options, ILogger logger)
    {
        lock (_lock)
        {
            if (_instance != null)
            {
                throw new InvalidOperationException("已经存在一个tosu数据源实例");
            }

            _instance = new TosuDataSource(options, logger);
            return _instance;
        }
    }

    /// <summary>
    /// 获取当前的tosu数据源实例
    /// </summary>
    /// <returns>tosu数据源实例，如果未创建则返回null</returns>
    public static ITosuDataSource? GetCurrent()
    {
        lock (_lock)
        {
            return _instance;
        }
    }

    /// <summary>
    /// 释放当前的tosu数据源实例
    /// </summary>
    public static async Task ReleaseAsync()
    {
        ITosuDataSource? instance;
        lock (_lock)
        {
            instance = _instance;
            _instance = null;
        }

        if (instance != null)
        {
            await instance.StopAsync();
            if (instance is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
} 