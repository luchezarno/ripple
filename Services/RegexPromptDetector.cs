using System.Text.RegularExpressions;

namespace Splash.Services;

/// <summary>
/// Detects REPL prompt boundaries by regex matching on the cleaned PTY
/// output stream. Used for adapters whose prompt strategy is `regex` —
/// shells/REPLs that don't speak OSC 633 (Python, Node, ghci, ...) and
/// instead surface command-completion state via a visible prompt string
/// like <c>&gt;&gt;&gt; </c> or <c>irb(main):001:0&gt;</c>.
///
/// The detector is intentionally stateless across the OscParser — both
/// run in parallel in ConsoleWorker.ReadOutputLoop, and the synthetic
/// PromptStart / CommandFinished events it emits are merged with any
/// real OSC events in TextOffset order so the CommandTracker sees one
/// coherent event stream regardless of which strategy fired.
///
/// Buffering: a small tail of the most recent chunk is retained so a
/// prompt pattern that lands across a chunk boundary still matches. The
/// buffer is capped to prevent pathological growth.
/// </summary>
public sealed class RegexPromptDetector
{
    private readonly Regex _primary;
    private string _buffer = "";
    private const int MaxBufferLength = 2048;

    public RegexPromptDetector(string primaryPattern)
    {
        _primary = new Regex(primaryPattern, RegexOptions.Compiled | RegexOptions.Multiline);
    }

    /// <summary>
    /// Scan a chunk of cleaned output for prompt matches. Returns the
    /// list of chunk-local offsets (one per match) at which a virtual
    /// PromptStart event should fire. Offsets are into the <paramref name="chunk"/>
    /// passed in, so the caller can merge them with OscParser events
    /// that already carry chunk-local TextOffset values.
    /// </summary>
    public List<int> Scan(string chunk)
    {
        var offsets = new List<int>();
        if (chunk.Length == 0) return offsets;

        var searchIn = _buffer + chunk;
        var bufferLen = _buffer.Length;

        int lastReportedEnd = 0;
        foreach (Match m in _primary.Matches(searchIn))
        {
            var absEnd = m.Index + m.Length;

            // Matches fully inside the previous chunk's trailing buffer
            // were already reported on that scan — skip them.
            if (absEnd <= bufferLen)
                continue;

            offsets.Add(absEnd - bufferLen);
            lastReportedEnd = absEnd;
        }

        // Advance the buffer past the farthest-right reported match (so the
        // next scan doesn't re-match the same bytes) and past the last
        // newline (so a partial prompt starting after that newline is still
        // available if it lands across the boundary).
        var lastNewline = searchIn.LastIndexOf('\n');
        var newBufferStart = Math.Max(lastReportedEnd, lastNewline + 1);
        if (newBufferStart >= searchIn.Length)
        {
            _buffer = "";
        }
        else
        {
            var tail = searchIn[newBufferStart..];
            _buffer = tail.Length <= MaxBufferLength
                ? tail
                : tail[^MaxBufferLength..];
        }

        return offsets;
    }
}
