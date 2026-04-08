using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ShellPilot.Services;

/// <summary>
/// Manages shell console processes via Named Pipe discovery.
/// Pipe naming: SP.{proxyPid}.{agentId}.{consolePid} (owned) / SP.{consolePid} (unowned)
/// </summary>
public class ConsoleManager
{
    public const string PipePrefix = "SP";

    private readonly ProcessLauncher _launcher;
    private readonly object _lock = new();
    private readonly Dictionary<int, ConsoleInfo> _consoles = new();
    private readonly HashSet<int> _busyPids = new();
    private int _activePid;
    private string? _sessionShell;
    private int _subAgentCounter;

    public int ProxyPid { get; } = Environment.ProcessId;

    public ConsoleManager(ProcessLauncher launcher)
    {
        _launcher = launcher;
    }

    public void Initialize()
    {
        // Future: category-based naming initialization
    }

    public string AllocateSubAgentId()
    {
        var id = $"sa-{Interlocked.Increment(ref _subAgentCounter):x4}";
        return id;
    }

    /// <summary>
    /// Start or reuse a console. Enforces single-shell-type per session.
    /// </summary>
    public async Task<StartConsoleResult> StartConsoleAsync(string? shell, string? cwd, string? reason, string agentId = "default")
    {
        // Enforce single shell type
        if (shell != null && _sessionShell != null && !shell.Equals(_sessionShell, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Session shell is locked to '{_sessionShell}'. Cannot start '{shell}'.");

        bool forceNew = !string.IsNullOrEmpty(reason);

        if (!forceNew)
        {
            var standby = await FindStandbyConsoleAsync(agentId);
            if (standby != null)
            {
                _activePid = standby.Value.Pid;
                return new StartConsoleResult("reused", standby.Value.Pid, standby.Value.DisplayName);
            }
        }

        _sessionShell ??= shell ?? GetDefaultShell();

        var consoleName = $"Console-{Environment.ProcessId}-{_consoles.Count + 1}";
        var title = $"shellpilot {consoleName}";

        // Build command line for the console process
        // TODO: Launch shellpilot.exe --console mode with ConPTY
        // For now, launch the shell directly with CreateProcessW
        var commandLine = $"\"{_sessionShell}\"";
        int pid = _launcher.LaunchWithCleanEnvironment(commandLine, cwd);

        var displayName = $"#{pid} {consoleName}";
        lock (_lock)
        {
            _consoles[pid] = new ConsoleInfo(GetPipePath(agentId, pid), displayName);
            _activePid = pid;
        }

        return new StartConsoleResult("started", pid, displayName);
    }

    /// <summary>
    /// Execute a command on the active console via Named Pipe.
    /// </summary>
    public async Task<ExecuteResult> ExecuteCommandAsync(string command, int timeoutSeconds, string agentId = "default")
    {
        var consolePid = _activePid;
        var pipeName = GetPipeName(agentId, consolePid);

        if (consolePid == 0 || !IsProcessAlive(consolePid))
        {
            // Auto-start console
            var startResult = await StartConsoleAsync(null, null, null, agentId);
            return new ExecuteResult
            {
                Switched = true,
                DisplayName = startResult.DisplayName,
                Output = $"Switched to console {startResult.DisplayName}. Pipeline NOT executed — cd to the correct directory and re-execute.",
            };
        }

        try
        {
            var response = await SendPipeRequestAsync(pipeName, new
            {
                type = "execute",
                id = Guid.NewGuid().ToString(),
                command,
                timeout = timeoutSeconds * 1000,
            }, TimeSpan.FromSeconds(timeoutSeconds + 5));

            var output = response.TryGetProperty("output", out var outputProp) ? outputProp.GetString() ?? "" : "";
            var exitCode = response.TryGetProperty("exitCode", out var exitProp) ? exitProp.GetInt32() : 0;
            var duration = response.TryGetProperty("duration", out var durProp) ? durProp.GetString() ?? "0" : "0";
            var cwdResult = response.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() : null;
            var displayName = _consoles.GetValueOrDefault(consolePid)?.DisplayName ?? $"#{consolePid}";

            return new ExecuteResult
            {
                Output = output,
                ExitCode = exitCode,
                Duration = duration,
                Command = command,
                DisplayName = displayName,
                Cwd = cwdResult,
            };
        }
        catch (TimeoutException)
        {
            var displayName = _consoles.GetValueOrDefault(consolePid)?.DisplayName ?? $"#{consolePid}";
            return new ExecuteResult { TimedOut = true, DisplayName = displayName, Command = command };
        }
    }

    // --- Pipe communication ---

    public static string GetPipeName(string agentId, int consolePid)
        => $"{PipePrefix}.{Environment.ProcessId}.{agentId}.{consolePid}";

    public static string GetPipePath(string agentId, int consolePid)
        => GetPipeName(agentId, consolePid);

    private async Task<JsonElement> SendPipeRequestAsync(string pipeName, object message, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        await client.ConnectAsync(cts.Token);

        var json = JsonSerializer.Serialize(message);
        var msgBytes = Encoding.UTF8.GetBytes(json);
        var lenBytes = BitConverter.GetBytes(msgBytes.Length);

        await client.WriteAsync(lenBytes, cts.Token);
        await client.WriteAsync(msgBytes, cts.Token);
        await client.FlushAsync(cts.Token);

        // Read response
        var recvLenBytes = new byte[4];
        await ReadExactAsync(client, recvLenBytes, cts.Token);
        var recvLen = BitConverter.ToInt32(recvLenBytes);

        var recvBytes = new byte[recvLen];
        await ReadExactAsync(client, recvBytes, cts.Token);

        return JsonSerializer.Deserialize<JsonElement>(recvBytes);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) throw new IOException("Pipe closed");
            offset += read;
        }
    }

    // --- Discovery ---

    private async Task<(int Pid, string DisplayName)?> FindStandbyConsoleAsync(string agentId)
    {
        // TODO: Enumerate existing pipes and find standby consoles
        return null;
    }

    // --- Helpers ---

    private static string GetDefaultShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check SHELLPILOT_SHELL env var first
            var envShell = Environment.GetEnvironmentVariable("SHELLPILOT_SHELL");
            if (!string.IsNullOrEmpty(envShell)) return envShell;
            return "pwsh.exe";
        }
        return Environment.GetEnvironmentVariable("SHELL") ?? "bash";
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return true;
        }
        catch { return false; }
    }

    // --- Types ---

    private record ConsoleInfo(string PipePath, string DisplayName);

    public record StartConsoleResult(string Status, int Pid, string DisplayName);

    public class ExecuteResult
    {
        public string Output { get; set; } = "";
        public int ExitCode { get; set; }
        public string Duration { get; set; } = "0";
        public string? Command { get; set; }
        public string? DisplayName { get; set; }
        public string? Cwd { get; set; }
        public bool Switched { get; set; }
        public bool TimedOut { get; set; }
    }
}
