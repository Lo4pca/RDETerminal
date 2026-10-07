namespace RDETerminal.UI;

/// <summary>
/// Pure text-editing rules for the terminal's input box: detecting what was
/// just typed, auto-indent and brace matching around curly brackets, and
/// caret-line detection. Deliberately free of Unity types so the logic is easy
/// to reason about in isolation.
/// </summary>
internal static class TerminalInputEditing
{
    /// <summary>One indentation level.</summary>
    internal const string IndentUnit = "    ";

    // ── Line detection ────────────────────────────────────────────────────────
    // "Line" here means a logical line (separated by '\n'), not a visually
    // wrapped one.

    /// <summary>True when no line break precedes <paramref name="caret"/>.</summary>
    internal static bool IsOnFirstLine(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        caret = Clamp(caret, 0, text.Length);
        int firstBreak = text.IndexOf('\n');
        return firstBreak < 0 || firstBreak >= caret;
    }

    /// <summary>True when no line break follows <paramref name="caret"/>.</summary>
    internal static bool IsOnLastLine(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        caret = Clamp(caret, 0, text.Length);
        return text.IndexOf('\n', caret) < 0;
    }

    // ── Auto-indent ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call right after a line break was inserted immediately before
    /// <paramref name="caret"/>. Indents the new line like the line above it,
    /// one level deeper when that line ends with '{'. If a '}' directly
    /// follows the caret ("{|}"), the brace is moved to its own line so the
    /// caret ends up on an indented line between the two braces.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryAutoIndentAfterNewline(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || text[caret - 1] != '\n')
        {
            return false;
        }

        // The line that was just left: [leftStart, leftEnd), where leftEnd is the '\n'.
        int leftEnd = caret - 1;
        int leftStart = leftEnd == 0 ? 0 : text.LastIndexOf('\n', leftEnd - 1) + 1;

        string indent = text.Substring(leftStart, CountLeadingBlanks(text, leftStart, leftEnd));
        bool opensBlock = LastNonBlank(text, leftStart, leftEnd) == '{';
        string innerIndent = opensBlock ? indent + IndentUnit : indent;

        // "{|}": the next non-blank character on the new line closes the block.
        int next = SkipBlanks(text, caret);
        bool braceFollows = opensBlock && next < text.Length && text[next] == '}';

        if (braceFollows)
        {
            newText = text[..caret] + innerIndent + "\n" + indent + text[next..];
            newCaret = caret + innerIndent.Length;
            return true;
        }

        if (innerIndent.Length == 0)
        {
            return false;
        }

