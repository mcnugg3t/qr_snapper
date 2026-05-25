namespace QrSnip.Notifications;

// The toast/balloon notification seam.
//
// Earns its interface on rule 2 (untestable without one — the real impl
// touches H.NotifyIcon's TaskbarIcon which can't be exercised cleanly from
// tests) and rule 1 (likely to swap — Stage 7 starts with H.NotifyIcon
// balloons, but if/when we add MSIX-identity AppNotifications for richer
// click handling, we swap implementations without touching consumers).
//
// Severity gives the OS a hint about how to render — Info gets a plain
// icon, Warning gets a yellow triangle, Error gets red. We use:
//   - Info for "snipped and copied" success notifications.
//   - Error for "clipboard write failed" notifications.
public interface IToastNotifier
{
    void Show(string title, string message, ToastSeverity severity);
}

public enum ToastSeverity { Info, Warning, Error }
