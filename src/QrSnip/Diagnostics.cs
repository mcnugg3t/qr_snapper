using System;
using System.IO;

namespace QrSnip;

// Minimal startup diagnostics. Writes to %LOCALAPPDATA%\QrSnip\startup.log
// so first-launch failures on teammate machines leave a trail we can ask for.
//
// Two levels:
//   - Log / LogException: ALWAYS write. Reserve for errors and unrecoverable
//     events. Show up in logs on every machine regardless of settings.
//   - LogVerbose: writes only when SettingsService says DebugMode = true.
//     Used for tracing capture/decode pipelines when debugging. Off by default
//     so teammate machines don't accumulate noise.
//
// Intentionally not a logging framework: a single append-only text file is
// enough for now, and "earn the abstraction" says we don't introduce
// Serilog/Microsoft.Extensions.Logging until we have a real need.
internal static class Diagnostics
{
    // Wired by Program.cs after SettingsService is constructed. Until then,
    // verbose calls no-op (safe default during early startup).
    private static Func<bool> s_isVerboseEnabled = () => false;

    public static void SetVerboseGate(Func<bool> gate) => s_isVerboseEnabled = gate;

    private static readonly Lazy<string> s_logPath = new(() =>
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QrSnip");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "startup.log");
    });

    public static void LogVerbose(string message)
    {
        if (s_isVerboseEnabled()) Log(message);
    }

    public static void Log(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
            File.AppendAllText(s_logPath.Value, line);
        }
        catch
        {
            // Diagnostics must never crash the app. If we can't even log, we're
            // running in a sandboxed context where logging is denied; that
            // information is itself unrecoverable and not worth a fallback.
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"[{context}] {ex.GetType().Name}: {ex.Message}");
        Log($"  {ex.StackTrace?.Replace("\n", "\n  ")}");
    }
}
