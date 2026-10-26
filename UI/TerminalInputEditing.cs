using System;
using System.Collections.Generic;
using System.Text;

namespace RDETerminal.UI;

/// <summary>
/// Pure text-editing rules for the terminal's input box: detecting what was
/// just typed, auto-indent, bracket and quote matching, surrounding a selection,
/// Tab indentation and caret-line detection. Deliberately free of Unity types
/// so the logic is easy to reason about in isolation. The behavior follows what
/// VS Code does.
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

    // ── Typing rules entry point ──────────────────────────────────────────────

    /// <summary>
    /// Given the input text and selection before, and the text and caret after,
    /// the InputField handled a frame's key presses, decides whether one of the
    /// typing rules applies (auto-indent, bracket and quote matching, surrounding
    /// a selection, pair deletion, tab-stop Backspace) and returns the adjusted
    /// text and selection. Nothing happens unless exactly one character was typed,
    /// deleted, or typed over a selection, so pasting, history recall and
    /// completion commits are never touched.
    /// </summary>
    /// <param name="beforeSelStart">Start of the selection before (equals the caret when there was none).</param>
    /// <param name="beforeSelEnd">End of the selection before.</param>
    /// <returns>False (with the "after" values returned) when no rule applies.</returns>
    internal static bool TryApplyTypingRules(
        string before, int beforeCaret, int beforeSelStart, int beforeSelEnd,
        string after, int afterCaret,
        out string newText, out int newSelStart, out int newSelEnd)
    {
        newText = after;
        newSelStart = afterCaret;
        newSelEnd = afterCaret;

        int caret;
        bool changed;

        if (TryGetInsertedChar(before, beforeCaret, after, afterCaret, out char typed))
        {
            changed = TryApplyInsertionRule(typed, after, afterCaret, out newText, out caret);
            newSelStart = caret;
            newSelEnd = caret;
            return changed;
        }

        // A bracket or quote typed over a selection wraps it instead of replacing it.
        if (beforeSelEnd > beforeSelStart
            && TryGetSelectionReplacedByChar(before, beforeSelStart, beforeSelEnd, after, afterCaret, out typed)
            && IsSurroundChar(typed))
        {
            return TrySurroundSelection(before, beforeSelStart, beforeSelEnd, typed,
                out newText, out newSelStart, out newSelEnd);
        }

        changed = TryDeleteMatchingCloser(before, beforeCaret, after, afterCaret, out newText, out caret)
                  || TryBackspaceToTabStop(before, beforeCaret, after, afterCaret, out newText, out caret);
        newSelStart = caret;
        newSelEnd = caret;
        return changed;
    }

    private static bool TryApplyInsertionRule(char typed, string after, int afterCaret, out string newText, out int newCaret)
    {
        if (typed == '\n')
        {
            return TryAutoIndentAfterNewline(after, afterCaret, out newText, out newCaret);
        }

        if (IsOpeningBracket(typed))
        {
            return TryAutoCloseBracket(after, afterCaret, out newText, out newCaret);
        }

        if (IsClosingBracket(typed))
        {
            // Typing a closer in front of the one that is already waiting there
            // steps over it; otherwise a closer that starts its line is dedented.
            return TryTypeOverClosingBracket(after, afterCaret, out newText, out newCaret)
                   || TryDedentClosingBracket(after, afterCaret, out newText, out newCaret);
        }

        if (IsQuote(typed))
        {
            return TryTypeOverClosingQuote(after, afterCaret, out newText, out newCaret)
                   || TryAutoCloseQuote(after, afterCaret, out newText, out newCaret);
        }

        newText = after;
        newCaret = afterCaret;
        return false;
    }

    // ── Auto-indent ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call right after a line break was inserted immediately before
    /// <paramref name="caret"/>. Indents the new line like the line above it,
    /// one level deeper when that line ends with an opening bracket. If the
    /// matching closing bracket directly follows the caret ("(|)", "[|]", "{|}"),
    /// it is moved to its own line so the caret ends up on an indented line
    /// between the two.
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
        char opener = LastNonBlank(text, leftStart, leftEnd);
        bool opensBlock = IsOpeningBracket(opener);
        string innerIndent = opensBlock ? indent + IndentUnit : indent;

        // "(|)": the next non-blank character on the new line closes what the line above opened.
        int next = SkipBlanks(text, caret);
        bool closerFollows = opensBlock && next < text.Length && text[next] == ClosingFor(opener);

        if (closerFollows)
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
    /// Call right after a closing bracket was inserted immediately before
    /// <paramref name="caret"/>. When it is the first thing on its line, removes
    /// one indentation level so it lines up with the line that opened the bracket.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryDedentClosingBracket(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || !IsClosingBracket(text[caret - 1]))
        {
            return false;
        }

        int bracket = caret - 1;
        int lineStart = bracket == 0 ? 0 : text.LastIndexOf('\n', bracket - 1) + 1;

        // Only whitespace may precede the bracket on its line.
        if (CountLeadingBlanks(text, lineStart, bracket) != bracket - lineStart || lineStart == bracket)
        {
            return false;
        }

        int remove = 0;
        while (remove < IndentUnit.Length
               && bracket - remove - 1 >= lineStart
               && text[bracket - remove - 1] == ' ')
        {
            remove++;
        }

        if (remove == 0 && text[bracket - 1] == '\t')
        {
            remove = 1;
        }

        if (remove == 0)
        {
            return false;
        }

        newText = text.Remove(bracket - remove, remove);
        newCaret = caret - remove;
        return true;
    }

    // ── Detecting what the user just typed ────────────────────────────────────

    /// <summary>
    /// True when <paramref name="after"/> is <paramref name="before"/> with
    /// exactly one character inserted at <paramref name="beforeCaret"/> and the
    /// caret moved just past it. That is what typing a single character looks
    /// like. Pasted text, a replaced selection, deletions, history recall and
    /// completion commits all fail the test, so the typing rules never touch
    /// them. It also needs no help from the Input class.
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

    // ── Bracket matching ──────────────────────────────────────────────────────

    /// <summary>
    /// Call right after an opening bracket was inserted immediately before
    /// <paramref name="caret"/>. Adds the matching closing bracket after the
    /// caret, so the caret ends up between the two. Like VS Code's default it
    /// only does so when the next character is whitespace, the end of the input
    /// or a closing/separating character, and never inside strings or // comments.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryAutoCloseBracket(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || !IsOpeningBracket(text[caret - 1]))
        {
            return false;
        }

        if (caret < text.Length && !IsSafeBeforeAutoClose(text[caret]))
        {
            return false;
        }

        int bracket = caret - 1;
        int lineStart = bracket == 0 ? 0 : text.LastIndexOf('\n', bracket - 1) + 1;
        if (ScanLine(text, lineStart, bracket) != LineState.Code)
        {
            return false;
        }

        newText = text.Insert(caret, ClosingFor(text[bracket]).ToString());
        return true;
    }

    /// <summary>
    /// Call right after a closing bracket was inserted immediately before
    /// <paramref name="caret"/>. If the same closing bracket directly follows
    /// and an opening bracket of that kind before the typed one is still open,
    /// the following bracket is the one that closes it (typically the
    /// auto-inserted one), so the typed bracket is absorbed instead of
    /// leaving a doubled "))".
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryTypeOverClosingBracket(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || !IsClosingBracket(text[caret - 1]))
        {
            return false;
        }

        char close = text[caret - 1];
        if (caret >= text.Length || text[caret] != close)
        {
            return false;
        }

        char open = OpeningFor(close);
        int depth = 0;
        for (int i = 0; i < caret - 1; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close && depth > 0)
            {
                depth--;
            }
        }

        if (depth == 0)
        {
            return false;
        }

        newText = text.Remove(caret, 1);
        return true;
    }

    /// <summary>
    /// Call after a single character was deleted just before the caret
    /// (Backspace). If it was an opening bracket, or an opening quote, whose
    /// partner sat directly after it, the now-orphaned partner is removed too,
    /// so Backspace on an empty "()" or "" deletes the pair.
    /// </summary>
    /// <returns>False (with the "after" values returned) when nothing needs to change.</returns>
    internal static bool TryDeleteMatchingCloser(
        string before, int beforeCaret, string after, int afterCaret, out string newText, out int newCaret)
    {
        newText = after;
        newCaret = afterCaret;

        if (!IsSingleCharDeletedBeforeCaret(before, beforeCaret, after, afterCaret) || beforeCaret >= before.Length)
        {
            return false;
        }

        char deleted = before[beforeCaret - 1];
        char next = before[beforeCaret];

        if (IsOpeningBracket(deleted))
        {
            if (next != ClosingFor(deleted))
            {
                return false;
            }
        }
        else if (IsQuote(deleted))
        {
            // Only an *opening* quote pairs with the one after it. Deleting the closing
            // quote of one string must not eat the opening quote of the next one.
            int index = beforeCaret - 1;
            if (next != deleted || ScanLine(before, LineStartOf(before, index), index) != LineState.Code)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        newText = after.Remove(afterCaret, 1);   // the orphaned partner now sits at the caret
        return true;
    }

    internal static bool IsOpeningBracket(char c) => c == '(' || c == '[' || c == '{';

    internal static bool IsClosingBracket(char c) => c == ')' || c == ']' || c == '}';

    // ── Quote matching ────────────────────────────────────────────────────────

    /// <summary>
    /// Call right after '"' or '\'' was inserted immediately before
    /// <paramref name="caret"/>. Adds the matching quote after the caret. Like
    /// VS Code it does so only for an opening quote: not inside a string, char
    /// literal or // comment, not directly after a word character ("don't"), and
    /// only before whitespace, the end of the input or a closing/separating character.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryAutoCloseQuote(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || !IsQuote(text[caret - 1]))
        {
            return false;
        }

        int quote = caret - 1;

        if (ScanLine(text, LineStartOf(text, quote), quote) != LineState.Code)
        {
            return false;
        }

        if (quote > 0 && IsWordChar(text[quote - 1]))
        {
            return false;
        }

        if (caret < text.Length && !IsSafeBeforeAutoClose(text[caret]))
        {
            return false;
        }

        newText = text.Insert(caret, text[quote].ToString());
        return true;
    }

    /// <summary>
    /// Call right after '"' or '\'' was inserted immediately before
    /// <paramref name="caret"/>. If the same quote directly follows and the typed
    /// one closes the literal it is inside (typically the auto-inserted quote),
    /// the typed quote is absorbed instead of leaving a doubled quote.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when nothing needs to change.</returns>
    internal static bool TryTypeOverClosingQuote(string text, int caret, out string newText, out int newCaret)
    {
        newText = text;
        newCaret = caret;

        if (string.IsNullOrEmpty(text) || caret < 1 || caret > text.Length || !IsQuote(text[caret - 1]))
        {
            return false;
        }

        char quote = text[caret - 1];
        if (caret >= text.Length || text[caret] != quote)
        {
            return false;
        }

        int index = caret - 1;
        int lineStart = LineStartOf(text, index);
        if (IsEscaped(text, lineStart, index))
        {
            return false;
        }

        LineState closes = quote == '"' ? LineState.String : LineState.Char;
        if (ScanLine(text, lineStart, index) != closes)
        {
            return false;
        }

        newText = text.Remove(caret, 1);
        return true;
    }

    // ── Surrounding a selection ───────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="after"/> is <paramref name="before"/> with the
    /// selection [selStart, selEnd) replaced by exactly one typed character and
    /// the caret just past it.
    /// </summary>
    internal static bool TryGetSelectionReplacedByChar(
        string before, int selStart, int selEnd, string after, int afterCaret, out char typed)
    {
        typed = '\0';

        if (before == null || after == null || selStart < 0 || selEnd > before.Length || selEnd <= selStart)
        {
            return false;
        }

        if (after.Length != before.Length - (selEnd - selStart) + 1 || afterCaret != selStart + 1)
        {
            return false;
        }

        if (string.CompareOrdinal(before, 0, after, 0, selStart) != 0
            || string.CompareOrdinal(before, selEnd, after, selStart + 1, before.Length - selEnd) != 0)
        {
            return false;
        }

        typed = after[selStart];
        return true;
    }

    /// <summary>
    /// Wraps the selection [selStart, selEnd) of <paramref name="before"/> in the
    /// bracket or quote pair that <paramref name="opener"/> starts, and keeps the
    /// wrapped text selected, so typing another bracket or quote wraps it again.
    /// </summary>
    internal static bool TrySurroundSelection(
        string before, int selStart, int selEnd, char opener,
        out string newText, out int newSelStart, out int newSelEnd)
    {
        newText = before;
        newSelStart = selStart;
        newSelEnd = selEnd;

        char closer = IsQuote(opener) ? opener : ClosingFor(opener);
        if (closer == '\0' || before == null || selStart < 0 || selEnd > before.Length || selEnd <= selStart)
        {
            return false;
        }

        newText = before.Substring(0, selStart)
                  + opener
                  + before.Substring(selStart, selEnd - selStart)
                  + closer
                  + before.Substring(selEnd);
        newSelStart = selStart + 1;
        newSelEnd = selEnd + 1;
        return true;
    }

    // ── Indentation keys ──────────────────────────────────────────────────────

    /// <summary>
    /// Call after a single space was deleted just before the caret (Backspace)
    /// while the caret sits in a line's leading whitespace: deletes on to the
    /// previous tab stop, so a 4-space indent goes in one press instead of four.
    /// </summary>
    /// <returns>False (with the "after" values returned) when nothing needs to change.</returns>
    internal static bool TryBackspaceToTabStop(
        string before, int beforeCaret, string after, int afterCaret, out string newText, out int newCaret)
    {
        newText = after;
        newCaret = afterCaret;

        if (!IsSingleCharDeletedBeforeCaret(before, beforeCaret, after, afterCaret) || before[beforeCaret - 1] != ' ')
        {
            return false;
        }

        int lineStart = LineStartOf(after, afterCaret);
        for (int i = lineStart; i < afterCaret; i++)
        {
            if (after[i] != ' ')
            {
                return false;                       // not in the leading whitespace
            }
        }

        int columnBefore = afterCaret - lineStart + 1;
        int target = (columnBefore - 1) / IndentUnit.Length * IndentUnit.Length;
        int extra = columnBefore - target - 1;      // spaces still to delete beyond the one already gone
        if (extra <= 0)
        {
            return false;
        }

        newText = after.Remove(afterCaret - extra, extra);
        newCaret = afterCaret - extra;
        return true;
    }

    /// <summary>
    /// Tab / Shift+Tab. With no selection, Tab inserts spaces up to the next tab
    /// stop; with a selection inside one line it replaces the selection the same
    /// way; with a multi-line (or whole-line) selection it indents every touched
    /// line. Shift+Tab removes up to one indent level from every touched line.
    /// A selection that ends at the very start of a line does not include that
    /// line, and empty lines are not indented. The selection follows the text.
    /// </summary>
    /// <returns>False (with the inputs unchanged) when there was nothing to do.</returns>
    internal static bool TryIndentLines(
        string text, int selStart, int selEnd, bool outdent,
        out string newText, out int newStart, out int newEnd)
    {
        newText = text;
        newStart = selStart;
        newEnd = selEnd;

        if (text == null)
        {
            return false;
        }

        int s = Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        int e = Clamp(Math.Max(selStart, selEnd), 0, text.Length);
        newStart = s;
        newEnd = e;

        int firstLine = LineStartOf(text, s);
        int lastIncluded = (e > s && text[e - 1] == '\n') ? e - 1 : e;
        int lastLine = LineStartOf(text, lastIncluded);

        // A selection containing a line break is a multi-line selection even when only one
        // line ends up included (e.g. a whole line selected up to the start of the next one).
        // Treating it as a single-line selection would replace the selected text with spaces.
        bool spansLines = e > s && text.IndexOf('\n', s, e - s) >= 0;

        if (!outdent && !spansLines && firstLine == lastLine)
        {
            bool wholeLine = e > s && s == firstLine && e == LineEndOf(text, firstLine);
            if (!wholeLine)
            {
                int spaces = IndentUnit.Length - ((s - firstLine) % IndentUnit.Length);
                newText = text.Substring(0, s) + new string(' ', spaces) + text.Substring(e);
                newStart = s + spaces;
                newEnd = newStart;
                return true;
            }
        }

        var sb = new StringBuilder(text.Length + 16);
        var positions = new List<int>();     // line starts that were edited
        var lengths = new List<int>();       // characters inserted (indent) or removed (outdent) there
        int copiedUpTo = 0;
        int lineStart = firstLine;

        while (true)
        {
            int lineEnd = LineEndOf(text, lineStart);

            if (outdent)
            {
                int remove = CountOutdentable(text, lineStart, lineEnd);
                if (remove > 0)
                {
                    sb.Append(text, copiedUpTo, lineStart - copiedUpTo);
                    copiedUpTo = lineStart + remove;
                    positions.Add(lineStart);
                    lengths.Add(remove);
                }
            }
            else if (lineEnd > lineStart)    // empty lines are left alone
            {
                sb.Append(text, copiedUpTo, lineStart - copiedUpTo);
                sb.Append(IndentUnit);
                copiedUpTo = lineStart;
                positions.Add(lineStart);
                lengths.Add(IndentUnit.Length);
            }

            if (lineStart >= lastLine || lineEnd >= text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        if (positions.Count == 0)
        {
            return false;
        }

        sb.Append(text, copiedUpTo, text.Length - copiedUpTo);
        newText = sb.ToString();
        newStart = MapPosition(s, positions, lengths, outdent);
        newEnd = MapPosition(e, positions, lengths, outdent);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsBlank(char c) => c == ' ' || c == '\t';

    private static char ClosingFor(char open) =>
        open == '(' ? ')' : open == '[' ? ']' : open == '{' ? '}' : '\0';

    private static char OpeningFor(char close) =>
        close == ')' ? '(' : close == ']' ? '[' : close == '}' ? '{' : '\0';

    /// <summary>
    /// Characters that may directly follow an auto-inserted closing bracket.
    /// This is VS Code's default "autoCloseBefore" set: whitespace plus ";:.,=}])>".
    /// </summary>
    private static bool IsSafeBeforeAutoClose(char next) =>
        next == '\n' || next == '\r' || next == ' ' || next == '\t'
        || next == ';' || next == ':' || next == '.' || next == ',' || next == '='
        || next == '}' || next == ']' || next == ')' || next == '>';

    private enum LineState { Code, String, Char, Comment }

    /// <summary>
    /// Scans one line from <paramref name="start"/> up to (not including)
    /// <paramref name="end"/> and reports what <paramref name="end"/> is inside:
    /// code, a string literal, a char literal or a // comment. Deliberately
    /// simple: no verbatim or raw strings and no block comments.
    /// </summary>
    private static LineState ScanLine(string text, int start, int end)
    {
        LineState state = LineState.Code;

        for (int i = start; i < end; i++)
        {
            char c = text[i];

            switch (state)
            {
                case LineState.String:
                    if (c == '\\') i++;                 // skip the escaped character
                    else if (c == '"') state = LineState.Code;
                    break;

                case LineState.Char:
                    if (c == '\\') i++;
                    else if (c == '\'') state = LineState.Code;
                    break;

                default:
                    if (c == '"') state = LineState.String;
                    else if (c == '\'') state = LineState.Char;
                    else if (c == '/' && i + 1 < end && text[i + 1] == '/') return LineState.Comment;
                    break;
            }
        }

        return state;
    }

    /// <summary>True when an odd number of backslashes directly precedes <paramref name="index"/>.</summary>
    private static bool IsEscaped(string text, int lineStart, int index)
    {
        int count = 0;
        for (int i = index - 1; i >= lineStart && text[i] == '\\'; i--)
        {
            count++;
        }

        return (count & 1) == 1;
    }

    internal static bool IsQuote(char c) => c == '"' || c == '\'';

    internal static bool IsSurroundChar(char c) => IsOpeningBracket(c) || IsQuote(c);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int LineStartOf(string text, int index) =>
        index <= 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;

    private static int LineEndOf(string text, int index)
    {
        int end = text.IndexOf('\n', index);
        return end < 0 ? text.Length : end;
    }

    /// <summary>True when exactly one character was removed, immediately before the caret.</summary>
    private static bool IsSingleCharDeletedBeforeCaret(string before, int beforeCaret, string after, int afterCaret)
    {
        return before != null && after != null
               && after.Length == before.Length - 1
               && beforeCaret >= 1 && beforeCaret <= before.Length
               && afterCaret == beforeCaret - 1
               && string.CompareOrdinal(before, 0, after, 0, afterCaret) == 0
               && string.CompareOrdinal(before, beforeCaret, after, afterCaret, before.Length - beforeCaret) == 0;
    }

    /// <summary>How many leading characters Shift+Tab removes from a line: one tab, or up to one indent of spaces.</summary>
    private static int CountOutdentable(string text, int lineStart, int lineEnd)
    {
        if (lineStart < lineEnd && text[lineStart] == '\t')
        {
            return 1;
        }

        int n = 0;
        while (n < IndentUnit.Length && lineStart + n < lineEnd && text[lineStart + n] == ' ')
        {
            n++;
        }

        return n;
    }

    /// <summary>
    /// Where position <paramref name="p"/> ends up after the per-line edits.
    /// Inserted indents push a position right only if it lies after the insertion
    /// point; removed characters pull it left, collapsing positions inside them.
    /// </summary>
    private static int MapPosition(int p, List<int> positions, List<int> lengths, bool outdent)
    {
        int result = p;

        for (int i = 0; i < positions.Count; i++)
        {
            int at = positions[i];

            if (outdent)
            {
                result -= Math.Max(0, Math.Min(p, at + lengths[i]) - at);
            }
            else if (at < p)
            {
                result += lengths[i];
            }
        }

        return result;
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