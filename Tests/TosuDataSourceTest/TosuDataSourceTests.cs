using System.Text;
using System.Text.Json;
using KeyAsio.MemoryReading;
using KeyAsio.MemoryReading.Logging;
using KeyAsio.Shared;
using KeyAsio.TosuSource;
using KeyAsio.TosuSource.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OsuMemoryDataProvider;

namespace TosuDataSourceTest;

[TestClass]
public class TosuDataSourceTests
{
    private ILogger _logger;
    private TosuDataSourceOptions _options;

    [TestInitialize]
    public void Initialize()
    {
        _logger = LogUtils.GetLogger<TosuDataSourceTests>();
        _options = new TosuDataSourceOptions
        {
            AutoStartTosuProcess = false, // 测试中不需要启动真实进程
            UpdateIntervalMs = 100
        };
    }

    [TestMethod]
    public void TestMemoryReadObject_PropertyUpdates()
    {
        // 由于TosuDataSource需要实际的进程和WebSocket连接
        // 这里我们只能模拟其行为并测试MemoryReadObject的更新

        // 创建测试目标
        var dataSource = new TosuDataSource(_options, _logger);

        // 获取内存读取对象
        var memoryReadObject = dataSource.MemoryReadObject;

        // 设置断言
        bool osuStatusChanged = false;
        bool comboChanged = false;
        bool scoreChanged = false;
        bool beatmapChanged = false;

        // 注册事件处理器
        memoryReadObject.OsuStatusChanged += (oldValue, newValue) =>
        {
            osuStatusChanged = true;
            Assert.AreEqual(OsuMemoryStatus.Playing, newValue);
        };

        memoryReadObject.ComboChanged += (oldValue, newValue) =>
        {
            comboChanged = true;
            Assert.AreEqual(42, newValue);
        };

        memoryReadObject.ScoreChanged += (oldValue, newValue) =>
        {
            scoreChanged = true;
            Assert.AreEqual(12345, newValue);
        };

        memoryReadObject.BeatmapIdentifierChanged += (oldValue, newValue) =>
        {
            beatmapChanged = true;
            Assert.AreEqual("test_folder", newValue.Folder);
            Assert.AreEqual("test_file.osu", newValue.Filename);
        };

        // 使用反射修改属性值来测试事件通知
        var privateMembers = typeof(TosuDataSource).GetField("_memoryReadObject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(privateMembers);
        var memReadObj = privateMembers.GetValue(dataSource) as MemoryReadObject;
        Assert.IsNotNull(memReadObj);

        // 更新属性
        memReadObj.OsuStatus = OsuMemoryStatus.Playing;
        memReadObj.Combo = 42;
        memReadObj.Score = 12345;
        memReadObj.BeatmapIdentifier = new BeatmapIdentifier("test_folder", "test_file.osu");

        // 验证事件是否被触发
        Assert.IsTrue(osuStatusChanged, "OsuStatus change event wasn't fired");
        Assert.IsTrue(comboChanged, "Combo change event wasn't fired");
        Assert.IsTrue(scoreChanged, "Score change event wasn't fired");
        Assert.IsTrue(beatmapChanged, "BeatmapIdentifier change event wasn't fired");
    }

    [TestMethod]
    public void TestJsonParsing()
    {
        // 创建模拟的JSON消息
        var jsonMessage = new V2Response
        {
            State = new State { Number = 2, Name = "Playing" },
            Play = new Play
            {
                PlayerName = "TestPlayer",
                Score = 15000,
                Combo = new Combo { Current = 120, Max = 300 },
                Mods = new State { Number = 64, Name = "DT" }
            },
            Beatmap = new Beatmap
            {
                Time = new Time { Live = 12345, FirstObject = 1000, LastObject = 60000 }
            },
            DirectPath = new DirectPath
            {
                BeatmapFolder = "Artist - Song",
                BeatmapFile = "C:\\osu\\Songs\\Artist - Song\\difficulty.osu"
            },
            Settings = new Settings
            {
                ReplayUiVisible = false
            }
        };

        // 序列化为JSON
        string jsonString = JsonSerializer.Serialize(jsonMessage);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

        // 创建TosuDataSource实例并使用内部测试方法
        var dataSource = new TosuDataSource(_options, _logger);

        // 使用内部测试方法解析消息
        dataSource.ParseAndProcessMessageForTest(jsonBytes);

        // 获取当前值
        var result = dataSource.GetCurrentValuesForTest();

        // 验证结果
        Assert.AreEqual(OsuMemoryStatus.Playing, result.Status);
        Assert.AreEqual("TestPlayer", result.PlayerName);
        Assert.AreEqual(15000, result.Score);
        Assert.AreEqual(120, result.Combo);
        Assert.AreEqual(12345, result.PlayTime);
        Assert.AreEqual("Artist - Song", result.BeatmapFolder);
        Assert.AreEqual("difficulty.osu", result.BeatmapFile);
    }

    [TestMethod]
    public void TestConnectionState()
    {
        var dataSource = new TosuDataSource(_options, _logger);

        // 初始状态应该是断开连接
        Assert.AreEqual(TosuConnectionState.Disconnected, dataSource.ConnectionState);

        // 测试状态事件
        TosuConnectionState? newState = null;
        dataSource.ConnectionStateChanged += (sender, state) =>
        {
            newState = state;
        };

        // 使用反射修改连接状态
        var field = typeof(TosuDataSource).GetField("_connectionState",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(field);

        // 设置状态
        var setStateMethod = typeof(TosuDataSource).GetProperty("ConnectionState").SetMethod;
        setStateMethod.Invoke(dataSource, new object[] { TosuConnectionState.Connected });

        // 验证事件被触发
        Assert.IsNotNull(newState);
        Assert.AreEqual(TosuConnectionState.Connected, newState);
    }
}