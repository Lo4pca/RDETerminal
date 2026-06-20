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
    private readonly IReadOnlyList<MetadataReference> _baseReferences;

    /// <summary>
    /// Serialises all workspace mutations. AdhocWorkspace is not thread-safe,
    /// and multiple async completion operations can be in-flight concurrently.
    /// SemaphoreSlim(1,1) gives async-compatible mutual exclusion without
    /// blocking any thread while waiting.
    /// </summary>
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private static readonly IEnumerable<string> DefaultImports = 
        [
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "RDLevelEditor",
            "RDETerminal.Domain",
            "RDETerminal.Domain.Core",
            "RDETerminal.Domain.Queries",
            "RDETerminal.Domain.Transforms.Common",
            "RDETerminal.Domain.Transforms.Text",
            "RDETerminal.Scripting"
        ];
    private static readonly IEnumerable<MetadataReference> DefaultReferences = 
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ScriptGlobals).Assembly.Location)
        ];
    public RoslynCompletionSession(IEnumerable<MetadataReference> baseReferences)
    {
        _baseReferences = [.. (baseReferences ?? []).Where(x => x != null)];

        MefHostServices host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        _workspace = new AdhocWorkspace(host);

        ProjectInfo projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "RDETerminalCompletion",
            "RDETerminalCompletion",
            LanguageNames.CSharp,
            metadataReferences: MergeReferences(DefaultReferences.Concat(_baseReferences)),
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
                    .Concat(_baseReferences)
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
            _workspace.TryApplyChanges(_workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(code)));

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
                cursorPosition,
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
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(code)));

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
            SourceText updated = SourceText.From(code).WithChanges(change.TextChange);
            return updated.ToString();
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
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(code)));

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
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(code)));

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
            SyntaxNode node = root.FindToken(Math.Max(0, caret - 1)).Parent;
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