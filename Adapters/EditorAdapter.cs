using System;
using System.Collections.Generic;
using RDETerminal.Domain;
using RDETerminal.Domain.Core;
using RDLevelEditor;

namespace RDETerminal.Adapters;

public sealed class EditorAdapter(ReflectionGameEventBridge eventBridge)
{
    private readonly ReflectionGameEventBridge _eventBridge = eventBridge ?? throw new ArgumentNullException(nameof(eventBridge));

    /// <summary>
    /// Resolves the current <see cref="scnEditor"/> instance on every access.
    /// Throws <see cref="InvalidOperationException"/> when the editor scene is not loaded.
    /// Never cache this value — the instance changes across scene reloads.
    /// </summary>
    private static scnEditor Editor =>
        scnEditor.instance ?? throw new InvalidOperationException(
            "scnEditor.instance is null. The editor scene is not active.");

    public EditorAdapter()
        : this(BridgeServices.Shared.EventBridge)
    {
    }

    public EventSet GetSelectedEvents()
    {
        List<LevelEventSnapshot> items = [];

        foreach (LevelEventControl_Base control in Editor.selectedControls)
        {
            if (control == null || control.levelEvent == null)
            {
                continue;
            }

            LevelEventSnapshot snapshot = _eventBridge.Capture(control.levelEvent);
            if (snapshot != null)
            {
                snapshot.MarkForUpdate();
                items.Add(snapshot);
            }
        }

        return new EventSet(items);
    }

    public LevelEventControl_Base CreateEvent(
        LevelEvent_Base levelEvent,
        Tab tab,
        BarAndBeat barAndBeat,
        Action<LevelEvent_Base> configure = null,
        bool selectCreatedControl = true,
        bool skipSaveState = false)
    {
        if (levelEvent == null)
        {
            throw new ArgumentNullException(nameof(levelEvent));
        }

        levelEvent.barAndBeat = barAndBeat;
        configure?.Invoke(levelEvent);
        levelEvent.OnCreate();

        LevelEventControl_Base control = Editor.CreateEventControl(levelEvent, tab, skipSaveState) ?? throw new InvalidOperationException("CreateEventControl returns null.");
        control.UpdateUI();

        if (selectCreatedControl)
        {
            Editor.SelectEventControl(control, false);
        }

        return control;
    }

    public void DeleteEventControl(LevelEventControl_Base eventControl, bool selectControlToTheLeft, bool sound = false)
    {
        Editor.DeleteEventControl(eventControl, selectControlToTheLeft, sound);
    }

