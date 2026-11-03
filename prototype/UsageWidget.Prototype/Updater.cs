// PROTOTYPE: throwaway. Finds out if GitHub has a newer version, and runs the installer to get it.
using System.IO;
using System.Net.Http;

namespace UsageWidget.Prototype;

static class Updater
{
    const string Repo = "photomohsen/Coding-Agent-Usage-Meter";

    /// The folder that holds Start Widget.vbs and Install.cmd: where the widget was installed.
    public static string? Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Start Widget.vbs"))) return dir.FullName;
            return null;
        }
    }

    /// A git clone is updated with git pull, not by this installer.
    public static bool IsClone => Root is { } r && Directory.Exists(Path.Combine(r, ".git"));

    /// The commit this copy was installed from; install.ps1 writes it. Null for a ZIP from before updates, or a clone.
    public static string? Installed
    {
        get
        {
            try { return Root is { } r ? File.ReadAllText(Path.Combine(r, "version.txt")).Trim() : null; }
            catch { return null; }
        }
    }

    /// The newest commit on GitHub's main branch; null when GitHub can't be reached.
    public static async Task<string?> LatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CodingAgentUsageMeter");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.sha");
            var sha = (await http.GetStringAsync($"https://api.github.com/repos/{Repo}/commits/main")).Trim();
            return sha.Length == 40 ? sha : null;
        }
        catch { return null; }
    }

    /// True when GitHub has a commit this copy lacks (also when this copy can't say which it has).
    public static bool IsNewer(string latest) => !string.Equals(Installed, latest, StringComparison.OrdinalIgnoreCase);

    /// Runs the installer in a visible window, so a failed build shows. The caller quits the widget right after.
    public static void Install() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
            $"-NoProfile -Command \"irm https://raw.githubusercontent.com/{Repo}/main/install.ps1 | iex\"") { UseShellExecute = true });
}
