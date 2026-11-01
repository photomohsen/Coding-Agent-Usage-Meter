// PROTOTYPE: throwaway. Dev servers: local dev programs (node, python, dotnet...) listening on a TCP port of this PC,
// and ports that WSL forwards to this PC (Docker containers or Linux programs running there).
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UsageWidget.Prototype;

record DevServer(int Pid, string Name, int Port, bool LoopbackOnly)
{
    public string Url => $"localhost:{Port}";
    /// The project folder's name, or the program's name when no project folder is found.
    public string Project { get; init; } = Name;
    /// The <title> of the page the server serves at /, once it has been read.
    public string? Title { get; init; }
    /// What the row shows: the page's title, or the project when the page has none.
    public string Shown => Title ?? Project;
    public string? Folder { get; init; }
    public string? CommandLine { get; init; }
    /// Set when WSL forwards the port: what runs it there. Pid is then wslrelay's, which serves every WSL port.
    public WslTarget? Wsl { get; init; }
}

/// What runs a port inside WSL: a Docker Compose project's containers, or one Linux process.
record WslTarget(string Distro, string[] Containers, int? LinuxPid);

static class DevServers
{
    /// Only these programs count, so system services that also listen (svchost, sql, ...) never get a Close button.
    static readonly HashSet<string> DevPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "deno", "bun", "python", "pythonw", "py", "dotnet", "java", "ruby", "php",
        "workerd", "uvicorn", "hugo", "go", "esbuild", "vite",
    };

    public static List<DevServer> Scan()
    {
        var found = new Dictionary<(int Pid, int Port), DevServer>();
        var me = Environment.ProcessId;
        foreach (var (pid, port, loopback) in Listeners(AfInet).Concat(Listeners(AfInet6)))
        {
            if (pid == me || pid == 0) continue;
            string name;
            try { using var p = Process.GetProcessById(pid); name = p.ProcessName; }
            catch { continue; } // gone, or not ours to read
            if (!DevPrograms.Contains(name) && !name.Equals(Wsl.Relay, StringComparison.OrdinalIgnoreCase)) continue;
            // The same port on IPv4 and IPv6 is one server; it is loopback-only if every socket is.
            found[(pid, port)] = found.TryGetValue((pid, port), out var seen)
                ? seen with { LoopbackOnly = seen.LoopbackOnly && loopback }
                : new DevServer(pid, name, port, loopback);
        }
        // A server's project doesn't change while it runs, so it is looked up once.
        foreach (var gone in projects.Keys.Except(found.Keys).ToList()) projects.Remove(gone);
        // WSL ports are looked up in WSL in the background, so until that is done they show as just "WSL".
        var relayed = found.Values.Where(s => s.Name.Equals(Wsl.Relay, StringComparison.OrdinalIgnoreCase)).ToList();
        Wsl.Refresh(relayed.Select(s => s.Port));
        PageTitles.Keep(found.Keys);
        return found.Values
            .Select(s => relayed.Contains(s) ? Wsl.Describe(s)
                : projects.TryGetValue((s.Pid, s.Port), out var p) ? p with { LoopbackOnly = s.LoopbackOnly }
                : projects[(s.Pid, s.Port)] = WithProject(s))
            .Select(s => s with { Title = PageTitles.Get(s.Pid, s.Port) })
            .OrderBy(s => s.Port).ToList();
    }

    static readonly Dictionary<(int Pid, int Port), DevServer> projects = [];

    /// Files that mark a folder as a project root.
    static readonly string[] Markers =
        ["package.json", "*.csproj", "*.sln", "pyproject.toml", "requirements.txt", "wrangler.*", "deno.json", ".git"];

    /// The folder is the nearest one with a marker, above the working folder or a path in the command line.
    /// The project is named after the git repo that holds that folder, so a server started in a subfolder
    /// (REA-Accounting\app) shows as REA-Accounting, not app. Without a repo, the folder names it.
    static DevServer WithProject(DevServer server)
    {
        var (cwd, cmd) = ProcessInfo.Read(server.Pid);
        var starts = new List<string>();
        if (cwd != null) starts.Add(cwd);
        if (cmd != null)
            starts.AddRange(cmd.Split('"', ' ').Where(a => a.Length > 3 && Path.IsPathFullyQualified(a)));
        foreach (var start in starts)
            if (Nearest(start, Markers) is { } root)
                return server with { Project = Path.GetFileName(MainRepo(Nearest(root, [".git"])) ?? root), Folder = root, CommandLine = cmd };
        return server with { Folder = cwd, CommandLine = cmd };
    }

    /// A git worktree's .git is a file that points into its main repo (<repo>\.git\worktrees\<name>),
    /// so a worktree with a made-up name (competent-payne-f88c3a) is named after the main repo.
    static string? MainRepo(string? repo)
    {
        if (repo == null) return null;
        try
        {
            var git = Path.Combine(repo, ".git");
            if (!File.Exists(git)) return repo;
            var link = File.ReadLines(git).FirstOrDefault(l => l.StartsWith("gitdir:"))?[7..].Trim();
            if (link == null) return repo;
            var dir = Path.GetFullPath(link.Replace('/', '\\'), repo);
            var at = dir.LastIndexOf(@"\.git\worktrees\", StringComparison.OrdinalIgnoreCase);
            return at > 0 ? dir[..at] : repo;
        }
        catch { return repo; } // no access, bad path
    }

    /// The nearest folder at or above the path that holds one of the markers.
    static string? Nearest(string path, string[] markers)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        try
        {
            var dir = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
            for (; dir != null; dir = dir.Parent)
            {
                // Stop at the user folder and Program Files: those are never one project.
                if (dir.FullName.TrimEnd('\\').Equals(home, StringComparison.OrdinalIgnoreCase) ||
                    dir.FullName.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase) ||
                    dir.FullName.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)) return null;
                if (dir.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                if (dir.Parent?.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) == true) continue;
                if (markers.Any(m => dir.EnumerateFileSystemInfos(m).Any())) return dir.FullName;
            }
        }
        catch { } // no access, bad path
        return null;
    }

    /// Stops the server's process and everything it started (npm → node, dotnet watch → app).
    /// A WSL port stops what runs it in WSL; wslrelay itself is never stopped, since it forwards every WSL port.
    public static string? Close(DevServer server)
    {
        if (server.Pid != 0 && IsRelay(server.Pid)) return Wsl.Close(server);
        try
        {
            using var p = Process.GetProcessById(server.Pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            return null;
        }
        catch (ArgumentException) { return null; } // already gone
        catch (Exception e) { return e.Message; }
    }

    static bool IsRelay(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName.Equals(Wsl.Relay, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    const int AfInet = 2, AfInet6 = 23, TcpTableOwnerPidListener = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);

    static IEnumerable<(int Pid, int Port, bool Loopback)> Listeners(int af)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TcpTableOwnerPidListener, 0);
        var buf = Marshal.AllocHGlobal(size);
        var rows = new List<(int, int, bool)>();
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, af, TcpTableOwnerPidListener, 0) != 0) return rows;
            var count = Marshal.ReadInt32(buf);
            // MIB_TCPROW_OWNER_PID is 24 bytes; MIB_TCP6ROW_OWNER_PID is 56.
            var rowSize = af == AfInet ? 24 : 56;
            for (var i = 0; i < count; i++)
            {
                var row = buf + 4 + i * rowSize;
                bool loopback;
                int rawPort, pid;
                if (af == AfInet)
                {
                    loopback = (Marshal.ReadInt32(row, 4) & 0xFF) == 127;
                    rawPort = Marshal.ReadInt32(row, 8);
                    pid = Marshal.ReadInt32(row, 20);
                }
                else
                {
                    var addr = new byte[16];
                    Marshal.Copy(row, addr, 0, 16);
                    loopback = addr.Take(15).All(b => b == 0) && addr[15] == 1; // ::1
                    rawPort = Marshal.ReadInt32(row, 20);
                    pid = Marshal.ReadInt32(row, 52);
                }
                var port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF); // network byte order
                rows.Add((pid, port, loopback));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return rows;
    }
}

