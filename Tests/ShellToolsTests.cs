using Ripple.Tools;

namespace Ripple.Tests;

/// <summary>
/// Tests for ShellTools post-processing helpers — currently only
/// FilterStartupBanners (the PSReadLine "screen reader detected"
/// warning stripper used by peek_console). Cheap unit tests that
/// don't need a real worker / pipe / pwsh; FilterStartupBanners is
/// pure string-in / string-out so no fixture beyond literal banner
/// samples is required.
/// </summary>
public static class ShellToolsTests
{
    public static void Run()
    {
        Console.WriteLine("=== ShellTools Tests ===");
        var pass = 0;
        var fail = 0;
        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        // --- FilterStartupBanners: no-op when banner is absent ---
        // The fast-path early return must not touch normal output.

        {
            var input = "PS C:\\> Get-ChildItem\nfoo\nbar\nPS C:\\> ";
            var got = ShellTools.FilterStartupBanners(input);
            Assert(got == input, "no-op when banner is absent");
        }

        // --- Empty / null inputs are returned verbatim. ---

        {
            Assert(ShellTools.FilterStartupBanners("") == "", "empty input passthrough");
            Assert(ShellTools.FilterStartupBanners(null!) == null,
                "null input passthrough");
        }

        // --- Banner at top: drops the warning + the trailing blank line.
        // ConPTY wraps the warning at column 100ish, so the captured
        // snapshot has the message split across two lines with the wrap
        // landing inside "compatibility". The closing-marker detection
        // ("'Import-Module PSReadLine'.") is what ends the drop region.

        {
            var input =
                "Warning: PowerShell detected that you might be using a screen reader and has disabled PSReadLine for comp\n" +
                "atibility purposes. If you want to re-enable it, run 'Import-Module PSReadLine'.\n" +
                "\n" +
                "PS C:\\Users\\me> Get-Date\n" +
                "Wednesday\n" +
                "PS C:\\Users\\me> ";
            var got = ShellTools.FilterStartupBanners(input);
            Assert(!got.Contains("screen reader"),
                "banner stripped: warning text gone");
            Assert(!got.Contains("Import-Module PSReadLine'."),
                "banner stripped: closing marker line gone");
            Assert(got.StartsWith("PS C:\\Users\\me>"),
                "banner stripped: filtered output starts cleanly with the prompt (no leading blank from the trailing gap)");
            Assert(got.Contains("Wednesday") && got.Contains("Get-Date"),
                "banner stripped: real content after the banner is preserved");
        }

        // --- Banner-only snapshot (no following content): the closing
        // marker line still terminates the drop, leaving an empty string.
        // Verifies the cap-by-marker path works even when the banner is
        // the entire input and there's no trailing prompt.

        {
            var input =
                "Warning: PowerShell detected that you might be using a screen reader and has disabled PSReadLine for comp\n" +
                "atibility purposes. If you want to re-enable it, run 'Import-Module PSReadLine'.\n";
            var got = ShellTools.FilterStartupBanners(input);
            Assert(got.Length == 0 || string.IsNullOrWhiteSpace(got),
                $"banner-only input: filtered to empty/blank ('{got}')");
        }

        // --- 5-line cap: if the banner never ends (corrupted snapshot,
        // unusual buffer state) we must NOT eat the entire output. After
        // 5 dropped lines we resume keeping content even though the
        // banner-end signal never arrived.

        {
            var input =
                "Warning: PowerShell detected that you might be using a screen reader and has disabled PSReadLine for comp\n" +
                "L1-no-end\n" +
                "L2-no-end\n" +
                "L3-no-end\n" +
                "L4-no-end\n" +
                "L5-no-end\n" +
                "L6-no-end\n" +
                "L7-real-content\n";
            var got = ShellTools.FilterStartupBanners(input);
            Assert(got.Contains("L7-real-content"),
                $"5-line cap: real content after the cap is preserved ('{got}')");
        }

        // --- Banner appearing mid-snapshot is still stripped: detection
        // runs on every line, not just the first. (The PSReadLine warning
        // is only ever at the top in practice, but the function shouldn't
        // make that an invariant.)

        {
            var input =
                "PS C:\\> echo hi\n" +
                "hi\n" +
                "Warning: PowerShell detected that you might be using a screen reader and has disabled PSReadLine for compatibility purposes. If you want to re-enable it, run 'Import-Module PSReadLine'.\n" +
                "\n" +
                "PS C:\\> ";
            var got = ShellTools.FilterStartupBanners(input);
            Assert(got.Contains("echo hi") && got.Contains("hi"),
                "mid-snapshot banner: pre-banner content preserved");
            Assert(!got.Contains("screen reader"),
                "mid-snapshot banner: warning removed");
        }

        Console.WriteLine($"  Total: {pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }
}
