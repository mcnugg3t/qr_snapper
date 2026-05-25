using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace QrSnip.Settings;

// Owns the lifecycle of the persisted Settings: loads on construction,
// watches the file for external edits (e.g., until Stage 6 ships a UI,
// users edit config.json directly), and raises Changed when the in-memory
// snapshot updates.
//
// Concrete class, no interface. Single implementation, directly testable
// (point at a temp directory), no swap planned. Per the abstraction filter,
// don't pre-extract.
//
// Thread-safety: Current returns an immutable snapshot. The FileSystemWatcher
// callback runs on a thread-pool thread; we use Interlocked.Exchange to
// publish the new snapshot atomically. Consumers always see a consistent
// Settings instance even mid-reload.
public sealed class SettingsService : IDisposable
{
    private readonly string _configPath;
    private readonly FileSystemWatcher? _watcher;
    private Settings _current;

    // Raised after a reload picks up an external change. Handlers run on a
    // ThreadPool thread; marshal to the UI thread before touching WPF state.
    public event EventHandler? Changed;

    public Settings Current => Volatile.Read(ref _current!);

    // Persists the new snapshot to disk and updates Current. Does NOT raise
    // Changed (callers that call Save already know what changed; Changed is
    // for EXTERNAL edits picked up by the FileSystemWatcher).
    //
    // Note: writing the file may trigger our own FileSystemWatcher, which
    // would reload + compare + see no change and skip raising Changed. So
    // there's a brief redundant disk read on every Save. Acceptable.
    public void Save(Settings updated)
    {
        Interlocked.Exchange(ref _current!, updated);
        WriteToDisk(updated);
    }

    public SettingsService() : this(DefaultConfigPath()) { }

    public SettingsService(string configPath)
    {
        _configPath = configPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        _current = LoadOrCreateDefault();

        // Watch the file for external edits. Until Stage 6 ships a settings
        // UI, this IS the user's editing interface — open the JSON in
        // Notepad, save, the running app picks up the change.
        try
        {
            var dir = Path.GetDirectoryName(_configPath)!;
            var name = Path.GetFileName(_configPath);
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("SettingsService.Watch", ex);
            // App keeps running with the loaded settings; user just won't
            // get hot-reload until they restart.
        }
    }

    private static string DefaultConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QrSnip",
        "config.json");

    private Settings LoadOrCreateDefault()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = new Settings();
            WriteToDisk(defaults);
            return defaults;
        }
        return ReadFromDisk() ?? new Settings();
    }

    private Settings? ReadFromDisk()
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<Settings>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("SettingsService.ReadFromDisk", ex);
            return null;
        }
    }

    private void WriteToDisk(Settings s)
    {
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(s, JsonOptions));
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("SettingsService.WriteToDisk", ex);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher can fire multiple events for a single save
        // (some editors write + rename + touch); a small debounce + retry
        // handles that. Sleeping 100ms is enough for Notepad-like editors.
        Thread.Sleep(100);
        var reloaded = ReadFromDisk();
        if (reloaded is null) return;

        var prev = Interlocked.Exchange(ref _current!, reloaded);
        if (!prev.Equals(reloaded))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
