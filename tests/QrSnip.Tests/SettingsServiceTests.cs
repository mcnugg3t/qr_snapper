using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using QrSnip.Settings;
using Xunit;

namespace QrSnip.Tests;

// Tests SettingsService against a temp directory so we don't touch the real
// %APPDATA%\QrSnip\config.json that the dev's own running app may use.
//
// Hot-reload is tested by writing the file directly and asserting that
// SettingsService picks it up. We give the FileSystemWatcher a generous
// window (1s) because the watcher debouncing is platform-dependent.
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QrSnipTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Constructing_with_no_existing_file_creates_one_with_defaults()
    {
        Assert.False(File.Exists(_configPath));

        using var svc = new SettingsService(_configPath);

        Assert.True(File.Exists(_configPath));
        Assert.False(svc.Current.DebugMode);
    }

    [Fact]
    public void Existing_file_is_loaded_on_construction()
    {
        File.WriteAllText(_configPath, "{ \"debugMode\": true }");

        using var svc = new SettingsService(_configPath);

        Assert.True(svc.Current.DebugMode);
    }

    [Fact]
    public void Malformed_file_falls_back_to_defaults_without_throwing()
    {
        File.WriteAllText(_configPath, "this is not json");

        using var svc = new SettingsService(_configPath);

        Assert.False(svc.Current.DebugMode);
    }

    [Fact]
    public void External_edit_to_file_raises_Changed_and_updates_Current()
    {
        using var svc = new SettingsService(_configPath);
        Assert.False(svc.Current.DebugMode);

        var changedFired = new ManualResetEventSlim(false);
        svc.Changed += (_, _) => changedFired.Set();

        // Simulate the user opening the file in Notepad and flipping debugMode.
        File.WriteAllText(_configPath, "{ \"debugMode\": true }");

        var fired = changedFired.Wait(TimeSpan.FromSeconds(2));
        Assert.True(fired, "SettingsService.Changed did not fire within 2s of file edit");
        Assert.True(svc.Current.DebugMode);
    }
}
