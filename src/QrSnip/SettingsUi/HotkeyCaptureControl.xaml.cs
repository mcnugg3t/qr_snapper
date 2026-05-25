using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QrSnip.Hotkey;
// Disambiguate: 'Hotkey' is also the name of the QrSnip.Hotkey namespace.
using HotkeyValue = QrSnip.Hotkey.Hotkey;

namespace QrSnip.SettingsUi;

// A "press your shortcut" control. Shows the current binding as text;
// click to enter capture mode; press a key combo to record it.
//
// Validation happens inline: the candidate hotkey is attempted via
// IHotkeyListener.TryRegister and immediately unregistered. If registration
// fails with AlreadyInUse, the control shows an error and does NOT accept
// the binding. The caller (SettingsWindow) provides the listener so the
// real production hotkey isn't disturbed during capture — see the
// constructor comment.
//
// Capture flow:
//   1. User clicks the control → enters capture mode (BorderBrush turns
//      blue, DisplayText shows "Press a key combo...").
//   2. User presses Ctrl/Alt/Shift/Win + a non-modifier key → captured.
//      Modifier-only presses are ignored (you can't bind "just Shift").
//   3. Successful capture → BindingChanged event fires with the new Hotkey;
//      control leaves capture mode.
//   4. ESC at any time → cancel capture, restore previous binding display.
public partial class HotkeyCaptureControl : UserControl
{
    private IHotkeyListener? _validator;
    private HotkeyValue? _currentBinding;
    private bool _isCapturing;

    public HotkeyCaptureControl()
    {
        InitializeComponent();
        // Click to enter capture mode. Using PreviewMouseDown so we get the
        // event before any default handling.
        PreviewMouseLeftButtonDown += (_, _) => EnterCaptureMode();
        // KeyDown only fires when we're focused, which only happens after
        // EnterCaptureMode calls Focus().
        KeyDown += OnKeyDown;
        LostFocus += (_, _) => ExitCaptureMode(committed: false);
    }

    // Fires when the user successfully captures a new combination.
    public event EventHandler<HotkeyValue>? BindingChanged;

    // Set by the SettingsWindow to a listener it uses ONLY for validation
    // (TryRegister + immediate Unregister). The production listener stays
    // unaffected so the user doesn't lose their active binding mid-capture.
    public void SetValidator(IHotkeyListener validator) => _validator = validator;

    public void SetBinding(HotkeyValue? binding)
    {
        _currentBinding = binding;
        DisplayText.Text = binding?.Display ?? "(none)";
    }

    private void EnterCaptureMode()
    {
        _isCapturing = true;
        Focus();
        DisplayText.Text = "Press a key combo... (Esc to cancel)";
        CaptureBorder.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void ExitCaptureMode(bool committed)
    {
        _isCapturing = false;
        CaptureBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
        if (!committed)
        {
            // Restore the prior binding's display.
            DisplayText.Text = _currentBinding?.Display ?? "(none)";
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturing) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            ExitCaptureMode(committed: false);
            return;
        }

        // Reject pure modifier presses — you can't bind "just Shift".
        if (IsModifierOnly(e.Key)) return;

        var modifiers = BuildModifiers();
        if (modifiers == KeyModifiers.None) return; // require at least one modifier

        // Use KeyInterop to convert WPF Key → Win32 VK code.
        var vk = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
        var candidate = new HotkeyValue(modifiers, vk);

        if (_validator is null)
        {
            // No validator was wired — accept blindly. Shouldn't happen in
            // production (SettingsWindow always sets one).
            CommitBinding(candidate);
            return;
        }

        // Try-and-immediately-unregister so we don't steal the user's
        // active hotkey for the rest of the session.
        var result = _validator.TryRegister(candidate);
        if (result == HotkeyRegistrationResult.Success)
        {
            _validator.Unregister();
            CommitBinding(candidate);
        }
        else
        {
            var msg = result == HotkeyRegistrationResult.AlreadyInUse
                ? $"'{candidate.Display}' is in use by another program."
                : $"Couldn't register '{candidate.Display}' (system error).";
            StatusText.Text = msg;
            StatusText.Visibility = Visibility.Visible;
            DisplayText.Text = candidate.Display + "  (unavailable)";
            // Stay in capture mode so the user can press another combo.
        }
    }

    private void CommitBinding(HotkeyValue binding)
    {
        _currentBinding = binding;
        DisplayText.Text = binding.Display;
        StatusText.Visibility = Visibility.Collapsed;
        ExitCaptureMode(committed: true);
        BindingChanged?.Invoke(this, binding);
    }

    private static bool IsModifierOnly(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl
           or Key.LeftAlt or Key.RightAlt
           or Key.LeftShift or Key.RightShift
           or Key.LWin or Key.RWin
           or Key.System;  // Alt comes through as Key.System sometimes

    private static KeyModifiers BuildModifiers()
    {
        var m = KeyModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) m |= KeyModifiers.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt)     != 0) m |= KeyModifiers.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) m |= KeyModifiers.Shift;
        // ModifierKeys.Windows is documented but Keyboard.Modifiers never sets
        // it — the Win key isn't tracked in WPF's modifier flags. Have to
        // check Win key state explicitly via Keyboard.IsKeyDown.
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= KeyModifiers.Win;
        return m;
    }
}