/// The <title> of the page each server serves at http://localhost:port/, read in the background once per server.
/// A server that doesn't answer yet (still starting) is asked again a little later. Until then, and for servers
/// that serve no HTML page (an API, a debugger port), the row shows the project.
static class PageTitles
{
    static readonly HttpClient http = new(new SocketsHttpHandler
    {
        MaxAutomaticRedirections = 3,
        // Only ever localhost, where dev servers use their own self-signed certificates.
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
    }) { Timeout = TimeSpan.FromSeconds(3) };

    static readonly Dictionary<(int Pid, int Port), string?> known = [];
    static readonly Dictionary<(int Pid, int Port), DateTime> asked = [];
    static readonly object gate = new();
    const int MaxBytes = 256 * 1024;

    /// The title once read, or null; starts a read when none is known and none was tried in the last 10 seconds.
    public static string? Get(int pid, int port)
    {
        lock (gate)
        {
            if (known.TryGetValue((pid, port), out var title)) return title;
            if (asked.TryGetValue((pid, port), out var at) && DateTime.Now - at < TimeSpan.FromSeconds(10)) return null;
            asked[(pid, port)] = DateTime.Now;
        }
        _ = Task.Run(async () =>
        {
            var title = await Read(port);
            if (title is { } t) lock (gate) known[(pid, port)] = t.Length > 0 ? t : null;
        });
        return null;
    }

