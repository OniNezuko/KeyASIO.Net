using System.Text.Json.Serialization;

namespace KeyAsio.TosuSource.Models;

public class V2Response
{
    [JsonPropertyName("state")]
    public State State { get; set; } = new();

    [JsonPropertyName("session")]
    public Session Session { get; set; } = new();

    [JsonPropertyName("settings")]
    public Settings Settings { get; set; } = new();

    [JsonPropertyName("profile")]
    public Profile Profile { get; set; } = new();

    [JsonPropertyName("beatmap")]
    public Beatmap Beatmap { get; set; } = new();

    [JsonPropertyName("play")]
    public Play Play { get; set; } = new();

    [JsonPropertyName("leaderboard")]
    public object[] Leaderboard { get; set; } = Array.Empty<object>();

    [JsonPropertyName("performance")]
    public Performance Performance { get; set; } = new();

    [JsonPropertyName("resultsScreen")]
    public ResultsScreen ResultsScreen { get; set; } = new();

    [JsonPropertyName("folders")]
    public Folders Folders { get; set; } = new();

    [JsonPropertyName("files")]
    public Files Files { get; set; } = new();

    [JsonPropertyName("directPath")]
    public DirectPath DirectPath { get; set; } = new();

    [JsonPropertyName("tourney")]
    public Tourney Tourney { get; set; } = new();
}

public class Beatmap
{
    [JsonPropertyName("time")]
    public Time Time { get; set; } = new();

    [JsonPropertyName("status")]
    public Status Status { get; set; } = new();

    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("set")]
    public long Set { get; set; }

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("artistUnicode")]
    public string ArtistUnicode { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("titleUnicode")]
    public string TitleUnicode { get; set; } = string.Empty;

    [JsonPropertyName("mapper")]
    public string Mapper { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("stats")]
    public Stats Stats { get; set; } = new();
}

public class Stats
{
    [JsonPropertyName("stars")]
    public Stars Stars { get; set; }

    [JsonPropertyName("ar")]
    public Ar Ar { get; set; }

    [JsonPropertyName("cs")]
    public Ar Cs { get; set; }

    [JsonPropertyName("od")]
    public Ar Od { get; set; }

    [JsonPropertyName("hp")]
    public Ar Hp { get; set; }

    [JsonPropertyName("bpm")]
    public Bpm Bpm { get; set; }

    [JsonPropertyName("objects")]
    public Objects Objects { get; set; }

    [JsonPropertyName("maxCombo")]
    public long MaxCombo { get; set; }
}

public class Ar
{
    [JsonPropertyName("original")]
    public long Original { get; set; }

    [JsonPropertyName("converted")]
    public long Converted { get; set; }
}

public class Bpm
{
    [JsonPropertyName("common")]
    public long Common { get; set; }

    [JsonPropertyName("min")]
    public long Min { get; set; }

    [JsonPropertyName("max")]
    public long Max { get; set; }
}

public class Objects
{
    [JsonPropertyName("circles")]
    public long Circles { get; set; }

    [JsonPropertyName("sliders")]
    public long Sliders { get; set; }

    [JsonPropertyName("spinners")]
    public long Spinners { get; set; }

    [JsonPropertyName("holds")]
    public long Holds { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }
}

public class Stars
{
    [JsonPropertyName("live")]
    public long Live { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }
}

public class Status
{
    [JsonPropertyName("number")]
    public long Number { get; set; }
}

public class Time
{
    [JsonPropertyName("live")]
    public int Live { get; set; }

    [JsonPropertyName("firstObject")]
    public int FirstObject { get; set; }

    [JsonPropertyName("lastObject")]
    public int LastObject { get; set; }
}

public class DirectPath
{
    [JsonPropertyName("beatmapFile")]
    public string BeatmapFile { get; set; } = string.Empty;

    [JsonPropertyName("beatmapBackground")]
    public string BeatmapBackground { get; set; } = string.Empty;

    [JsonPropertyName("beatmapAudio")]
    public string BeatmapAudio { get; set; } = string.Empty;

    [JsonPropertyName("beatmapFolder")]
    public string BeatmapFolder { get; set; } = string.Empty;

    [JsonPropertyName("skinFolder")]
    public string SkinFolder { get; set; } = string.Empty;
}

