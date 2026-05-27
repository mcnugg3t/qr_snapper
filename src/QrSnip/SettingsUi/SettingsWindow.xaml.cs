using System;
using System.Windows;
using System.Windows.Controls;
using QrSnip.Hotkey;
using QrSnip.Settings;
// Disambiguate: 'Hotkey' is the namespace; HotkeyValue is the type.
using HotkeyValue = QrSnip.Hotkey.Hotkey;

namespace QrSnip.SettingsUi;

// The single settings window. Three controls: hotkey, auto-start, debug mode.
// Changes are saved live (each control updates SettingsService immediately on
// change) so there's no Save / Cancel choice — Close is the only action.
//
// Why live-save: matches Windows app convention (modern Settings dialogs work
// this way). Avoids the question "did I forget to save?" Avoids needing a
// dirty-tracking layer.
//
// Hotkey rebinding has an interesting interaction with the live RegisterHotKey
// listener: when the user accepts a new binding, we unregister the old one
// and register the new one with the SAME listener instance. SettingsWindow
// receives the production listener via the constructor.
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly IHotkeyListener _productionHotkeyListener;
    private readonly AutoStartService _autoStart;

    public SettingsWindow(
        SettingsService settings,
        IHotkeyListener productionHotkeyListener,
        AutoStartService autoStart)
    {
        InitializeComponent();
        _settings = settings;
        _productionHotkeyListener = productionHotkeyListener;
        _autoStart = autoStart;

        // Use a SEPARATE listener for hotkey-capture validation so the user's
        // active hotkey isn't disturbed while they're trying out new combos.
        var validator = new RegisterHotKeyListener();
        Closed += (_, _) => validator.Dispose();

        HotkeyControl.SetValidator(validator);
        HotkeyControl.SetBinding(_settings.Current.Hotkey);
        HotkeyControl.BindingChanged += OnHotkeyChanged;

        AutoStartCheck.IsChecked = _settings.Current.AutoStartEnabled;
        AutoStartCheck.Checked   += (_, _) => OnAutoStartChanged(true);
        AutoStartCheck.Unchecked += (_, _) => OnAutoStartChanged(false);

        AutoPasteCheck.IsChecked = _settings.Current.AutoPasteEnabled;
        AutoPasteCheck.Checked   += (_, _) => OnAutoPasteChanged(true);
        AutoPasteCheck.Unchecked += (_, _) => OnAutoPasteChanged(false);

        // ComboBox uses Tag strings ("None"/"Tab"/"Enter") that map 1:1 to
        // the AutoPasteAppendKey enum names. Select the item whose Tag
        // matches the persisted setting.
        var currentAppendName = _settings.Current.AutoPasteAppendKey.ToString();
        foreach (ComboBoxItem item in AppendKeyCombo.Items)
        {
            if ((string)item.Tag == currentAppendName)
            {
                AppendKeyCombo.SelectedItem = item;
                break;
            }
        }
        AppendKeyCombo.SelectionChanged += OnAppendKeyChanged;

        ShowToastsCheck.IsChecked = _settings.Current.ShowToastsOnSuccess;
        ShowToastsCheck.Checked   += (_, _) => OnShowToastsChanged(true);
        ShowToastsCheck.Unchecked += (_, _) => OnShowToastsChanged(false);

        DebugModeCheck.IsChecked = _settings.Current.DebugMode;
        DebugModeCheck.Checked   += (_, _) => OnDebugModeChanged(true);
        DebugModeCheck.Unchecked += (_, _) => OnDebugModeChanged(false);

        CloseButton.Click += (_, _) => Close();
    }

    private void OnHotkeyChanged(object? sender, HotkeyValue newHotkey)
    {
        // Save + re-register against the PRODUCTION listener.
        _productionHotkeyListener.Unregister();
        var result = _productionHotkeyListener.TryRegister(newHotkey);
        if (result != HotkeyRegistrationResult.Success)
        {
            // Theoretically unreachable — capture already validated this
            // hotkey works. But the OS state could have changed between
            // capture and apply, so handle it gracefully.
            MessageBox.Show(
                this,
                $"Couldn't apply hotkey '{newHotkey.Display}': another program may have claimed it just now.",
                "QR Snapper",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // Restore the previous hotkey.
            if (_settings.Current.Hotkey is { } previous)
            {
                _productionHotkeyListener.TryRegister(previous);
                HotkeyControl.SetBinding(previous);
            }
            return;
        }
        _settings.Save(_settings.Current with { Hotkey = newHotkey });
    }

    private void OnAutoStartChanged(bool enabled)
    {
        _autoStart.Set(enabled);
        _settings.Save(_settings.Current with { AutoStartEnabled = enabled });
    }

    private void OnAutoPasteChanged(bool enabled)
    {
        _settings.Save(_settings.Current with { AutoPasteEnabled = enabled });
    }

    private void OnAppendKeyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AppendKeyCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AutoPasteAppendKey>((string)item.Tag, out var key))
        {
            _settings.Save(_settings.Current with { AutoPasteAppendKey = key });
        }
    }

    private void OnShowToastsChanged(bool enabled)
    {
        _settings.Save(_settings.Current with { ShowToastsOnSuccess = enabled });
    }

    private void OnDebugModeChanged(bool enabled)
    {
        _settings.Save(_settings.Current with { DebugMode = enabled });
    }
}
