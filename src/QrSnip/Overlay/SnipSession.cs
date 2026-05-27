using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using QrSnip.Capture;
using QrSnip.Clipboard;
using QrSnip.Decoding;
using QrSnip.Interop;
using QrSnip.Notifications;
using QrSnip.Settings;

namespace QrSnip.Overlay;

// Orchestrates the snip workflow: capture → spawn overlays → wait for user
// to drag-select OR cancel → decode the crop → copy to clipboard → optionally
// auto-paste into the previously-focused window → tear down overlays.
//
// One SnipSession per hotkey press. Stateful, single-use; calling RunAsync
// twice on the same instance is an error.
//
// Why a separate orchestrator instead of code inside OverlayWindow:
// - OverlayWindow is one-per-monitor and shouldn't know about its siblings.
// - The "if any window completes or cancels, close all" coordination needs
//   a single owner.
// - Decode + clipboard + auto-paste are app-level concerns; the overlay
//   just emits pixels.
internal sealed class SnipSession
{
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(180);

    private readonly IScreenCapture _capture;
    private readonly IQrDecoder _decoder;
    private readonly IClipboardService _clipboard;
    private readonly AutoPasteService _autoPaste;
    private readonly IToastNotifier _toast;
    private readonly bool _autoPasteEnabled;
    private readonly AutoPasteAppendKey _autoPasteAppendKey;
    private readonly bool _showToastsOnSuccess;
    private readonly IntPtr _autoPasteTarget;

    // Settings are snapshotted at session-creation time, NOT read live, so
    // toggling the setting mid-snip doesn't change behavior.
    // The target HWND is captured by App.xaml.cs BEFORE the overlay opens
    // (otherwise we'd record our own overlay window).
    public SnipSession(
        IScreenCapture capture,
        IQrDecoder decoder,
        IClipboardService clipboard,
        AutoPasteService autoPaste,
        IToastNotifier toast,
        bool autoPasteEnabled,
        AutoPasteAppendKey autoPasteAppendKey,
        bool showToastsOnSuccess,
        IntPtr autoPasteTarget)
    {
        _capture = capture;
        _decoder = decoder;
        _clipboard = clipboard;
        _autoPaste = autoPaste;
        _toast = toast;
        _autoPasteEnabled = autoPasteEnabled;
        _autoPasteAppendKey = autoPasteAppendKey;
        _showToastsOnSuccess = showToastsOnSuccess;
        _autoPasteTarget = autoPasteTarget;
    }

    public async Task RunAsync()
    {
        Diagnostics.LogVerbose("SnipSession: capturing monitors");
        var monitors = await _capture.CaptureAllMonitorsAsync();
        if (monitors.Length == 0)
        {
            Diagnostics.Log("SnipSession: no monitors captured, aborting");
            return;
        }

        var overlays = new List<OverlayWindow>(monitors.Length);
        var tcs = new TaskCompletionSource<SelectionCompletedEventArgs?>();

        foreach (var monitor in monitors)
        {
            var overlay = new OverlayWindow(monitor);
            overlay.Completed += (_, args) => tcs.TrySetResult(args);
            overlay.Cancelled += (_, _) => tcs.TrySetResult(null);
            overlays.Add(overlay);
            overlay.Show();
        }

        Diagnostics.LogVerbose($"SnipSession: {overlays.Count} overlay(s) shown, awaiting selection");
        var completion = await tcs.Task;

        if (completion is null)
        {
            Diagnostics.LogVerbose("SnipSession: cancelled");
            CloseAll(overlays);
            return;
        }

        // Run the decoder on the cropped pixels. Decoders are synchronous
        // (no async surface) so this completes inline.
        var results = _decoder.Decode(completion.Pixels, completion.Width, completion.Height, completion.Stride);
        Diagnostics.LogVerbose($"SnipSession: decode returned {results.Count} result(s)");

        if (results.Count == 0)
        {
            // Red flash on the selection, then close ALL overlays.
            completion.Source.FlashAndClose(Colors.Red, FlashDuration);
            CloseAllExcept(overlays, completion.Source);
            return;
        }

        // Join multiple payloads with newlines per the Stage 2 decision.
        // Multi-code picker UX is deferred per CLAUDE.md §8 / your earlier
        // answer; this is the v1 behavior.
        var clipboardText = string.Join("\n", results.Select(r => r.Payload));
        var copied = await _clipboard.TrySetTextAsync(clipboardText);
        if (!copied)
        {
            Diagnostics.Log("SnipSession: clipboard write failed after retries");
            // Clipboard failure is a real error per the Stage 3 design.
            // Toast is force-shown EVEN IF user has toasts disabled —
            // silent clipboard failure would be the worst kind of bug
            // (user pastes stale content thinking it was the snip).
            _toast.Show(
                title: "QR Snapper",
                message: "Couldn't copy to clipboard — another program may be holding it. Try snipping again.",
                severity: ToastSeverity.Error);
        }
        else if (_showToastsOnSuccess)
        {
            // Truncate long payloads for the toast — full payload is on
            // the clipboard so the user has the complete value.
            var preview = clipboardText.Length > 100
                ? clipboardText.Substring(0, 100) + "…"
                : clipboardText;
            _toast.Show(
                title: results.Count == 1 ? "QR copied" : $"{results.Count} QRs copied",
                message: preview,
                severity: ToastSeverity.Info);
        }

        // Pick the flash color. Distinct color for auto-paste so the user
        // sees a different signal than a plain decoded-and-copied snip.
        var flashColor = Colors.LimeGreen;
        if (copied && _autoPasteEnabled)
        {
            // Close the OTHER overlays first so the target window isn't
            // obscured when we restore focus. The flashing overlay closes
            // itself via the FlashAndClose timer below.
            CloseAllExcept(overlays, completion.Source);

            // Kick off the paste in the background — we don't await it
            // because the flash timer is running concurrently and we want
            // the paste to start IMMEDIATELY rather than after the flash.
            _ = _autoPaste.PasteToWindowAsync(_autoPasteTarget, _autoPasteAppendKey);
            flashColor = Colors.DeepSkyBlue;
        }
        else
        {
            CloseAllExcept(overlays, completion.Source);
        }

        completion.Source.FlashAndClose(flashColor, FlashDuration);
    }

    private static void CloseAll(IEnumerable<OverlayWindow> overlays)
    {
        foreach (var w in overlays) w.Close();
    }

    private static void CloseAllExcept(IEnumerable<OverlayWindow> overlays, OverlayWindow keep)
    {
        // The "keep" window is closing itself via its flash timer; we close
        // the others immediately so they don't sit there during the flash.
        foreach (var w in overlays)
        {
            if (!ReferenceEquals(w, keep)) w.Close();
        }
    }
}