public class Files
{
    [JsonPropertyName("beatmap")]
    public string Beatmap { get; set; } = string.Empty;

    [JsonPropertyName("background")]
    public string Background { get; set; } = string.Empty;

    [JsonPropertyName("audio")]
    public string Audio { get; set; } = string.Empty;
}

public class Folders
{
    [JsonPropertyName("game")]
    public string Game { get; set; } = string.Empty;

    [JsonPropertyName("skin")]
    public string Skin { get; set; } = string.Empty;

    [JsonPropertyName("songs")]
    public string Songs { get; set; } = string.Empty;

    [JsonPropertyName("beatmap")]
    public string Beatmap { get; set; } = string.Empty;
}

public class Performance
{
    [JsonPropertyName("accuracy")]
    public Dictionary<string, long> Accuracy { get; set; } = new();

    [JsonPropertyName("graph")]
    public Graph Graph { get; set; } = new();
}

public class Graph
{
    [JsonPropertyName("series")]
    public object[] Series { get; set; } = Array.Empty<object>();

    [JsonPropertyName("xaxis")]
    public object[] Xaxis { get; set; } = Array.Empty<object>();
}

public class Play
{
    [JsonPropertyName("playerName")]
    public string PlayerName { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public State Mode { get; set; } = new();

    [JsonPropertyName("score")]
    public long Score { get; set; }

    [JsonPropertyName("accuracy")]
    public long Accuracy { get; set; }

    [JsonPropertyName("healthBar")]
    public HealthBar HealthBar { get; set; } = new();

    [JsonPropertyName("hits")]
    public Hits Hits { get; set; } = new();

    [JsonPropertyName("hitErrorArray")]
    public object[] HitErrorArray { get; set; } = Array.Empty<object>();

    [JsonPropertyName("combo")]
    public Combo Combo { get; set; } = new();

    [JsonPropertyName("mods")]
    public State Mods { get; set; } = new();

    [JsonPropertyName("rank")]
    public Rank Rank { get; set; } = new();

    [JsonPropertyName("pp")]
    public PlayPp Pp { get; set; } = new();

    [JsonPropertyName("unstableRate")]
    public long UnstableRate { get; set; }
}

public class Combo
{
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

public class HealthBar
{
    [JsonPropertyName("normal")]
    public long Normal { get; set; }

    [JsonPropertyName("smooth")]
    public long Smooth { get; set; }
}

public class Hits
{
    [JsonPropertyName("0")]
    public long The0 { get; set; }

    [JsonPropertyName("50")]
    public long The50 { get; set; }

    [JsonPropertyName("100")]
    public long The100 { get; set; }

    [JsonPropertyName("300")]
    public long The300 { get; set; }

    [JsonPropertyName("geki")]
    public long Geki { get; set; }

    [JsonPropertyName("katu")]
    public long Katu { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sliderBreaks")]
    public long? SliderBreaks { get; set; }
}

public class State
{
    [JsonPropertyName("number")]
    public long Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class PlayPp
{
    [JsonPropertyName("current")]
    public long Current { get; set; }

    [JsonPropertyName("fc")]
    public long Fc { get; set; }

    [JsonPropertyName("maxAchievedThisPlay")]
    public long MaxAchievedThisPlay { get; set; }
}

public class Rank
{
    [JsonPropertyName("current")]
    public string Current { get; set; }

    [JsonPropertyName("maxThisPlay")]
    public string MaxThisPlay { get; set; }
}

public class Profile
{
    [JsonPropertyName("userStatus")]
    public State UserStatus { get; set; }

    [JsonPropertyName("banchoStatus")]
    public State BanchoStatus { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("mode")]
    public State Mode { get; set; }

    [JsonPropertyName("rankedScore")]
    public long RankedScore { get; set; }

    [JsonPropertyName("level")]
    public double Level { get; set; }

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; set; }

    [JsonPropertyName("pp")]
    public long Pp { get; set; }

