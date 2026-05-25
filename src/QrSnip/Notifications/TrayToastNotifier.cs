using System;
using System.Windows;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace QrSnip.Notifications;

// IToastNotifier backed by H.NotifyIcon's TaskbarIcon.ShowNotification.
// Renders as a Windows toast on Win10/11 (Action Center notification) or
// a tray balloon on older versions — H.NotifyIcon picks automatically.
//
// Marshals to the WPF dispatcher because ShowNotification touches the
// HwndSource the tray icon owns, which has thread-affinity to the UI
// thread. Callers can fire from any thread.
internal sealed class TrayToastNotifier : IToastNotifier
{
    private readonly TaskbarIcon _taskbar;

    public TrayToastNotifier(TaskbarIcon taskbar)
    {
        _taskbar = taskbar;
    }

    public void Show(string title, string message, ToastSeverity severity)
    {
        var icon = severity switch
        {
            ToastSeverity.Warning => NotificationIcon.Warning,
            ToastSeverity.Error   => NotificationIcon.Error,
            _                     => NotificationIcon.Info,
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // No WPF context — log and drop. Shouldn't happen in production.
            Diagnostics.Log($"TrayToastNotifier: no Dispatcher, dropping toast '{title}'");
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                _taskbar.ShowNotification(title: title, message: message, icon: icon);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("TrayToastNotifier.Show", ex);
            }
        });
    }
}
