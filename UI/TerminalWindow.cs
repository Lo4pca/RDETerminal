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
            // Escape. (The list only opens while an identifier is being typed or
            // after an explicit trigger character, so it does not swallow the
            // Enter pressed after '{' or '}'. See TerminalCompletionController.)
            if (Input.GetKeyDown(KeyCode.UpArrow))   { _completion.MoveSelection(-1); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { _completion.MoveSelection( 1); return; }
            if (EnterPressedThisFrame())             { _completion.Commit();          return; }
            if (Input.GetKeyDown(KeyCode.Escape))    { _completion.Hide();            return; }
        }
        else if (HandleHistoryKeys())
        {
            return; // the text was replaced by a history entry; nothing left to post-process
        }

        ApplyTypingRules();
    }

    private void LateUpdate()
    {
        if (!_editorSceneActive || _inputField == null) return;

        // Remember where the caret is now, so next frame's arrow-key handling can
        // tell where it was before the InputField moved it.
        _lastInputText = _inputField.text ?? string.Empty;
        _lastCaret = _inputField.caretPosition;
    }

    // ── Keyboard helpers ──────────────────────────────────────────────────────

    private static bool IsControlHeld() =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

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
        if ((addedChar == '\n' || addedChar == '\r') && IsControlHeld() && EnterKeyActive())
        {
            return '\0';
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
    private void SetInputText(string value, int? caret)
    {
        if (_inputField == null) return;

        value ??= string.Empty;
        int position = Mathf.Clamp(caret ?? value.Length, 0, value.Length);

        _completion.SuppressInputChanged = true;
        _completion.RequestVersion++;

        _inputField.text = value;
        _inputField.caretPosition           = position;
        _inputField.selectionAnchorPosition = position;
        _inputField.selectionFocusPosition  = position;

        _completion.SuppressInputChanged = false;
        _ = _completion.RefreshSignatureAsync();
    }

    // ── Typing rules: auto-indent and brace matching ─────────────────────────
    // The InputField has already handled this frame's key presses by the time
    // Update runs. These rules look at what it just did and adjust the result.

    /// <summary>
    /// Reacts only when exactly one character was just inserted at the caret
    /// (compared with the snapshot from the end of the previous frame). That
    /// is what typing looks like; pasting, history recall, completion commits
    /// and deletions never match, so they are never altered.
    /// </summary>
    private void ApplyTypingRules()
    {
        string text = _inputField.text ?? string.Empty;
        int caret = _inputField.caretPosition;

        if (!TerminalInputEditing.TryGetInsertedChar(_lastInputText, _lastCaret, text, caret, out char typed))
        {
            return;
        }

        string newText;
        int newCaret;
        bool changed;

        switch (typed)
        {
            case '\n':
                changed = TerminalInputEditing.TryAutoIndentAfterNewline(text, caret, out newText, out newCaret);
                break;

            case '{':
                changed = TerminalInputEditing.TryAutoCloseBrace(text, caret, out newText, out newCaret);
                break;

            case '}':
                // Typing '}' in front of the brace that is already waiting there
                // steps over it; otherwise a '}' that starts its line is dedented.
                changed = TerminalInputEditing.TryTypeOverClosingBrace(text, caret, out newText, out newCaret)
                          || TerminalInputEditing.TryDedentClosingBrace(text, caret, out newText, out newCaret);
                break;

            default:
                return;
        }

        if (changed)
        {
            ApplyAutomaticEdit(newText, newCaret);
        }
    }

    private void ApplyAutomaticEdit(string newText, int caret)
    {
        // The edit invalidates any completion computed for the old text.
        _completion.Hide();
        SetInputText(newText, caret);
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