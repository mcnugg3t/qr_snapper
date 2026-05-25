using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HotkeyProbe;

// Throwaway probe for qr_snapper Stage 0.
// Answers: can we RegisterHotKey(MOD_WIN, 'Q') and actually receive WM_HOTKEY,
// or does the Search reservation win? Decides whether the IHotkeyListener
// abstraction in Stage 4 needs an LL-hook fallback.
internal static class Program
{
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint MOD_WIN      = 0x0008;
    private const uint MOD_CONTROL  = 0x0002;
    private const uint MOD_ALT      = 0x0001;
    private const uint MOD_SHIFT    = 0x0004;

    private const uint VK_Q = 0x51;

    private const int WM_HOTKEY = 0x0312;

    private record Candidate(int Id, uint Mods, uint Vk, string Label);

    private static readonly Candidate[] s_candidates =
    {
        new(1, MOD_WIN     | MOD_NOREPEAT,             VK_Q, "Win+Q"),
        new(2, MOD_WIN     | MOD_SHIFT  | MOD_NOREPEAT, VK_Q, "Win+Shift+Q"),
        new(3, MOD_CONTROL | MOD_ALT    | MOD_NOREPEAT, VK_Q, "Ctrl+Alt+Q"),
        new(4, MOD_CONTROL | MOD_SHIFT  | MOD_NOREPEAT, VK_Q, "Ctrl+Shift+Q"),
    };

    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);

    private const uint PM_REMOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public POINT  pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static void Main()
    {
        Console.WriteLine("qr_snapper Stage 0 hotkey probe");
        Console.WriteLine("================================");
        Console.WriteLine();

        var registered = new List<Candidate>();
        var failed = new List<(Candidate Candidate, int Error)>();
        var eventCounts = new Dictionary<int, int>();

        foreach (var c in s_candidates)
        {
            var ok = RegisterHotKey(IntPtr.Zero, c.Id, c.Mods, c.Vk);
            var err = Marshal.GetLastWin32Error();
            if (ok)
            {
                Console.WriteLine($"  [OK]   RegisterHotKey({c.Label,-14}) succeeded.");
                registered.Add(c);
                eventCounts[c.Id] = 0;
            }
            else
            {
                var reason = err == ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "already claimed by another process or the shell"
                    : $"Win32 error {err}";
                Console.WriteLine($"  [FAIL] RegisterHotKey({c.Label,-14}) {reason}");
                failed.Add((c, err));
            }
        }

        Console.WriteLine();

        if (registered.Count > 0)
        {
            Console.WriteLine($"Listening for hotkey events for 8 seconds.");
            Console.WriteLine("Press each registered hotkey to verify the OS routes it to us (not to another app).");
            Console.WriteLine();

            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(8))
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_HOTKEY)
                    {
                        var id = (int)(uint)msg.wParam;
                        eventCounts[id] = eventCounts.GetValueOrDefault(id) + 1;
                        var label = s_candidates.First(c => c.Id == id).Label;
                        Console.WriteLine($"  [{deadline.Elapsed.TotalSeconds:F1}s] WM_HOTKEY ({label})");
                    }
                }
                Thread.Sleep(20);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Results");
        Console.WriteLine("-------");
        foreach (var c in s_candidates)
        {
            var isReg = registered.Contains(c);
            var events = eventCounts.GetValueOrDefault(c.Id);
            var status = !isReg
                ? "BLOCKED  (another process owns it)"
                : events > 0
                    ? $"USABLE   (received {events} event{(events == 1 ? "" : "s")})"
                    : "UNVERIFIED (registered but not pressed during window)";
            Console.WriteLine($"  {c.Label,-14} : {status}");
        }
        Console.WriteLine();

        var firstUsable = s_candidates.FirstOrDefault(c => eventCounts.GetValueOrDefault(c.Id) > 0);
        if (firstUsable is not null)
        {
            Console.WriteLine($"VERDICT: Highest-preference usable hotkey: {firstUsable.Label}");
        }
        else
        {
            Console.WriteLine("VERDICT: No hotkey was both registered AND pressed during the test window.");
            Console.WriteLine("         Re-run and press each registered hotkey at least once.");
        }

        foreach (var c in registered)
            UnregisterHotKey(IntPtr.Zero, c.Id);
    }
}
