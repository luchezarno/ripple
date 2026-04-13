using Splash.Services;

namespace Splash.Tests;

public static class RegexPromptDetectorTests
{
    public static void Run()
    {
        var pass = 0;
        var fail = 0;
        void Assert(bool condition, string name)
        {
            if (condition) { pass++; Console.WriteLine($"  PASS: {name}"); }
            else { fail++; Console.Error.WriteLine($"  FAIL: {name}"); }
        }

        Console.WriteLine("=== RegexPromptDetector Tests ===");

        // Python-style primary prompt: >>> at line start (or buffer start).
        // The trailing space is part of the visible prompt but optional in
        // the regex to tolerate pasted input.
        const string pythonPrompt = @"(^|\n)>>> ";

        // Fresh chunk with a single prompt at start.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var offsets = d.Scan(">>> ");
            Assert(offsets.Count == 1 && offsets[0] == 4,
                $"prompt at chunk start fires once at end-of-prompt (got {string.Join(",", offsets)})");
        }

        // Prompt after output.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var chunk = "hello\n>>> ";
            var offsets = d.Scan(chunk);
            Assert(offsets.Count == 1 && offsets[0] == chunk.Length,
                $"prompt after output fires at end-of-chunk (got {string.Join(",", offsets)})");
        }

        // Two prompts in one chunk (rare but possible on fast commands).
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var chunk = ">>> 1\n>>> ";
            var offsets = d.Scan(chunk);
            Assert(offsets.Count == 2,
                $"two prompts in one chunk (got {offsets.Count})");
        }

        // No prompt: nothing emitted.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var offsets = d.Scan("just some output with no prompt\n");
            Assert(offsets.Count == 0,
                $"no prompt, no events (got {offsets.Count})");
        }

        // Prompt split across chunk boundary — the \n arrives in chunk 1,
        // the >>> in chunk 2. The detector must carry the \n in its buffer
        // so the match anchors correctly.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var off1 = d.Scan("some output\n");
            Assert(off1.Count == 0, "partial chunk (trailing newline only) no match");
            var off2 = d.Scan(">>> ");
            Assert(off2.Count == 1,
                $"boundary-spanning prompt matches on the second chunk (got {off2.Count})");
        }

        // Prompt split mid-sequence: "\n>>" in chunk 1, "> " in chunk 2.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var off1 = d.Scan("hello\n>>");
            Assert(off1.Count == 0, "partial '>>' alone no match");
            var off2 = d.Scan("> ");
            Assert(off2.Count == 1,
                $"'>>> ' split across two chunks matches (got {off2.Count})");
        }

        // Already-reported prompt is not re-reported on the next scan.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var off1 = d.Scan("line\n>>> ");
            Assert(off1.Count == 1, "first scan reports the prompt once");
            var off2 = d.Scan("next line\n");
            Assert(off2.Count == 0, "second scan does NOT re-report the same prompt");
        }

        // Output containing ">>>" that isn't at line start must not match.
        {
            var d = new RegexPromptDetector(pythonPrompt);
            var offsets = d.Scan("look: >>> that is mid-line");
            Assert(offsets.Count == 0,
                $"'>>>' mid-line does NOT match (got {offsets.Count})");
        }

        // irb-style prompt with a numeric counter — proves the detector
        // is not Python-specific and handles regex patterns other than
        // a fixed literal.
        {
            const string irbPrompt = @"(^|\n)irb\(main\):\d+:\d+> ";
            var d = new RegexPromptDetector(irbPrompt);
            var offsets = d.Scan("irb(main):001:0> ");
            Assert(offsets.Count == 1,
                $"irb prompt regex fires on match (got {offsets.Count})");
            var offsets2 = d.Scan("=> 2\nirb(main):002:0> ");
            Assert(offsets2.Count == 1,
                $"irb counter increments, still matches (got {offsets2.Count})");
        }

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }
}
