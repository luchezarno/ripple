using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SplashShell.Services;

namespace SplashShell.Tests;

/// <summary>
/// E2E test: launch ConsoleWorker in --console mode, send commands via Named Pipe.
/// Tests ConPTY + shell integration + OSC parsing + command tracking.
/// </summary>
public class ConsoleWorkerTests
{
    /// <summary>
    /// Quick unit tests for UnescapeInput — runs without PTY/pipe setup.
    /// </summary>
    public static void RunUnitTests()
    {
        var pass = 0;
        var fail = 0;
        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        Console.WriteLine("=== ConsoleWorker Unit Tests ===");

        // UnescapeInput
        Assert(ConsoleWorker.UnescapeInput("hello") == "hello", "unescape: plain text unchanged");
        Assert(ConsoleWorker.UnescapeInput("abc\\r") == "abc\r", "unescape: \\r → CR");
        Assert(ConsoleWorker.UnescapeInput("abc\\n") == "abc\n", "unescape: \\n → LF");
        Assert(ConsoleWorker.UnescapeInput("abc\\t") == "abc\t", "unescape: \\t → TAB");
        Assert(ConsoleWorker.UnescapeInput("\\x03") == "\x03", "unescape: \\x03 → ETX");
        Assert(ConsoleWorker.UnescapeInput("\\x1b[A") == "\x1b[A", "unescape: \\x1b[A → ESC[A");
        Assert(ConsoleWorker.UnescapeInput("a\\\\b") == "a\\b", "unescape: \\\\\\\\ → literal backslash");
        Assert(ConsoleWorker.UnescapeInput("\\r").Length == 1, "unescape: \\r length is 1");
        Assert(ConsoleWorker.UnescapeInput("\\x03").Length == 1, "unescape: \\x03 length is 1");

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }

    public static async Task Run()
    {
        var pass = 0;
        var fail = 0;

        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        Console.WriteLine("=== ConsoleWorker E2E Tests ===");

        // Find splashshell executable
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null)
        {
            Console.Error.WriteLine("  SKIP: Cannot determine exe path");
            return;
        }

