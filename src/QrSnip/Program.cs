using System;
using System.Threading;
using System.Windows;

namespace QrSnip;

// Composition root. WPF would normally auto-emit a Main from App.xaml's
// ApplicationDefinition, but we disabled that in the csproj so we can wire
// services ourselves and control the app lifetime explicitly.
internal static class Program
{
    // Per-user single-instance check. We never acquire the mutex; we only use
    // its named existence as a flag. The kernel object disappears when our
    // handle closes, so a clean exit lets a relaunch see createdNew=true.
    private const string SingleInstanceMutexName = "QrSnip.SingleInstance.4f7b2e";

    // A named auto-reset event the first instance waits on. A second instance
    // launches, opens this same name, sets the event, and exits. The first
    // instance's wait thread observes the signal and dispatches to ShowSettings.
    // Named EventWaitHandle is the lightest mechanism that delivers a one-way
    // "wake up" signal without rolling a named pipe or hidden message window.
    private const string ShowSettingsEventName = "QrSnip.ShowSettings.4f7b2e";

    [STAThread]
    private static int Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: false, name: SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return SignalRunningInstance();
        }

        Diagnostics.Log("--- QrSnip starting ---");

        var app = new App
        {
            // No main window. The app lives in the tray and only shuts down when
            // the user picks Quit (or a future code path calls Application.Shutdown).
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        using var showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        using var cts = new CancellationTokenSource();
        var waitThread = StartShowSettingsWaitThread(app, showSettingsEvent, cts.Token);
        app.Exit += (_, _) =>
        {
            cts.Cancel();
            showSettingsEvent.Set(); // unblock the wait thread so it can observe cancellation
        };

        try
        {
            return app.Run();
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("App.Run", ex);
            throw;
        }
        finally
        {
            Diagnostics.Log("--- QrSnip exiting ---");
            waitThread.Join(TimeSpan.FromSeconds(1));
        }
    }

    private static int SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var ev))
            {
                using (ev)
                {
                    ev.Set();
                }
                Console.Error.WriteLine("QR Snapper is already running; surfaced settings on the existing instance.");
                return 0;
            }
        }
        catch
        {
            // Fall through to silent exit if signaling fails for any reason
            // (permissions, race with first-instance shutdown, etc.).
        }
        Console.Error.WriteLine("QR Snapper is already running.");
        return 0;
    }

    private static Thread StartShowSettingsWaitThread(App app, EventWaitHandle ev, CancellationToken ct)
    {
        var t = new Thread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ev.WaitOne();
                    if (ct.IsCancellationRequested) return;
                    // Marshal back to the UI thread before touching tray UI.
                    app.Dispatcher.BeginInvoke((Action)(() => app.Tray?.ShowSettings()));
                }
                catch (Exception ex)
                {
                    Diagnostics.LogException("ShowSettingsWaitThread", ex);
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "QrSnip.ShowSettingsWait",
        };
        t.Start();
        return t;
    }
}
