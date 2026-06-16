using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using RDETerminal.Notebook;
using UnityEngine;
using UnityEngine.UI;

namespace RDETerminal.UI;

/// <summary>
/// Owns all completion and signature-help state and logic.
/// Plain C# class — no MonoBehaviour dependency.
/// Calls back into <see cref="TerminalWindow"/> for input-field mutations
/// via the <c>setInputText</c> delegate supplied at construction.
/// </summary>
internal sealed class TerminalCompletionController
{
    internal sealed class CompletionRow
    {
        public RectTransform Root;
        public Image Background;
        public Text Label;
    }

    // ── Layout constants (must match TerminalUiBuilder) ───────────────────────
    private const int MaxVisibleItems = 8;
    private const float ItemHeight = 18f;
    private const float PanelPadding = 6f;
    private const float SignaturePanelHeight = 52f;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly NotebookKernel _kernel;
    private readonly Action<string> _setInputText;

    // ── UI references (assigned after build) ─────────────────────────────────
    private InputField _inputField;
    private GameObject _completionPanel;
    private IReadOnlyList<CompletionRow> _completionRows;
    private GameObject _completionDetailPanel;
    private Text _completionDetailText;
    private GameObject _signaturePanel;
    private Text _signatureText;

    // ── State ─────────────────────────────────────────────────────────────────
    private IReadOnlyList<CompletionItem> _completionItems = [];
    private int _completionIndex;
    private string _currentPrefix = string.Empty;

    internal bool IsVisible { get; private set; }
    internal bool SuppressInputChanged { get; set; }
    internal int RequestVersion { get; set; }

