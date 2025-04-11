namespace KeyAsio.TosuSource.Json;

/// <summary>
/// JSON路径追踪器，用于高效追踪当前的JSON路径而不创建字符串
/// </summary>
public class JsonPathTracker
{
    // 最大支持的路径深度
    private const int MaxDepth = 16;

    // 存储路径部分的数组，使用字节数组而不是ReadOnlyMemory
    private readonly byte[]?[] _pathParts = new byte[MaxDepth][];

    // 存储每个路径部分的长度
    private readonly int[] _pathLengths = new int[MaxDepth];

    // 当前路径深度
    private int _depth = 0;

    /// <summary>
    /// 获取指定索引处的路径部分
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>路径部分</returns>
    public ReadOnlySpan<byte> this[int index]
    {
        get
        {
            if (index >= _depth || _pathParts[index] == null) return default;
            return new ReadOnlySpan<byte>(_pathParts[index], 0, _pathLengths[index]);
        }
    }

    /// <summary>
    /// 将属性名推入路径栈
    /// </summary>
    /// <param name="propertyName">属性名</param>
    public void PushProperty(ReadOnlySpan<byte> propertyName)
    {
        if (_depth >= MaxDepth) return;
        // 确保我们有足够的空间来存储这个属性名
        int length = propertyName.Length;

        // 如果当前深度没有分配数组，或者数组太小，则分配新数组
        if (_pathParts[_depth] == null || _pathParts[_depth]!.Length < length)
        {
            _pathParts[_depth] = new byte[length];
        }

        // 复制到存储数组
        propertyName.CopyTo(_pathParts[_depth].AsSpan(0, length));
        _pathLengths[_depth] = length;
        _depth++;
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
}