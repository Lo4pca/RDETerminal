using System.Linq;
using Microsoft.CodeAnalysis.Scripting;
using RDLevelEditor;
using RDETerminal.Domain;
using RDETerminal.Domain.Core;

namespace RDETerminal.Scripting;

public static class ScriptImports
{
    public static ScriptOptions Create()
    {
        return ScriptOptions.Default
            .WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(EventSet).Assembly,
                typeof(LevelDocument).Assembly,
                typeof(ScriptGlobals).Assembly,
                typeof(Tab).Assembly,
                typeof(BarAndBeat).Assembly)
            .WithImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "RDLevelEditor",
                "RDETerminal.Domain",
                "RDETerminal.Domain.Core",
                "RDETerminal.Domain.Queries",
                "RDETerminal.Domain.Transforms.Common",
                "RDETerminal.Domain.Transforms.Text",
                "RDETerminal.Scripting");
    }
}