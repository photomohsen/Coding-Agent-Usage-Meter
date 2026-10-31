// PROTOTYPE: throwaway. Settings in %APPDATA%\ClaudeUsageWidget\settings.json (the path the spec names).
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageWidget.Prototype;

class Settings
{
    /// In the order of LookOptions.Names.
    public int Look { get; set; }
    /// 2 since the ten looks became six; a file without it holds an old look number.
    public int Version { get; set; }
    public double Scale { get; set; } = 1.0;
    public double IdleOpacity { get; set; } = 0.85;
    public bool Topmost { get; set; } = true;
    public int UpdateMinutes { get; set; } = 5;
    /// Each look's Compact set of parts instead of its Full set.
    public bool Compact { get; set; }
    /// What each look shows, by look name; a look missing here uses LookOptions.Default.
    public Dictionary<string, LookSets> Looks { get; set; } = [];
    /// 0 two bars, 1 two rings.
    public int TrayStyle { get; set; }
    /// Codex Usage under Claude's; only has an effect when Codex is on this machine.
    public bool ShowCodex { get; set; } = true;
    /// Null until the user drags it: then it sits just above the clock.
    public double? Left { get; set; }
    public double? Top { get; set; }

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageWidget", "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// A missing or corrupt file gives the defaults.
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new() { Version = 2 };
            if (s.Version < 2) s.FromTenLooks();
            return s;
        }
        catch { return new() { Version = 2 }; }
    }

    /// The parts look `look` shows in the current set.
    public LookOptions Options(int look)
    {
        var name = LookOptions.Names[look];
        if (!Looks.TryGetValue(name, out var sets)) Looks[name] = sets = new();
        return Compact ? sets.Compact ??= LookOptions.Default(look, true) : sets.Full ??= LookOptions.Default(look, false);
    }

    /// Back to the suggested parts for the current set of `look`.
    public void ResetOptions(int look)
    {
        if (!Looks.TryGetValue(LookOptions.Names[look], out var sets)) return;
        if (Compact) sets.Compact = null; else sets.Full = null;
    }

    /// The old looks (Rings, Pill, Strip, Gauge, Meter, Target, Numbers, Mini, Pace, Halo) as one of the six with its parts.
    void FromTenLooks()
    {
        int[] to = [1, 0, 4, 3, 0, 2, 5, 0, 0, 2];
        var old = Math.Clamp(Look, 0, to.Length - 1);
        Look = to[old];
        Version = 2;
        if (old is 7 or 9) Compact = true; // Mini, Halo
        if (old is not (4 or 8)) return;
        var full = LookOptions.Default(0, false);
        if (old == 4) full.BarStyle = BarStyle.Blocks; // Meter
        else full.Tick = full.PaceWords = true; // Pace
        Looks[LookOptions.Names[0]] = new() { Full = full };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { } // a failed save must not take the widget down
    }
}

class LookSets
{
    public LookOptions? Full { get; set; }
    public LookOptions? Compact { get; set; }
}

enum Limits { Both, Session, Higher }
enum Arrangement { Stacked, SideBySide }
enum BarStyle { Smooth, Blocks }
enum BarLength { Short, Long }
enum ResetShow { Off, TimeLeft, ClockTime }
enum StaleShow { Reason, Icon }

/// Which parts a look shows. Each look has a Full and a Compact set; spacing is the same in both.
class LookOptions
{
    public static readonly string[] Names = ["Bars", "Rings", "Target", "Gauge", "Strip", "Numbers"];

    public Limits Limits { get; set; }
    public Arrangement Arrange { get; set; } = Arrangement.SideBySide;
    public bool Logo { get; set; } = true;
    public bool Pct { get; set; } = true;
    public bool Labels { get; set; } = true;
    public BarStyle BarStyle { get; set; }
    public BarLength BarLength { get; set; } = BarLength.Long;
    /// A white mark where an even spread of the use until the Reset would be.
    public bool Tick { get; set; }
    public bool PaceWords { get; set; }
    public bool Hairline { get; set; } = true;
    public ResetShow Reset { get; set; } = ResetShow.TimeLeft;
    /// The reset time on its side, read bottom to top, beside the rows instead of in a row of its own.
    public bool Rotate { get; set; }
    public bool Updated { get; set; }
    /// Stale always shows: the ⚠ with its short reason, or the ⚠ alone.
    public StaleShow Stale { get; set; }

    /// The parts a look's Settings lists, in order; the others don't apply to it.
    public static string[] Shown(int look) => look switch
    {
        0 => ["Limits", "Arrange", "Logo", "Pct", "Labels", "BarStyle", "BarLength", "Tick", "PaceWords", "Reset", "Rotate", "Updated", "Stale"],
        3 => ["Limits", "Arrange", "Logo", "Pct", "Labels", "Reset", "Rotate", "Updated", "Stale"],
        5 => ["Limits", "Arrange", "Logo", "Labels", "Hairline", "Reset", "Rotate", "Updated", "Stale"],
        _ => ["Limits", "Arrange", "Logo", "Pct", "Labels", "Reset", "Updated", "Stale"],
    };

    /// The suggested sets, as approved on the proposal page.
    public static LookOptions Default(int look, bool compact)
    {
        var o = new LookOptions();
        if (compact) { o.Labels = false; o.Stale = StaleShow.Icon; }
        switch (look)
        {
            case 0: // Bars
                o.Arrange = Arrangement.Stacked;
                if (compact) { o.Limits = Limits.Higher; o.BarLength = BarLength.Short; }
                else o.Rotate = true;
                break;
            case 2 when compact: o.Pct = false; break; // Target
            case 3 when compact: o.Rotate = true; break; // Gauge
            case 4 when compact: o.Reset = ResetShow.Off; break; // Strip
            case 5: // Numbers
                if (compact) { o.Limits = Limits.Higher; o.Hairline = false; }
                else { o.Arrange = Arrangement.Stacked; o.Rotate = true; }
                break;
        }
        return o;
    }
}

/// Start with Windows is the launcher in the user's Startup folder, not a stored value: the file is the truth.
static class StartWithWindows
{
    static readonly string Launcher = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Claude Gauge.vbs");

    public static bool IsOn => File.Exists(Launcher);

    public static void Set(bool on)
    {
        if (!on) { File.Delete(Launcher); return; }
        // Through the signed dotnet.exe (company app control blocks our own .exe); 0 hides the console window.
        var dll = typeof(StartWithWindows).Assembly.Location;
        File.WriteAllText(Launcher, $"CreateObject(\"WScript.Shell\").Run \"dotnet \"\"{dll}\"\"\", 0\r\n");
    }
}
