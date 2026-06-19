using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace RDETerminal.Scripting.HotReload;
public sealed class UserScriptWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly UserScriptCatalog _catalog;

    private readonly object _lock = new();

    private System.Threading.Timer _debounceTimer;
    private const int DebounceMs = 400;

    private readonly HashSet<string> _pendingChanges = [];
    public event Action OnReloadRequested;

    public UserScriptWatcher(string rootDirectory, UserScriptCatalog catalog)
    {
        _catalog = catalog;

        if (!Directory.Exists(rootDirectory))
        {
            Directory.CreateDirectory(rootDirectory);
        }

        _watcher = new FileSystemWatcher(rootDirectory)
        {
            IncludeSubdirectories = true,
            Filter = "*.cs",
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime |
                NotifyFilters.Size
        };

        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;

        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add(e.FullPath);
            ScheduleDebounce();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add(e.OldFullPath);
            _pendingChanges.Add(e.FullPath);
            ScheduleDebounce();
        }
    }

    private void ScheduleDebounce()
    {
        _debounceTimer?.Dispose();

        _debounceTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                TriggerReload();
            }
            catch
            {
                
            }
        }, null, DebounceMs, Timeout.Infinite);
    }

    private void TriggerReload()
    {
        List<string> changedFiles;

        lock (_lock)
        {
            if (_pendingChanges.Count == 0)
                return;

            changedFiles = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        _catalog.ApplyChanges(changedFiles);

        OnReloadRequested?.Invoke();
    }
    public void ForceReload()
    {
        _catalog.Refresh();
        OnReloadRequested?.Invoke();
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
    }
}