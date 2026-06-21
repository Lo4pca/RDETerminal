using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using RDLevelEditor;

namespace RDETerminal.UI;

/// <summary>
/// Constructs the terminal's Unity UI hierarchy from scratch.
/// Stateless — all results are returned via <see cref="UiBuildResult"/>.
/// </summary>
internal static class TerminalUiBuilder
{
    // ── Layout constants ──────────────────────────────────────────────────────
    internal const float WindowPadding = 10f;
    internal const float HeaderHeight = 18f;
    internal const float InputAreaHeight = 70f;
    internal const float ButtonRowHeight = 26f;
    internal const float FooterGap = 1f;
    internal const float TranscriptTopGap = 4f;
    internal const int CompletionMaxVisibleItems = 8;
    internal const float CompletionItemHeight = 18f;
    internal const float CompletionPanelPadding = 6f;
    internal const float CompletionDetailPanelWidth = 170f;
    internal const float SignaturePanelHeight = 52f;

    internal readonly struct UiBuildResult(
        GameObject root, Canvas canvas, bool ownsCanvas,
        ScrollRect scrollRect, RectTransform transcriptContent,
        Text transcriptText, LayoutElement transcriptLayoutElement,
        InputField inputField,
        GameObject completionPanel, TerminalCompletionController.CompletionRow[] completionRows,
        GameObject completionDetailPanel, Text completionDetailText,
        GameObject signaturePanel, Text signatureText)
    {
        public readonly GameObject Root = root;
        public readonly Canvas Canvas = canvas;
        public readonly bool OwnsCanvas = ownsCanvas;
        public readonly ScrollRect ScrollRect = scrollRect;
        public readonly RectTransform TranscriptContent = transcriptContent;
        public readonly Text TranscriptText = transcriptText;
        public readonly LayoutElement TranscriptLayoutElement = transcriptLayoutElement;
        public readonly InputField InputField = inputField;
        public readonly GameObject CompletionPanel = completionPanel;
        public readonly TerminalCompletionController.CompletionRow[] CompletionRows = completionRows;
        public readonly GameObject CompletionDetailPanel = completionDetailPanel;
        public readonly Text CompletionDetailText = completionDetailText;
        public readonly GameObject SignaturePanel = signaturePanel;
        public readonly Text SignatureText = signatureText;
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    internal static UiBuildResult Build(
        InputField.OnValidateInput onValidateInput,
        Action<string> onInputChanged,
        MonoBehaviour coroutineHost)
    {
        Canvas canvas = FindBestCanvas();
        bool ownsCanvas = canvas == null;
        if (ownsCanvas)
        {
            canvas = CreateFallbackCanvas();
        }

        scnEditor editor = scnEditor.instance;
        GameObject template = editor?.publishPopup?.gameObject;

        GameObject root = UnityEngine.Object.Instantiate(template, canvas.transform, false);
        root.name = "RDETerminalPanel";
        root.SetActive(false);

        StripProblematicBehaviours(root);
        ClearChildren(root);

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(0f, 1f);
        rootRt.pivot = new Vector2(0f, 1f);
        rootRt.anchoredPosition = new Vector2(20f, -20f);
        rootRt.sizeDelta = new Vector2(320f, 320f);

        BuildHeader(root.transform, root.GetComponent<RectTransform>());
        var (scrollRect, transcriptContent, transcriptText, transcriptLayoutElement)
            = BuildTranscriptArea(root.transform);
        var inputField = BuildInputArea(root.transform, onValidateInput, onInputChanged, coroutineHost);
        var (completionPanel, completionRows) = BuildCompletionPanel(root.transform);
        var (detailPanel, detailText) = BuildCompletionDetailPanel(root.transform);
        var (sigPanel, sigText) = BuildSignaturePanel(root.transform);
        BuildButtonRow(root.transform, coroutineHost);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
        Canvas.ForceUpdateCanvases();

        return new UiBuildResult(
            root, canvas, ownsCanvas,
            scrollRect, transcriptContent, transcriptText, transcriptLayoutElement,
            inputField,
            completionPanel, completionRows,
            detailPanel, detailText,
            sigPanel, sigText);
    }

    // ── Section builders ──────────────────────────────────────────────────────

    private static void BuildHeader(Transform parent, RectTransform windowRt)
    {
        GameObject row = new("HeaderRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);

        RectTransform rt = row.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(10f, -18f);
        rt.offsetMax = new Vector2(-10f, 0f);

        LayoutElement le = row.GetComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = HeaderHeight;
        le.flexibleHeight = 0f;

        Text title = CreateText(row.transform, "RDETerminal", 10, TextAnchor.MiddleCenter, new Color(0.95f, 0.95f, 0.95f, 1f), true);
        title.raycastTarget = false;
        FillParent(title.rectTransform);

        TerminalWindow.WindowDragHandle drag = row.AddComponent<TerminalWindow.WindowDragHandle>();
        drag.Initialize(windowRt);
    }

    private static (ScrollRect scrollRect, RectTransform content, Text text, LayoutElement layoutElement)
        BuildTranscriptArea(Transform parent)
    {
        GameObject wrapper = new("TranscriptWrapper", typeof(RectTransform), typeof(Image));
        wrapper.transform.SetParent(parent, false);

        RectTransform wrapperRt = wrapper.GetComponent<RectTransform>();
        wrapperRt.anchorMin = Vector2.zero;
        wrapperRt.anchorMax = Vector2.one;
        wrapperRt.pivot = new Vector2(0.5f, 0.5f);
        wrapperRt.offsetMin = new Vector2(WindowPadding, InputAreaHeight + ButtonRowHeight + FooterGap);
        wrapperRt.offsetMax = new Vector2(-WindowPadding, -(HeaderHeight + TranscriptTopGap));
        wrapper.GetComponent<Image>().color = new Color(0.14f, 0.14f, 0.14f, 0.96f);

        GameObject scrollGo = new("TranscriptScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(wrapper.transform, false);
        FillParent(scrollGo.GetComponent<RectTransform>());

        ScrollRect scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        FillParent(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        VerticalLayoutGroup vl = content.GetComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.UpperLeft;
        vl.childControlHeight = vl.childControlWidth = true;
        vl.childForceExpandHeight = false;
        vl.childForceExpandWidth = true;
        vl.spacing = 4f;
        vl.padding = new RectOffset(12, 12, 12, 12);

        ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text transcriptText = CreateText(contentRt, "TranscriptText", 12, TextAnchor.UpperLeft, new Color(0.88f, 0.88f, 0.88f, 1f), false);
        transcriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
        transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
        transcriptText.raycastTarget = false;
        transcriptText.lineSpacing = 1.1f;

        RectTransform textRt = transcriptText.rectTransform;
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.pivot = new Vector2(0.5f, 1f);
        textRt.offsetMin = textRt.offsetMax = Vector2.zero;

        LayoutElement le = transcriptText.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 0f;

        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRt;

        return (scrollRect, contentRt, transcriptText, le);
    }

    private static InputField BuildInputArea(
        Transform parent,
        InputField.OnValidateInput onValidateInput,
        Action<string> onValueChanged,
        MonoBehaviour coroutineHost)
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
        FillParent(inputGo.GetComponent<RectTransform>());

        Image inputBg = inputGo.GetComponent<Image>();
        inputBg.color = new Color(1f, 1f, 1f, 0.08f);

        InputField input = inputGo.GetComponent<InputField>();
        input.lineType = InputField.LineType.MultiLineNewline;
        input.shouldHideMobileInput = true;
        input.transition = Selectable.Transition.ColorTint;
        input.targetGraphic = inputBg;
        input.enabled = false;

        Text text = CreateInputText(inputGo.transform, "Text", 13, false);
        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8f, 8f);
        textRt.offsetMax = new Vector2(-20f, -8f);

        Text placeholder = CreateInputText(inputGo.transform, "Placeholder", 13, true);
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.text = "Code here...";
        RectTransform phRt = placeholder.rectTransform;
        phRt.anchorMin = textRt.anchorMin;
        phRt.anchorMax = textRt.anchorMax;
        phRt.offsetMin = textRt.offsetMin;
        phRt.offsetMax = textRt.offsetMax;

        input.textComponent = text;
        input.placeholder = placeholder;
        input.onValidateInput += onValidateInput;
        input.onValueChanged.AddListener(onValueChanged.Invoke);

        coroutineHost.StartCoroutine(EnableNextFrame(input));

        return input;
    }

    private static (GameObject panel, TerminalCompletionController.CompletionRow[] rows)
        BuildCompletionPanel(Transform parent)
    {
        GameObject panel = new("CompletionPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(WindowPadding, ButtonRowHeight + FooterGap + InputAreaHeight + 8f);
        rt.sizeDelta = new Vector2(280f, CompletionMaxVisibleItems * CompletionItemHeight + CompletionPanelPadding * 2f);

        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.10f, 0.97f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 2f;
        layout.padding = new RectOffset(
            (int)CompletionPanelPadding, (int)CompletionPanelPadding,
            (int)CompletionPanelPadding, (int)CompletionPanelPadding);

        panel.SetActive(false);

        var rows = new TerminalCompletionController.CompletionRow[CompletionMaxVisibleItems];
        for (int i = 0; i < CompletionMaxVisibleItems; i++)
        {
            GameObject row = new($"CompletionRow_{i}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);

            RectTransform rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);

            LayoutElement le = row.GetComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = CompletionItemHeight;
            le.flexibleWidth = 1f;

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

            rows[i] = new TerminalCompletionController.CompletionRow
            {
                Root = rowRt,
                Background = rowBg,
                Label = label
            };
        }

        return (panel, rows);
    }

    private static (GameObject panel, Text text) BuildCompletionDetailPanel(Transform parent)
    {
        GameObject panel = new("CompletionDetailPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(WindowPadding + 280f + 8f, ButtonRowHeight + FooterGap + InputAreaHeight + 8f);
        rt.sizeDelta = new Vector2(CompletionDetailPanelWidth, CompletionMaxVisibleItems * CompletionItemHeight + CompletionPanelPadding * 2f);
        panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.97f);

        Text text = CreateText(panel.transform, "CompletionDetailText", 11, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f, 1f), false);
        text.supportRichText = true;
        text.raycastTarget = false;

        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 6f);
        textRt.offsetMax = new Vector2(-6f, -6f);

        panel.SetActive(false);
        return (panel, text);
    }

    private static (GameObject panel, Text text) BuildSignaturePanel(Transform parent)
    {
        GameObject panel = new("SignaturePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(WindowPadding + 280f + 8f, ButtonRowHeight + FooterGap + InputAreaHeight - SignaturePanelHeight - 6f);
        rt.sizeDelta = new Vector2(CompletionDetailPanelWidth, SignaturePanelHeight);
        panel.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 0.97f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.padding = new RectOffset(6, 6, 6, 6);

        ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text text = CreateText(panel.transform, "SignatureText", 11, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.92f, 1f), false);
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.lineSpacing = 1.05f;
        FillParent(text.rectTransform);

        LayoutElement le = text.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = le.flexibleHeight = 1f;

        panel.SetActive(false);
        return (panel, text);
    }

    private static void BuildButtonRow(Transform parent, MonoBehaviour host)
    {
        var window = (TerminalWindow)host;

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
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = layout.childForceExpandWidth = false;

        Transform leftGroup  = MakeButtonGroup(row.transform, "LeftGroup",  TextAnchor.MiddleLeft,  60f);
        Transform spacer     = MakeSpacer(row.transform);
        Transform rightGroup = MakeButtonGroup(row.transform, "RightGroup", TextAnchor.MiddleRight, 132f);

        _ = spacer; // used for layout only

        CreateButton(leftGroup, "Run", 60f, () => window.ExecuteCurrentCellPublic());
        CreateButton(rightGroup, "Reload", 64f, () => window.ReloadUserScripts());
        CreateButton(rightGroup, "Reset", 60f, () => window.ResetKernel());
    }

    // ── Shared primitives ─────────────────────────────────────────────────────

    internal static Text CreateText(
        Transform parent, string name, int fontSize,
        TextAnchor anchor, Color color, bool bold)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text t = go.GetComponent<Text>();
        t.font = GetFont();
        t.fontSize = fontSize;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.color = color;
        t.text = name;
        return t;
    }

