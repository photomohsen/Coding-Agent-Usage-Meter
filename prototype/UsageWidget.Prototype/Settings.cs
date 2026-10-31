// PROTOTYPE: throwaway. Settings in %APPDATA%\ClaudeUsageWidget\settings.json (the path the spec names).
using System.IO;
using System.Text.Json;

namespace UsageWidget.Prototype;

class Settings
{
    public int Look { get; set; } = 1; // B · Pill
    public double Scale { get; set; } = 1.0;
    public double IdleOpacity { get; set; } = 0.85;
    public bool Topmost { get; set; } = true;
    public int UpdateMinutes { get; set; } = 5;
    /// Same parts in every look, with less space around and between them.
    public bool Compact { get; set; }
    /// 0 two bars, 1 two rings.
    public int TrayStyle { get; set; }
    /// Codex Usage under Claude's; only has an effect when Codex is on this machine.
    public bool ShowCodex { get; set; } = true;
    /// Null until the user drags it: then it sits just above the clock.
    public double? Left { get; set; }
    public double? Top { get; set; }

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageWidget", "settings.json");

    /// A missing or corrupt file gives the defaults.
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { } // a failed save must not take the widget down
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