    [JsonPropertyName("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("globalRank")]
    public long GlobalRank { get; set; }

    [JsonPropertyName("countryCode")]
    public State CountryCode { get; set; }

    [JsonPropertyName("backgroundColour")]
    public string BackgroundColour { get; set; }
}

public class ResultsScreen
{
    [JsonPropertyName("playerName")]
    public string PlayerName { get; set; }

    [JsonPropertyName("mode")]
    public State Mode { get; set; }

    [JsonPropertyName("score")]
    public long Score { get; set; }

    [JsonPropertyName("accuracy")]
    public long Accuracy { get; set; }

    [JsonPropertyName("hits")]
    public Hits Hits { get; set; }

    [JsonPropertyName("mods")]
    public State Mods { get; set; }

    [JsonPropertyName("rank")]
    public string Rank { get; set; }

    [JsonPropertyName("maxCombo")]
    public long MaxCombo { get; set; }

    [JsonPropertyName("pp")]
    public ResultsScreenPp Pp { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; }
}

public class ResultsScreenPp
{
    [JsonPropertyName("current")]
    public long Current { get; set; }

    [JsonPropertyName("fc")]
    public long Fc { get; set; }
}

public class Session
{
    [JsonPropertyName("playTime")]
    public long PlayTime { get; set; }

    [JsonPropertyName("playCount")]
    public long PlayCount { get; set; }
}

public class Settings
{
    [JsonPropertyName("interfaceVisible")]
    public bool InterfaceVisible { get; set; }

    [JsonPropertyName("replayUIVisible")]
    public bool ReplayUiVisible { get; set; }

    [JsonPropertyName("chatVisibilityStatus")]
    public State ChatVisibilityStatus { get; set; }

    [JsonPropertyName("leaderboard")]
    public Leaderboard Leaderboard { get; set; }

    [JsonPropertyName("progressBar")]
    public State ProgressBar { get; set; }

    [JsonPropertyName("bassDensity")]
    public double BassDensity { get; set; }

    [JsonPropertyName("resolution")]
    public Resolution Resolution { get; set; }

    [JsonPropertyName("client")]
    public SettingsClient Client { get; set; }

    [JsonPropertyName("scoreMeter")]
    public ScoreMeter ScoreMeter { get; set; }

    [JsonPropertyName("cursor")]
    public Cursor Cursor { get; set; }

    [JsonPropertyName("mouse")]
    public Mouse Mouse { get; set; }

    [JsonPropertyName("mania")]
    public Mania Mania { get; set; }

    [JsonPropertyName("sort")]
    public State Sort { get; set; }

    [JsonPropertyName("group")]
    public State Group { get; set; }

    [JsonPropertyName("skin")]
    public Skin Skin { get; set; }

    [JsonPropertyName("mode")]
    public State Mode { get; set; }

    [JsonPropertyName("audio")]
    public Audio Audio { get; set; }

    [JsonPropertyName("background")]
    public Background Background { get; set; }

    [JsonPropertyName("keybinds")]
    public Keybinds Keybinds { get; set; }
}

public class Audio
{
    [JsonPropertyName("ignoreBeatmapSounds")]
    public bool IgnoreBeatmapSounds { get; set; }

    [JsonPropertyName("useSkinSamples")]
    public bool UseSkinSamples { get; set; }

    [JsonPropertyName("volume")]
    public Volume Volume { get; set; }

    [JsonPropertyName("offset")]
    public Offset Offset { get; set; }
}

public class Offset
{
    [JsonPropertyName("universal")]
    public long Universal { get; set; }
}

public class Volume
{
    [JsonPropertyName("master")]
    public long Master { get; set; }

    [JsonPropertyName("music")]
    public long Music { get; set; }

    [JsonPropertyName("effect")]
    public long Effect { get; set; }
}

public class Background
{
    [JsonPropertyName("dim")]
    public long Dim { get; set; }

    [JsonPropertyName("video")]
    public bool Video { get; set; }

    [JsonPropertyName("storyboard")]
    public bool Storyboard { get; set; }
}

public class SettingsClient
{
    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("branch")]
    public long Branch { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }
}

public class Cursor
{
    [JsonPropertyName("useSkinCursor")]
    public bool UseSkinCursor { get; set; }

