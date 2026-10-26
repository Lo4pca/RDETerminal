using System.Text;
using RDETerminal.Notebook;
using UnityEngine;
using UnityEngine.UI;

namespace RDETerminal.UI;

/// <summary>
/// Owns and updates the transcript scroll area.
/// Plain C# class — no MonoBehaviour dependency.
/// </summary>
internal sealed class TerminalTranscriptView
{
    private readonly NotebookKernel _kernel;
    private Text _transcriptText;
    private RectTransform _transcriptContent;
    private LayoutElement _transcriptLayoutElement;
    private ScrollRect _scrollRect;

    internal TerminalTranscriptView(NotebookKernel kernel)
    {
        _kernel = kernel;
    }

    internal void Attach(
        Text transcriptText,
        RectTransform transcriptContent,
        LayoutElement transcriptLayoutElement,
        ScrollRect scrollRect)
    {
        _transcriptText = transcriptText;
        _transcriptContent = transcriptContent;
        _transcriptLayoutElement = transcriptLayoutElement;
        _scrollRect = scrollRect;
    }

    internal void Detach()
    {
        _transcriptText = null;
        _transcriptContent = null;
        _transcriptLayoutElement = null;
        _scrollRect = null;
    }

    internal void Refresh()
    {
        if (_transcriptText == null || _transcriptContent == null)
        {
            return;
        }

        _transcriptText.text = BuildTranscript();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_transcriptText.rectTransform);

        float preferredHeight = _transcriptText.preferredHeight + 16f;
        if (preferredHeight < 100f)
        {
            preferredHeight = 100f;
        }

        _transcriptLayoutElement?.preferredHeight = preferredHeight;

        _transcriptContent.sizeDelta = new Vector2(_transcriptContent.sizeDelta.x, preferredHeight);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_transcriptContent);

        _scrollRect?.verticalNormalizedPosition = 0f;
    }

    private string BuildTranscript()
    {
        if (_kernel?.Session == null)
        {
            return "(kernel not ready)";
        }

        NotebookSession session = _kernel.Session;
        StringBuilder sb = new();
        int nextNotice = 0;

        // Notices are recorded in chronological order, so a single forward pass
        // interleaves them with the cells they followed.
        for (int i = 0; i <= session.Cells.Count; i++)
        {
            while (nextNotice < session.Notices.Count && session.Notices[nextNotice].AfterCellCount <= i)
            {
                sb.AppendLine("-- " + session.Notices[nextNotice].Message + " --");
                sb.AppendLine();
                nextNotice++;
            }

            if (i == session.Cells.Count)
            {
                break;
            }

            NotebookCell cell = session.Cells[i];
            sb.AppendLine("In [" + (i + 1) + "]");
            sb.AppendLine(cell.Code);
            sb.AppendLine("Out:");
            sb.AppendLine(cell.Result == null ? "(running)" : cell.Result.ToDisplayString());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}