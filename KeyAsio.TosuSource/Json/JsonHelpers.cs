using System.Text.Json;

namespace KeyAsio.TosuSource.Json;

/// <summary>
/// JSON辅助类，提供处理JSON的静态方法
/// </summary>
public static class JsonHelpers
{
    /// <summary>
    /// 委托定义：处理指定JSON路径的值并返回处理结果
    /// </summary>
    /// <typeparam name="T">值的类型</typeparam>
    /// <param name="reader">JSON读取器，定位在值上</param>
    /// <returns>处理结果，包含值和是否有值的标志</returns>
    public delegate (T value, bool hasValue) ValueExtractor<T>(ref Utf8JsonReader reader);

    /// <summary>
    /// 委托定义：处理提取的值
    /// </summary>
    /// <typeparam name="T">值的类型</typeparam>
    /// <param name="result">提取的值和有效标志</param>
    public delegate void ValueProcessor<T>((T value, bool hasValue) result);

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
    private static void ParseElement(ref Utf8JsonReader reader, ValueHandlerMapping handlers,
        JsonPathTracker pathTracker, bool skipStartToken)
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
                        handlers.InvokeHandler(pathTracker, ref reader);
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
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            int depth = 1;

            while (depth > 0 && reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    depth++;
                }
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    depth--;
                }
            }
        }
        // 原始值类型不需要额外跳过操作
    }
}