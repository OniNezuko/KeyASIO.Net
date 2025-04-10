using System.Text;
using System.Text.Json;
using KeyAsio.MemoryReading.Logging;
using KeyAsio.Shared;
using KeyAsio.TosuSource;
using KeyAsio.TosuSource.Models;
using OsuMemoryDataProvider;

namespace TosuDataSourceConsoleTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TosuDataSource 测试程序");
        Console.WriteLine("=============================");

        var logger = LogUtils.GetLogger<Program>();

        // 创建选项
        var options = new TosuDataSourceOptions
        {
            AutoStartTosuProcess = false, // 测试时不自动启动tosu
            UpdateIntervalMs = 100
        };

        // 运行基本测试
        await RunBasicTests(options, logger);

        // 运行UTF8JsonReader性能测试
        RunUtf8JsonReaderTests();

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    static async Task RunBasicTests(TosuDataSourceOptions options, ILogger logger)
    {
        Console.WriteLine("\n基本功能测试");
        Console.WriteLine("---------------------------");

        // 创建测试用TosuDataSource
        var dataSource = new TosuDataSource(options, logger);
        await dataSource.StartAsync();
        Console.ReadLine();
        // 测试MemoryReadObject事件
        TestMemoryReadObjectEvents(dataSource);

        // 测试JSON解析
        TestJsonParsing(dataSource);
    }

    static void TestMemoryReadObjectEvents(TosuDataSource dataSource)
    {
        Console.WriteLine("测试MemoryReadObject事件...");

        var memoryReadObject = dataSource.MemoryReadObject;

        // 注册事件处理器
        memoryReadObject.OsuStatusChanged += (old, @new) =>
            Console.WriteLine($"OsuStatus变更: {old} -> {@new}");

        memoryReadObject.ComboChanged += (old, @new) =>
            Console.WriteLine($"Combo变更: {old} -> {@new}");

        memoryReadObject.ScoreChanged += (old, @new) =>
            Console.WriteLine($"分数变更: {old} -> {@new}");

        // 设置属性
        memoryReadObject.OsuStatus = OsuMemoryStatus.Playing;
        memoryReadObject.Combo = 100;
        memoryReadObject.Score = 10000;
    }

    static void TestJsonParsing(TosuDataSource dataSource)
    {
        Console.WriteLine("\n测试JSON解析...");

        // 创建一个测试JSON
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

        // 转换为JSON
        string jsonString = JsonSerializer.Serialize(jsonMessage);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

        // 解析
        dataSource.ParseAndProcessMessageForTest(jsonBytes);

        // 获取结果
        var result = dataSource.GetCurrentValuesForTest();

        // 输出结果
        Console.WriteLine($"解析结果:");
        Console.WriteLine($"  状态: {result.Status}");
        Console.WriteLine($"  玩家: {result.PlayerName}");
        Console.WriteLine($"  分数: {result.Score}");
        Console.WriteLine($"  连击: {result.Combo}");
        Console.WriteLine($"  播放时间: {result.PlayTime}ms");
        Console.WriteLine($"  谱面文件夹: {result.BeatmapFolder}");
        Console.WriteLine($"  谱面文件: {result.BeatmapFile}");
    }

    static void RunUtf8JsonReaderTests()
    {
        Console.WriteLine("\nUTF8JsonReader性能测试");
        Console.WriteLine("---------------------------");

        // 创建测试JSON
        var largeJson = CreateLargeTestJson();
        var jsonBytes = Encoding.UTF8.GetBytes(largeJson);

        // 预热
        for (int i = 0; i < 10; i++)
        {
            TestStringMethod(largeJson);
            TestUtf8JsonReaderMethod(jsonBytes);
        }

        // 测试字符串方法
        Console.WriteLine("测试基于字符串比较的方法...");
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            TestStringMethod(largeJson);
        }
        sw1.Stop();

        // 测试Utf8JsonReader方法
        Console.WriteLine("测试基于Utf8JsonReader的方法...");
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            TestUtf8JsonReaderMethod(jsonBytes);
        }
        sw2.Stop();

        // 输出比较
        Console.WriteLine($"字符串方法: {sw1.ElapsedMilliseconds} ms");
        Console.WriteLine($"Utf8JsonReader方法: {sw2.ElapsedMilliseconds} ms");
        Console.WriteLine($"性能提升: {(double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds:F2}x");
    }

    static void TestStringMethod(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<V2Response>(json, options);

            if (response?.Play != null)
            {
                var combo = response.Play.Combo?.Current ?? 0;
                var playerName = response.Play.PlayerName ?? string.Empty;
                var score = response.Play.Score;
            }

            if (response?.State != null)
            {
                var state = response.State.Number;
            }
        }
        catch
        {
            // 忽略异常
        }
    }

    static void TestUtf8JsonReaderMethod(byte[] jsonBytes)
    {
        try
        {
            var reader = new Utf8JsonReader(jsonBytes);
            int? combo = null;
            string? playerName = null;
            long? score = null;
            long? state = null;

            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                ReadOnlySpan<byte> propertyName = reader.ValueSpan;
                reader.Read(); // 移到值

                if (propertyName.SequenceEqual("state"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName &&
                                reader.ValueSpan.SequenceEqual("number"u8))
                            {
                                reader.Read();
                                if (reader.TokenType == JsonTokenType.Number)
                                {
                                    state = reader.GetInt64();
                                }
                            }
                        }
                    }
                }
                else if (propertyName.SequenceEqual("play"u8))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName)
                                continue;

                            ReadOnlySpan<byte> playProp = reader.ValueSpan;
                            reader.Read();

                            if (playProp.SequenceEqual("playerName"u8) &&
                                reader.TokenType == JsonTokenType.String)
                            {
                                playerName = reader.GetString();
                            }
                            else if (playProp.SequenceEqual("score"u8) &&
                                reader.TokenType == JsonTokenType.Number)
                            {
                                score = reader.GetInt64();
                            }
                            else if (playProp.SequenceEqual("combo"u8) &&
                                reader.TokenType == JsonTokenType.StartObject)
                            {
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                {
                                    if (reader.TokenType == JsonTokenType.PropertyName &&
                                        reader.ValueSpan.SequenceEqual("current"u8))
                                    {
                                        reader.Read();
                                        if (reader.TokenType == JsonTokenType.Number)
                                        {
                                            combo = reader.GetInt32();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略异常
        }
    }

    static string CreateLargeTestJson()
    {
        var response = new V2Response
        {
            State = new State { Number = 2, Name = "Playing" },
            Play = new Play
            {
                PlayerName = "TestPlayer",
                Score = 15000,
                Combo = new Combo { Current = 120, Max = 300 },
                Mods = new State { Number = 64, Name = "DT" },
                HitErrorArray = new object[100], // 添加一些数组元素
                Hits = new Hits
                {
                    The0 = 0,
                    The50 = 5,
                    The100 = 10,
                    The300 = 100,
                    Geki = 50,
                    Katu = 20,
                    SliderBreaks = 0
                }
            },
            Beatmap = new Beatmap
            {
                Time = new Time { Live = 12345, FirstObject = 1000, LastObject = 60000 },
                Id = 12345,
                Set = 54321,
                Artist = "TestArtist",
                ArtistUnicode = "TestArtistUnicode",
                Title = "TestTitle",
                TitleUnicode = "TestTitleUnicode",
                Mapper = "TestMapper",
                Version = "TestVersion",
                Stats = new Stats
                {
                    Stars = new Stars { Live = 5, Total = 5 },
                    Ar = new Ar { Original = 9, Converted = 9 },
                    Cs = new Ar { Original = 4, Converted = 4 },
                    Od = new Ar { Original = 8, Converted = 8 },
                    Hp = new Ar { Original = 5, Converted = 5 },
                    Bpm = new Bpm { Common = 180, Min = 180, Max = 180 },
                    Objects = new Objects
                    {
                        Circles = 100,
                        Sliders = 100,
                        Spinners = 10,
                        Holds = 0,
                        Total = 210
                    },
                    MaxCombo = 300
                }
            },
            DirectPath = new DirectPath
            {
                BeatmapFolder = "Artist - Song",
                BeatmapFile = "C:\\osu\\Songs\\Artist - Song\\difficulty.osu",
                BeatmapAudio = "C:\\osu\\Songs\\Artist - Song\\audio.mp3",
                BeatmapBackground = "C:\\osu\\Songs\\Artist - Song\\bg.jpg",
                SkinFolder = "C:\\osu\\Skins\\Default"
            },
            Settings = new Settings
            {
                ReplayUiVisible = false,
                InterfaceVisible = true
            },
            Leaderboard = new object[50] // 添加一些排行榜元素
        };

        // 添加一些额外的属性以使JSON更大
        for (int i = 0; i < response.Leaderboard.Length; i++)
        {
            response.Leaderboard[i] = new { Rank = i + 1, Username = $"Player{i}", Score = 1000000 - i * 10000 };
        }

        return JsonSerializer.Serialize(response);
    }
}