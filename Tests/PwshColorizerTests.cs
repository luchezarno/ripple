using SplashShell.Services;

namespace SplashShell.Tests;

/// <summary>
/// Sanity tests for PwshColorizer — checks that common syntax elements are
/// wrapped in the expected ANSI sequences and that the original text is
/// preserved byte-for-byte (no characters lost or reordered).
/// </summary>
public class PwshColorizerTests
{
    private const string Reset     = "\x1b[0m";
    private const string Command   = "\x1b[93m";
    private const string Parameter = "\x1b[90m";
    private const string String_   = "\x1b[36m";
    private const string Variable  = "\x1b[92m";
    private const string Number    = "\x1b[97m";
    private const string Operator  = "\x1b[90m";
    private const string Comment   = "\x1b[32m";

    public static void Run()
    {
        var pass = 0;
        var fail = 0;

        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        void AssertContains(string haystack, string needle, string name)
            => Assert(haystack.Contains(needle), $"{name}: should contain '{EscapeAnsi(needle)}'");

        // Raw text (ANSI stripped) must equal the original command.
        string StripAnsi(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '\x1b' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    i += 2;
                    while (i < s.Length && s[i] != 'm') i++;
                    if (i < s.Length) i++;
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        Console.WriteLine("=== PwshColorizer Tests ===");

        // 1. Empty / whitespace passes through.
        {
            var r = PwshColorizer.Colorize("");
            Assert(r == "", "empty string passes through");
        }

        // 2. Simple cmdlet name is in command position → yellow.
        {
            var r = PwshColorizer.Colorize("Get-Date");
            Assert(r == $"{Command}Get-Date{Reset}", "bare Get-Date is wrapped in Command color");
        }

        // 3. Cmdlet + parameter + string literal.
        {
            var cmd = "Get-Date -Format 'yyyyMMdd'";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Get-Date{Reset}", "cmdlet colored");
            AssertContains(r, $"{Parameter}-Format{Reset}", "parameter colored");
            AssertContains(r, $"{String_}'yyyyMMdd'{Reset}", "string colored");
            Assert(StripAnsi(r) == cmd, "text preserved verbatim after ANSI strip");
        }

        // 4. Variable and subexpression variants.
        {
            var cmd = "Write-Host $PID $env:PATH $(Get-Date)";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Write-Host{Reset}", "Write-Host colored as command");
            AssertContains(r, $"{Variable}$PID{Reset}", "$PID colored as variable");
            AssertContains(r, $"{Variable}$env:PATH{Reset}", "$env:PATH colored as variable");
            AssertContains(r, $"{Variable}$(Get-Date){Reset}", "$(Get-Date) colored as variable");
        }

        // 5. Double-quoted string with escape.
        {
            var cmd = "Write-Output \"hello `\"world\"";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, "Write-Output", "command text present");
            Assert(StripAnsi(r) == cmd, "text preserved with backtick escape");
        }

        // 6. Semicolon resets to command position — second identifier is also
        //    colored as a command.
        {
            var cmd = "Set-Location C:\\Windows; Get-Date";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Set-Location{Reset}", "first statement command colored");
            AssertContains(r, $"{Operator};{Reset}", "semicolon colored as operator");
            AssertContains(r, $"{Command}Get-Date{Reset}", "second statement command colored");
        }

        // 7. Pipeline also resets to command position.
        {
            var cmd = "Get-Process | Where-Object { $_.CPU -gt 1 }";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Get-Process{Reset}", "Get-Process colored");
            AssertContains(r, $"{Operator}|{Reset}", "pipe colored as operator");
            AssertContains(r, $"{Command}Where-Object{Reset}", "Where-Object colored as command after pipe");
            Assert(StripAnsi(r) == cmd, "complex pipeline text preserved");
        }

        // 8. Line comment.
        {
            var cmd = "Get-Date # current time";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Get-Date{Reset}", "command before comment colored");
            AssertContains(r, $"{Comment}# current time{Reset}", "comment colored");
        }

        // 9. Number literal.
        {
            var cmd = "Start-Sleep 5";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Start-Sleep{Reset}", "cmdlet colored");
            AssertContains(r, $"{Number}5{Reset}", "number colored");
        }

        // 10. Path argument: should pass through without being mis-colored.
        {
            var cmd = "Set-Location C:\\MyProj\\splashshell";
            var r = PwshColorizer.Colorize(cmd);
            AssertContains(r, $"{Command}Set-Location{Reset}", "cmdlet colored");
            Assert(StripAnsi(r) == cmd, "path text preserved");
        }

        // 11. Get-Date inside pipeline (hyphenated cmdlet name is not
        //     mistaken for `-Date` parameter).
        {
            var cmd = "Get-Date";
            var r = PwshColorizer.Colorize(cmd);
            Assert(!r.Contains(Parameter), "Get-Date does not contain parameter color");
        }

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }

    private static string EscapeAnsi(string s)
        => s.Replace("\x1b", "\\e");
}