    /// Forgets servers that stopped, so a new server on the same port is read again.
    public static void Keep(IEnumerable<(int Pid, int Port)> running)
    {
        var keep = running.ToHashSet();
        lock (gate)
        {
            foreach (var gone in known.Keys.Where(k => !keep.Contains(k)).ToList()) known.Remove(gone);
            foreach (var gone in asked.Keys.Where(k => !keep.Contains(k)).ToList()) asked.Remove(gone);
        }
    }

    /// The page's title, "" when it answered without one, or null when it didn't answer.
    static async Task<string?> Read(int port)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/");
            request.Headers.Accept.ParseAdd("text/html"); // Vite and others only serve index.html to a browser
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/html") return "";
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[MaxBytes];
            int read = 0, n;
            while (read < MaxBytes && (n = await stream.ReadAsync(buffer.AsMemory(read))) > 0) read += n;
            var html = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            var m = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? Regex.Replace(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), @"\s+", " ").Trim() : "";
        }
        catch { return null; } // not up yet, no HTTP on this port, or too slow
    }
}

/// Ports that WSL forwards to this PC, and what runs each one inside WSL. One wsl.exe call per change in the ports.
static class Wsl
{
    public const string Relay = "wslrelay";

    record Found(string Project, string? Folder, string Name, string? CommandLine, WslTarget Target);

    static Dictionary<int, Found> known = [];
    static string knownPorts = "";
    static bool looking;
    static readonly object gate = new();

    /// Starts a lookup in the background when the forwarded ports changed since the last one.
    public static void Refresh(IEnumerable<int> ports)
    {
        var key = string.Join(",", ports.Order());
        lock (gate)
        {
            if (key == knownPorts || looking || key == "") return;
            looking = true;
        }
        Task.Run(() =>
        {
            var found = Lookup();
            lock (gate) { known = found ?? []; knownPorts = key; looking = false; }
        });
    }

    /// Waits for a lookup in flight; for the end-to-end test.
    public static void WaitIdle(TimeSpan timeout)
    {
        var until = DateTime.Now + timeout;
        while (DateTime.Now < until)
        {
            lock (gate) if (!looking) return;
            Thread.Sleep(100);
        }
    }

    public static DevServer Describe(DevServer relay)
    {
        Found? f;
        lock (gate) known.TryGetValue(relay.Port, out f);
        return f == null
            ? relay with { Project = "WSL", CommandLine = "forwarded from WSL by wslrelay" }
            : relay with { Name = f.Name, Project = f.Project, Folder = f.Folder, CommandLine = f.CommandLine, Wsl = f.Target };
    }

    /// The sh script run in the default WSL distro: its name, the running containers, and the Linux listeners
    /// with their working folder and the git repo that holds it.
    const string Script = """
        echo "d|$WSL_DISTRO_NAME"
        if command -v docker >/dev/null 2>&1; then
          docker ps --format '{{.Names}}|{{.Ports}}|{{.Label "com.docker.compose.project"}}|{{.Label "com.docker.compose.project.working_dir"}}|{{.Image}}' 2>/dev/null | sed 's/^/c|/'
        fi
        ss -ltnpH 2>/dev/null | sed 's/^/s|/'
        for p in $(ss -ltnpH 2>/dev/null | grep -o 'pid=[0-9]*' | cut -d= -f2 | sort -u); do
          c=$(readlink /proc/$p/cwd); r=""
          [ -n "$c" ] && r=$(git -C "$c" rev-parse --show-toplevel 2>/dev/null)
          echo "p|$p|$c|$r|$(tr '\0' ' ' < /proc/$p/cmdline)"
        done
        """;

