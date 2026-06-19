using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RDETerminal.Scripting.HotReload;
public sealed class UserScriptCatalog
{
    public sealed class ScriptFile
    {
        public string Path { get; init; }
        public string Name { get; init; }
        public string SourceCode { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }
    }

    private readonly string _rootDirectory;
    private Dictionary<string, ScriptFile> _files = [];

    private readonly object _lock = new();

    public UserScriptCatalog(string rootDirectory)
    {
        _rootDirectory = rootDirectory;

        if (!Directory.Exists(_rootDirectory))
        {
            Directory.CreateDirectory(_rootDirectory);
        }

        Refresh();
    }
    public void Refresh()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, ScriptFile>();

            var files = Directory.GetFiles(
                _rootDirectory,
                "*.cs",
                SearchOption.AllDirectories
            );

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);

                    string code = File.ReadAllText(file);

                    var script = new ScriptFile
                    {
                        Path = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        SourceCode = code,
                        LastWriteTimeUtc = info.LastWriteTimeUtc
                    };

                    result[file] = script;
                }
                catch
                {
                    
                }
            }

            _files = result;
        }
    }
    public void ApplyChanges(IEnumerable<string> changedFiles)
    {
        lock (_lock)
        {
            foreach (var path in changedFiles)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        _files.Remove(path);
                        continue;
                    }

                    var info = new FileInfo(path);
                    var code = File.ReadAllText(path);

                    _files[path] = new ScriptFile
                    {
                        Path = path,
                        Name = Path.GetFileNameWithoutExtension(path),
                        SourceCode = code,
                        LastWriteTimeUtc = info.LastWriteTimeUtc
                    };
                }
                catch
                {
                    
                }
            }
        }
    }
    public IReadOnlyList<ScriptFile> GetSnapshot()
    {
        lock (_lock)
        {
            return [.. _files.Values.OrderBy(x => x.Path)];
        }
    }
    public IReadOnlyList<ScriptFile> GetChangedSince(DateTime utcTime)
    {
        lock (_lock)
        {
            return [.. _files.Values
                .Where(x => x.LastWriteTimeUtc > utcTime)
                .OrderBy(x => x.Path)];
        }
    }
    public bool HasAnyScripts()
    {
        lock (_lock)
        {
            return _files.Count > 0;
        }
    }
}