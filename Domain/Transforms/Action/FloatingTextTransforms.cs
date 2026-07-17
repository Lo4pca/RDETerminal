using System;
using System.Collections.Generic;
using System.Text;
using RDETerminal.Domain.Core;
using RDETerminal.Domain.Transforms.Common;
using RDETerminal.Domain.Queries;

namespace RDETerminal.Domain.Transforms.Action;

public static class FloatingTextTransforms
{
    public static LevelDocument SplitAndAdvanceTextsWithOffset(
        LevelDocument level,
        Func<LevelEventSnapshot, bool> filter,
        float offset = 0.1f)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));

        var floatingTextIds = new HashSet<int>();
        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (EventQueries.TypeIs(evt,EventTypeNames.FloatingText)&&filter(evt))
            {
                string originalText = evt.GetString(EventFieldNames.Text, string.Empty);
                string modifiedText = NormalizeFloatingText(originalText);

                var modified = evt.Clone();
                modified.Set(EventFieldNames.Text, modifiedText);

                int id = modified.GetInt(EventFieldNames.Id,-1);
                int bar = modified.GetInt(EventFieldNames.Bar);
                double beat = modified.GetFloat(EventFieldNames.Beat);
                int y = modified.GetInt(EventFieldNames.Y);

                int numSyllables = CountChar(modifiedText, '/');
                floatingTextIds.Add(id);

                newEvents.Add(modified);

                for (int i = 0; i < numSyllables; i++)
                {
                    EventTransforms.IncrementBarAndBeats(ref bar, ref beat, offset);

                    var advance = new LevelEventSnapshot(EventTypeNames.AdvanceText);
                    advance.MarkForCreate("Actions");
                    advance.Set(EventFieldNames.Id, modified.Get(EventFieldNames.Id));
                    advance.Set(EventFieldNames.Bar, bar);
                    advance.Set(EventFieldNames.Beat, beat);
                    advance.Set(EventFieldNames.Y, y);

                    newEvents.Add(advance);
                }
            }
            else if (EventQueries.TypeIs(evt,EventTypeNames.AdvanceText))
            {
                int id = evt.GetInt(EventFieldNames.Id,-1);
                if (floatingTextIds.Contains(id)) evt.MarkForDelete();
                newEvents.Add(evt);
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }

    public static LevelDocument RandomizeTextsAnglesPositions(
        LevelDocument level,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Func<LevelEventSnapshot, bool> textFilter,
        Random rng = null)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (textFilter == null) throw new ArgumentNullException(nameof(textFilter));
        if (minX > maxX) throw new ArgumentException("minX must be <= maxX");
        if (minY > maxY) throw new ArgumentException("minY must be <= maxY");

        rng ??= new Random();

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (EventQueries.TypeIs(evt,EventTypeNames.FloatingText) && textFilter(evt))
            {
                var modified = evt.Clone();
                modified.Set(EventFieldNames.TextPosition, new[]
                {
                    rng.Next(minX, maxX + 1),
                    rng.Next(minY, maxY + 1)
                });
                modified.Set(EventFieldNames.Angle, rng.Next(0, 361));
                newEvents.Add(modified);
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }

    public static LevelDocument SetTextFontSize(
        LevelDocument level,
        double newSize,
        Func<LevelEventSnapshot, bool> textFilter)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (textFilter == null) throw new ArgumentNullException(nameof(textFilter));

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (EventQueries.TypeIs(evt,EventTypeNames.FloatingText) && textFilter(evt))
            {
                var modified = evt.Clone();
                modified.Set(EventFieldNames.Size, newSize);
                newEvents.Add(modified);
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }

    public static LevelDocument SetTextDuration(
        LevelDocument level,
        float newDuration,
        Func<LevelEventSnapshot, bool> textFilter)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));
        if (textFilter == null) throw new ArgumentNullException(nameof(textFilter));

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (EventQueries.TypeIs(evt,EventTypeNames.FloatingText) && textFilter(evt))
            {
                var modified = evt.Clone();
                modified.Set(EventFieldNames.FadeOutRate, newDuration);
                newEvents.Add(modified);
            }
            else
            {
                newEvents.Add(evt);
            }
        }

        return new LevelDocument(newEvents);
    }

    private static string NormalizeFloatingText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string cleaned = text.Replace("/", string.Empty);
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        if (cleaned.Length == 1)
        {
            return cleaned;
        }

        var sb = new StringBuilder(cleaned.Length * 2 - 1);
        for (int i = 0; i < cleaned.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('/');
            }

            sb.Append(cleaned[i]);
        }

        return sb.ToString();
    }

    private static int CountChar(string text, char c)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == c)
            {
                count++;
            }
        }

        return count;
    }
}