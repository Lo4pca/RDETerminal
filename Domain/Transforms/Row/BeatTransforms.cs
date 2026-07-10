using System;
using System.Collections.Generic;
using RDETerminal.Domain.Core;
using RDETerminal.Domain.Queries;

namespace RDETerminal.Domain.Transforms.Row;

public static class BeatTransforms
{
    public static LevelDocument TransferBeatsFrom(
        LevelDocument level,
        int fromRow,
        int toRow,
        int startBar,
        float startBeat)
    {
        if (level == null) throw new ArgumentNullException(nameof(level));

        var newEvents = new List<LevelEventSnapshot>(level.Events.Count);

        foreach (var evt in level.Events)
        {
            if (EventQueries.IsEventInRowTab(evt)&&evt.GetInt(EventFieldNames.Row)==fromRow&&evt.GetInt(EventFieldNames.Bar)>=startBar&&evt.GetFloat(EventFieldNames.Beat)>=startBeat)
            {
                //根据scnEditor.AddNewEventControl，位于"Rows"的事件挂载的parent transform取决于room id
                //如果只修改Row属性，转移不处于同一房间的beat将会出错
                //因此选择删除原有事件并创建新事件
                var modified = evt.Clone();
                var deleted = evt.Clone();
                deleted.MarkForDelete();
                modified.MarkForCreate("Rows");
                modified.Set(EventFieldNames.Row, toRow);
                newEvents.Add(modified);
                newEvents.Add(deleted);
            }
            else newEvents.Add(evt);
        }

        return new LevelDocument(newEvents);
    }
}