    public LevelEventControl_Base CreateEventFromSnapshot(
        LevelEventSnapshot snapshot,
        bool selectCreatedControl = true,
        bool skipSaveState = false,
        bool callOnCreate = false)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.Action != SnapshotAction.Create)
        {
            throw new InvalidOperationException("CreateEventFromSnapshot can only handle snapshots with Action == Create.");
        }

        Tab tab = ParseTab(snapshot.TargetTabName, Tab.Actions);
        LevelEventType eventType = ParseEventType(snapshot.Type);
        LevelEvent_Base levelEvent = CreateLevelEventInstance(eventType);

        _eventBridge.Apply(snapshot, levelEvent);

        if (callOnCreate)
        {
            levelEvent.OnCreate();
        }

        LevelEventControl_Base control = Editor.CreateEventControl(levelEvent, tab, skipSaveState) ?? throw new InvalidOperationException("CreateEventControl returns null.");
        control.UpdateUI();

        if (selectCreatedControl)
        {
            Editor.SelectEventControl(control, false);
        }

        return control;
    }

    public EventSet CreateEvents(
        string eventTypeName,
        float spacing,
        int number,
        int numTracks,
        int startY=0,
        bool selectLastCreatedControl = true,
        bool skipSaveState = false)
    {
        LevelEventControl_Base anchor = GetFirstSelectedControl(Editor);
        if (anchor != null && anchor.levelEvent != null)
        {
            return CreateEventsFromControl(
                eventTypeName,
                spacing,
                number,
                numTracks,
                anchor,
                selectLastCreatedControl,
                skipSaveState);
        }

        return CreateEvents(
            eventTypeName,
            new BarAndBeat(1, 1f),
            spacing,
            number,
            numTracks,
            startY,
            Editor.currentTab,
            selectLastCreatedControl,
            skipSaveState);
    }

    public EventSet CreateEvents(
        string eventTypeName,
        int startBar,
        float startBeat,
        float spacing,
        int number,
        int numTracks,
        int startY=0,
        bool selectLastCreatedControl = true,
        bool skipSaveState = false)
    {
        return CreateEvents(
            eventTypeName,
            new BarAndBeat(startBar, startBeat),
            spacing,
            number,
            numTracks,
            startY,
            Editor.currentTab,
            selectLastCreatedControl,
            skipSaveState);
    }

    public EventSet CreateEvents(
        string eventTypeName,
        BarAndBeat startBarAndBeat,
        float spacing,
        int number,
        int numTracks,
        int startY,
        Tab tab,
        bool selectLastCreatedControl = true,
        bool skipSaveState = false)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("eventTypeName is empty.", nameof(eventTypeName));
        }

        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "number must be greater than zero.");
        }

        if (numTracks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numTracks), "numTracks must be greater than zero.");
        }

        if (spacing < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing), "spacing must be non-negative.");
        }

        LevelEventType eventType = ParseEventType(eventTypeName);
        bool fixBarAndBeat = ShouldFixBarAndBeat(eventType);
        List<LevelEventControl_Base> createdControls = CreateEventsCore(
            eventType,
            startBarAndBeat,
            spacing,
            number,
            numTracks,
            startY,
            tab,
            fixBarAndBeat,
            selectLastCreatedControl,
            skipSaveState);

        return CaptureCreatedEvents(createdControls);
    }

    private List<LevelEventControl_Base> CreateEventsCore(
        LevelEventType eventType,
        BarAndBeat startBarAndBeat,
        float spacing,
        int number,
        int numTracks,
        int startY,
        Tab tab,
        bool fixBarAndBeat,
        bool selectLastCreatedControl,
        bool skipSaveState)
    {
        List<LevelEventControl_Base> created = new(number);

        for (int i = 0; i < number; i++)
        {
            LevelEvent_Base levelEvent = CreateLevelEventInstance(eventType);

            BarAndBeat currentBarAndBeat = OffsetBarAndBeat(Editor, startBarAndBeat, spacing * i, fixBarAndBeat);

            LevelEventControl_Base control = CreateEvent(
                levelEvent,
                tab,
                currentBarAndBeat,
                delegate (LevelEvent_Base ev)
                {
                    ev.y = startY+(i%numTracks);
                },
                false,
                skipSaveState);

            created.Add(control);
        }

        if (selectLastCreatedControl && created.Count > 0)
        {
            Editor.SelectEventControl(created[created.Count - 1], false);
        }

        return created;
    }

    public EventSet CreateEventsFromControl(
        string eventTypeName,
        float spacing,
        int number,
        int numTracks,
        LevelEventControl_Base anchorControl,
        bool selectLastCreatedControl = true,
        bool skipSaveState = false)
    {
        if (anchorControl == null)
        {
            throw new ArgumentNullException(nameof(anchorControl));
        }

        if (anchorControl.levelEvent == null)
        {
            throw new InvalidOperationException("anchorControl.levelEvent is null.");
        }
        return CreateEvents(
            eventTypeName,
            anchorControl.levelEvent.barAndBeat,
            spacing,
            number,
            numTracks,
            anchorControl.levelEvent.y,
            Editor.currentTab,
            selectLastCreatedControl,
            skipSaveState);
    }

    private EventSet CaptureCreatedEvents(IEnumerable<LevelEventControl_Base> controls)
    {
        List<LevelEventSnapshot> items = [];

        foreach (LevelEventControl_Base control in controls)
        {
            if (control == null || control.levelEvent == null)
            {
                continue;
            }

            LevelEventSnapshot snapshot = _eventBridge.Capture(control.levelEvent);
            if (snapshot != null)
            {
                snapshot.MarkForUpdate();
                items.Add(snapshot);
            }
        }

        return new EventSet(items);
    }

    private static LevelEventControl_Base GetFirstSelectedControl(scnEditor editor)
    {
        if (editor == null || editor.selectedControls == null)
        {
            return null;
        }

        foreach (LevelEventControl_Base control in editor.selectedControls)
        {
            if (control != null && control.levelEvent != null)
            {
                return control;
            }
        }

        return null;
    }

    private static LevelEvent_Base CreateLevelEventInstance(LevelEventType eventType)
    {
        string typeName = "RDLevelEditor.LevelEvent_" + eventType;
        Type type = ResolveType(typeName) ?? throw new InvalidOperationException("We can't find the type name: " + typeName);
        object instance = Activator.CreateInstance(type);

        if (instance is not LevelEvent_Base levelEvent)
        {
            throw new InvalidOperationException("The type of the created object is not LevelEvent_Base: " + typeName);
        }

        return levelEvent;
    }

    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);

    private static Type ResolveType(string typeName)
    {
        if (TypeCache.TryGetValue(typeName, out Type cached))
        {
            return cached;
        }

        Type type = Type.GetType(typeName, false);

        if (type == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false, false);
                if (type != null)
                {
                    break;
                }
            }
        }

        if (type != null)
        {
            TypeCache[typeName] = type;
        }

        return type;
    }

    private static LevelEventType ParseEventType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new InvalidOperationException("The snapshot has no Type, event can't be created.");
        }

        if (!Enum.TryParse(typeName, true, out LevelEventType eventType))
        {
            throw new InvalidOperationException("Can't parse the event type: " + typeName);
        }

        return eventType;
    }

    private static Tab ParseTab(string tabName, Tab fallback)
    {
        if (string.IsNullOrWhiteSpace(tabName))
        {
            return fallback;
        }

        if (!Enum.TryParse(tabName, true, out Tab tab))
        {
            return fallback;
        }

        return tab;
    }

    private static bool ShouldFixBarAndBeat(LevelEventType eventType)
    {
        return eventType == LevelEventType.AdvanceText
            || eventType == LevelEventType.AddOneshotBeat
            || eventType == LevelEventType.AddClassicBeat;
    }

    private static BarAndBeat OffsetBarAndBeat(scnEditor editor, BarAndBeat startBarAndBeat, float beatOffset, bool fixBarAndBeat)
    {
        BarAndBeat barAndBeat = new(startBarAndBeat.bar, startBarAndBeat.beat + beatOffset);

        if (!fixBarAndBeat || editor == null || editor.timeline == null)
        {
            return barAndBeat;
        }

        return editor.timeline.FixBarAndBeat(barAndBeat);
    }
}