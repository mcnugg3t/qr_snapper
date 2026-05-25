using System;
using System.Collections.Generic;
using QrSnip.Hotkey;
using Xunit;
// Disambiguate: 'Hotkey' is also the name of the QrSnip.Hotkey namespace.
using HotkeyValue = QrSnip.Hotkey.Hotkey;

namespace QrSnip.Tests;

// Tests the fallback-chain decision logic in isolation. Uses a fake
// IHotkeyListener so we can simulate "this hotkey is taken, try the next"
// without actually claiming OS hotkeys.
public sealed class HotkeyFallbackChainTests
{
    private const uint VK_Q = 0x51;
    private static readonly HotkeyValue WinShiftQ  = new(KeyModifiers.Win     | KeyModifiers.Shift, VK_Q);
    private static readonly HotkeyValue CtrlAltQ   = new(KeyModifiers.Control | KeyModifiers.Alt,   VK_Q);
    private static readonly HotkeyValue CtrlShiftQ = new(KeyModifiers.Control | KeyModifiers.Shift, VK_Q);

    [Fact]
    public void Preferred_hotkey_registers_first_when_available()
    {
        var listener = new FakeListener(accept: _ => true);

        var result = HotkeyFallbackChain.RegisterFirstAvailable(listener, preferred: CtrlAltQ);

        Assert.Equal(CtrlAltQ, result);
        Assert.Equal(new[] { CtrlAltQ }, listener.Attempts);
    }

    [Fact]
    public void Falls_through_to_chain_when_preferred_is_taken()
    {
        // Pretend Win+Shift+Q is taken; Ctrl+Alt+Q works.
        var listener = new FakeListener(accept: h => !h.Equals(WinShiftQ));

        var result = HotkeyFallbackChain.RegisterFirstAvailable(listener, preferred: WinShiftQ);

        Assert.Equal(CtrlAltQ, result);
        Assert.Equal(new[] { WinShiftQ, CtrlAltQ }, listener.Attempts);
    }

    [Fact]
    public void Skips_preferred_in_defaults_to_avoid_double_attempting_it()
    {
        // Preferred is Win+Shift+Q (which is the first default). The chain
        // should attempt it once via the preferred path, then SKIP it in
        // defaults rather than re-trying.
        var listener = new FakeListener(accept: _ => false); // all fail

        HotkeyFallbackChain.RegisterFirstAvailable(listener, preferred: WinShiftQ);

        // Expect 3 attempts total: WinShiftQ (preferred), CtrlAltQ, CtrlShiftQ.
        // NOT 4 (with WinShiftQ appearing twice).
        Assert.Equal(new[] { WinShiftQ, CtrlAltQ, CtrlShiftQ }, listener.Attempts);
    }

    [Fact]
    public void Returns_null_when_every_candidate_fails()
    {
        var listener = new FakeListener(accept: _ => false);

        var result = HotkeyFallbackChain.RegisterFirstAvailable(listener, preferred: null);

        Assert.Null(result);
    }

    [Fact]
    public void Null_preferred_starts_with_first_default()
    {
        var listener = new FakeListener(accept: _ => true);

        var result = HotkeyFallbackChain.RegisterFirstAvailable(listener, preferred: null);

        Assert.Equal(WinShiftQ, result);
        Assert.Equal(new[] { WinShiftQ }, listener.Attempts);
    }

    private sealed class FakeListener : IHotkeyListener
    {
        private readonly Func<HotkeyValue, bool> _accept;
        public List<HotkeyValue> Attempts { get; } = new();
        public HotkeyValue? Current { get; private set; }
        public event EventHandler? HotkeyPressed { add { } remove { } }

        public FakeListener(Func<HotkeyValue, bool> accept) => _accept = accept;

        public HotkeyRegistrationResult TryRegister(HotkeyValue hotkey)
        {
            Attempts.Add(hotkey);
            if (!_accept(hotkey)) return HotkeyRegistrationResult.AlreadyInUse;
            Current = hotkey;
            return HotkeyRegistrationResult.Success;
        }

        public void Unregister() => Current = null;
        public void Dispose() { }
    }
}
