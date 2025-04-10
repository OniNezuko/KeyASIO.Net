using System.Text.Json.Serialization;
using KeyAsio.MemoryReading;
using OsuMemoryDataProvider;

namespace KeyAsio.TosuSource;

/// <summary>
/// tosu返回的数据模型基类
/// </summary>
internal class TosuBaseResponse
{
    [JsonPropertyName("state")]
    public int State { get; set; }
}

/// <summary>
/// Menu数据
/// </summary>
internal class TosuMenuResponse : TosuBaseResponse
{
    [JsonPropertyName("menu")]
    public TosuMenuData? Menu { get; set; }
}

/// <summary>
/// 菜单数据
/// </summary>
internal class TosuMenuData
{
    [JsonPropertyName("state")]
    public int State { get; set; }

    [JsonPropertyName("bm")]
    public TosuBeatmapData? Beatmap { get; set; }

    [JsonPropertyName("mods")]
    public TosuModsData? Mods { get; set; }

    [JsonPropertyName("pp")]
    public TosuPpData? Pp { get; set; }
}

/// <summary>
/// 谱面数据
/// </summary>
internal class TosuBeatmapData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("set")]
    public int SetId { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("mapper")]
    public string? Mapper { get; set; }

    [JsonPropertyName("ar")]
    public float Ar { get; set; }

    [JsonPropertyName("cs")]
    public float Cs { get; set; }

    [JsonPropertyName("hp")]
    public float Hp { get; set; }

    [JsonPropertyName("od")]
    public float Od { get; set; }

    [JsonPropertyName("path")]
    public TosuPathData? Path { get; set; }
}

/// <summary>
/// 路径数据
/// </summary>
internal class TosuPathData
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("bg")]
    public string? Background { get; set; }

    [JsonPropertyName("audio")]
    public string? Audio { get; set; }
}

/// <summary>
/// Mod数据
/// </summary>
internal class TosuModsData
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("str")]
    public string? Str { get; set; }
}

/// <summary>
/// PP数据
/// </summary>
internal class TosuPpData
{
    [JsonPropertyName("100")]
    public float Pp100 { get; set; }

    [JsonPropertyName("99")]
    public float Pp99 { get; set; }

    [JsonPropertyName("98")]
    public float Pp98 { get; set; }

    [JsonPropertyName("97")]
    public float Pp97 { get; set; }

    [JsonPropertyName("95")]
    public float Pp95 { get; set; }

    [JsonPropertyName("current")]
    public float CurrentPp { get; set; }

    [JsonPropertyName("fc")]
    public float FcPp { get; set; }

    [JsonPropertyName("maxThisPlay")]
    public float MaxThisPlayPp { get; set; }
}

/// <summary>
/// 玩家数据
/// </summary>
internal class TosuPlayResponse : TosuBaseResponse
{
    [JsonPropertyName("gameplay")]
    public TosuGameplayData? Gameplay { get; set; }
}

/// <summary>
/// 游戏数据
/// </summary>
internal class TosuGameplayData
{
    [JsonPropertyName("name")]
    public string? PlayerName { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; set; }

    [JsonPropertyName("combo")]
    public TosuComboData? Combo { get; set; }

    [JsonPropertyName("hp")]
    public TosuHpData? Hp { get; set; }

    [JsonPropertyName("hits")]
    public TosuHitsData? Hits { get; set; }

    [JsonPropertyName("time")]
    public TosuTimeData? Time { get; set; }
}

/// <summary>
/// 连击数据
/// </summary>
internal class TosuComboData
{
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

/// <summary>
/// 血量数据
/// </summary>
internal class TosuHpData
{
    [JsonPropertyName("normal")]
    public double Normal { get; set; }

    [JsonPropertyName("smooth")]
    public double Smooth { get; set; }
}

/// <summary>
/// 击打数据
/// </summary>
internal class TosuHitsData
{
    [JsonPropertyName("300")]
    public int Hit300 { get; set; }

    [JsonPropertyName("geki")]
    public int HitGeki { get; set; }

    [JsonPropertyName("100")]
    public int Hit100 { get; set; }

    [JsonPropertyName("katu")]
    public int HitKatu { get; set; }

    [JsonPropertyName("50")]
    public int Hit50 { get; set; }

    [JsonPropertyName("0")]
    public int HitMiss { get; set; }

    [JsonPropertyName("sliderBreaks")]
    public int SliderBreaks { get; set; }

    [JsonPropertyName("grade")]
    public TosuGradeData? Grade { get; set; }

    [JsonPropertyName("unstableRate")]
    public double UnstableRate { get; set; }
}

/// <summary>
/// 等级数据
/// </summary>
internal class TosuGradeData
{
    [JsonPropertyName("current")]
    public string? Current { get; set; }

    [JsonPropertyName("maxThisPlay")]
    public string? MaxThisPlay { get; set; }
}

/// <summary>
/// 时间数据
/// </summary>
internal class TosuTimeData
{
    [JsonPropertyName("firstObj")]
    public int FirstObject { get; set; }

    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("full")]
    public int Full { get; set; }

    [JsonPropertyName("mp3")]
    public int Mp3 { get; set; }
}

/// <summary>
/// tosu菜单状态
/// </summary>
internal enum TosuMenuState
{
    MainMenu = 0,
    EditingMap = 1,
    Playing = 2,
    GameShutdownAnimation = 3,
    SongSelectEdit = 4,
    SongSelect = 5,
    ResultsScreen = 7,
    GameStartupAnimation = 10,
    MultiplayerRooms = 11,
    MultiplayerRoom = 12,
    MultiplayerSongSelect = 13,
    MultiplayerResultsscreen = 14,
    OsuDirect = 15,
    RankingTagCoop = 17,
    RankingTeam = 18,
    ProcessingBeatmaps = 19,
    Tourney = 22,
    Battleroyal = 23,
}

/// <summary>
/// tosu数据转换器
/// </summary>
internal static class TosuDataConverter
{
    /// <summary>
    /// 将tosu菜单状态转换为OsuStatus
    /// </summary>
    public static OsuMemoryStatus ConvertMenuStateToOsuStatus(int menuState)
    {
        return menuState switch
        {
            (int)TosuMenuState.MainMenu => OsuMemoryStatus.MainMenu,
            (int)TosuMenuState.EditingMap => OsuMemoryStatus.EditingMap,
            (int)TosuMenuState.Playing => OsuMemoryStatus.Playing,
            (int)TosuMenuState.SongSelect => OsuMemoryStatus.SongSelect,
            (int)TosuMenuState.ResultsScreen => OsuMemoryStatus.ResultsScreen,
            _ => OsuMemoryStatus.NotRunning
        };
    }

    /// <summary>
    /// 将tosu的Mod值转换为Mods枚举
    /// </summary>
    public static Mods ConvertToMods(int modsValue)
    {
        return (Mods)modsValue;
    }
} 