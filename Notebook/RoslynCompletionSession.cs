using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using RDETerminal.Scripting;
using RDETerminal.Scripting.HotReload;
using UnityEngine;

namespace RDETerminal.Notebook;

public sealed class RoslynCompletionSession
{
    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private readonly DocumentId _documentId;

    /// <summary>
    /// Serialises all workspace mutations. AdhocWorkspace is not thread-safe,
    /// and multiple async completion operations can be in-flight concurrently.
    /// SemaphoreSlim(1,1) gives async-compatible mutual exclusion without
    /// blocking any thread while waiting.
    /// </summary>
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);

    /// <summary>
    /// Supplies the code of every cell that has executed successfully in the
    /// current session (see <c>RoslynScriptHost.GetCommittedCells</c>). The
    /// completion document is "those cells + the text being typed", which is
    /// what lets variables declared in earlier cells show up in completion.
    /// Contract: each returned list must be an immutable snapshot (a new
    /// instance whenever the contents change), because it is cached by
    /// reference.
    /// </summary>
    private readonly Func<IReadOnlyList<string>> _committedCellsProvider;

    // The fields below are only read or written while holding _workspaceLock.
    private IReadOnlyList<string> _cachedCells;
    private string _cachedContext = string.Empty;

    /// <summary>
    /// Context prefix that the most recent <see cref="GetItemsAsync"/> call was
    /// computed against. <see cref="ApplyAsync"/> and
    /// <see cref="GetCompletionDescriptionAsync"/> reuse it so that a completion
    /// item is always interpreted against the same document it came from, even
    /// if a cell finished executing in between.
    /// </summary>
    private string _itemsContext = string.Empty;

    /// <summary>Full text currently stored in the workspace document.</summary>
    private string _documentText = string.Empty;
    private static readonly IEnumerable<string> DefaultImports = 
        [
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "RDETerminal.Domain",
            "RDETerminal.Domain.Core",
            "RDETerminal.Domain.Queries",
            "RDETerminal.Domain.Transforms.Common",
            "RDETerminal.Domain.Transforms.Action",
            "RDETerminal.Domain.Transforms.Row",
            "RDETerminal.Scripting"
        ];
    private static readonly IEnumerable<MetadataReference> DefaultReferences = 
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ScriptGlobals).Assembly.Location)
        ];
    public RoslynCompletionSession(Func<IReadOnlyList<string>> committedCellsProvider = null)
    {
        _committedCellsProvider = committedCellsProvider;

        MefHostServices host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        _workspace = new AdhocWorkspace(host);

        ProjectInfo projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "RDETerminalCompletion",
            "RDETerminalCompletion",
            LanguageNames.CSharp,
            metadataReferences: DefaultReferences,
            parseOptions: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                kind: SourceCodeKind.Script,
                languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest),
            compilationOptions: new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: DefaultImports),
            isSubmission: true,
            hostObjectType: typeof(ScriptGlobals));

        Project project = _workspace.AddProject(projectInfo);
        _projectId = project.Id;

        var scriptDocumentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(_projectId), "Script",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(string.Empty), VersionStamp.Create())));

        Document document = _workspace.AddDocument(scriptDocumentInfo);
        _documentId = document.Id;
    }


    public async Task ApplyReloadResult(UserScriptReloadResult result)
    {
        if (result == null || !result.Success)
        {
            return;
        }

        await _workspaceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Project project = _workspace.CurrentSolution.GetProject(_projectId);
            if (project == null)
            {
                return;
            }

            IReadOnlyList<MetadataReference> references = MergeReferences(
                DefaultReferences
                    .Concat(result.MetadataReference != null ? [result.MetadataReference] : Array.Empty<MetadataReference>()));

            IReadOnlyList<string> imports = [.. DefaultImports
                .Concat(result.ExportedNamespaces.Where(x => !string.IsNullOrWhiteSpace(x)))
                .Distinct(StringComparer.Ordinal)];

            var compilationOptions = ((Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)project.CompilationOptions)
                .WithUsings(imports);

            var solution = _workspace.CurrentSolution
                .WithProjectMetadataReferences(_projectId, references)
                .WithProjectCompilationOptions(_projectId, compilationOptions);

            _workspace.TryApplyChanges(solution);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<IReadOnlyList<CompletionItem>> GetItemsAsync(
        string code,
        int cursorPosition,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code))
        {
            return [];
        }

        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Roslyn analyses "earlier cells + typed text" so variables declared in
            // previous cells are in scope. All positions Roslyn sees are therefore
            // shifted by the length of the context prefix.
            string context = GetContextPrefix();
            _itemsContext = context;
            int offset = context.Length;
            int caret = Mathf.Clamp(cursorPosition, 0, code.Length);
            SetDocumentText(context + code);

            Document document = _workspace.CurrentSolution.GetDocument(_documentId);
            if (document == null)
            {
                return [];
            }

            CompletionService service = CompletionService.GetService(document);
            if (service == null)
            {
                return [];
            }

            var results = await service.GetCompletionsAsync(
                document,
                caret + offset,
                trigger: CreateTrigger(code, cursorPosition),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (results == null || results.ItemsList.Count == 0)
            {
                return [];
            }

            SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var cache = new Dictionary<TextSpan, string>();
            string prefix = GetCurrentPrefix(code, cursorPosition);

            var sortedItems = results.ItemsList
                .Where(item => MatchesFilterText(service, document, item, text, cache))
                .Select(item => new {
                    Item = item,
                    PrefixScore = item.DisplayText?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true ? 2 :
                                item.DisplayText?.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0
                })
                .OrderByDescending(x => x.PrefixScore)
                .ThenBy(x => x.Item.SortText, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item)
                .ToList();

            return sortedItems;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    internal static string GetCurrentPrefix(string code, int cursorPosition)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        int caret = Mathf.Clamp(cursorPosition, 0, code.Length);
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

    private static bool MatchesFilterText(
        CompletionService completionService,
        Document document,
        CompletionItem item,
        SourceText text,
        Dictionary<TextSpan, string> textSpanToText)
    {
        var filterText = GetFilterText(item, text, textSpanToText);
        if (string.IsNullOrEmpty(filterText)) return true;
        return completionService.FilterItems(document, ImmutableArray.Create(item), filterText).Length > 0;
    }

    private static string GetFilterText(
        CompletionItem item,
        SourceText text,
        Dictionary<TextSpan, string> textSpanToText)
    {
        TextSpan textSpan = item.Span;

        if (textSpan.End > text.Length)
        {
            return string.Empty;
        }

        if (!textSpanToText.TryGetValue(textSpan, out string filterText))
        {
            filterText = text.GetSubText(textSpan).ToString();
            textSpanToText[textSpan] = filterText;
        }

        return filterText;
    }

    public async Task<string> ApplyAsync(
        string code,
        CompletionItem item)
    {
        if (string.IsNullOrEmpty(code) || item == null)
        {
            return code ?? string.Empty;
        }

        await _workspaceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Use the context the item was produced against (see _itemsContext).
            string context = _itemsContext;
            int offset = context.Length;
            SetDocumentText(context + code);

            Document document = _workspace.CurrentSolution.GetDocument(_documentId);
            if (document == null)
            {
                return code;
            }

            CompletionService service = CompletionService.GetService(document);
            if (service == null)
            {
                return code;
            }

            var change = await service.GetChangeAsync(document, item).ConfigureAwait(false);

            // The change is expressed against the full document (context + typed
            // text). Translate it back onto the text the user actually typed.
            TextChange documentChange = change.TextChange;
            int start = documentChange.Span.Start - offset;
            if (start < 0 || start + documentChange.Span.Length > code.Length)
            {
                // The edit touches the context prefix, not the user's text.
                // Refuse rather than corrupt the input.
                return code;
            }

            var userChange = new TextChange(
                new TextSpan(start, documentChange.Span.Length),
                documentChange.NewText);

            return SourceText.From(code).WithChanges(userChange).ToString();
        }
        finally
        {
            _workspaceLock.Release();
        }
    }
    private static CompletionTrigger CreateTrigger(string code, int cursorPosition)
    {
        if (string.IsNullOrEmpty(code) || cursorPosition <= 0 || cursorPosition > code.Length)
        {
            return CompletionTrigger.Invoke;
        }

        char ch = code[cursorPosition - 1];

        return ch switch
        {
            '.' or '(' or ',' or ':' or ';' or '=' or '[' or '{'
                => CompletionTrigger.CreateInsertionTrigger(ch),
            _ => CompletionTrigger.Invoke
        };
    }
    public async Task<string> GetCompletionDescriptionAsync(
        string code,
        CompletionItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code) || item == null)
        {
            return string.Empty;
        }

        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetDocumentText(_itemsContext + code);

            Document document = _workspace.CurrentSolution.GetDocument(_documentId);
            if (document == null)
            {
                return string.Empty;
            }

            CompletionService service = CompletionService.GetService(document);
            if (service == null)
            {
                return string.Empty;
            }

            var description = await service.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false);
            if (description == null || description.Text == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new();
            foreach (var part in description.TaggedParts)
            {
                sb.Append(part.Text);
            }

            return sb.ToString().Trim();
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<string> GetSignatureTextAsync(
        string code,
        int cursorPosition,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string context = GetContextPrefix();
            int offset = context.Length;
            SetDocumentText(context + code);

            Document document = _workspace.CurrentSolution.GetDocument(_documentId);
            if (document == null)
            {
                return string.Empty;
            }

            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            SemanticModel model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root == null || model == null)
            {
                return string.Empty;
            }

            int caret = Mathf.Clamp(cursorPosition, 0, code.Length);
            if (caret == 0)
            {
                return string.Empty;
            }

            // Document-relative position of the character before the caret.
            SyntaxNode node = root.FindToken(offset + caret - 1).Parent;
            if (node == null)
            {
                return string.Empty;
            }

            IMethodSymbol method = null;
            int openParen = -1;

            InvocationExpressionSyntax invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null)
            {
                var info = model.GetSymbolInfo(invocation.Expression, cancellationToken);
                method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                openParen = invocation.ArgumentList != null ? invocation.ArgumentList.OpenParenToken.SpanStart : -1;
            }
            else
            {
                ObjectCreationExpressionSyntax creation = node.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
                if (creation != null)
                {
                    var info = model.GetSymbolInfo(creation, cancellationToken);
                    method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                    openParen = creation.ArgumentList != null ? creation.ArgumentList.OpenParenToken.SpanStart : -1;
                }
            }

            if (method == null)
            {
                return string.Empty;
            }

            // Translate the document-relative '(' position back into the typed text.
            if (openParen >= 0)
            {
                openParen -= offset;
                if (openParen < 0)
                {
                    return string.Empty;
                }
            }

            int activeIndex = GetActiveParameterIndex(code, openParen, caret, method.Parameters.Length);
            return BuildSignatureText(method, activeIndex);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    private static int GetActiveParameterIndex(string code, int openParenIndex, int cursorPosition, int parameterCount)
    {
        if (string.IsNullOrEmpty(code) || openParenIndex < 0 || parameterCount <= 0)
        {
            return 0;
        }

        int start = Math.Min(openParenIndex + 1, code.Length);
        int end = Mathf.Clamp(cursorPosition, start, code.Length);

        int commas = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        bool inDoubleString = false;
        bool inSingleChar = false;
        bool escape = false;

        for (int i = start; i < end; i++)
        {
            char c = code[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && (inDoubleString || inSingleChar))
            {
                escape = true;
                continue;
            }

            if (inDoubleString)
            {
                if (c == '"')
                {
                    inDoubleString = false;
                }
                continue;
            }

            if (inSingleChar)
            {
                if (c == '\'')
                {
                    inSingleChar = false;
                }
                continue;
            }

            if (c == '"')
            {
                inDoubleString = true;
                continue;
            }

            if (c == '\'')
            {
                inSingleChar = true;
                continue;
            }

            switch (c)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0) braceDepth--;
                    break;
                case ',':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        commas++;
                    }
                    break;
            }
        }

        return Math.Min(commas, parameterCount - 1);
    }

    private static string BuildSignatureText(IMethodSymbol method, int activeParameterIndex)
    {
        StringBuilder sb = new();
        sb.Append(method.Name);
        sb.Append("(");

        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            string part = method.Parameters[i].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if (i == activeParameterIndex)
            {
                sb.Append("<color=#FFD54A><b>").Append(part).Append("</b></color>");
            }
            else
            {
                sb.Append(part);
            }
        }

        sb.Append(")");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the text that precedes the user's input in the completion
    /// document: every successfully executed cell, in order. Must be called
    /// while holding <c>_workspaceLock</c>.
    /// </summary>
    private string GetContextPrefix()
    {
        IReadOnlyList<string> cells = _committedCellsProvider?.Invoke();

        if (cells == null || cells.Count == 0)
        {
            _cachedCells = null;
            _cachedContext = string.Empty;
            return string.Empty;
        }

        // Snapshots are immutable and replaced on change, so reference equality
        // is a reliable "nothing changed since last time" check.
        if (!ReferenceEquals(cells, _cachedCells))
        {
            _cachedContext = BuildContextPrefix(cells);
            _cachedCells = cells;
        }

        return _cachedContext;
    }

    /// <summary>
    /// Joins cells into one script. Each cell is terminated explicitly because
    /// the scripting engine tolerates a missing final semicolon in a
    /// submission, but a combined script does not. The terminator goes on its
    /// own line so it can't be swallowed by a trailing // comment.
    /// </summary>
    private static string BuildContextPrefix(IReadOnlyList<string> cells)
    {
        var sb = new StringBuilder();

        foreach (string cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue;
            }

            string trimmed = cell.TrimEnd();
            sb.Append(trimmed);

            if (trimmed[^1] != ';')
            {
                sb.Append("\n;");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces the workspace document's text, skipping the (expensive) reparse
    /// and rebinding when the text hasn't changed. Completion, description and
    /// signature requests for one keystroke all use identical text, so this
    /// avoids analysing the whole session history several times per keystroke.
    /// Must be called while holding <c>_workspaceLock</c>.
    /// </summary>
    private void SetDocumentText(string fullText)
    {
        if (string.Equals(fullText, _documentText, StringComparison.Ordinal))
        {
            return;
        }

        if (_workspace.TryApplyChanges(
                _workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(fullText))))
        {
            _documentText = fullText;
        }
    }

    private static IReadOnlyList<MetadataReference> MergeReferences(IEnumerable<MetadataReference> references)
    {
        var merged = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        foreach (MetadataReference reference in references ?? [])
        {
            if (reference == null)
            {
                continue;
            }

            string key = reference.Display ?? reference.GetHashCode().ToString();
            if (!merged.ContainsKey(key))
            {
                merged[key] = reference;
            }
        }
        return [.. merged.Values];
    }
}