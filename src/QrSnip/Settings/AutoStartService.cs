using System;
using Microsoft.Win32;

namespace QrSnip.Settings;

// Manages the Windows "launch at login" behavior via the HKCU\Run registry
// key. No UAC required (HKCU is per-user, not machine-wide).
//
// This implementation is for the UNPACKAGED build path. Once Stage 7 ships
// MSIX packaging, the manifest will declare a StartupTask extension which
// the OS manages via Settings → Apps → Startup; the user can disable it
// there too. The two mechanisms aren't mutually exclusive — but for v1
// we only ship the registry-based path because MSIX isn't built yet.
//
// Why concrete class, no interface: single implementation, easy to test by
// pointing at a different registry key. Per the abstraction filter, don't
// pre-extract.
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // The value name under HKCU\Run. Convention: an app's product name.
    // If the user inspects the Startup tab in Task Manager, this is what
    // they'll see.
    private const string ValueName = "QR Snapper";

    private readonly string _executablePath;

    public AutoStartService(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(existing);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("AutoStartService.IsEnabled", ex);
            return false;
        }
    }

    // Adds or removes the HKCU\Run entry. Idempotent: calling Set(true)
    // when already enabled just overwrites the path (useful if the EXE
    // moved). Set(false) is a no-op if not enabled.
    public void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                Diagnostics.Log("AutoStartService: HKCU\\Run key not available");
                return;
            }
            if (enabled)
            {
                // Quote the path so Windows handles spaces in the install
                // directory correctly.
                key.SetValue(ValueName, $"\"{_executablePath}\"");
                Diagnostics.LogVerbose($"AutoStart enabled at {_executablePath}");
            }
            else
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName);
                    Diagnostics.LogVerbose("AutoStart disabled");
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("AutoStartService.Set", ex);
        }
    }
}
