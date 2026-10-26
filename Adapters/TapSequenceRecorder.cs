using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RDETerminal.Domain.Core;
using UnityEngine;
using RDLevelEditor;

namespace RDETerminal.Adapters;

/// <summary>
/// Interactive time-sequence recorder. The recorder may be armed while the
/// editor is stopped; the first tap then starts/resumes playback and becomes
/// the semantic sequence origin once playback is actually running.
/// </summary>
public sealed class TapSequenceRecorder : MonoBehaviour
{
    private enum RecordingState
    {
        Idle,
        Armed,
        StartingPlayback,
        Recording
    }

    private EditorAdapter _editor;
    private EditorPositionSource _positionSource;
    private RecordingState _state;
    private KeyCode _tapKey;
    private KeyCode _stopKey;
    private readonly List<float> _offsets = new();
    private TaskCompletionSource<TimeSequence> _completion;
    private float _origin;

    internal bool IsActive => _state != RecordingState.Idle;

    internal void Initialize(EditorAdapter editor, EditorPositionSource positionSource)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _positionSource = positionSource ?? throw new ArgumentNullException(nameof(positionSource));
    }

    internal Task<TimeSequence> RecordAsync(KeyCode tapKey, KeyCode stopKey)
    {
        if (_editor == null || _positionSource == null)
        {
            throw new InvalidOperationException("TapSequenceRecorder is not initialized.");
        }

        if (_state != RecordingState.Idle || _completion != null)
        {
            throw new InvalidOperationException("A tap sequence is already being recorded.");
        }

        if (tapKey == stopKey)
        {
            throw new ArgumentException("tapKey and stopKey must be different.");
        }

        _tapKey = tapKey;
        _stopKey = stopKey;
        _offsets.Clear();
        _origin = 0f;
        _completion = new TaskCompletionSource<TimeSequence>(TaskCreationOptions.RunContinuationsAsynchronously);
        _state = RecordingState.Armed;

        // Suppression begins before the next game Update so the first tap cannot
        // also trigger the game's own pre-start logic or become a gameplay hit.
        TapRecordingInputSuppression.Begin();

        return _completion.Task;
    }

    private void Update()
    {
        if (_state == RecordingState.Idle)
        {
            return;
        }

        // Direct Unity input is intentionally used here. Harmony suppression only
        // affects the game's RDInput path, leaving the recorder's tap/stop keys visible.
        if (Input.GetKeyDown(_stopKey))
        {
            Complete();
            return;
        }

        if (scnEditor.instance == null)
        {
            Fail(new InvalidOperationException("The editor scene was unloaded while recording taps."));
            return;
        }

        switch (_state)
        {
            case RecordingState.Armed:
                UpdateArmed();
                break;

            case RecordingState.StartingPlayback:
                UpdateStartingPlayback();
                break;

            case RecordingState.Recording:
                UpdateRecording();
                break;
        }
    }

    private void UpdateArmed()
    {
        if (!Input.GetKeyDown(_tapKey))
        {
            return;
        }

        // The physical first tap is always semantic time offset 0. When playback is
        // already running, the current playback time is the recording origin immediately.
        if (_editor.IsPlaying)
        {
            BeginRecordingAtCurrentPlaybackTime();
            return;
        }

        _state = RecordingState.StartingPlayback;
        _editor.StartPlaybackForRecording(allowReload: true);
    }

    private void UpdateStartingPlayback()
    {
        if (_editor.IsPlaying)
        {
            BeginRecordingAtCurrentPlaybackTime();
            return;
        }

        // The first call is allowed to reload the game scene. Subsequent calls
        // only advance an already-requested load into PreStart/Recording state.
        _editor.StartPlaybackForRecording(allowReload: false);
    }

    private void UpdateRecording()
    {
        if (!_editor.IsPlaying)
        {
            // Playback can be interrupted externally. Do not manufacture timing
            // data while stopped; wait for the user/game to become playable again.
            return;
        }

        if (!Input.GetKeyDown(_tapKey))
        {
            return;
        }

        float time = _positionSource.GetPlaybackTime();
        float offset = time - _origin;

        // Avoid a tiny negative caused by floating-point/read-order noise on the
        // first frame after playback starts.
        if (offset < 0f && offset > -1e-6f)
        {
            offset = 0f;
        }

        _offsets.Add(offset);
    }

    private void BeginRecordingAtCurrentPlaybackTime()
    {
        _origin = _positionSource.GetPlaybackTime();
        _offsets.Clear();
        _offsets.Add(0f);
        _state = RecordingState.Recording;
    }

    private void Complete()
    {
        if (_completion == null)
        {
            ResetState();
            return;
        }

        TaskCompletionSource<TimeSequence> completion = _completion;
        TimeSequence result = TimeSequence.FromOffsets(_offsets);

        ResetState();
        TapRecordingInputSuppression.EndAfterCurrentFrame();
        completion.TrySetResult(result);
    }

    private void Fail(Exception error)
    {
        TaskCompletionSource<TimeSequence> completion = _completion;

        ResetState();
        TapRecordingInputSuppression.EndAfterCurrentFrame();

        if (completion != null)
        {
            completion.TrySetException(error);
        }
    }

    private void ResetState()
    {
        _state = RecordingState.Idle;
        _offsets.Clear();
        _origin = 0f;
        _completion = null;
        _tapKey = KeyCode.None;
        _stopKey = KeyCode.None;
    }

    private void OnDestroy()
    {
        if (_completion != null)
        {
            Fail(new InvalidOperationException("Tap sequence recorder was destroyed while recording."));
        }

        TapRecordingInputSuppression.ForceEnd();
    }
}