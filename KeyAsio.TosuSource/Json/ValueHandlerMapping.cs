using System.Text;
using System.Text.Json;

namespace KeyAsio.TosuSource.Json;

/// <summary>
/// 值处理器映射，高效存储和检索路径处理器
/// </summary>
public class ValueHandlerMapping
{
    // 路径部分的最大长度
    private const int MaxPathPartLength = 64;

    // 存储路径处理器的数组
    private readonly List<HandlerEntry> _handlers = new();

    // 预分配的缓冲区，用于转换路径部分
    private readonly byte[] _byteBuffer = new byte[MaxPathPartLength];

    // 存储上次处理的值，用于处理相关字段
    private readonly Dictionary<string, object> _processedValues = new();

    /// <summary>
    /// 添加路径处理器
    /// </summary>
    /// <param name="path">点分隔的路径，如 "state.number"</param>
    /// <param name="extractor">值提取器</param>
    /// <param name="processor">值处理器</param>
    public void AddHandler<T>(string path, JsonHelpers.ValueExtractor<T> extractor,
        JsonHelpers.ValueProcessor<T> processor)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(processor);

        // 解析路径字符串为路径部分
        var parts = path.Split('.');
        var entry = new HandlerEntry(parts.Length)
        {
            Path = path,
            ExtractorProcessor = new ExtractorProcessor<T>
            {
                Extractor = extractor,
                Processor = processor
            }
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
    /// 获取上次处理的特定路径的值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="path">路径</param>
    /// <returns>上次处理的值</returns>
    public T? GetLastProcessedValue<T>(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _processedValues.TryGetValue(path, out var value) && value is T typedValue
            ? typedValue
            : default;
    }

    /// <summary>
    /// 重置所有处理过的值
    /// </summary>
    public void ResetProcessedValues()
    {
        _processedValues.Clear();
    }

    /// <summary>
    /// 调用适合给定路径的处理器
    /// </summary>
    /// <param name="pathTracker">路径追踪器</param>
    /// <param name="reader">JSON读取器</param>
    public void InvokeHandler(JsonPathTracker pathTracker, ref Utf8JsonReader reader)
    {
        ArgumentNullException.ThrowIfNull(pathTracker);

        foreach (var entry in _handlers)
        {
            if (!IsMatch(entry, pathTracker)) continue;

            var processor = entry.ExtractorProcessor;
            processor.Extract(ref reader, out var result);

            if (result != null)
            {
                _processedValues[entry.Path] = result;
            }

            processor.Process(result);
            break;
        }
    }

    /// <summary>
    /// 检查路径是否匹配
    /// </summary>
    private static bool IsMatch(HandlerEntry entry, JsonPathTracker pathTracker)
    {
        if (entry.PathParts.Length != pathTracker.Depth)
            return false;

        for (int i = 0; i < entry.PathParts.Length; i++)
        {
            ReadOnlySpan<byte> trackerPart = pathTracker[i];
            ReadOnlySpan<byte> entryPart = new ReadOnlySpan<byte>(entry.PathParts[i], 0, entry.PathLengths[i]);

            if (!trackerPart.SequenceEqual(entryPart))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 提取器和处理器接口
    /// </summary>
    private interface IExtractorProcessor
    {
        void Extract(ref Utf8JsonReader reader, out object? result);
        void Process(object? value);
    }

    /// <summary>
    /// 泛型提取器和处理器，包装了强类型的处理委托
    /// </summary>
    private class ExtractorProcessor<T> : IExtractorProcessor
    {
        public required JsonHelpers.ValueExtractor<T> Extractor { get; init; }
        public required JsonHelpers.ValueProcessor<T> Processor { get; init; }

        public void Extract(ref Utf8JsonReader reader, out object? result)
        {
            var hasValue = Extractor(ref reader, out var result1);
            if (hasValue)
            {
                result = result1;
            }
            else
            {
                result = null;
            }
        }

        public void Process(object? value)
        {
            if (value is T typedValue)
            {
                Processor(typedValue, true);
            }
            else
            {
                Processor(default, false);
            }
        }
    }

    /// <summary>
    /// 处理器条目
    /// </summary>
    private class HandlerEntry(int partsLength)
    {
        public byte[][] PathParts { get; } = new byte[partsLength][];
        public int[] PathLengths { get; } = new int[partsLength]; // 可以优化，因为PathParts的长度是准确的
        public required string Path { get; init; }
        public required IExtractorProcessor ExtractorProcessor { get; init; }
    }
}