    internal TerminalCompletionController(NotebookKernel kernel, Action<string> setInputText)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _setInputText = setInputText ?? throw new ArgumentNullException(nameof(setInputText));
    }

    internal void Attach(
        InputField inputField,
        GameObject completionPanel,
        IReadOnlyList<CompletionRow> completionRows,
        GameObject completionDetailPanel,
        Text completionDetailText,
        GameObject signaturePanel,
        Text signatureText)
    {
        _inputField = inputField;
        _completionPanel = completionPanel;
        _completionRows = completionRows;
        _completionDetailPanel = completionDetailPanel;
        _completionDetailText = completionDetailText;
        _signaturePanel = signaturePanel;
        _signatureText = signatureText;
    }

    internal void Detach()
    {
        Hide();
        _inputField = null;
        _completionPanel = null;
        _completionRows = null;
        _completionDetailPanel = null;
        _completionDetailText = null;
        _signaturePanel = null;
        _signatureText = null;
    }

    // ── Input callbacks (wired by TerminalWindow) ─────────────────────────────

    internal char OnValidateInput(string text, int charIndex, char addedChar)
    {
        if (addedChar == '\t')
        {
            return '\0';
        }

        if (IsVisible && (addedChar == '\n' || addedChar == '\r'))
        {
            return '\0';
        }

        return addedChar;
    }

    internal void OnInputChanged(string text)
    {
        if (SuppressInputChanged)
        {
            return;
        }

        int version = ++RequestVersion;
        _ = OnInputChangedAsync(text, version);
    }

    // ── Completion operations ─────────────────────────────────────────────────

    internal void MoveSelection(int delta)
    {
        if (!IsVisible || _completionItems.Count == 0)
        {
            return;
        }

        _completionIndex = (_completionIndex + delta + _completionItems.Count) % _completionItems.Count;
        RefreshPanel();
        _ = RefreshDetailPanelAsync();
    }

    internal async void Commit()
    {
        if (!IsVisible || _completionItems.Count == 0 || _inputField == null)
        {
            return;
        }

        CompletionItem selected = _completionItems[_completionIndex];
        string code = _inputField.text ?? string.Empty;

        Hide();

        string newText = await _kernel.Completion.ApplyAsync(code, selected);
        _setInputText(newText);
    }

    internal void Hide()
    {
        IsVisible = false;
        _completionItems = [];
        _completionIndex = 0;
        _currentPrefix = string.Empty;

        _completionPanel?.SetActive(false);
        _completionDetailPanel?.SetActive(false);
        _signaturePanel?.SetActive(false);
    }

    internal Task RefreshSignatureAsync()
    {
        return RefreshSignaturePanelAsync();
    }

    // ── Private async pipeline ────────────────────────────────────────────────

    private async Task OnInputChangedAsync(string text, int version)
    {
        if (_kernel?.Completion == null || _inputField == null)
        {
            return;
        }

        text ??= string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            _currentPrefix = string.Empty;
            if (version == RequestVersion)
            {
                Hide();
            }
            return;
        }

        int caret = Mathf.Clamp(_inputField.caretPosition, 0, text.Length);
        _currentPrefix = RoslynCompletionSession.GetCurrentPrefix(text, caret);

        IReadOnlyList<CompletionItem> items = await _kernel.Completion.GetItemsAsync(text, caret);

        if (version != RequestVersion)
        {
            return;
        }

        _completionItems = items ?? [];
        _completionIndex = 0;
        IsVisible = _completionItems.Count > 0;

        RefreshPanel();
        await RefreshDetailPanelAsync();
        await RefreshSignaturePanelAsync();
    }

    private void RefreshPanel()
    {
        if (_completionPanel == null)
        {
            return;
        }

        if (!IsVisible || _completionItems.Count == 0)
        {
            _completionPanel.SetActive(false);
            return;
        }

        _completionPanel.SetActive(true);

        int visibleCount = Math.Min(MaxVisibleItems, _completionItems.Count);
        int maxStart = Math.Max(0, _completionItems.Count - visibleCount);
        int windowStart = Mathf.Clamp(_completionIndex - (visibleCount / 2), 0, maxStart);

        float panelHeight = (visibleCount * ItemHeight) + (PanelPadding * 2f) + ((visibleCount - 1) * 2f);
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

            row.Label.text = BuildLabel(item, _currentPrefix);
        }
    }

    private async Task RefreshDetailPanelAsync()
    {
        if (_completionDetailPanel == null || _completionDetailText == null)
        {
            return;
        }

        if (!IsVisible || _completionItems.Count == 0 || _inputField == null)
        {
            _completionDetailPanel.SetActive(false);
            return;
        }

        CompletionItem selected = _completionItems[_completionIndex];
        string kind = GetKindLabel(selected);
        string description = await _kernel.Completion
            .GetCompletionDescriptionAsync(_inputField.text ?? string.Empty, selected);

        // Guard: selection may have changed while awaiting
        if (!IsVisible || _completionItems.Count == 0)
        {
            return;
        }

        _completionDetailText.text =
            "<b>" + Escape(selected.DisplayText) + "</b>\n" +
            "<size=10><color=#A8A8A8>" + Escape(kind) + "</color></size>\n\n" +
            description;

        _completionDetailPanel.SetActive(true);
    }

    private async Task RefreshSignaturePanelAsync()
    {
        if (_signaturePanel == null || _signatureText == null || _inputField == null)
        {
            return;
        }

        string code = _inputField.text ?? string.Empty;
        int caret = _inputField.caretPosition;

        string signature = await _kernel.Completion.GetSignatureTextAsync(code, caret);

        // Guard: input may have changed while awaiting
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

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string GetKindLabel(CompletionItem item)
    {
        if (item?.Tags == null || item.Tags.Length == 0)
        {
            return "symbol";
        }

        return item.Tags[0] switch
        {
            WellKnownTags.Keyword   => "keyword",
            WellKnownTags.Class     => "class",
            WellKnownTags.Structure => "struct",
            WellKnownTags.Enum      => "enum",
            WellKnownTags.Interface => "interface",
            WellKnownTags.Delegate  => "delegate",
            WellKnownTags.Method    => "method",
            WellKnownTags.Property  => "property",
            WellKnownTags.Field     => "field",
            WellKnownTags.Event     => "event",
            WellKnownTags.Namespace => "namespace",
            string tag              => tag.ToLowerInvariant()
        };
    }

    private static string BuildLabel(CompletionItem item, string prefix)
    {
        string display = Escape(item?.DisplayText ?? string.Empty);

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
        return "<color=#FFD54A><b>" + display.Substring(0, matchLength) + "</b></color>" +
               display.Substring(matchLength);
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}