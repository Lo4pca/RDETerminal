using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RDLevelEditor;
using RDETerminal.Domain.Abstractions;
using RDETerminal.Domain.Core;
using RDETerminal.Domain;

namespace RDETerminal.Adapters;

public sealed class GameLevelBridge(EditorAdapter editorAdapter) : IGameLevelBridge
{
    private readonly EditorAdapter _editorAdapter = editorAdapter ?? throw new ArgumentNullException(nameof(editorAdapter));
    private readonly ReflectionGameEventBridge _eventBridge = BridgeServices.Shared.EventBridge;

    public LevelDocument CaptureLevel()
    {
        scnEditor editor = scnEditor.instance;
        if (editor == null)
        {
            return new LevelDocument();
        }

        var events = new List<LevelEventSnapshot>();

        foreach (LevelEventControl_Base control in editor.eventControls)
        {
            if (control?.levelEvent == null)
            {
                continue;
            }

            LevelEventSnapshot snapshot = _eventBridge.Capture(control.levelEvent);
            if (snapshot != null)
            {
                snapshot.MarkForUpdate();
                events.Add(snapshot);
            }
        }

        return new LevelDocument(events);
    }

    public LevelApplyResult ApplyLevel(LevelDocument level)
    {
        scnEditor editor = scnEditor.instance;
        if (editor == null || level == null)
        {
            return new LevelApplyResult();
        }

        var liveControls = editor.eventControls
            .Where(c => c != null && c.levelEvent != null)
            .ToList();

        var liveByKey = new Dictionary<string, LevelEventControl_Base>(StringComparer.Ordinal);
        foreach (LevelEventControl_Base control in liveControls)
        {
            string key = GetLiveKey(control.levelEvent);
            if (!liveByKey.ContainsKey(key))
            {
                liveByKey[key] = control;
            }
        }

        bool anyApplied = false;
        var result = new LevelApplyResult
        {
            TotalEvents = level.Events.Count
        };

        foreach (LevelEventSnapshot snapshot in level.Events)
        {
            if (snapshot == null)
            {
                continue;
            }

            if (snapshot.IsCreate)
            {
                _editorAdapter.CreateEventFromSnapshot(
                    snapshot,
                    selectCreatedControl: true,
                    skipSaveState: false,
                    callOnCreate: true);

                result.AppliedEvents++;
                anyApplied = true;
                continue;
            }

            if (snapshot.IsDelete)
            {
                if (TryResolveControl(liveByKey, snapshot, out LevelEventControl_Base deleteControl))
                {
                    editor.DeleteEventControl(deleteControl,false,false);
                    result.AppliedEvents++;
                    anyApplied = true;
                }
                else
                {
                    result.SkippedEvents++;
                }

                continue;
            }

            if (!snapshot.HasChanges)
            {
                continue;
            }

            if (!TryResolveControl(liveByKey, snapshot, out LevelEventControl_Base control) || control?.levelEvent == null)
            {
                result.SkippedEvents++;
                continue;
            }

            EventApplyResult item = _eventBridge.Apply(snapshot, control.levelEvent);
            result.AppliedEvents++;
            result.WrittenCount += item.WrittenCount;
            result.FailedCount += item.FailedCount;

            if (!item.Success)
            {
                result.AddIssue(item.ToSummaryString(3));
            }

            control.UpdateUI();
            anyApplied = true;
        }

        if (anyApplied)
        {
            editor.InspectorPanel_UpdateUI();
        }

        return result;
    }

    private static bool TryResolveControl(
        Dictionary<string, LevelEventControl_Base> liveByKey,
        LevelEventSnapshot snapshot,
        out LevelEventControl_Base control)
    {
        control = null;

        if (snapshot == null)
        {
            return false;
        }

        string key = GetSnapshotKey(snapshot);
        return liveByKey.TryGetValue(key, out control);
    }

    private static string GetLiveKey(object gameEvent)
    {
        if (ReflectionUtil.TryGetMemberValue(gameEvent, "uid", out object uid) && uid != null)
        {
            return "uid:" + Convert.ToString(uid, CultureInfo.InvariantCulture);
        }

        return "fallback:" +
               gameEvent.GetType().Name + "|" +
               GetValueAsString(gameEvent, "bar") + "|" +
               GetValueAsString(gameEvent, "beat") + "|" +
               GetValueAsString(gameEvent, "y");
    }

    private static string GetSnapshotKey(LevelEventSnapshot snapshot)
    {
        if (snapshot.TryGet("uid", out object uid) && uid != null)
        {
            return "uid:" + Convert.ToString(uid, CultureInfo.InvariantCulture);
        }

        return "fallback:" +
               snapshot.Type + "|" +
               snapshot.GetString("bar", "1") + "|" +
               snapshot.GetString("beat", "1") + "|" +
               snapshot.GetString("y", "0");
    }

    private static string GetValueAsString(object target, string memberName)
    {
        return ReflectionUtil.TryGetMemberValue(target, memberName, out object value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : string.Empty;
    }
}