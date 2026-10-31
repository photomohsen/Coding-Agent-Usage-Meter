// PROTOTYPE: throwaway. End-to-end check of dev-server finding on this PC: `--e2e [port:project,...]`.
// Starts a real Linux server and a real Docker container in WSL (and a Windows python server when python is there),
// checks the widget's scan finds each one with the right project, closes each through the widget's Close, and
// checks it is gone. The optional list names servers already running that must be found too.
using System.Diagnostics;
using System.IO;

namespace UsageWidget.Prototype;

static class E2e
{
    const int LinuxPort = 8797, DockerPort = 8798, WindowsPort = 8796;
    const string Container = "usage-meter-e2e", LinuxFolder = "/tmp/usage-meter-e2e";

    static int failures;

    public static int Run(string? expect)
    {
        var log = new List<string>();
        void Say(string line) { Console.WriteLine(line); log.Add(line); }
        void Check(bool ok, string what) { if (!ok) failures++; Say($"{(ok ? "PASS" : "FAIL")}  {what}"); }

        Say("Servers found now:");
        foreach (var s in Settled()) Say($"  {s.Url,-16} {s.Project,-20} {s.Name,-10} {s.Folder}");

        foreach (var pair in (expect ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var (port, project) = (int.Parse(pair.Split(':')[0]), pair.Split(':')[1]);
            var s = Settled().FirstOrDefault(x => x.Port == port);
            Check(s?.Project == project, $"localhost:{port} is shown as {project} (got {s?.Project ?? "nothing"})");
        }

        // A Linux dev server and a container started in WSL just for this test.
        // setsid, or WSL stops it when this hidden wsl.exe exits.
        Wsl($"mkdir -p {LinuxFolder} && cd {LinuxFolder} && setsid -f python3 -m http.server {LinuxPort} --bind 127.0.0.1 >/dev/null 2>&1");
        Wsl($"docker rm -f {Container} >/dev/null 2>&1; docker run -d --rm --name {Container} -p 127.0.0.1:{DockerPort}:6379 redis:6.2-alpine");
        Process? windows = null;
        try { windows = Process.Start(new ProcessStartInfo("python", $"-m http.server {WindowsPort} --bind 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false }); }
        catch { Say("SKIP  no python on Windows, so no Windows server check"); }

        var linux = WaitFor(LinuxPort, s => s.Wsl != null);
        Check(linux?.Project == "usage-meter-e2e" && linux.Name == "python3" && linux.Wsl?.LinuxPid != null,
            $"WSL python server on :{LinuxPort} found as usage-meter-e2e / python3 (got {linux?.Project} / {linux?.Name})");
        Check(linux?.Folder == $@"\\wsl.localhost\{linux?.Wsl?.Distro}\tmp\usage-meter-e2e", $"its folder is the WSL path (got {linux?.Folder})");
        var docker = WaitFor(DockerPort, s => s.Wsl != null);
        Check(docker?.Project == Container && docker.Name == "docker" && docker.Wsl?.Containers.SequenceEqual([Container]) == true,
            $"WSL container on :{DockerPort} found as {Container} / docker (got {docker?.Project} / {docker?.Name})");
        DevServer? win = null;
        if (windows != null)
        {
            win = WaitFor(WindowsPort, _ => true);
            Check(win?.Name == "python" && win.Pid == windows.Id, $"Windows python server on :{WindowsPort} found (got {win?.Name} PID {win?.Pid})");
        }

        // Close each through the widget, as its Close button does, and check it is gone.
        foreach (var (s, what) in new[] { (linux, "WSL python server"), (docker, "WSL container"), (win, "Windows python server") })
        {
            if (s == null) continue;
            var error = DevServers.Close(s);
            Check(error == null, $"Close {what} :{s.Port} reports no error{(error != null ? $" (got: {error})" : "")}");
            Check(WaitFor(s.Port, _ => true, gone: true) == null, $"{what} :{s.Port} is gone after Close");
        }
        Check(Process.GetProcessesByName("wslrelay").Length > 0, "wslrelay still runs, so other WSL ports keep working");
        foreach (var pair in (expect ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var port = int.Parse(pair.Split(':')[0]);
            Check(Settled().Any(x => x.Port == port), $"localhost:{port} is still there after the closes");
        }

        // Leave nothing behind, whatever failed.
        Wsl($"docker rm -f {Container} >/dev/null 2>&1; pkill -f 'http.server {LinuxPort}'; rm -rf {LinuxFolder}; true");
        try { if (windows is { HasExited: false }) windows.Kill(); } catch { }

        Say(failures == 0 ? "E2E PASSED" : $"E2E FAILED: {failures} check(s)");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "e2e.log"), log);
        return failures == 0 ? 0 : 1;
    }

    /// A scan once the WSL lookup it started is done, so WSL ports have their projects.
    static List<DevServer> Settled()
    {
        DevServers.Scan();
        UsageWidget.Prototype.Wsl.WaitIdle(TimeSpan.FromSeconds(20));
        return DevServers.Scan();
    }

    /// Scans until the port shows up and matches (or is gone, with `gone`), for up to 30 seconds.
    static DevServer? WaitFor(int port, Func<DevServer, bool> match, bool gone = false)
    {
        DevServer? s = null;
        for (var i = 0; i < 30; i++)
        {
            s = Settled().FirstOrDefault(x => x.Port == port);
            if (gone ? s == null : s != null && match(s)) return s;
            Thread.Sleep(1000);
        }
        return s;
    }

    static void Wsl(string script)
    {
        var psi = new ProcessStartInfo("wsl.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-e", "sh", "-c", script }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(60000);
    }
}
