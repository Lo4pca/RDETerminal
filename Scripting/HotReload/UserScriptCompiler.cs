using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace RDETerminal.Scripting.HotReload;

public sealed class UserScriptCompiler
{
    private const string HotReloadAssemblyPrefix = "RDETerminal.UserScripts.";

    private readonly IReadOnlyList<string> _defaultImports;
    private readonly IReadOnlyList<MetadataReference> _extraReferences;
    private readonly CSharpParseOptions _parseOptions;
    private readonly CSharpCompilationOptions _compilationOptions;

    /// <summary>
    /// Caches resolved <see cref="MetadataReference"/>s for assemblies already
    /// seen, keyed by assembly file location. AppDomain.GetAssemblies() is
    /// scanned in full only once (here); subsequent <see cref="BuildReferences"/>
    /// calls only resolve assemblies not already present in this cache,
    /// which in practice is almost always zero during a hot-reload session.
    /// </summary>
    private readonly Dictionary<string, MetadataReference> _assemblyReferenceCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _cacheLock = new();

    public UserScriptCompiler(
        IEnumerable<string> defaultImports = null,
        IEnumerable<MetadataReference> extraReferences = null,
        bool allowUnsafe = false)
    {
        _defaultImports = [.. defaultImports ?? []];
        _extraReferences = [.. (extraReferences ?? []).Where(x => x != null)];

        _parseOptions = new CSharpParseOptions(
            kind: SourceCodeKind.Regular,
            languageVersion: LanguageVersion.Latest);

        _compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            usings: _defaultImports,
            allowUnsafe: allowUnsafe,
            optimizationLevel: OptimizationLevel.Debug);
    }

    public UserScriptReloadResult Compile(UserScriptCatalog catalog)
    {
        if (catalog == null)
        {
            return UserScriptReloadResult.Failed(
                [
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "USR001",
                            title: "Catalog is null",
                            messageFormat: "UserScriptCatalog is null.",
                            category: "UserScriptCompiler",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None)
                ]);
        }

        IReadOnlyList<UserScriptCatalog.ScriptFile> snapshot = catalog.GetSnapshot();
        var sourceFiles = snapshot.Select(x => x.Path ?? x.Name ?? "<UserScript>").ToArray();

        var syntaxTrees = new List<SyntaxTree>(snapshot.Count + 1);

        SyntaxTree preludeTree = BuildPreludeTree();
        if (preludeTree != null)
        {
            syntaxTrees.Add(preludeTree);
        }

        foreach (UserScriptCatalog.ScriptFile file in snapshot)
        {
            if (file == null)
            {
                continue;
            }

            string code = file.SourceCode ?? string.Empty;
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                code,
                _parseOptions,
                path: file.Path ?? file.Name ?? "<UserScript>"));
        }

        IReadOnlyList<MetadataReference> references = BuildReferences();

        string assemblyName = HotReloadAssemblyPrefix + DateTime.UtcNow.Ticks.ToString("x");

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: syntaxTrees,
            references: references,
            options: _compilationOptions);

        ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return UserScriptReloadResult.Failed(diagnostics, sourceFiles);
        }

        using var peStream = new MemoryStream();
        EmitResult emitResult = compilation.Emit(peStream);

        diagnostics = diagnostics.AddRange(emitResult.Diagnostics);

        if (!emitResult.Success)
        {
            return UserScriptReloadResult.Failed(diagnostics, sourceFiles);
        }

        byte[] image = peStream.ToArray();
        Assembly assembly = Assembly.Load(image);
        PortableExecutableReference metadataReference = MetadataReference.CreateFromImage(ImmutableArray.Create(image));

        return UserScriptReloadResult.Succeeded(
            assembly,
            metadataReference,
            diagnostics,
            sourceFiles);
    }

    private SyntaxTree BuildPreludeTree()
    {
        if (_defaultImports.Count == 0)
        {
            return null;
        }

        string code = string.Join(
            Environment.NewLine,
            _defaultImports.Select(x => "using " + x + ";")) + Environment.NewLine;

        return CSharpSyntaxTree.ParseText(
            code,
            _parseOptions,
            path: "<UserScriptPrelude>.cs");
    }

    private IReadOnlyList<MetadataReference> BuildReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        void AddReference(MetadataReference reference)
        {
            if (reference == null)
            {
                return;
            }

            string key = reference.Display ?? reference.GetHashCode().ToString();
            if (!references.ContainsKey(key)) references.Add(key, reference);
        }

        foreach (MetadataReference cached in GetOrUpdateAssemblyReferenceCache())
        {
            AddReference(cached);
        }

        foreach (MetadataReference reference in _extraReferences)
        {
            AddReference(reference);
        }

        return [.. references.Values];
    }

    /// <summary>
    /// Returns cached <see cref="MetadataReference"/>s for all assemblies seen
    /// so far, resolving only assemblies not already in the cache. The full
    /// AppDomain.GetAssemblies() array is still enumerated every call (its
    /// growth is bounded by total assemblies ever loaded), but the relatively
    /// expensive per-assembly resolution (name/location reflection and
    /// MetadataReference.CreateFromFile) only runs once per unique assembly
    /// location, not once per reload.
    /// </summary>
    private IReadOnlyCollection<MetadataReference> GetOrUpdateAssemblyReferenceCache()
    {
        lock (_cacheLock)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryCacheAssemblyReference(asm);
            }

            // Snapshot while still holding the lock — Dictionary.Values is a
            // live view and would throw if mutated by a concurrent Compile()
            // call while the caller enumerates it outside this lock.
            return [.. _assemblyReferenceCache.Values];
        }
    }

    private void TryCacheAssemblyReference(Assembly asm)
    {
        if (asm == null || asm.IsDynamic)
        {
            return;
        }

        string location;
        try
        {
            location = asm.Location;
        }
        catch
        {
            return;
        }

        // Dynamically-loaded assemblies (including our own hot-reloaded user
        // script assemblies via Assembly.Load(byte[])) report an empty
        // Location and are never resolvable as file-based references anyway.
        if (string.IsNullOrWhiteSpace(location))
        {
            return;
        }

        if (_assemblyReferenceCache.ContainsKey(location))
        {
            return;
        }

        string assemblyName;
        try
        {
            assemblyName = asm.GetName().Name ?? string.Empty;
        }
        catch
        {
            return;
        }

        if (assemblyName.StartsWith(HotReloadAssemblyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _assemblyReferenceCache[location] = MetadataReference.CreateFromFile(location);
    }
}