        var proxyPid = Environment.ProcessId;
        var agentId = "test";
        var shell = OperatingSystem.IsWindows() ? "pwsh.exe" : "bash";
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Launch worker via ProcessLauncher (uses CREATE_NEW_CONSOLE on Windows,
        // required so the worker process has a console for ConPTY to attach to)
        var launcher = new Services.ProcessLauncher();
        int workerPid = launcher.LaunchConsoleWorker(proxyPid, agentId, shell, cwd);

        var pipeName = $"SP.{proxyPid}.{agentId}.{workerPid}";
        Console.WriteLine($"  Pipe: {pipeName}, Worker PID: {workerPid}");

        // Wait for pipe to be ready (up to 30s)
        Console.WriteLine("  Waiting for pipe...");
        var ready = await WaitForPipeAsync(pipeName, TimeSpan.FromSeconds(30));
        Assert(ready, "Worker pipe became ready");

        if (!ready)
        {
            try { Process.GetProcessById(workerPid).Kill(); } catch { }
            Console.WriteLine($"\n{pass} passed, {fail} failed");
            if (fail > 0) Environment.Exit(1);
            return;
        }

        // Test 1: ping
        {
            var resp = await SendRequest(pipeName, w => w.WriteString("type", "ping"));
            var status = resp.TryGetProperty("status", out var s) ? s.GetString() : null;
            Assert(status == "ok", "Ping returns ok");
        }

        // Test 2: get_status (should be standby)
        {
            var resp = await SendRequest(pipeName, w => w.WriteString("type", "get_status"));
            var status = resp.TryGetProperty("status", out var s) ? s.GetString() : null;
            Assert(status == "standby", $"Status is standby (got: {status})");
        }

        // Test 3: execute simple command
        {
            var command = OperatingSystem.IsWindows() ? "Write-Output 'hello splashshell'" : "echo 'hello splashshell'";
            Console.WriteLine($"  Executing: {command}");
            var resp = await SendRequest(pipeName, w => { w.WriteString("type", "execute"); w.WriteString("command", command); w.WriteNumber("timeout", 10000); }, TimeSpan.FromSeconds(15));

            var output = resp.TryGetProperty("output", out var o) ? o.GetString() ?? "" : "";
            var timedOut = resp.TryGetProperty("timedOut", out var t) && t.GetBoolean();
            var exitCode = resp.TryGetProperty("exitCode", out var e) ? e.GetInt32() : -1;
            var cwdResult = resp.TryGetProperty("cwd", out var c) ? c.GetString() : null;

            Console.WriteLine($"  Output: '{output.Replace("\n", "\\n")}'");
            Console.WriteLine($"  ExitCode: {exitCode}, TimedOut: {timedOut}, Cwd: {cwdResult}");

            Assert(!timedOut, "Command did not time out");
            Assert(output.Contains("hello splashshell"), "Output contains expected text");
            Assert(exitCode == 0, "Exit code is 0");
            Assert(cwdResult != null, "Cwd is reported");
        }

        // Test 4: execute command with non-zero exit (native command)
        {
            var command = OperatingSystem.IsWindows()
                ? "cmd /c exit 42"
                : "bash -c 'exit 42'";
            Console.WriteLine($"  Executing: {command}");
            var resp = await SendRequest(pipeName, w => { w.WriteString("type", "execute"); w.WriteString("command", command); w.WriteNumber("timeout", 10000); }, TimeSpan.FromSeconds(15));

            var exitCode = resp.TryGetProperty("exitCode", out var e) ? e.GetInt32() : -1;
            var timedOut = resp.TryGetProperty("timedOut", out var t) && t.GetBoolean();
            Console.WriteLine($"  ExitCode: {exitCode}, TimedOut: {timedOut}");

            Assert(!timedOut, "Non-zero exit: did not time out");
            Assert(exitCode == 42, $"Non-zero exit: code is 42 (got: {exitCode})");
        }

        // Test 5: get_status after commands (should be standby again)
        {
            var resp = await SendRequest(pipeName, w => w.WriteString("type", "get_status"));
            var status = resp.TryGetProperty("status", out var s) ? s.GetString() : null;
            Assert(status == "standby", $"Status back to standby (got: {status})");
        }

        // Test 6: version check (worker refuses claim from strictly newer proxy)
        // Send claim with a fake proxy_version that is strictly greater than any real
        // version. The worker's HandleClaim is version-aware: it marks itself obsolete
        // and returns status="obsolete". The shell (PTY) must remain alive afterwards
        // so the human user can keep working in the terminal.
        {
            var unownedPipe = $"SP.{workerPid}";
            var resp = await SendRequest(unownedPipe, w =>
            {
                w.WriteString("type", "claim");
                w.WriteNumber("proxy_pid", proxyPid);
                w.WriteString("proxy_version", "99.99.99");
                w.WriteString("agent_id", "v2test");
                w.WriteString("title", "#fake high version");
            });
            var status = resp.TryGetProperty("status", out var s) ? s.GetString() : null;
            Assert(status == "obsolete", $"Claim with higher proxy_version returns obsolete (got: {status})");

            var workerVersion = resp.TryGetProperty("worker_version", out var wv) ? wv.GetString() : null;
            Assert(!string.IsNullOrEmpty(workerVersion), $"Response includes worker_version (got: {workerVersion})");

            // PTY must still be alive so the user can continue working.
            var execResp = await SendRequest(pipeName,
                w => { w.WriteString("type", "execute"); w.WriteString("command", "Write-Output 'still-alive'"); w.WriteNumber("timeout", 10000); },
                TimeSpan.FromSeconds(15));
            var execOutput = execResp.TryGetProperty("output", out var eo) ? eo.GetString() ?? "" : "";
            Assert(execOutput.Contains("still-alive"), $"PTY still alive after obsolete state (output: {execOutput.Replace("\n", "\\n")})");
        }

        // Cleanup
        try
        {
            var proc = Process.GetProcessById(workerPid);
            proc.Kill();
            await proc.WaitForExitAsync();
        }
        catch { }

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }

    private static async Task<bool> WaitForPipeAsync(string pipeName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await SendRequest(pipeName, w => w.WriteString("type", "ping"), TimeSpan.FromSeconds(2));
                return true;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
        return false;
    }

    private static async Task<JsonElement> SendRequest(string pipeName, Action<Utf8JsonWriter> writeBody, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(timeout.Value);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        await client.ConnectAsync(cts.Token);

        var msgBytes = PipeJson.BuildObjectBytes(writeBody);
        var lenBytes = BitConverter.GetBytes(msgBytes.Length);

        await client.WriteAsync(lenBytes, cts.Token);
        await client.WriteAsync(msgBytes, cts.Token);
        await client.FlushAsync(cts.Token);

        var recvLenBytes = new byte[4];
        await ReadExactAsync(client, recvLenBytes, cts.Token);
        var recvLen = BitConverter.ToInt32(recvLenBytes);

        var recvBytes = new byte[recvLen];
        await ReadExactAsync(client, recvBytes, cts.Token);

        return PipeJson.ParseElement(recvBytes);
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
}