        newText = text.Insert(caret, innerIndent);
        newCaret = caret + innerIndent.Length;
        return true;
    }

    /// <summary>
    /// Call right after a '}' was inserted immediately before
    /// <paramref name="caret"/>. When the brace is the first thing on its line,
    /// removes one indentation level so it lines up with the block's opener.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryDedentClosingBrace(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || text[caret - 1] != '}')
        {
            return false;
        }

        int brace = caret - 1;
        int lineStart = brace == 0 ? 0 : text.LastIndexOf('\n', brace - 1) + 1;

        // Only whitespace may precede the brace on its line.
        if (CountLeadingBlanks(text, lineStart, brace) != brace - lineStart || lineStart == brace)
        {
            return false;
        }

        int remove = 0;
        while (remove < IndentUnit.Length
               && brace - remove - 1 >= lineStart
               && text[brace - remove - 1] == ' ')
        {
            remove++;
        }

        if (remove == 0 && text[brace - 1] == '\t')
        {
            remove = 1;
        }

        if (remove == 0)
        {
            return false;
        }

        newText = text.Remove(brace - remove, remove);
        newCaret = caret - remove;
        return true;
    }

    // ── Detecting what the user just typed ────────────────────────────────────

    /// <summary>
    /// True when <paramref name="after"/> is <paramref name="before"/> with
    /// exactly one character inserted at <paramref name="beforeCaret"/> and the
    /// caret moved just past it. That is what typing a single character looks
    /// like. Pasted text, a replaced selection, deletions, history recall and
    /// completion commits all fail the test, so the typing rules below never
    /// touch them. It also needs no help from the Input class.
    /// </summary>
    internal static bool TryGetInsertedChar(string before, int beforeCaret, string after, int afterCaret, out char inserted)
    {
        inserted = '\0';

        if (before == null || after == null
            || after.Length != before.Length + 1
            || beforeCaret < 0 || beforeCaret > before.Length
            || afterCaret != beforeCaret + 1)
        {
            return false;
        }

        // Everything before the caret, and everything after it, must be unchanged.
        if (string.CompareOrdinal(before, 0, after, 0, beforeCaret) != 0
            || string.CompareOrdinal(before, beforeCaret, after, beforeCaret + 1, before.Length - beforeCaret) != 0)
        {
            return false;
        }

        inserted = after[beforeCaret];
        return true;
    }

    // ── Brace matching ────────────────────────────────────────────────────────

    /// <summary>
    /// Call right after '{' was inserted immediately before
    /// <paramref name="caret"/>. Adds the matching '}' after the caret, so the
    /// caret ends up between the braces. Skipped when text that would be
    /// pushed against the new brace follows ("{" typed in front of existing
    /// code) and inside strings and // comments.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryAutoCloseBrace(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || text[caret - 1] != '{')
        {
            return false;
        }

        if (caret < text.Length && !IsSafeToPrecedeAutoClosedBrace(text[caret]))
        {
            return false;
        }

        int brace = caret - 1;
        int lineStart = brace == 0 ? 0 : text.LastIndexOf('\n', brace - 1) + 1;
        if (IsInsideStringOrComment(text, lineStart, brace))
        {
            return false;
        }

        newText = text.Insert(caret, "}");
        return true;
    }

    /// <summary>
    /// Call right after '}' was inserted immediately before
    /// <paramref name="caret"/>. If another '}' directly follows and some '{'
    /// before the typed one is still open, that following brace is the one that
    /// closes it (typically the auto-inserted one), so the typed brace is
    /// absorbed instead of leaving a doubled "}}".
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryTypeOverClosingBrace(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || text[caret - 1] != '}')
        {
            return false;
        }

        if (caret >= text.Length || text[caret] != '}')
        {
            return false;
        }

        int open = 0;
        for (int i = 0; i < caret - 1; i++)
        {
            if (text[i] == '{')
            {
                open++;
            }
            else if (text[i] == '}' && open > 0)
            {
                open--;
            }
        }

        if (open == 0)
        {
            return false;
        }

        newText = text.Remove(caret, 1);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsBlank(char c) => c == ' ' || c == '\t';

    /// <summary>Characters that may directly follow an auto-inserted '}'.</summary>
    private static bool IsSafeToPrecedeAutoClosedBrace(char next) =>
        next == '\n' || next == '\r' || next == ' ' || next == '\t'
        || next == ')' || next == ']' || next == '}' || next == ';' || next == ',';

    /// <summary>
    /// Whether <paramref name="end"/> lies inside a string literal or after a
    /// // comment, looking only at the current line from <paramref name="start"/>.
    /// Deliberately simple: no verbatim strings, char literals or block comments.
    /// </summary>
    private static bool IsInsideStringOrComment(string text, int start, int end)
    {
        bool inString = false;

        for (int i = start; i < end; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;                       // skip the escaped character
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '/' && i + 1 < end && text[i + 1] == '/')
            {
                return true;                   // the rest of the line is a comment
            }
        }

        return inString;
    }

    private static int CountLeadingBlanks(string text, int start, int end)
    {
        int i = start;
        while (i < end && IsBlank(text[i]))
        {
            i++;
        }

        return i - start;
    }

    /// <summary>Last non-blank character in [start, end), or '\0' if none.</summary>
    private static char LastNonBlank(string text, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (!IsBlank(text[i]))
            {
                return text[i];
            }
        }

        return '\0';
    }

    /// <summary>Index of the first non-blank character at or after <paramref name="index"/> (or text.Length).</summary>
    private static int SkipBlanks(string text, int index)
    {
        while (index < text.Length && IsBlank(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : (value > max ? max : value);
    }
}