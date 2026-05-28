using System.Collections.Generic;
using RDETerminal.Notebook;

namespace RDETerminal.Domain;

public sealed class VarApi(NotebookSession session)
{
    private readonly NotebookSession _session = session;

    /// <summary>Sets or replaces a named variable in the session.</summary>
    public void Set(string name, object value)
    {
        _session.Set(name, value);
    }

    /// <summary>Tries to retrieve a variable by name.</summary>
    public bool TryGet(string name, out object value)
    {
        return _session.TryGet(name, out value);
    }

    /// <summary>
    /// Returns a variable cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> when the name is not found or the value is null.
    /// </summary>
    public T Get<T>(string name)
    {
        return _session.Get<T>(name);
    }

    /// <summary>Names of all currently stored variables.</summary>
    public IEnumerable<string> Names => _session.VariableNames;
}