    internal static void CreateButton(Transform parent, string label, float width, Action onClick)
    {
        GameObject go = new(label + "Button",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width, 16f);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
        le.preferredHeight = le.minHeight = 16f;
        le.flexibleWidth = le.flexibleHeight = 0f;

        Image bg = go.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.1f);

        Button button = go.GetComponent<Button>();
        ColorBlock cb = button.colors;
        cb.normalColor      = new Color(1f, 1f, 1f, 0.10f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        cb.pressedColor     = new Color(1f, 1f, 1f, 0.24f);
        cb.selectedColor    = new Color(1f, 1f, 1f, 0.18f);
        cb.colorMultiplier  = 1f;
        button.colors = cb;

        Text text = CreateText(go.transform, "Text", 11, TextAnchor.MiddleCenter, Color.white, false);
        text.text = label;
        text.raycastTarget = false;
        FillParent(text.rectTransform);

        if (onClick != null)
        {
            button.onClick.AddListener(delegate { onClick(); });
        }
    }

    internal static Font GetFont() =>
        Resources.GetBuiltinResource<Font>("Arial.ttf");

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Text CreateInputText(Transform parent, string name, int fontSize, bool isPlaceholder)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.font = GetFont();
        t.fontSize = fontSize;
        t.alignment = TextAnchor.UpperLeft;
        t.color = isPlaceholder ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static Transform MakeButtonGroup(Transform parent, string name, TextAnchor alignment, float width)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
        le.flexibleWidth = 0f;

