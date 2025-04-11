using System.Text;
using System.Text.Json;
using KeyAsio.MemoryReading;
using KeyAsio.MemoryReading.Logging;
using KeyAsio.Shared;
using KeyAsio.TosuSource;
using KeyAsio.TosuSource.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OsuMemoryDataProvider;

namespace TosuDataSourceUnitTest;

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
        var jsonString = """
                         {
                             "client": "lazer",
                             "server": "ppy.sh",
                             "state": {
                                 "number": 2,
                                 "name": "play"
                             },
                             "session": {
                                 "playTime": 0,
                                 "playCount": 0
                             },
                             "settings": {
                                 "interfaceVisible": false,
                                 "replayUIVisible": true,
                                 "chatVisibilityStatus": {
                                     "number": 0,
                                     "name": "hidden"
                                 },
                                 "leaderboard": {
                                     "visible": false,
                                     "type": {
                                         "number": 0,
                                         "name": "local"
                                     }
                                 },
                                 "progressBar": {
                                     "number": 0,
                                     "name": "off"
                                 },
                                 "bassDensity": 0,
                                 "resolution": {
                                     "fullscreen": false,
                                     "width": 0,
                                     "height": 0,
                                     "widthFullscreen": 0,
                                     "heightFullscreen": 0
                                 },
                                 "client": {
                                     "updateAvailable": false,
                                     "branch": 0,
                                     "version": ""
                                 },
                                 "scoreMeter": {
                                     "type": {
                                         "number": 0,
                                         "name": "none"
                                     },
                                     "size": 0
                                 },
                                 "cursor": {
                                     "useSkinCursor": false,
                                     "autoSize": false,
                                     "size": 0
                                 },
                                 "mouse": {
                                     "rawInput": false,
                                     "disableButtons": false,
                                     "disableWheel": false,
                                     "sensitivity": 0
                                 },
                                 "mania": {
                                     "speedBPMScale": false,
                                     "usePerBeatmapSpeedScale": false
                                 },
                                 "sort": {
                                     "number": 0,
                                     "name": "artist"
                                 },
                                 "group": {
                                     "number": 0,
                                     "name": "none"
                                 },
                                 "skin": {
                                     "useDefaultSkinInEditor": false,
                                     "ignoreBeatmapSkins": false,
                                     "tintSliderBall": false,
                                     "useTaikoSkin": false,
                                     "name": ""
                                 },
                                 "mode": {
                                     "number": 0,
                                     "name": "osu"
                                 },
                                 "audio": {
                                     "ignoreBeatmapSounds": false,
                                     "useSkinSamples": false,
                                     "volume": {
                                         "master": 0,
                                         "music": 0,
                                         "effect": 0
                                     },
                                     "offset": {
                                         "universal": 0
                                     }
                                 },
                                 "background": {
                                     "dim": 0,
                                     "video": false,
                                     "storyboard": false
                                 },
                                 "keybinds": {
                                     "osu": {
                                         "k1": "",
                                         "k2": "",
                                         "smokeKey": ""
                                     },
                                     "fruits": {
                                         "k1": "",
                                         "k2": "",
                                         "Dash": ""
                                     },
                                     "taiko": {
                                         "innerLeft": "",
                                         "innerRight": "",
                                         "outerLeft": "",
                                         "outerRight": ""
                                     },
                                     "quickRetry": ""
                                 }
                             },
                             "profile": {
                                 "userStatus": {
                                     "number": 0,
                                     "name": "reconnecting"
                                 },
                                 "banchoStatus": {
                                     "number": 0,
                                     "name": "idle"
                                 },
                                 "id": 0,
                                 "name": "Guest",
                                 "mode": {
                                     "number": 0,
                                     "name": "osu"
                                 },
                                 "rankedScore": 0,
                                 "level": 0,
                                 "accuracy": 0,
                                 "pp": 0,
                                 "playCount": 0,
                                 "globalRank": 0,
                                 "countryCode": {
                                     "number": 0,
                                     "name": ""
                                 },
                                 "backgroundColour": "ffffffff"
                             },
                             "beatmap": {
                                 "isKiai": false,
                                 "isBreak": false,
                                 "isConvert": false,
                                 "time": {
                                     "live": 3031,
                                     "firstObject": 0,
                                     "lastObject": 0,
                                     "mp3Length": 92656
                                 },
                                 "status": {
                                     "number": 1,
                                     "name": "notSubmitted"
                                 },
                                 "checksum": "87cae92a990bac7639e29a761cf3229b",
                                 "id": 3610953,
                                 "set": 1764200,
                                 "mode": {
                                     "number": 0,
                                     "name": "osu"
                                 },
                                 "artist": "Yousei Teikoku",
                                 "artistUnicode": "妖精帝國",
                                 "title": "Baptize (TV Size)",
                                 "titleUnicode": "Baptize (TV Size)",
                                 "mapper": "Where Am I Now",
                                 "version": "Chaos",
                                 "stats": {
                                     "stars": {
                                         "live": 0,
                                         "total": 0
                                     },
                                     "ar": {
                                         "original": 0,
                                         "converted": 0
                                     },
                                     "cs": {
                                         "original": 0,
                                         "converted": 0
                                     },
                                     "od": {
                                         "original": 0,
                                         "converted": 0
                                     },
                                     "hp": {
                                         "original": 0,
                                         "converted": 0
                                     },
                                     "bpm": {
                                         "realtime": 0,
                                         "common": 0,
                                         "min": 0,
                                         "max": 0
                                     },
                                     "objects": {
                                         "circles": 0,
                                         "sliders": 0,
                                         "spinners": 0,
                                         "holds": 0,
                                         "total": 0
                                     },
                                     "maxCombo": 0
                                 }
                             },
                             "play": {
                                 "playerName": "TestPlayer",
                                 "mode": {
                                     "number": 0,
                                     "name": "osu"
                                 },
                                 "score": 114514,
                                 "accuracy": 100,
                                 "healthBar": {
                                     "normal": 0,
                                     "smooth": 0
                                 },
                                 "hits": {
                                     "0": 0,
                                     "50": 0,
                                     "100": 0,
                                     "300": 0,
                                     "geki": 0,
                                     "katu": 0,
                                     "sliderBreaks": 0,
                                     "sliderEndHits": 0,
                                     "smallTickHits": 0,
                                     "largeTickHits": 0
                                 },
                                 "hitErrorArray": [],
                                 "combo": {
                                     "current": 233,
                                     "max": 0
                                 },
                                 "mods": {
                                     "checksum": "",
                                     "number": 0,
                                     "name": "",
                                     "array": [],
                                     "rate": 1
                                 },
                                 "rank": {
                                     "current": "",
                                     "maxThisPlay": ""
                                 },
                                 "pp": {
                                     "current": 0,
                                     "fc": 0,
                                     "maxAchievedThisPlay": 0,
                                     "detailed": {
                                         "current": {
                                             "aim": 0,
                                             "speed": 0,
                                             "accuracy": 0,
                                             "difficulty": 0,
                                             "flashlight": 0,
                                             "total": 0
                                         },
                                         "fc": {
                                             "aim": 0,
                                             "speed": 0,
                                             "accuracy": 0,
                                             "difficulty": 0,
                                             "flashlight": 0,
                                             "total": 0
                                         }
                                     }
                                 },
                                 "unstableRate": 0
                             },
                             "leaderboard": [],
                             "performance": {
                                 "accuracy": {
                                     "90": 0,
                                     "91": 0,
                                     "92": 0,
                                     "93": 0,
                                     "94": 0,
                                     "95": 0,
                                     "96": 0,
                                     "97": 0,
                                     "98": 0,
                                     "99": 0,
                                     "100": 0
                                 },
                                 "graph": {
                                     "series": [],
                                     "xaxis": []
                                 }
                             },
                             "resultsScreen": {
                                 "scoreId": 0,
                                 "playerName": "",
                                 "mode": {
                                     "number": 0,
                                     "name": "osu"
                                 },
                                 "score": 0,
                                 "accuracy": 0,
                                 "name": "",
                                 "hits": {
                                     "0": 0,
                                     "50": 0,
                                     "100": 0,
                                     "300": 0,
                                     "geki": 0,
                                     "katu": 0,
                                     "sliderEndHits": 0,
                                     "smallTickHits": 0,
                                     "largeTickHits": 0
                                 },
                                 "mods": {
                                     "checksum": "",
                                     "number": 0,
                                     "name": "",
                                     "array": [],
                                     "rate": 1
                                 },
                                 "maxCombo": 0,
                                 "rank": "",
                                 "pp": {
                                     "current": 0,
                                     "fc": 0
                                 },
                                 "createdAt": ""
                             },
                             "folders": {
                                 "game": "C:\\Users\\milki\\AppData\\Local\\osulazer\\current",
                                 "skin": "E:\\其他文件\\osu-lazer\\files",
                                 "songs": "",
                                 "beatmap": "."
                             },
                             "files": {
                                 "beatmap": "7\\70\\70b3ffe622aa00779374e090440ea049123db3b08428588c8582249d8d1d9287",
                                 "background": "9\\98\\98323c1efab0822278dee2542ccf1f76b2c2a70c9fe0a98c90ba33bfb1b4ab71",
                                 "audio": "1\\1b\\1b95c6fbd5a4d64548b39eeeab0570ac78984d4ea6ab448b81c3806aca96f994"
                             },
                             "directPath": {
                                 "beatmapFile": "7\\70\\70b3ffe622aa00779374e090440ea049123db3b08428588c8582249d8d1d9287",
                                 "beatmapBackground": "9\\98\\98323c1efab0822278dee2542ccf1f76b2c2a70c9fe0a98c90ba33bfb1b4ab71",
                                 "beatmapAudio": "1\\1b\\1b95c6fbd5a4d64548b39eeeab0570ac78984d4ea6ab448b81c3806aca96f994",
                                 "beatmapFolder": ".",
                                 "skinFolder": "E:\\其他文件\\osu-lazer\\files"
                             },
                             "tourney": {
                                 "scoreVisible": false,
                                 "starsVisible": false,
                                 "ipcState": 0,
                                 "bestOF": 0,
                                 "team": {
                                     "left": "",
                                     "right": ""
                                 },
                                 "points": {
                                     "left": 0,
                                     "right": 0
                                 },
                                 "chat": [],
                                 "totalScore": {
                                     "left": 0,
                                     "right": 0
                                 },
                                 "clients": []
                             }
                         }
                         """;
        //// 创建模拟的JSON消息
        //var jsonMessage = new V2Response
        //{
        //    State = new State { Number = 2, Name = "Playing" },
        //    Play = new Play
        //    {
        //        PlayerName = "TestPlayer",
        //        Score = 15000,
        //        Combo = new Combo { Current = 120, Max = 300 },
        //        Mods = new State { Number = 64, Name = "DT" }
        //    },
        //    Beatmap = new Beatmap
        //    {
        //        Time = new Time { Live = 12345, FirstObject = 1000, LastObject = 60000 }
        //    },
        //    DirectPath = new DirectPath
        //    {
        //        BeatmapFolder = "Artist - Song",
        //        BeatmapFile = "C:\\osu\\Songs\\Artist - Song\\difficulty.osu"
        //    },
        //    Settings = new Settings
        //    {
        //        ReplayUiVisible = false
        //    }
        //};

        //// 序列化为JSON
        //string jsonString = JsonSerializer.Serialize(jsonMessage);
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
        Assert.AreEqual(114514, result.Score);
        Assert.AreEqual(233, result.Combo);
        Assert.AreEqual(3031, result.PlayTime);
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