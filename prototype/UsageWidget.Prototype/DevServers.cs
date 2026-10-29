// PROTOTYPE: throwaway. Dev servers: local dev programs (node, python, dotnet...) listening on a TCP port of this PC.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UsageWidget.Prototype;

record DevServer(int Pid, string Name, int Port, bool LoopbackOnly)
{
    public string Url => $"localhost:{Port}";
    /// The project folder's name, or the program's name when no project folder is found.
    public string Project { get; init; } = Name;
    public string? Folder { get; init; }
    public string? CommandLine { get; init; }
}

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
            if (!DevPrograms.Contains(name)) continue;
            // The same port on IPv4 and IPv6 is one server; it is loopback-only if every socket is.
            found[(pid, port)] = found.TryGetValue((pid, port), out var seen)
                ? seen with { LoopbackOnly = seen.LoopbackOnly && loopback }
                : new DevServer(pid, name, port, loopback);
        }
        // A server's project doesn't change while it runs, so it is looked up once.
        foreach (var gone in projects.Keys.Except(found.Keys).ToList()) projects.Remove(gone);
        return found.Values
            .Select(s => projects.TryGetValue((s.Pid, s.Port), out var p) ? p with { LoopbackOnly = s.LoopbackOnly }
                : projects[(s.Pid, s.Port)] = WithProject(s))
            .OrderBy(s => s.Port).ToList();
    }

    static readonly Dictionary<(int Pid, int Port), DevServer> projects = [];

    /// Files that mark a folder as a project root.
    static readonly string[] Markers =
        ["package.json", "*.csproj", "*.sln", "pyproject.toml", "requirements.txt", "wrangler.*", "deno.json", ".git"];

    /// The project is the nearest folder with a marker, above the working folder or a path in the command line.
    static DevServer WithProject(DevServer server)
    {
        var (cwd, cmd) = ProcessInfo.Read(server.Pid);
        var starts = new List<string>();
        if (cwd != null) starts.Add(cwd);
        if (cmd != null)
            starts.AddRange(cmd.Split('"', ' ').Where(a => a.Length > 3 && Path.IsPathFullyQualified(a)));
        foreach (var start in starts)
            if (ProjectRoot(start) is { } root)
                return server with { Project = Path.GetFileName(root), Folder = root, CommandLine = cmd };
        return server with { Folder = cwd, CommandLine = cmd };
    }

    static string? ProjectRoot(string path)
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
                if (Markers.Any(m => dir.EnumerateFileSystemInfos(m).Any())) return dir.FullName;
            }
        }
        catch { } // no access, bad path
        return null;
    }

    /// Stops the server's process and everything it started (npm → node, dotnet watch → app).
    public static string? Close(DevServer server)
    {
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
