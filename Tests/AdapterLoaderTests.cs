using Splash.Services.Adapters;

namespace Splash.Tests;

/// <summary>
/// Contract tests for the adapter loader: verifies that every embedded
/// adapter YAML parses, that required fields are populated, and that the
/// registry exposes them under the names ConsoleManager.NormalizeShellFamily
/// will look up.
/// </summary>
public static class AdapterLoaderTests
{
    public static void Run(AdapterRegistry registry, AdapterRegistry.LoadReport report)
    {
        var pass = 0;
        var fail = 0;
        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        Console.WriteLine("=== AdapterLoader Tests ===");

        // Load errors are a hard fail — they mean a shipped adapter is broken.
        Assert(report.ParseErrors.Count == 0,
            $"no parse errors (got {report.ParseErrors.Count}: {string.Join("; ", report.ParseErrors.Select(e => $"{e.Resource}: {e.Error}"))})");

        Assert(report.Collisions.Count == 0,
            $"no name collisions (got {report.Collisions.Count}: {string.Join("; ", report.Collisions)})");

        // All four shell adapters we shipped should be present.
        Assert(registry.Count >= 4, $"at least 4 adapters registered (got {registry.Count})");

        // Per-adapter smoke checks. These are deliberately shallow — they
        // prove the YAML -> model mapping works end-to-end for each
        // section, without asserting specific values that may evolve.
        foreach (var name in new[] { "pwsh", "bash", "zsh", "cmd" })
        {
            var adapter = registry.Find(name);
            Assert(adapter != null, $"{name}: found in registry");
            if (adapter == null) continue;

            Assert(adapter.Schema == 1, $"{name}: schema v1");
            Assert(!string.IsNullOrEmpty(adapter.Name), $"{name}: name is set");
            Assert(!string.IsNullOrEmpty(adapter.Version), $"{name}: version is set");
            Assert(adapter.Family == "shell", $"{name}: family == shell");
            Assert(!string.IsNullOrEmpty(adapter.Process.CommandTemplate),
                $"{name}: process.command_template is set");
            Assert(!string.IsNullOrEmpty(adapter.Signals.Interrupt),
                $"{name}: signals.interrupt is set");
            Assert(!string.IsNullOrEmpty(adapter.Lifecycle.Shutdown.Command),
                $"{name}: lifecycle.shutdown.command is set");
        }

        // pwsh-specific: verify the integration_script block made it through
        // the YAML multi-line literal into the model verbatim.
        var pwsh = registry.Find("pwsh");
        if (pwsh != null)
        {
            Assert(!string.IsNullOrEmpty(pwsh.IntegrationScript),
                "pwsh: integration_script block is present");
            Assert(pwsh.IntegrationScript?.Contains("__SplashInjected") == true,
                "pwsh: integration_script contains __SplashInjected guard");
            Assert(pwsh.IntegrationScript?.Contains("PreCommandLookupAction") == true,
                "pwsh: integration_script contains PreCommandLookupAction hook");
            Assert(pwsh.Init.HookType == "precommand_lookup_action",
                "pwsh: init.hook_type == precommand_lookup_action");
            Assert(pwsh.Input.MultilineDelivery == "tempfile",
                "pwsh: input.multiline_delivery == tempfile");
            Assert(pwsh.Process.InheritEnvironment == false,
                "pwsh: process.inherit_environment == false (clean env)");
            Assert(pwsh.Capabilities.ExitCode == "true",
                "pwsh: capabilities.exit_code == true");
        }

        // bash-specific: PS0 hook, direct multiline delivery
        var bash = registry.Find("bash");
        if (bash != null)
        {
            Assert(bash.Init.HookType == "ps0", "bash: init.hook_type == ps0");
            Assert(bash.Input.MultilineDelivery == "direct",
                "bash: input.multiline_delivery == direct");
            Assert(bash.Process.InheritEnvironment == true,
                "bash: process.inherit_environment == true (MSYS2 needs it)");
            Assert(bash.Capabilities.JobControl == true,
                "bash: capabilities.job_control == true");
        }

        // cmd-specific: unreliable exit code + deterministic echo strip
        var cmd = registry.Find("cmd");
        if (cmd != null)
        {
            Assert(cmd.Init.Strategy == "prompt_variable",
                "cmd: init.strategy == prompt_variable");
            Assert(cmd.Init.HookType == "none",
                "cmd: init.hook_type == none (no preexec available)");
            Assert(cmd.Capabilities.ExitCode == "unreliable",
                "cmd: capabilities.exit_code == unreliable");
            Assert(cmd.Output.InputEchoStrategy == "deterministic_byte_match",
                "cmd: output.input_echo_strategy == deterministic_byte_match");
            Assert(cmd.Capabilities.UserBusyDetection == "process_polling",
                "cmd: capabilities.user_busy_detection == process_polling");
        }

        // Alias test: "powershell" should resolve to the pwsh adapter.
        var aliased = registry.Find("powershell");
        Assert(aliased != null && aliased.Name == "pwsh",
            "alias: 'powershell' resolves to pwsh");

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }
}