    [JsonPropertyName("autoSize")]
    public bool AutoSize { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class Keybinds
{
    [JsonPropertyName("osu")]
    public Osu Osu { get; set; }

    [JsonPropertyName("fruits")]
    public Fruits Fruits { get; set; }

    [JsonPropertyName("taiko")]
    public Taiko Taiko { get; set; }

    [JsonPropertyName("quickRetry")]
    public string QuickRetry { get; set; }
}

public class Fruits
{
    [JsonPropertyName("k1")]
    public string K1 { get; set; }

    [JsonPropertyName("k2")]
    public string K2 { get; set; }

    [JsonPropertyName("Dash")]
    public string Dash { get; set; }
}

public class Osu
{
    [JsonPropertyName("k1")]
    public string K1 { get; set; }

    [JsonPropertyName("k2")]
    public string K2 { get; set; }

    [JsonPropertyName("smokeKey")]
    public string SmokeKey { get; set; }
}

public class Taiko
{
    [JsonPropertyName("innerLeft")]
    public string InnerLeft { get; set; }

    [JsonPropertyName("innerRight")]
    public string InnerRight { get; set; }

    [JsonPropertyName("outerLeft")]
    public string OuterLeft { get; set; }

    [JsonPropertyName("outerRight")]
    public string OuterRight { get; set; }
}

public class Leaderboard
{
    [JsonPropertyName("visible")]
    public bool Visible { get; set; }

    [JsonPropertyName("type")]
    public State Type { get; set; }
}

public class Mania
{
    [JsonPropertyName("speedBPMScale")]
    public bool SpeedBpmScale { get; set; }

    [JsonPropertyName("usePerBeatmapSpeedScale")]
    public bool UsePerBeatmapSpeedScale { get; set; }
}

public class Mouse
{
    [JsonPropertyName("rawInput")]
    public bool RawInput { get; set; }

    [JsonPropertyName("disableButtons")]
    public bool DisableButtons { get; set; }

    [JsonPropertyName("disableWheel")]
    public bool DisableWheel { get; set; }

    [JsonPropertyName("sensitivity")]
    public long Sensitivity { get; set; }
}

public class Resolution
{
    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; }

    [JsonPropertyName("width")]
    public long Width { get; set; }

    [JsonPropertyName("height")]
    public long Height { get; set; }

    [JsonPropertyName("widthFullscreen")]
    public long WidthFullscreen { get; set; }

    [JsonPropertyName("heightFullscreen")]
    public long HeightFullscreen { get; set; }
}

public class ScoreMeter
{
    [JsonPropertyName("type")]
    public State Type { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class Skin
{
    [JsonPropertyName("useDefaultSkinInEditor")]
    public bool UseDefaultSkinInEditor { get; set; }

    [JsonPropertyName("ignoreBeatmapSkins")]
    public bool IgnoreBeatmapSkins { get; set; }

    [JsonPropertyName("tintSliderBall")]
    public bool TintSliderBall { get; set; }

    [JsonPropertyName("useTaikoSkin")]
    public bool UseTaikoSkin { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class Tourney
{
    [JsonPropertyName("scoreVisible")]
    public bool ScoreVisible { get; set; }

    [JsonPropertyName("starsVisible")]
    public bool StarsVisible { get; set; }

    [JsonPropertyName("ipcState")]
    public long IpcState { get; set; }

    [JsonPropertyName("bestOF")]
    public long BestOf { get; set; }

    [JsonPropertyName("team")]
    public Team Team { get; set; }

    [JsonPropertyName("points")]
    public Points Points { get; set; }

    [JsonPropertyName("chat")]
    public object[] Chat { get; set; }

    [JsonPropertyName("totalScore")]
    public Points TotalScore { get; set; }

    [JsonPropertyName("clients")]
    public ClientElement[] Clients { get; set; }
}

public class ClientElement
{
    [JsonPropertyName("team")]
    public string Team { get; set; }

    [JsonPropertyName("user")]
    public User User { get; set; }

    [JsonPropertyName("play")]
    public Play Play { get; set; }
}

public class User
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("accuracy")]
    public long Accuracy { get; set; }

    [JsonPropertyName("rankedScore")]
    public long RankedScore { get; set; }

    [JsonPropertyName("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("globalRank")]
    public long GlobalRank { get; set; }

    [JsonPropertyName("totalPP")]
    public long TotalPp { get; set; }
}

public class Points
{
    [JsonPropertyName("left")]
    public long Left { get; set; }

    [JsonPropertyName("right")]
    public long Right { get; set; }
}

public class Team
{
    [JsonPropertyName("left")]
    public string Left { get; set; }

    [JsonPropertyName("right")]
    public string Right { get; set; }
}