        HorizontalLayoutGroup hg = go.GetComponent<HorizontalLayoutGroup>();
        hg.spacing = 4f;
        hg.childAlignment = alignment;
        hg.childControlHeight = hg.childControlWidth = true;
        hg.childForceExpandHeight = hg.childForceExpandWidth = false;

        return go.transform;
    }

    private static Transform MakeSpacer(Transform parent)
    {
        GameObject go = new("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return go.transform;
    }

    private static void FillParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Canvas FindBestCanvas()
    {
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        Canvas fallback = null;

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.gameObject.scene.isLoaded)
            {
                continue;
            }

            fallback ??= canvas;

            if (canvas.name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                canvas.name.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return canvas;
            }
        }

        return fallback;
    }

    private static Canvas CreateFallbackCanvas()
    {
        GameObject go = new("RDETerminalCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.Object.DontDestroyOnLoad(go);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static void StripProblematicBehaviours(GameObject root)
    {
        if (root == null) return;

        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string ns   = mb.GetType().Namespace ?? string.Empty;
            string name = mb.GetType().Name;
            if (ns.StartsWith("RDLevelEditor", StringComparison.OrdinalIgnoreCase) ||
                name == "RDPublishPopup" || name == "RDStringToUIText")
            {
                UnityEngine.Object.Destroy(mb);
            }
        }
    }

    private static void ClearChildren(GameObject root)
    {
        if (root == null) return;

        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = root.transform.GetChild(i);
            if (child != null)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static IEnumerator EnableNextFrame(InputField inputField)
    {
        yield return null;
        yield return null;
        inputField?.enabled = true;
    }
}