    static Dictionary<int, Found>? Lookup()
    {
        if (Run(["-e", "sh", "-c", Script.Replace("\r", "")], 15000, out _) is not { } output) return null;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var distro = lines.FirstOrDefault(l => l.StartsWith("d|"))?[2..] ?? "WSL";
        string? Windows(string? linux) => string.IsNullOrEmpty(linux) ? null : $@"\\wsl.localhost\{distro}{linux.Replace('/', '\\')}";
        var result = new Dictionary<int, Found>();

        // Containers first: each published host port belongs to its Compose project (or the lone container).
        var containers = lines.Where(l => l.StartsWith("c|")).Select(l => l[2..].Split('|')).Where(c => c.Length >= 5).ToList();
        foreach (var c in containers)
        {
            var (name, ports, project, dir, image) = (c[0], c[1], c[2], c[3], c[4]);
            string[] group = project == "" ? [name] : containers.Where(o => o[2] == project).Select(o => o[0]).Order().ToArray();
            foreach (Match m in Regex.Matches(ports, @"[^ ,]*:(\d+)(?:-(\d+))?->"))
            {
                var from = int.Parse(m.Groups[1].Value);
                var to = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : from;
                for (var port = from; port <= to; port++)
                    result.TryAdd(port, new Found(project == "" ? name : project, Windows(dir), "docker",
                        $"{name} · {image}", new WslTarget(distro, group, null)));
            }
        }

        // Then Linux programs listening in WSL (a node or python dev server started there).
        var procs = lines.Where(l => l.StartsWith("p|")).Select(l => l[2..].Split('|', 4)).Where(p => p.Length == 4)
            .ToDictionary(p => int.Parse(p[0]), p => (Cwd: p[1], Repo: p[2], Cmd: p[3].Trim()));
        foreach (var l in lines.Where(l => l.StartsWith("s|")))
        {
            var fields = l[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !int.TryParse(fields[3][(fields[3].LastIndexOf(':') + 1)..], out var port)) continue;
            if (result.ContainsKey(port) || Regex.Match(l, @"\(""([^""]+)"",pid=(\d+)") is not { Success: true } u) continue;
            var pid = int.Parse(u.Groups[2].Value);
            var (cwd, repo, cmd) = procs.TryGetValue(pid, out var p) ? p : ("", "", "");
            // The git repo names the project, as on Windows. The home folder is never one project, so the program's
            // name stands in for it.
            static bool Home(string dir) => dir.StartsWith("/home/") && dir.Count(ch => ch == '/') == 2;
            var named = repo is "" or "/" || Home(repo) ? cwd : repo;
            result[port] = new Found(named is "" or "/" || Home(named) ? u.Groups[1].Value : named[(named.LastIndexOf('/') + 1)..],
                Windows(cwd), u.Groups[1].Value, cmd == "" ? null : cmd, new WslTarget(distro, [], pid));
        }
        return result;
    }

    /// Stops the port's containers (they keep their data) or its Linux process.
    public static string? Close(DevServer server)
    {
        if (server.Wsl is not { } t) return "It isn't known yet what runs this port in WSL, so it was left running. Try again in a few seconds.";
        string[] args = t.Containers.Length > 0 ? ["-e", "docker", "stop", .. t.Containers]
            : t.LinuxPid is { } pid ? ["-e", "kill", pid.ToString()] : [];
        if (args.Length == 0) return "Nothing to stop.";
        return Run(args, 60000, out var error) == null ? error : null;
    }

    static string? Run(string[] args, int timeoutMs, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo("wsl.exe")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(true); error = "WSL didn't answer in time."; return null; }
            if (p.ExitCode != 0) { error = err.Result.Trim() is { Length: > 0 } e ? e : $"wsl.exe exited with {p.ExitCode}"; return null; }
            return output.Result;
        }
        catch (Exception e) { error = e.Message; return null; }
    }
}

/// A process's working folder and command line, read from its PEB (64-bit processes of this user).
static class ProcessInfo
{
    const int QueryInformation = 0x0400, VmRead = 0x0010;

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, long[] info, int size, out int len);

    public static (string? Cwd, string? CommandLine) Read(int pid)
    {
        var h = OpenProcess(QueryInformation | VmRead, false, pid);
        if (h == IntPtr.Zero) return (null, null);
        try
        {
            // PROCESS_BASIC_INFORMATION: PebBaseAddress is the second pointer.
            var pbi = new long[6];
            if (NtQueryInformationProcess(h, 0, pbi, 48, out _) != 0) return (null, null);
            var parameters = ReadPtr(h, (IntPtr)(pbi[1] + 0x20)); // PEB.ProcessParameters
            if (parameters == IntPtr.Zero) return (null, null);
            // RTL_USER_PROCESS_PARAMETERS: CurrentDirectory.DosPath at 0x38, CommandLine at 0x70.
            return (ReadString(h, parameters + 0x38)?.TrimEnd('\\'), ReadString(h, parameters + 0x70));
        }
        catch { return (null, null); }
        finally { CloseHandle(h); }
    }

    static IntPtr ReadPtr(IntPtr h, IntPtr addr)
    {
        var b = new byte[8];
        return ReadProcessMemory(h, addr, b, 8, out _) ? (IntPtr)BitConverter.ToInt64(b) : IntPtr.Zero;
    }

    /// A UNICODE_STRING: ushort length in bytes, then the buffer pointer at +8.
    static string? ReadString(IntPtr h, IntPtr addr)
    {
        var head = new byte[16];
        if (!ReadProcessMemory(h, addr, head, 16, out _)) return null;
        int len = BitConverter.ToUInt16(head, 0);
        if (len == 0) return null;
        var text = new byte[len];
        return ReadProcessMemory(h, (IntPtr)BitConverter.ToInt64(head, 8), text, len, out _)
            ? System.Text.Encoding.Unicode.GetString(text) : null;
    }
}
