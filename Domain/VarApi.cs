using System.Collections.Generic;
using RDETerminal.Notebook;

namespace RDETerminal.Domain;

public sealed class VarApi(NotebookSession session)
{
    private readonly NotebookSession _session = session;

    public void Set(string name, object value)
    {
        _session.Variables[name] = value;
    }

    public bool TryGet(string name, out object value)
    {
        return _session.Variables.TryGetValue(name, out value);
    }

    public T Get<T>(string name)
    {
        if (!_session.Variables.TryGetValue(name, out object value) || value == null)
        {
            return default;
        }

        if (value is T t)
        {
            return t;
        }

        return (T)System.Convert.ChangeType(value, typeof(T));
    }

    public IEnumerable<string> Names
    {
        get { return _session.Variables.Keys; }
    }
}