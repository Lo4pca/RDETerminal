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

        if (_visible && _inputField != null && _inputField.isFocused &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            ExecuteCurrentCellPublic();
        }

        if (!_visible || _inputField == null || !_inputField.isFocused) return;

        if (_completion.IsVisible && _completion.IsVisible)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))    { _completion.MoveSelection(-1); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow))  { _completion.MoveSelection( 1); return; }
            if (Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter)) { _completion.Commit(); return; }
            if (Input.GetKeyDown(KeyCode.Escape))     { _completion.Hide(); return; }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                string prev = _history.MovePrevious();
                if (prev != null) SetInputTextSilently(prev);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                string next = _history.MoveNext();
                if (next != null) SetInputTextSilently(next);
            }
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildNativeUi()
    {
        DestroyNativeUi();

        var result = TerminalUiBuilder.Build(
            _completion.OnValidateInput,
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
        _completion.Hide();
        SetInputTextSilently(string.Empty);
        _history.ResetCursor();

        NotebookCellResult result = await _kernel.ExecuteAsync(code);
        if (!result.Success)
        {
            Plugin.LogError(result.Error);
        }

        _transcript?.Refresh();
        FocusInput();
    }

    /// <summary>Called by the Reset button via TerminalUiBuilder.</summary>
    internal void ResetKernel()
    {
        _kernel?.ResetExecutionState();
        _transcript?.Refresh();
        FocusInput();
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private void SetInputTextSilently(string value)
    {
        if (_inputField == null) return;

        _completion.SuppressInputChanged = true;
        _completion.RequestVersion++;

        _inputField.text = value ?? string.Empty;

        int caret = _inputField.text.Length;
        _inputField.caretPosition          = caret;
        _inputField.selectionAnchorPosition = caret;
        _inputField.selectionFocusPosition  = caret;

        _completion.SuppressInputChanged = false;
        _ = _completion.RefreshSignatureAsync();
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