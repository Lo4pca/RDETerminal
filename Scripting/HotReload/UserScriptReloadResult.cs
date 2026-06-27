using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace RDETerminal.Scripting.HotReload;

/// <summary>
/// One hot-reload attempt and the data needed by runtime and completion.
/// </summary>
public sealed class UserScriptReloadResult
{
    public bool Success { get; init; }

    public Assembly Assembly { get; init; }

    public PortableExecutableReference MetadataReference { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; } = ImmutableArray<Diagnostic>.Empty;

    public ImmutableArray<string> SourceFiles { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> ExportedNamespaces { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> ExportedTypeNames { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> ExportedMemberNames { get; init; } = ImmutableArray<string>.Empty;

    public DateTime ReloadTimeUtc { get; init; } = DateTime.UtcNow;

    public string ErrorSummary =>
        string.Join(
            Environment.NewLine,
            Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

    public static UserScriptReloadResult Failed(
        IEnumerable<Diagnostic> diagnostics,
        IEnumerable<string> sourceFiles = null)
    {
        return new UserScriptReloadResult
        {
            Success = false,
            Diagnostics = diagnostics?.ToImmutableArray() ?? ImmutableArray<Diagnostic>.Empty,
            SourceFiles = sourceFiles?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
            ReloadTimeUtc = DateTime.UtcNow
        };
    }

    public static UserScriptReloadResult Succeeded(
        Assembly assembly,
        PortableExecutableReference metadataReference,
        IEnumerable<Diagnostic> diagnostics,
        IEnumerable<string> sourceFiles = null)
    {
        ImmutableArray<string> exportedTypeNames = ImmutableArray<string>.Empty;
        ImmutableArray<string> exportedNamespaces = ImmutableArray<string>.Empty;
        ImmutableArray<string> exportedMemberNames = ImmutableArray<string>.Empty;

        if (assembly != null)
        {
            var types = new List<string>();
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<string>();

            try
            {
                foreach (Type type in assembly.GetExportedTypes())
                {
                    if (type == null)
                    {
                        continue;
                    }

                    types.Add(type.FullName ?? type.Name);

                    if (!string.IsNullOrWhiteSpace(type.Namespace))
                    {
                        namespaces.Add(type.Namespace);
                    }

                    foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (member == null)
                        {
                            continue;
                        }

                        members.Add($"{member.DeclaringType?.FullName ?? member.DeclaringType?.Name}.{member.Name}");
                    }
                }
            }
            catch
            {
                // Keep best-effort summary only.
            }

            exportedTypeNames = types.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
            exportedNamespaces = namespaces.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
            exportedMemberNames = members.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
        }

        return new UserScriptReloadResult
        {
            Success = true,
            Assembly = assembly,
            MetadataReference = metadataReference,
            Diagnostics = diagnostics?.ToImmutableArray() ?? ImmutableArray<Diagnostic>.Empty,
            SourceFiles = sourceFiles?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
            ExportedTypeNames = exportedTypeNames,
            ExportedNamespaces = exportedNamespaces,
            ExportedMemberNames = exportedMemberNames,
            ReloadTimeUtc = DateTime.UtcNow
        };
    }
}
