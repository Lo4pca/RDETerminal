using System;
using System.Collections;
using RDETerminal.Notebook;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RDETerminal.UI;

/// <summary>
/// MonoBehaviour root for the terminal. Owns scene lifecycle, the input loop,
/// and cell execution. UI construction, completion, and transcript display are
/// handled by dedicated collaborator classes.
/// </summary>
public sealed class TerminalWindow : MonoBehaviour
{
    // ── Nested types used by collaborators ────────────────────────────────────

    /// <summary>Drag handle MonoBehaviour placed on the header row.</summary>
    public sealed class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private RectTransform _window;
        private RectTransform _parent;
        private Vector2 _startPointerLocal;
        private Vector2 _startAnchoredPosition;

        public void Initialize(RectTransform window)
        {
            _window = window;
            _parent = window != null ? window.parent as RectTransform : null;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window == null || _parent == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent, eventData.position, eventData.pressEventCamera, out _startPointerLocal);
            _startAnchoredPosition = _window.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || _parent == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parent, eventData.position, eventData.pressEventCamera, out Vector2 current))
            {
                return;
            }

            _window.anchoredPosition = _startAnchoredPosition + (current - _startPointerLocal);
        }
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private NotebookKernel _kernel;
    private TerminalCompletionController _completion;
    private TerminalTranscriptView _transcript;

    private GameObject _root;
    private Canvas _canvas;
    private bool _ownsCanvas;
    private InputField _inputField;
    private bool _visible;
    private bool _editorSceneActive;

    private readonly CommandHistory _history = new();

    // History navigation state (see HandleHistoryKeys).
    // What the user had typed before stepping back into history; restored when
    // they step forward past the newest entry.
    private string _draft = string.Empty;
    // Input text and caret as of the end of the previous frame, i.e. *before* the
    // InputField handled this frame's arrow keys (it runs before this script).
    private string _lastInputText = string.Empty;
    private int _lastCaret;
    // Whether the input field had focus at the end of the previous frame (see RecoverFromEscapeCancel).
    private bool _inputWasFocused;
    // Selection as of the end of the previous frame (anchor == focus when nothing is selected).
    private int _lastSelAnchor;
    private int _lastSelFocus;

    // The game takes focus away from the input when Tab is pressed (see HandleTabKey), so
    // focus is handed back once Tab is released. These describe the state to return to.
    private bool _focusReturnPending;
    private int _focusReturnAnchor;
    private int _focusReturnFocus;
    private bool _focusReturnCaretAtEnd;
    private string _focusReturnText = string.Empty;

    // Undo / redo (see RecordUndoStep).
    private readonly TerminalUndoHistory _undo = new();
    private EditKind _pendingEditKind;   // what kind of edit this frame's typing was
    private char _pendingTypedChar;
    private bool _programmaticEdit;      // code set the text this frame (it was not typed)
    private bool _skipUndoRecord;        // this frame's change is an undo/redo or a cleared input
    private bool _applyingText;          // true while the field's text is being set from code

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        // Intentionally empty. TerminalBootstrap calls Initialize() immediately
        // after AddComponent with fully-constructed dependencies.
    }

    /// <summary>
    /// Called by <see cref="TerminalBootstrap"/> immediately after AddComponent.
    /// Wires all dependencies and bootstraps the UI if the editor scene is active.
    /// </summary>
    internal void Initialize(NotebookKernel kernel)
    {
        try
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _completion = new TerminalCompletionController(kernel, SetInputTextSilently);
            _transcript = new TerminalTranscriptView(kernel);

            SceneManager.sceneLoaded   += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            if (SceneManager.GetActiveScene().name == "scnEditor")
            {
                _editorSceneActive = true;
                BuildNativeUi();
                SetVisible(false);
            }

            Plugin.LogInfo("TerminalWindow initialized successfully.");
        }
        catch (Exception ex)
        {
            Plugin.LogError("TerminalWindow initialization failed:");
            Plugin.LogError(ex.ToString());
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded   -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        DestroyNativeUi();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "scnEditor") return;
        _editorSceneActive = true;
        BuildNativeUi();
        SetVisible(false);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != "scnEditor") return;

        _editorSceneActive = false;
        _completion?.Detach();
        _transcript?.Detach();

        // Scene objects are being destroyed by Unity — null our references so
        // DestroyNativeUi doesn't try to Destroy() already-dead objects.
        _root = null;
        _canvas = null;
        _ownsCanvas = false;
        _inputField = null;
    }

    // ── Update / input loop ───────────────────────────────────────────────────

    private void Update()
    {
        if (!_editorSceneActive) return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleVisible();
        }

        // These have to run before the focus check below, because both Escape (Unity's
        // "cancel edit") and Tab (a game hotkey) make the input field lose focus.
        RecoverFromEscapeCancel();
        ReturnFocusAfterTab();
        if (HandleTabKey()) return;

        if (!_visible || _inputField == null || !_inputField.isFocused) return;

        // Ctrl+Enter runs the cell. OnValidateInput stops that same key press
        // from also inserting a line break into the code.
        if (EnterPressedThisFrame() && IsControlHeld())
        {
            ExecuteCurrentCellPublic();
            return;
        }

        if (_completion.IsVisible)
        {
            // While the completion list is open it owns the arrow keys, Enter and
            // Escape (and Tab, see HandleTabKey). The list only opens while an
            // identifier is being typed or after an explicit trigger character, so it
            // does not swallow the Enter pressed after '{' or '}'. See
            // TerminalCompletionController.
            if (Input.GetKeyDown(KeyCode.UpArrow))   { _completion.MoveSelection(-1); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { _completion.MoveSelection( 1); return; }
            if (EnterPressedThisFrame())             { _completion.Commit();          return; }
            if (Input.GetKeyDown(KeyCode.Escape))    { _completion.Hide();            return; }
        }
        else if (HandleHistoryKeys())
        {
            return; // the text was replaced by a history entry; nothing left to post-process
        }

        if (HandleUndoRedoKeys())
        {
            return;
        }

        if (HandleIndentShortcuts())
        {
            return;
        }

        ApplyTypingRules();
    }

    private void LateUpdate()
    {
        if (!_editorSceneActive || _inputField == null) return;

        // Record what changed this frame for undo/redo. This compares against last frame's
        // snapshot, so it has to run before the snapshot is refreshed below.
        RecordUndoStep();

        // Remember where the caret is now, so next frame's arrow-key handling can
        // tell where it was before the InputField moved it.
        _lastInputText = _inputField.text ?? string.Empty;
        _lastCaret = _inputField.caretPosition;
        _inputWasFocused = _inputField.isFocused;

        _lastSelAnchor = _inputField.selectionAnchorPosition;
        _lastSelFocus = _inputField.selectionFocusPosition;
    }

    // ── Escape ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unity's InputField treats Escape as "cancel editing": it reverts its text to
    /// what it was when it gained focus (usually empty, so everything typed since is
    /// lost) and drops focus. That is built-in InputField behavior (see
    /// InputField.wasCanceled), it cannot be switched off, and it happens before
    /// this script's Update runs. So undo it here: put back the text from before the
    /// key press, close the completion list (what Escape is meant to do in the
    /// terminal) and take focus again.
    /// </summary>
    private void RecoverFromEscapeCancel()
    {
        if (!_visible || _inputField == null) return;
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // Only a cancel if the field had focus last frame and has lost it since.
        if (!_inputWasFocused || _inputField.isFocused) return;

        int caret = _lastCaret;

        _completion.Hide();
        SetInputText(_lastInputText, caret);
        StartCoroutine(RefocusInput(caret, caret, caretAtEnd: false));
    }

    /// <summary>
    /// Gives the input focus back and restores the selection. Focusing selects all
    /// text, and the field does that in its own LateUpdate, so the selection is
    /// restored a frame later.
    /// </summary>
    private IEnumerator RefocusInput(int anchor, int focus, bool caretAtEnd)
    {
        FocusInput();
        yield return null;

        if (_inputField == null || !_visible || !_inputField.isFocused) yield break;

        int length = (_inputField.text ?? string.Empty).Length;
        if (caretAtEnd)
        {
            anchor = length;
            focus = length;
        }

        anchor = Mathf.Clamp(anchor, 0, length);
        focus = Mathf.Clamp(focus, 0, length);

        _inputField.caretPosition           = focus;
        _inputField.selectionAnchorPosition = anchor;
        _inputField.selectionFocusPosition  = focus;
        _inputField.ForceLabelUpdate();
    }

    // ── Keyboard helpers ──────────────────────────────────────────────────────

    private static bool IsControlHeld() =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    private static bool IsShiftHeld() =>
        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    private static bool IsAltHeld() =>
        Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    private static bool EnterPressedThisFrame() =>
        Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

    /// <summary>Enter is down right now (including key repeat, which GetKeyDown misses).</summary>
    private static bool EnterKeyActive() =>
        EnterPressedThisFrame() || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);

    /// <summary>
    /// Wired to the InputField. Ctrl+Enter executes the cell, so the line break
    /// that same key press would insert is dropped here. Requiring the Enter key
    /// to be active keeps line breaks that arrive by pasting (Ctrl+V) intact.
    /// </summary>
    private char OnValidateInput(string text, int charIndex, char addedChar)
    {
        // Setting the field's text from code replays every character through here, so the key
        // chords below must only filter what the user types, not text that code is applying.
        if (!_applyingText)
        {
            if ((addedChar == '\n' || addedChar == '\r') && IsControlHeld() && EnterKeyActive())
            {
                return '\0';
            }

            // Ctrl+Z / Ctrl+Y are undo and redo (HandleUndoRedoKeys); the key press must not
            // also type a letter.
            if (IsControlHeld() && !IsAltHeld() && (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Y)))
            {
                return '\0';
            }
        }

        return _completion.OnValidateInput(text, charIndex, addedChar);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildNativeUi()
    {
        DestroyNativeUi();

        var result = TerminalUiBuilder.Build(
            OnValidateInput,
            _completion.OnInputChanged,
            this);

        _root       = result.Root;
        _canvas     = result.Canvas;
        _ownsCanvas = result.OwnsCanvas;
        _inputField = result.InputField;

        _transcript.Attach(
            result.TranscriptText,
            result.TranscriptContent,
            result.TranscriptLayoutElement,
            result.ScrollRect);

        _completion.Attach(
            result.InputField,
            result.CompletionPanel,
            result.CompletionRows,
            result.CompletionDetailPanel,
            result.CompletionDetailText,
            result.SignaturePanel,
            result.SignatureText);

        _transcript.Refresh();

        // A fresh input field: forget the old one's snapshot and undo history.
        _lastInputText = string.Empty;
        _lastCaret = 0;
        _lastSelAnchor = 0;
        _lastSelFocus = 0;
        _undo.Clear();
        _pendingEditKind = EditKind.Other;
        _pendingTypedChar = '\0';
        _programmaticEdit = false;
        _skipUndoRecord = false;
    }

    private void DestroyNativeUi()
    {
        _completion?.Detach();
        _transcript?.Detach();

        _inputField?.DeactivateInputField();
        _inputField = null;
        EventSystem.current?.SetSelectedGameObject(null);

        if (_root != null)
        {
            Destroy(_root);
            _root = null;
        }

        if (_ownsCanvas && _canvas != null)
        {
            Destroy(_canvas.gameObject);
            _canvas     = null;
            _ownsCanvas = false;
        }
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    private void ToggleVisible() => SetVisible(!_visible);

    private void SetVisible(bool visible)
    {
        _visible = visible;

        if (!visible)
        {
            _completion?.Hide();
            _inputField?.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        _root?.SetActive(visible);

        if (visible)
        {
            _transcript?.Refresh();
            StartCoroutine(FocusInputNextFrame());
        }
    }

    // ── Cell execution ────────────────────────────────────────────────────────

    /// <summary>Called by Update and the Run button via TerminalUiBuilder.</summary>
    internal async void ExecuteCurrentCellPublic()
    {
        if (_kernel == null || _inputField == null) return;

        string code = _inputField.text.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;

        _history.Add(code);
        _draft = string.Empty;
        _completion.Hide();
        SetInputTextSilently(string.Empty);
        _undo.Clear();               // the input starts over; there is nothing to undo into
        _skipUndoRecord = true;
        _history.ResetCursor();
        try
        {
            // Long-running commands such as await editor.RecordTaps() should not
            // keep the terminal InputField focused while Space/other keys are
            // being used for recording.
            _inputField.DeactivateInputField();

            NotebookCellResult result = await _kernel.ExecuteAsync(code);
            if (!result.Success)
            {
                Plugin.LogError(result.Error);
            }

            _transcript?.Refresh();
        }
        finally
        {
            FocusInput();
        }
    }

    /// <summary>Called by the Reset button via TerminalUiBuilder.</summary>
    internal void ResetKernel()
    {
        _kernel?.ResetExecutionState();
        _transcript?.Refresh();
        FocusInput();
    }

    /// <summary>
    /// Called by the Reload button via TerminalUiBuilder. Recompiles the
    /// Scripts folder and applies the result to the kernel and completion
    /// session. See <see cref="TerminalBootstrap"/> remarks for why this is
    /// a manual, explicit action rather than automatic on file save.
    /// </summary>
    internal void ReloadUserScripts()
    {
        TerminalBootstrap.ReloadUserScripts();
        FocusInput();
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private void SetInputTextSilently(string value) => SetInputText(value, null);

    /// <summary>
    /// Replaces the input text without re-triggering completion, and places the
    /// caret at <paramref name="caret"/> (end of text when null).
    /// </summary>
    /// <param name="cancelPendingCompletion">
    /// Drop completion results that are still being computed. Pass false when the
    /// text left of the caret is unchanged, so those results remain valid.
    /// </param>
    private void SetInputText(string value, int? caret, bool cancelPendingCompletion = true)
    {
        value ??= string.Empty;
        int position = Mathf.Clamp(caret ?? value.Length, 0, value.Length);
        SetInputSelection(value, position, position, cancelPendingCompletion);
    }

    /// <summary>
    /// Like <see cref="SetInputText"/>, but selects [anchor, focus]; the caret is
    /// at <paramref name="focus"/>. Equal values just place the caret.
    /// </summary>
    private void SetInputSelection(string value, int anchor, int focus, bool cancelPendingCompletion = true)
    {
        if (_inputField == null) return;

        value ??= string.Empty;
        anchor = Mathf.Clamp(anchor, 0, value.Length);
        focus = Mathf.Clamp(focus, 0, value.Length);

        _completion.SuppressInputChanged = true;
        if (cancelPendingCompletion)
        {
            _completion.RequestVersion++;
        }

        _programmaticEdit = true;
        _applyingText = true;
        try
        {
            _inputField.text = value;   // replays every character through OnValidateInput
        }
        finally
        {
            _applyingText = false;
        }

        _inputField.caretPosition           = focus;
        _inputField.selectionAnchorPosition = anchor;
        _inputField.selectionFocusPosition  = focus;
        _inputField.ForceLabelUpdate();   // redraw the selection highlight now

        _completion.SuppressInputChanged = false;
        _ = _completion.RefreshSignatureAsync();
    }

    // ── Typing rules: auto-indent, bracket and quote matching, surround ──────
    // The InputField has already handled this frame's key presses by the time
    // Update runs. These rules look at what it just did and adjust the result.

    /// <summary>
    /// Reacts only when exactly one character was just typed or deleted at the
    /// caret, or typed over a selection (compared with the snapshot from the end
    /// of the previous frame); pasting, history recall and completion commits
    /// never match, so they are never altered. The rules themselves live in
    /// <see cref="TerminalInputEditing"/>.
    /// </summary>
    private void ApplyTypingRules()
    {
        // Text that code set this frame (a completion, history recall, undo, ...) was not typed
        // by the user, so the typing rules must not touch it. It is an undo step of its own.
        if (_programmaticEdit)
        {
            return;
        }

        string text = _inputField.text ?? string.Empty;
        int caret = _inputField.caretPosition;

        // Tell the undo history what kind of edit this was: typing and deleting runs merge into one step.
        _pendingEditKind = TerminalInputEditing.ClassifyEdit(
            _lastInputText, _lastCaret, text, caret, out _pendingTypedChar);

        if (!TerminalInputEditing.TryApplyTypingRules(
                _lastInputText, _lastCaret,
                Math.Min(_lastSelAnchor, _lastSelFocus), Math.Max(_lastSelAnchor, _lastSelFocus),
                text, caret,
                out string newText, out int newSelStart, out int newSelEnd))
        {
            return;
        }

        // If nothing left of the caret changed (a closing bracket or quote was only added
        // after it), completion results already being computed are still valid. Keeping
        // them is what lets "(" still open its argument list after the ")" is auto-inserted.
        bool leftContextUnchanged = newSelStart == newSelEnd
                                    && newSelEnd == caret
                                    && string.CompareOrdinal(text, 0, newText, 0, caret) == 0;

        ApplyAutomaticEdit(newText, newSelStart, newSelEnd, keepCompletion: leftContextUnchanged);
    }

    private void ApplyAutomaticEdit(string newText, int anchor, int focus, bool keepCompletion)
    {
        if (!keepCompletion)
        {
            // The edit invalidates any completion computed for the old text.
            _completion.Hide();
        }

        SetInputSelection(newText, anchor, focus, cancelPendingCompletion: !keepCompletion);
    }

    // ── Tab and indentation ───────────────────────────────────────────────────
    // Tab is also a game hotkey (hold it for the help panel), and the game takes keyboard
    // focus away from the input field when it sees it. That happens before this script's
    // Update runs, so Tab is handled ahead of the focus check, triggered by "the field had
    // focus last frame", acting on last frame's snapshot, and focus is handed back once
    // Tab is released (not before: the game may still be showing its help panel).

    /// <summary>
    /// Tab accepts the highlighted completion while the list is open; otherwise Tab
    /// indents the current line or selected lines and Shift+Tab outdents them.
    /// OnValidateInput discards the '\t' the InputField would otherwise insert.
    /// </summary>
    /// <returns>True when the key press was a Tab meant for the terminal.</returns>
    private bool HandleTabKey()
    {
        if (!_visible || _inputField == null || !Input.GetKeyDown(KeyCode.Tab) || IsControlHeld())
        {
            return false;
        }

        // Only if the user was typing in the terminal; otherwise Tab belongs to the game.
        if (!_inputWasFocused)
        {
            return false;
        }

        if (_completion.IsVisible && !IsShiftHeld())
        {
            AcceptCompletionThenReturnFocus();
            return true;
        }

        (int anchor, int focus) = ApplyIndent(outdent: IsShiftHeld());
        ScheduleFocusReturn(anchor, focus, caretAtEnd: false);
        return true;
    }

    /// <summary>
    /// Ctrl+] and Ctrl+[ indent and outdent (VS Code's shortcuts). Unlike Tab they
    /// don't clash with the game. Ignored while Alt is held, because AltGr (Ctrl+Alt)
    /// is how some keyboard layouts type these brackets.
    /// </summary>
    private bool HandleIndentShortcuts()
    {
        if (!IsControlHeld() || IsAltHeld()) return false;

        bool indent = Input.GetKeyDown(KeyCode.RightBracket);
        bool outdent = Input.GetKeyDown(KeyCode.LeftBracket);
        if (!indent && !outdent) return false;

        ApplyIndent(outdent);
        return true;
    }

    /// <summary>Indents or outdents the touched lines and returns the selection afterwards.</summary>
    private (int anchor, int focus) ApplyIndent(bool outdent)
    {
        // If the game already took focus, the field's own selection has been reset to
        // the start, so use last frame's.
        bool focused = _inputField.isFocused;
        string text = focused ? (_inputField.text ?? string.Empty) : _lastInputText;
        int anchor = focused ? _inputField.selectionAnchorPosition : _lastSelAnchor;
        int focus = focused ? _inputField.selectionFocusPosition : _lastSelFocus;

        if (TerminalInputEditing.TryIndentLines(
                text, anchor, focus, outdent, out string newText, out int newStart, out int newEnd))
        {
            // Keep which end of the selection holds the caret.
            bool forward = anchor <= focus;
            anchor = forward ? newStart : newEnd;
            focus = forward ? newEnd : newStart;
            ApplyAutomaticEdit(newText, anchor, focus, keepCompletion: false);
        }
        else if (!focused && !string.Equals(_inputField.text, text, StringComparison.Ordinal))
        {
            // Nothing to indent, but losing focus changed the field's text: put it back.
            SetInputSelection(text, anchor, focus);
        }

        return (anchor, focus);
    }

    private async void AcceptCompletionThenReturnFocus()
    {
        await _completion.CommitAsync();

        // The accepted text is in place with the caret at its end.
        ScheduleFocusReturn(0, 0, caretAtEnd: true);
    }

    private void ScheduleFocusReturn(int anchor, int focus, bool caretAtEnd)
    {
        _focusReturnPending = true;
        _focusReturnAnchor = anchor;
        _focusReturnFocus = focus;
        _focusReturnCaretAtEnd = caretAtEnd;
        _focusReturnText = _inputField != null ? (_inputField.text ?? string.Empty) : string.Empty;
    }

    /// <summary>Hands focus back to the input once Tab has been released.</summary>
    private void ReturnFocusAfterTab()
    {
        if (!_focusReturnPending) return;

        if (!_visible || _inputField == null)
        {
            _focusReturnPending = false;
            return;
        }

        // Still held: the game may be showing its help panel, so don't fight it for focus yet.
        if (Input.GetKey(KeyCode.Tab)) return;

        _focusReturnPending = false;
        if (_inputField.isFocused) return;

        // Losing focus should not change the text, but if it did, put our version back.
        if (!string.Equals(_inputField.text, _focusReturnText, StringComparison.Ordinal))
        {
            SetInputSelection(_focusReturnText, _focusReturnAnchor, _focusReturnFocus);
        }

        StartCoroutine(RefocusInput(_focusReturnAnchor, _focusReturnFocus, _focusReturnCaretAtEnd));
    }

    // ── Undo / redo ───────────────────────────────────────────────────────────
    // The legacy InputField has no undo. Every change to the text is recorded at the end of
    // the frame by comparing it with the previous frame's snapshot, so typing, deleting,
    // pasting, completions, indents and history recall are all covered without hooking each
    // one. Only runs of typed or deleted characters are merged into one step; see
    // TerminalUndoHistory for the grouping rules.

    private void RecordUndoStep()
    {
        EditKind kind = _pendingEditKind;
        char typed = _pendingTypedChar;
        _pendingEditKind = EditKind.Other;
        _pendingTypedChar = '\0';
        _programmaticEdit = false;

        if (_skipUndoRecord)
        {
            // An undo/redo (or clearing the input) already accounts for this frame's change.
            _skipUndoRecord = false;
            return;
        }

        string text = _inputField.text ?? string.Empty;
        UndoState after = new(text, _inputField.selectionAnchorPosition, _inputField.selectionFocusPosition);

        if (string.Equals(text, _lastInputText, StringComparison.Ordinal))
        {
            // Typing a closing bracket or quote over an auto-inserted one changes nothing but the
            // caret; keep the typing run contiguous.
            if (kind == EditKind.Typing)
            {
                _undo.NoteTypedOver(after, typed, Time.unscaledTime);
            }

            return;
        }

        _undo.Record(
            new UndoState(_lastInputText, _lastSelAnchor, _lastSelFocus),
            after, kind, typed, Time.unscaledTime);
    }

    /// <summary>Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo.</summary>
    /// <returns>True when the key press was an undo/redo chord (consumed even if there was nothing to do).</returns>
    private bool HandleUndoRedoKeys()
    {
        // Alt held means AltGr on many keyboard layouts.
        if (!IsControlHeld() || IsAltHeld()) return false;

        bool z = Input.GetKeyDown(KeyCode.Z);
        bool y = Input.GetKeyDown(KeyCode.Y);
        if (!z && !y) return false;

        bool redo = y || IsShiftHeld();

        if (redo ? _undo.TryRedo(out UndoState state) : _undo.TryUndo(out state))
        {
            ApplyUndoState(state);
        }

        return true;
    }

    private void ApplyUndoState(UndoState state)
    {
        _skipUndoRecord = true;     // this frame's change is the undo itself, not a new edit
        _completion.Hide();

        // Not browsing history and holding no draft any more: the text is whatever the undo says.
        _history.ResetCursor();
        _draft = string.Empty;

        SetInputSelection(state.Text, state.Anchor, state.Focus);
    }

    // ── History vs. caret movement ────────────────────────────────────────────

    /// <summary>
    /// Up/Down walk through command history only when the caret is already on
    /// the first (Up) or last (Down) line of the input; otherwise they just move
    /// the caret between lines, which the InputField does itself.
    /// </summary>
    /// <returns>True when the input text was replaced by a history entry.</returns>
    private bool HandleHistoryKeys()
    {
        bool up = Input.GetKeyDown(KeyCode.UpArrow);
        bool down = Input.GetKeyDown(KeyCode.DownArrow);
        if (!up && !down) return false;

        // Shift+Up/Down extends the selection.
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return false;

        // The InputField already moved the caret for this key press, so decide
        // from where the caret was at the end of the previous frame.
        if (up && TerminalInputEditing.IsOnFirstLine(_lastInputText, _lastCaret))
        {
            return RecallPreviousCommand();
        }

        if (down && _history.IsBrowsing && TerminalInputEditing.IsOnLastLine(_lastInputText, _lastCaret))
        {
            // Down only means "newer command" while browsing; otherwise it must
            // never replace what the user is typing.
            return RecallNextCommand();
        }

        return false;
    }

    private bool RecallPreviousCommand()
    {
        bool wasBrowsing = _history.IsBrowsing;

        string previous = _history.MovePrevious();
        if (previous == null) return false;   // no history yet: leave the input alone

        if (!wasBrowsing)
        {
            _draft = _inputField.text ?? string.Empty;
        }

        SetInputTextSilently(previous);
        return true;
    }

    private bool RecallNextCommand()
    {
        string next = _history.MoveNext();
        if (next == null) return false;

        if (!_history.IsBrowsing)
        {
            // Stepped past the newest entry: back to what the user was typing.
            next = _draft;
            _draft = string.Empty;
        }

        SetInputTextSilently(next);
        return true;
    }

    private void FocusInput()
    {
        if (_inputField == null || !_visible) return;
        EventSystem.current?.SetSelectedGameObject(null);
        _inputField.ActivateInputField();
        _inputField.Select();
    }

    private IEnumerator FocusInputNextFrame()
    {
        yield return null;
        if (_inputField != null && _visible)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            _inputField.enabled = true;
            _inputField.ActivateInputField();
            _inputField.Select();
        }
    }
}