using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using RDETerminal.Adapters;
using RDETerminal.Notebook;
using RDLevelEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

namespace RDETerminal.UI;

public sealed class TerminalWindow : MonoBehaviour
{
    private sealed class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
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
            if (_window == null || _parent == null)
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent,
                eventData.position,
                eventData.pressEventCamera,
                out _startPointerLocal);

            _startAnchoredPosition = _window.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || _parent == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 currentLocal))
            {
                return;
            }

            _window.anchoredPosition = _startAnchoredPosition + (currentLocal - _startPointerLocal);
        }
    }
    private sealed class CompletionRow
    {
        public RectTransform Root;
        public Image Background;
        public Text Label;
    }
    private NotebookKernel _kernel;
    private EditorAdapter _editorAdapter;
    private GameLevelBridge _levelBridge;

    private GameObject _root;
    private Canvas _canvas;
    private bool _ownsCanvas;
    private ScrollRect _scrollRect;
    private RectTransform _transcriptContent;
    private Text _transcriptText;
    private InputField _inputField;
    private LayoutElement _transcriptLayoutElement;
    private bool _visible;
    private const float WindowPadding = 10f;
    private const float HeaderHeight = 18f;
    private const float InputAreaHeight = 70f;
    private const float ButtonRowHeight = 26f;
    private const float FooterGap = 1f;
    private const float TranscriptTopGap = 4f;
    private readonly CommandHistory _history = new();
    private IReadOnlyList<CompletionItem> _completionItems = [];
    private int _completionIndex;
    private bool _completionVisible;
    private bool _suppressInputChanged;
    private int _completionRequestVersion;
    private GameObject _completionPanel;
    private readonly List<CompletionRow> _completionRows = [];
    private string _currentCompletionPrefix = string.Empty;
    private const int CompletionMaxVisibleItems = 8;
    private const float CompletionItemHeight = 18f;
    private const float CompletionPanelPadding = 6f;
    private GameObject _completionDetailPanel;
    private Text _completionDetailText;

    private GameObject _signaturePanel;
    private Text _signatureText;

    private const float CompletionDetailPanelWidth = 170f;
    private const float SignaturePanelHeight = 52f;

    /// <summary>
    /// Tracks whether the editor scene is currently active.
    /// Set by scene load/unload events — never polled in Update.
    /// </summary>
    private bool _editorSceneActive;

    private void Awake()
    {
        try
        {
            _editorAdapter = new EditorAdapter();
            _levelBridge = new GameLevelBridge(_editorAdapter);
            _kernel = new NotebookKernel(_editorAdapter, _levelBridge);

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            // The Harmony patch fires after scnEditor.Start(), so the scene is
            // already active. Bootstrap the UI immediately.
            if (SceneManager.GetActiveScene().name == "scnEditor")
            {
                _editorSceneActive = true;
                BuildNativeUi();
                SetVisible(false);
            }

            Plugin.LogInfo("NotebookKernel created successfully.");
        }
        catch (Exception ex)
        {
            Plugin.LogError("NotebookKernel init failed:");
            Plugin.LogError(ex.ToString());
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        DestroyNativeUi();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "scnEditor")
        {
            return;
        }

        _editorSceneActive = true;
        BuildNativeUi();
        SetVisible(false);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != "scnEditor")
        {
            return;
        }

        _editorSceneActive = false;
        HideCompletion();

        // The scene canvas and all UI children are being destroyed by Unity.
        // Null out our references so DestroyNativeUi won't try to Destroy()
        // already-dead objects when the scene unloads.
        _root = null;
        _canvas = null;
        _ownsCanvas = false;
        _scrollRect = null;
        _transcriptContent = null;
        _transcriptText = null;
        _transcriptLayoutElement = null;
        _completionPanel = null;
        _completionRows.Clear();
        _completionDetailPanel = null;
        _completionDetailText = null;
        _signaturePanel = null;
        _signatureText = null;
        _inputField = null;
    }

    private void Update()
    {
        if (!_editorSceneActive)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleVisible();
        }

        if (_visible && _inputField != null && _inputField.isFocused &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            ExecuteCurrentCell();
        }

        if (_visible && _inputField != null && _inputField.isFocused)
        {
            if (_completionVisible && _completionItems.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    MoveCompletion(-1);
                    return;
                }

                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    MoveCompletion(1);
                    return;
                }

                if (Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    CommitCompletion();
                    return;
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    HideCompletion();
                    return;
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    string previous = _history.MovePrevious();
                    if (previous != null)
                    {
                        SetInputTextSilently(previous);
                    }
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    string next = _history.MoveNext();
                    if (next != null)
                    {
                        SetInputTextSilently(next);
                    }
                }
            }
        }
    }

    private static void StripOnlyProblematicBehaviours(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            Type type = behaviour.GetType();
            string ns = type.Namespace ?? string.Empty;
            string name = type.Name;

            bool isGameScript =
                ns.StartsWith("RDLevelEditor", StringComparison.OrdinalIgnoreCase) ||
                name == "RDPublishPopup" ||
                name == "RDStringToUIText";

            if (isGameScript)
            {
                Destroy(behaviour);
            }
        }
    }

    private static void ClearAllChildren(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = root.transform.GetChild(i);
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }
    }

    private void BuildNativeUi()
    {
        DestroyNativeUi();

        var editor = scnEditor.instance;
        GameObject template = editor != null && editor.publishPopup != null ? editor.publishPopup.gameObject : null;

        _canvas = FindBestCanvas();
        if (_canvas == null)
        {
            _canvas = CreateFallbackCanvas();
            _ownsCanvas = true;
        }
        else
        {
            _ownsCanvas = false;
        }

        _root = Instantiate(template, _canvas.transform, false);
        _root.name = "RDETerminalPanel";
        _root.SetActive(false);

        StripOnlyProblematicBehaviours(_root);
        ClearAllChildren(_root);

        RectTransform rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(0f, 1f);
        rootRt.pivot = new Vector2(0f, 1f);
        rootRt.anchoredPosition = new Vector2(20f, -20f);
        rootRt.sizeDelta = new Vector2(320f, 320f);

        BuildHeaderRow(_root.transform);
        BuildTranscriptArea(_root.transform);
        BuildInputArea(_root.transform);
        BuildCompletionPanel(_root.transform);
        BuildCompletionDetailPanel(_root.transform);
        BuildSignaturePanel(_root.transform);
        BuildButtonRow(_root.transform);

        Canvas.ForceUpdateCanvases();
        if (_root != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root.GetComponent<RectTransform>());
        }
        Canvas.ForceUpdateCanvases();
        RefreshTranscript();
    }

    private void BuildCompletionPanel(Transform parent)
    {
        GameObject panel = new("CompletionPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(WindowPadding, ButtonRowHeight + FooterGap + InputAreaHeight + 8f);
        panelRt.sizeDelta = new Vector2(280f, CompletionMaxVisibleItems * CompletionItemHeight + CompletionPanelPadding * 2f);

        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.10f, 0.97f);
        bg.raycastTarget = true;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 2f;
        layout.padding = new RectOffset(
            (int)CompletionPanelPadding,
            (int)CompletionPanelPadding,
            (int)CompletionPanelPadding,
            (int)CompletionPanelPadding);

        _completionPanel = panel;
        _completionPanel.SetActive(false);

        for (int i = 0; i < CompletionMaxVisibleItems; i++)
        {
            GameObject row = new($"CompletionRow_{i}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);

            RectTransform rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);

            LayoutElement rowLe = row.GetComponent<LayoutElement>();
            rowLe.preferredHeight = CompletionItemHeight;
            rowLe.minHeight = CompletionItemHeight;
            rowLe.flexibleWidth = 1f;

            Image rowBg = row.GetComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, 0.06f);

            Text label = CreateText(row.transform, "Label", 11, TextAnchor.MiddleLeft, new Color(0.92f, 0.92f, 0.92f, 1f), false);
            label.supportRichText = true;
            label.raycastTarget = false;

            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 0f);
            labelRt.offsetMax = new Vector2(-6f, 0f);

            _completionRows.Add(new CompletionRow
            {
                Root = rowRt,
                Background = rowBg,
                Label = label
            });
        }
    }
    private void BuildCompletionDetailPanel(Transform parent)
    {
        GameObject panel = new("CompletionDetailPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(WindowPadding + 280f + 8f, ButtonRowHeight + FooterGap + InputAreaHeight + 8f);
        panelRt.sizeDelta = new Vector2(CompletionDetailPanelWidth, CompletionMaxVisibleItems * CompletionItemHeight + CompletionPanelPadding * 2f);

        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.12f, 0.97f);
        bg.raycastTarget = true;

        Text text = CreateText(panel.transform, "CompletionDetailText", 11, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f, 1f), false);
        text.supportRichText = true;
        text.raycastTarget = false;

        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 6f);
        textRt.offsetMax = new Vector2(-6f, -6f);

        _completionDetailPanel = panel;
        _completionDetailText = text;
        _completionDetailPanel.SetActive(false);
    }
    private void BuildSignaturePanel(Transform parent)
    {
        GameObject panel = new("SignaturePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(WindowPadding + 280f + 8f, ButtonRowHeight + FooterGap + InputAreaHeight - SignaturePanelHeight - 6f);
        panelRt.sizeDelta = new Vector2(CompletionDetailPanelWidth, SignaturePanelHeight);

        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.10f, 0.97f);
        bg.raycastTarget = true;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 0f;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text text = CreateText(panel.transform, "SignatureText", 11, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f, 1f), false);
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 1.05f;

        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        LayoutElement le = text.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 1f;

        _signaturePanel = panel;
        _signatureText = text;
        _signaturePanel.SetActive(false);
    }
    private void BuildHeaderRow(Transform parent)
    {
        GameObject row = new("HeaderRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        Image rowBg = row.GetComponent<Image>();
        rowBg.color = new Color(1f, 1f, 1f, 0.001f);
        rowBg.raycastTarget = true;

        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.offsetMin = new Vector2(10f, -18f);
        rowRt.offsetMax = new Vector2(-10f, 0f);

        LayoutElement le = row.GetComponent<LayoutElement>();
        le.preferredHeight = HeaderHeight;
        le.minHeight = HeaderHeight;
        le.flexibleHeight = 0f;

        Text title = CreateText(row.transform, "RDETerminal", 10, TextAnchor.MiddleCenter, new Color(0.95f, 0.95f, 0.95f, 1f), true);
        title.raycastTarget = false;

        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = Vector2.zero;
        titleRt.offsetMax = Vector2.zero;

        WindowDragHandle drag = row.AddComponent<WindowDragHandle>();
        drag.Initialize(_root.GetComponent<RectTransform>());
    }

    private void BuildTranscriptArea(Transform parent)
    {
        GameObject wrapper = new("TranscriptWrapper", typeof(RectTransform), typeof(Image));
        wrapper.transform.SetParent(parent, false);

        RectTransform wrapperRt = wrapper.GetComponent<RectTransform>();
        wrapperRt.anchorMin = new Vector2(0f, 0f);
        wrapperRt.anchorMax = new Vector2(1f, 1f);
        wrapperRt.pivot = new Vector2(0.5f, 0.5f);
        wrapperRt.offsetMin = new Vector2(WindowPadding, InputAreaHeight + ButtonRowHeight + FooterGap);
        wrapperRt.offsetMax = new Vector2(-WindowPadding, -(HeaderHeight + TranscriptTopGap));

        Image bg = wrapper.GetComponent<Image>();
        bg.color = new Color(0.14f, 0.14f, 0.14f, 0.96f);

        GameObject scrollGo = new("TranscriptScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(wrapper.transform, false);

        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        _scrollRect = scrollGo.GetComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 24f;

        GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);

        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;

        Image viewportBg = viewport.GetComponent<Image>();
        viewportBg.color = new Color(1f, 1f, 1f, 0.01f);

        Mask mask = viewport.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.spacing = 4f;
        contentLayout.padding = new RectOffset(12, 12, 12, 12);

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _transcriptContent = contentRt;

        _transcriptText = CreateText(_transcriptContent, "TranscriptText", 12, TextAnchor.UpperLeft, new Color(0.88f, 0.88f, 0.88f, 1f), false);
        _transcriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
        _transcriptText.raycastTarget = false;
        _transcriptText.lineSpacing = 1.1f;

        RectTransform textRt = _transcriptText.rectTransform;
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.pivot = new Vector2(0.5f, 1f);
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        _transcriptLayoutElement = _transcriptText.gameObject.AddComponent<LayoutElement>();
        _transcriptLayoutElement.flexibleWidth = 1f;
        _transcriptLayoutElement.preferredHeight = 0f;

        _scrollRect.viewport = viewportRt;
        _scrollRect.content = _transcriptContent;
    }

    private System.Collections.IEnumerator EnableInputFieldNextFrame()
    {
        yield return null;
        yield return null;

        _inputField?.enabled = true;
    }

    private void BuildInputArea(Transform parent)
    {
        GameObject area = new("InputArea", typeof(RectTransform));
        area.transform.SetParent(parent, false);

        RectTransform areaRt = area.GetComponent<RectTransform>();
        areaRt.anchorMin = new Vector2(0f, 0f);
        areaRt.anchorMax = new Vector2(1f, 0f);
        areaRt.pivot = new Vector2(0.5f, 0f);
        areaRt.offsetMin = new Vector2(WindowPadding, ButtonRowHeight + FooterGap);
        areaRt.offsetMax = new Vector2(-WindowPadding, ButtonRowHeight + FooterGap + InputAreaHeight);

        GameObject inputGo = new("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(area.transform, false);

        RectTransform inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = Vector2.zero;
        inputRt.anchorMax = Vector2.one;
        inputRt.offsetMin = Vector2.zero;
        inputRt.offsetMax = Vector2.zero;

        Image inputBg = inputGo.GetComponent<Image>();
        inputBg.color = new Color(1f, 1f, 1f, 0.08f);
        inputBg.raycastTarget = true;

        InputField input = inputGo.GetComponent<InputField>();
        input.lineType = InputField.LineType.MultiLineNewline;
        input.shouldHideMobileInput = true;
        input.transition = Selectable.Transition.ColorTint;
        input.targetGraphic = inputBg;
        input.enabled = false;

        GameObject textGo = new("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(inputGo.transform, false);

        Text text = textGo.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = 13;
        text.alignment = TextAnchor.UpperLeft;
        text.color = Color.white;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = new Vector2(0f, 0f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.offsetMin = new Vector2(8f, 8f);
        textRt.offsetMax = new Vector2(-20f, -8f);

        GameObject placeholderGo = new("Placeholder", typeof(RectTransform), typeof(Text));
        placeholderGo.transform.SetParent(inputGo.transform, false);

        Text placeholder = placeholderGo.GetComponent<Text>();
        placeholder.font = GetFont();
        placeholder.fontSize = 13;
        placeholder.alignment = TextAnchor.UpperLeft;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.text = "Code here...";
        placeholder.raycastTarget = false;

        RectTransform placeholderRt = placeholder.rectTransform;
        placeholderRt.anchorMin = textRt.anchorMin;
        placeholderRt.anchorMax = textRt.anchorMax;
        placeholderRt.offsetMin = textRt.offsetMin;
        placeholderRt.offsetMax = textRt.offsetMax;

        input.textComponent = text;
        input.onValidateInput += OnValidateInput;
        input.onValueChanged.AddListener(OnInputChanged);
        input.placeholder = placeholder;

        _inputField = input;
        StartCoroutine(EnableInputFieldNextFrame());
    }
    private char OnValidateInput(string text, int charIndex, char addedChar)
    {
        if (addedChar == '\t')
        {
            return '\0';
        }

        if (_completionVisible && (addedChar == '\n' || addedChar == '\r'))
        {
            return '\0';
        }

        return addedChar;
    }
    private void OnInputChanged(string text)
    {
        if (_suppressInputChanged)
        {
            return;
        }

        int version = ++_completionRequestVersion;
        _ = OnInputChangedAsync(text, version);
    }

    private async Task OnInputChangedAsync(string text, int version)
    {
        if (_kernel == null || _inputField == null || _kernel.Completion == null)
        {
            return;
        }

        text ??= string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            _currentCompletionPrefix = string.Empty;
            if (version == _completionRequestVersion)
            {
                HideCompletion();
            }
            return;
        }

        int caret = Mathf.Clamp(_inputField.caretPosition, 0, text.Length);
        _currentCompletionPrefix = GetCompletionPrefix(text, caret);

        IReadOnlyList<CompletionItem> items = await _kernel.Completion.GetItemsAsync(text, caret);

        if (version != _completionRequestVersion)
        {
            return;
        }

        _completionItems = items ?? [];
        _completionIndex = 0;
        _completionVisible = _completionItems.Count > 0;
        RefreshCompletionPanel();
        await RefreshCompletionDetailPanelAsync();
        await RefreshSignaturePanelAsync();
        LogCompletionItems();
    }

    private static string GetCompletionPrefix(string code, int caret)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        caret = Mathf.Clamp(caret, 0, code.Length);

        int start = caret;
        while (start > 0)
        {
            char c = code[start - 1];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            {
                break;
            }

            start--;
        }

        return code.Substring(start, caret - start);
    }

    private void LogCompletionItems()
    {
        if (_completionItems.Count == 0)
        {
            return;
        }

        StringBuilder sb = new();
        sb.Append("[Completion] count=").Append(_completionItems.Count).Append(" => ");

        int limit = Math.Min(_completionItems.Count, 8);
        for (int i = 0; i < limit; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            if (i == _completionIndex)
            {
                sb.Append('[').Append(_completionItems[i].DisplayText).Append(']');
            }
            else
            {
                sb.Append(_completionItems[i].DisplayText);
            }
        }

        if (_completionItems.Count > limit)
        {
            sb.Append(", ...");
        }
    }
    private void MoveCompletion(int delta)
    {
        if (!_completionVisible || _completionItems.Count == 0)
        {
            return;
        }

        _completionIndex = (_completionIndex + delta + _completionItems.Count) % _completionItems.Count;
        RefreshCompletionPanel();
        _ = RefreshCompletionDetailPanelAsync();
        LogCompletionItems();
    }

    private async void CommitCompletion()
    {
        if (!_completionVisible || _completionItems.Count == 0 || _kernel == null || _inputField == null)
        {
            return;
        }

        CompletionItem selected = _completionItems[_completionIndex];
        string code = _inputField.text ?? string.Empty;

        HideCompletion();

        string newText = await _kernel.Completion.ApplyAsync(code, selected);
        SetInputTextSilently(newText);
    }

    private void HideCompletion()
    {
        _completionVisible = false;
        _completionItems = [];
        _completionIndex = 0;
        _currentCompletionPrefix = string.Empty;

        _completionPanel?.SetActive(false);
        _completionDetailPanel?.SetActive(false);
        _signaturePanel?.SetActive(false);
    }

    private void SetInputTextSilently(string value)
    {
        if (_inputField == null)
        {
            return;
        }

        _suppressInputChanged = true;
        _completionRequestVersion++;

        _inputField.text = value ?? string.Empty;

        int caret = _inputField.text.Length;
        _inputField.caretPosition = caret;
        _inputField.selectionAnchorPosition = caret;
        _inputField.selectionFocusPosition = caret;

        _suppressInputChanged = false;
        _ = RefreshSignaturePanelAsync();
    }

    private void RefreshCompletionPanel()
    {
        if (_completionPanel == null)
        {
            return;
        }

        if (!_completionVisible || _completionItems.Count == 0)
        {
            _completionPanel.SetActive(false);
            return;
        }

        _completionPanel.SetActive(true);

        int visibleCount = Math.Min(CompletionMaxVisibleItems, _completionItems.Count);
        int maxStart = Math.Max(0, _completionItems.Count - visibleCount);
        int windowStart = Mathf.Clamp(_completionIndex - (visibleCount / 2), 0, maxStart);

        float panelHeight = (visibleCount * CompletionItemHeight) + (CompletionPanelPadding * 2f) + ((visibleCount - 1) * 2f);
        RectTransform panelRt = _completionPanel.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, panelHeight);

        for (int i = 0; i < _completionRows.Count; i++)
        {
            int itemIndex = windowStart + i;
            bool hasItem = i < visibleCount && itemIndex < _completionItems.Count;

            CompletionRow row = _completionRows[i];
            row.Root.gameObject.SetActive(hasItem);

            if (!hasItem)
            {
                continue;
            }

            CompletionItem item = _completionItems[itemIndex];
            bool selected = itemIndex == _completionIndex;

            row.Background.color = selected
                ? new Color(0.28f, 0.42f, 0.68f, 0.92f)
                : new Color(1f, 1f, 1f, 0.06f);

            row.Label.color = selected
                ? Color.white
                : new Color(0.92f, 0.92f, 0.92f, 1f);

            row.Label.text = BuildCompletionLabel(item, _currentCompletionPrefix);
        }
    }
    private async Task RefreshCompletionDetailPanelAsync()
    {
        if (_completionDetailPanel == null || _completionDetailText == null)
        {
            return;
        }

        if (!_completionVisible || _completionItems.Count == 0 || _kernel == null || _inputField == null)
        {
            _completionDetailPanel.SetActive(false);
            return;
        }

        CompletionItem selected = _completionItems[_completionIndex];
        string kind = GetCompletionKindLabel(selected);
        string description = await _kernel.Completion
            .GetCompletionDescriptionAsync(_inputField.text ?? string.Empty, selected);

        // Guard: user may have changed selection while we awaited
        if (!_completionVisible || _completionItems.Count == 0)
        {
            return;
        }

        _completionDetailText.text =
            "<b>" + EscapeRichText(selected.DisplayText) + "</b>\n" +
            "<size=10><color=#A8A8A8>" + EscapeRichText(kind) + "</color></size>\n\n" +
            description;

        _completionDetailPanel.SetActive(true);
    }

    private async Task RefreshSignaturePanelAsync()
    {
        if (_signaturePanel == null || _signatureText == null || _kernel == null || _inputField == null)
        {
            return;
        }

        string code = _inputField.text ?? string.Empty;
        int caret = _inputField.caretPosition;

        string signature = await _kernel.Completion.GetSignatureTextAsync(code, caret);

        // Guard: input may have changed while we awaited
        if (_signaturePanel == null || _signatureText == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            _signaturePanel.SetActive(false);
            _signatureText.text = string.Empty;
            return;
        }

        _signatureText.text = signature;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_signatureText.rectTransform);

        float preferredHeight = _signatureText.preferredHeight + 12f;
        if (preferredHeight < SignaturePanelHeight)
        {
            preferredHeight = SignaturePanelHeight;
        }

        RectTransform panelRt = _signaturePanel.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, preferredHeight);

        _signaturePanel.SetActive(true);
    }
    private static string GetCompletionKindLabel(CompletionItem item)
    {
        if (item == null || item.Tags == null || item.Tags.Length == 0)
        {
            return "symbol";
        }

        string tag = item.Tags[0];

        return tag switch
        {
            WellKnownTags.Keyword => "keyword",
            WellKnownTags.Class => "class",
            WellKnownTags.Structure => "struct",
            WellKnownTags.Enum => "enum",
            WellKnownTags.Interface => "interface",
            WellKnownTags.Delegate => "delegate",
            WellKnownTags.Method => "method",
            WellKnownTags.Property => "property",
            WellKnownTags.Field => "field",
            WellKnownTags.Event => "event",
            WellKnownTags.Namespace => "namespace",
            _ => tag.ToLowerInvariant()
        };
    }
    private static string BuildCompletionLabel(CompletionItem item, string prefix)
    {
        string display = EscapeRichText(item?.DisplayText ?? string.Empty);

        if (string.IsNullOrEmpty(prefix))
        {
            return display;
        }

        string filterText = item?.FilterText ?? item?.DisplayText ?? string.Empty;
        if (!filterText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return display;
        }

        int matchLength = Math.Min(prefix.Length, display.Length);
        string matched = display.Substring(0, matchLength);
        string rest = display.Substring(matchLength);

        return "<color=#FFD54A><b>" + matched + "</b></color>" + rest;
    }

    private static string EscapeRichText(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private void BuildButtonRow(Transform parent)
    {
        GameObject row = new("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 0f);
        rowRt.anchorMax = new Vector2(1f, 0f);
        rowRt.pivot = new Vector2(0.5f, 0f);
        rowRt.offsetMin = new Vector2(WindowPadding, 6f);
        rowRt.offsetMax = new Vector2(-WindowPadding, 6f);
        rowRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ButtonRowHeight);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        GameObject leftGroup = new("LeftGroup", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        leftGroup.transform.SetParent(row.transform, false);
        LayoutElement leftLe = leftGroup.GetComponent<LayoutElement>();
        leftLe.preferredWidth = 60f;
        leftLe.minWidth = 60f;
        leftLe.flexibleWidth = 0f;

        HorizontalLayoutGroup leftLayout = leftGroup.GetComponent<HorizontalLayoutGroup>();
        leftLayout.spacing = 0f;
        leftLayout.childAlignment = TextAnchor.MiddleLeft;
        leftLayout.childControlHeight = true;
        leftLayout.childControlWidth = true;
        leftLayout.childForceExpandHeight = false;
        leftLayout.childForceExpandWidth = false;

        GameObject spacer = new("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(row.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject rightGroup = new("RightGroup", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        rightGroup.transform.SetParent(row.transform, false);
        LayoutElement rightLe = rightGroup.GetComponent<LayoutElement>();
        rightLe.preferredWidth = 60f;
        rightLe.minWidth = 60f;
        rightLe.flexibleWidth = 0f;

        HorizontalLayoutGroup rightLayout = rightGroup.GetComponent<HorizontalLayoutGroup>();
        rightLayout.spacing = 0f;
        rightLayout.childAlignment = TextAnchor.MiddleRight;
        rightLayout.childControlHeight = true;
        rightLayout.childControlWidth = true;
        rightLayout.childForceExpandHeight = false;
        rightLayout.childForceExpandWidth = false;

        CreateButton(leftGroup.transform, "Run", 60f, ExecuteCurrentCell);
        CreateButton(rightGroup.transform, "Reset", 60f, delegate
        {
            _kernel.ResetExecutionState();
            RefreshTranscript();
            FocusInput();
        });
    }

    private static Text CreateText(Transform parent, string name, int fontSize, TextAnchor anchor, Color color, bool bold)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = fontSize;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = anchor;
        text.color = color;
        text.text = name;

        return text;
    }

    private Button CreateButton(Transform parent, string label, float width, Action onClick)
    {
        GameObject go = new(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width, 16f);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.preferredHeight = 16f;
        le.minHeight = 16f;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;

        Image bg = go.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.1f);

        Button button = go.GetComponent<Button>();
        ColorBlock cb = button.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0.1f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        cb.pressedColor = new Color(1f, 1f, 1f, 0.24f);
        cb.selectedColor = new Color(1f, 1f, 1f, 0.18f);
        cb.colorMultiplier = 1f;
        button.colors = cb;

        GameObject textGo = new("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);

        Text text = textGo.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = 11;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;

        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        if (onClick != null)
        {
            button.onClick.AddListener(delegate { onClick(); });
        }

        return button;
    }

    private static Font GetFont()
    {
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void RefreshTranscript()
    {
        if (_transcriptText == null || _transcriptContent == null)
        {
            return;
        }

        string transcript = BuildTranscript();
        _transcriptText.text = transcript;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_transcriptText.rectTransform);

        float preferredHeight = _transcriptText.preferredHeight + 16f;
        if (preferredHeight < 100f)
        {
            preferredHeight = 100f;
        }

        _transcriptLayoutElement?.preferredHeight = preferredHeight;

        Vector2 sizeDelta = _transcriptContent.sizeDelta;
        _transcriptContent.sizeDelta = new Vector2(sizeDelta.x, preferredHeight);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_transcriptContent);

        _scrollRect?.verticalNormalizedPosition = 1f;
    }

    private void ToggleVisible()
    {
        SetVisible(!_visible);
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;

        if (!visible)
        {
            HideCompletion();
            _inputField?.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        _root?.SetActive(visible);

        if (visible)
        {
            RefreshTranscript();
            StartCoroutine(FocusInputNextFrame());
        }
    }

    private System.Collections.IEnumerator FocusInputNextFrame()
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

    private void FocusInput()
    {
        if (_inputField == null || !_visible)
        {
            return;
        }

        EventSystem.current?.SetSelectedGameObject(null);

        _inputField.ActivateInputField();
        _inputField.Select();
    }

    private async void ExecuteCurrentCell()
    {
        if (_kernel == null || _inputField == null)
        {
            return;
        }

        string code = _inputField.text;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _history.Add(code);
        HideCompletion();
        SetInputTextSilently(string.Empty);
        _history.ResetCursor();

        NotebookCellResult result = await _kernel.ExecuteAsync(code);
        if (!result.Success)
        {
            Plugin.LogError(result.Error);
        }

        RefreshTranscript();
        FocusInput();
    }

    private string BuildTranscript()
    {
        if (_kernel == null || _kernel.Session == null)
        {
            return "(kernel not ready)";
        }

        StringBuilder sb = new();

        for (int i = 0; i < _kernel.Session.Cells.Count; i++)
        {
            NotebookCell cell = _kernel.Session.Cells[i];
            sb.AppendLine("In [" + (i + 1) + "]");
            sb.AppendLine(cell.Code);
            sb.AppendLine("Out:");
            sb.AppendLine(cell.Result == null ? "(running)" : cell.Result.ToDisplayString());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private Canvas FindBestCanvas()
    {
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        Canvas fallback = null;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            if (!canvas.gameObject.scene.isLoaded)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = canvas;
            }

            if (canvas.name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                canvas.name.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return canvas;
            }
        }

        return fallback;
    }

    private Canvas CreateFallbackCanvas()
    {
        GameObject go = new("RDETerminalCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private void DestroyNativeUi()
    {
        HideCompletion();

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
            _canvas = null;
            _ownsCanvas = false;
        }

        _scrollRect = null;
        _transcriptContent = null;
        _transcriptText = null;
        _transcriptLayoutElement = null;
        _completionPanel = null;
        _completionRows.Clear();
    }
}