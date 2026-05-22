# CLAUDE.md

Guidance for Claude Code when working in this repository.

---

## 1. Project Overview

**Working name:** `QrSnip` (rename freely).

A lightweight Windows tray application. It sits idle in the background and
listens for a global hotkey (default **Win + Q**, user-reconfigurable). On
trigger it:

1. Captures the screen(s).
2. Shows a dimmed, full-virtual-desktop selection overlay (rubber-band box,
   like Win + Shift + S).
3. Decodes any QR codes inside the selected region.
4. Copies the decoded text to the clipboard.

It does **not** edit images or copy a bitmap. The deliverable is *text*.

**Non-goals (for now):** non-QR barcode formats beyond what comes free with the
decoder, OCR of plain text, history/UI beyond a settings window, cloud sync.

---

## 2. Tech Stack

| Concern              | Choice                                   | Rationale |
|----------------------|------------------------------------------|-----------|
| Language / runtime   | C# / **.NET 8**, `net8.0-windows10.0.19041.0` | WGC needs the Windows SDK projection; 19041 is a safe floor. |
| UI framework         | **WPF**                                  | Rubber-band overlay + settings window are far easier than raw Win32; per-monitor DPI v2 support is mature. |
| Tray icon            | **H.NotifyIcon** (WPF)                   | Avoids the WinForms `NotifyIcon` dependency drag. |
| Screen capture       | **Windows.Graphics.Capture** (WGC) via CsWinRT | Robust against HW-accelerated / HDR surfaces; the modern path. |
| D3D surface → bitmap | **Win2D** (`Microsoft.Graphics.Win2D`)   | `CanvasBitmap.CreateFromDirect3D11Surface` removes most staging-texture boilerplate. Fallback: Vortice.Windows. |
| QR decode            | **ZXing.Net**                            | De facto C# barcode library; supports multi-decode. |
| Config persistence   | `System.Text.Json` → `%APPDATA%\QrSnip\config.json` | No registry needed except the optional auto-start key. |

**Do not** add SharpDX — it is archived/dead. If raw D3D is unavoidable, use
**Vortice.Windows**.

---

## 3. Architecture

Single process, no main window. Components:

- **`App`** — tray icon, single-instance mutex, lifetime, settings window host.
- **`IHotkeyListener`** — abstraction with two implementations (see Problem 1):
  - `RegisterHotKeyListener` — Win32 `RegisterHotKey`, the simple path.
  - `LowLevelHookListener` — `WH_KEYBOARD_LL` chord detection + suppression.
- **`ScreenCaptureService`** — WGC; produces one frozen `CanvasBitmap` (or
  `WriteableBitmap`) per monitor.
- **`OverlayWindow`** — one borderless topmost window **per monitor**, each
  displaying that monitor's frozen frame under a semi-transparent dim layer.
  Owns the rubber-band selection and ESC/cancel handling.
- **`QrDecoder`** — wraps ZXing; takes a cropped bitmap, returns 0..n results.
- **`ClipboardService`** — STA + retry-safe clipboard writes.
- **`SettingsService`** — load/save config, hotkey re-binding at runtime.

### The "freeze-frame" flow (important — adopt this)

```
hotkey → capture ALL monitors into memory → show overlays displaying the
frozen frames → user draws box → crop from the in-memory bitmap → decode → copy
```

Capturing *first* and showing the frozen image, rather than dimming the live
desktop, eliminates three bug classes at once:

- No risk of decoding a grayed-out screen (the dim is just a layer painted on
  top of the frozen image).
- The crop is taken from the **exact** bitmap the user sees, so selection
  coordinates map 1:1 to pixels — no live-desktop coordinate drift.
- No race between "screen changed" and "user finished selecting."

This is effectively how Snipping Tool / ShareX behave. Treat it as the default
design, not an optimization.

---

## 4. Build / Run / Publish

```bash
dotnet restore
dotnet build
dotnet run --project src/QrSnip

# Release, self-contained, trimmed:
dotnet publish src/QrSnip -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true

# NativeAOT (see Problem 7 for caveats — verify CsWinRT + Win2D AOT support
# before committing to this):
dotnet publish src/QrSnip -c Release -r win-x64 -p:PublishAot=true
```

`<TargetFramework>` must carry the Windows SDK suffix or WGC types won't
resolve. Declare per-monitor-DPI-v2 awareness in `app.manifest`, **not** via
`SetProcessDpiAwarenessContext` at runtime (too late for WPF).

---

## 5. Conventions

- Nullable reference types **on**; warnings as errors in `Release`.
- All Win32 / WinRT interop isolated under `src/QrSnip/Interop/`. Never scatter
  `[DllImport]` through UI code.
- Async for capture and decode; the LL-hook callback path stays **synchronous
  and minimal** (see Problem 1).
- Coordinates: name every variable's space explicitly — `*_dip` vs `*_px`.
  Mixed-DPI coordinate bugs are the #1 expected defect. Never let an unlabeled
  `Rect` cross a method boundary.
- No telemetry, no network calls. The app reads the screen and writes the
  clipboard; keep that surface small and auditable.

---

## 6. Known Hard Problems & Hypotheses

Ranked by expected pain. Each lists hypotheses to try in order — cheap/likely
first.

### Problem 1 — Intercepting Win + Q (and arbitrary custom hotkeys)

Win + Q is **reserved by the shell** (opens Search). Two unknowns: (a) can we
even register it, (b) if not, can we suppress the Search UI.

- **H1 (try first):** Call `RegisterHotKey(MOD_WIN | MOD_NOREPEAT, 'Q')`.
  Some Win+key combos *can* be registered and will override the shell; some
  cannot. **This is empirically uncertain for Q specifically — test it on a
  real machine before designing around either outcome.** If it succeeds, we
  are done: no hook, no lone-Win problem, and custom non-reserved combos
  (e.g. Ctrl+Alt+5) work through the exact same path.
- **H2 (fallback):** `WH_KEYBOARD_LL` hook. Detect Win-down + Q-down, return
  `1` from the callback to swallow the Q event so Search never sees it.
- **H2-corollary — the "lone Win key" trap:** if you swallow Q, the OS sees an
  isolated Win press/release and opens the **Start menu** on Win-up. Fix:
  immediately `SendInput` a no-op keystroke (AutoHotkey injects `VK_CONTROL`
  down+up) so the Win press registers as part of a chord. Tag injected input
  via `dwExtraInfo` and skip it in your own hook, or you'll recurse.
- **H3 (architecture):** Ship *both*. `IHotkeyListener` with a factory: attempt
  `RegisterHotKey`; on failure (or for known-reserved combos) fall back to the
  hook. Custom user combos that are non-reserved naturally take the clean
  `RegisterHotKey` path.

**Callback discipline (H2/H3):** the LL-hook callback runs inline on the
message pump and is unhooked by Windows if slow. It must *only* update a small
key-state machine and post a message — capture/decode happens elsewhere.

**Side effect:** an LL keyboard hook + screen reads + clipboard writes is the
exact behavioral signature of malware. Expect AV/EDR flags. Hypotheses:
sign the binary with a code-signing certificate; prefer the `RegisterHotKey`
path when possible (no hook = no flag); document behavior for users.

### Problem 2 — Custom hotkey capture, validation, persistence

- **Capture:** a settings-window "press your shortcut" control that listens for
  keydown, records modifier flags + virtual-key, and renders a human label.
- **Validation hypotheses:** reject modifier-only combos; reject anything that
  fails to `RegisterHotKey` *and* isn't hook-suppressible; warn (don't block)
  on reserved combos like Win+L (un-overridable).
- **Persistence:** store `{ modifiers: int, vk: int }` in `config.json`. On
  change, unregister/unhook and rebind live — no restart.

### Problem 3 — `Windows.Graphics.Capture` from C#

- **Per-monitor item:** WGC has no "whole virtual desktop" capture item. You
  must create one `GraphicsCaptureItem` **per `HMONITOR`** via the interop
  interface `IGraphicsCaptureItemInterop.CreateForMonitor` — which is not
  surfaced by the projection. Hypothesis: QI the
  `GraphicsCaptureItem` activation factory for the interop IID (known
  boilerplate; isolate it in `Interop/`).
- **One-shot from a streaming API:** WGC is built for continuous capture. For a
  single frame: create a `Direct3D11CaptureFramePool`, start the session, take
  the **first** `FrameArrived` frame, then dispose everything. Expect first-
  frame latency of a few frames' worth — capture eagerly on hotkey, before the
  overlay is built.
- **The capture border:** WGC paints a colored capture indicator.
  `GraphicsCaptureSession.IsBorderRequired = false` removes it, but only on
  **Windows 11 (build 20348+)**. Hypotheses: set it inside a version guard and
  accept the border on Windows 10; or call
  `GraphicsCaptureAccess.RequestAccessAsync` if a packaged identity is added.
- **Surface → CPU bitmap:** the frame is an `IDirect3DSurface`. H1: hand it to
  Win2D `CanvasBitmap.CreateFromDirect3D11Surface` and read pixels / save from
  there. H2: manual D3D11 — copy to a staging texture, `Map`, read rows
  (handle row pitch ≠ width). Prefer H1.
- **Cursor:** set `IsCursorCaptureEnabled = false` so the pointer isn't baked
  into the frozen frame.

### Problem 4 — Multi-monitor + DPI

- **Virtual desktop geometry:** monitors can sit at negative coordinates and
  run different scale factors. The overlay must cover the full union.
- **DPI hypothesis (recommended):** *one borderless overlay window per
  monitor*, each parented to its display and DPI-isolated. Avoids a single
  window straddling mixed-DPI monitors, which produces stretched dim layers and
  off-by-N crop rectangles.
- **Coordinate mapping:** because of the freeze-frame design (Section 3), the
  selection rectangle is in the same image space as the captured bitmap *for
  that monitor*. Crop is then trivial. Still: label every coordinate `_dip` /
  `_px` and convert at exactly one boundary.

### Problem 5 — QR decoding robustness

ZXing decoding itself is easy; the failure modes are image-quality ones.

- **Multiple codes:** use `QRCodeMultiReader` /
  `GenericMultipleBarcodeReader`, not the single-result reader. Decide UX: copy
  all (newline-joined), or copy the first, or prompt.
- **Small / low-contrast codes:** set the `TRY_HARDER` hint; if a decode fails,
  retry with 2–3× nearest-neighbor upscaling and/or an alternate binarizer
  (`HybridBinarizer` vs `GlobalHistogramBinarizer`).
- **No code found:** explicit user feedback (tray balloon / brief toast), not
  silent failure or an empty clipboard write.

### Problem 6 — Clipboard reliability

- The clipboard is a single shared OS resource; `SetText` throws
  `CLIPBRD_E_CANT_OPEN` if another process holds it.
- **Hypothesis:** wrap writes in a short retry loop (e.g. 5 tries, ~80 ms
  backoff) on an STA thread. WPF `Clipboard.SetDataObject(text, copy: true)`
  with retry is more durable than a bare `SetText`.

### Problem 7 — Footprint, background behavior, startup

- Idle cost is essentially free: `RegisterHotKey` and LL hooks are both event-
  driven. The real baseline is the .NET runtime + a hidden message-pump window
  + tray icon.
- **NativeAOT** cuts startup and working set noticeably — **but verify CsWinRT
  and Win2D are AOT-clean on the chosen SDK before committing**; treat AOT as a
  later optimization, not a launch requirement.
- **Single instance:** named `Mutex`; second launch surfaces settings and exits.
- **Auto-start hypotheses:** `HKCU\...\Run` registry value (simple, no
  elevation); or a Scheduled Task (needed if the app runs elevated — see
  Problem 8).

### Problem 8 — Elevation / UIPI

A non-elevated LL hook does **not** receive keystrokes while an elevated window
has focus; a non-elevated overlay can't reliably sit above an elevated
fullscreen app. Trade-off, no free lunch:

- **H1:** run non-elevated. Simpler, clean auto-start, but the hotkey silently
  no-ops over elevated windows (Task Manager, some installers).
- **H2:** run elevated (manifest `requireAdministrator`). Works everywhere, but
  auto-start now needs a Scheduled Task with highest privileges, and the UAC
  story is worse.
- **Recommendation:** ship H1; expose elevation as an opt-in setting and
  document the limitation.

---

## 7. Testing Strategy

Hotkeys, hooks, and overlays resist unit testing — keep logic testable by
isolating it:

- **Unit:** key-chord state machine, coordinate `_dip`↔`_px` conversion, config
  serialization, ZXing wrapper against a fixture folder of QR images (clean,
  tiny, rotated, low-contrast, multi-code, none).
- **Manual integration checklist:** trigger over an elevated window; mixed-DPI
  dual monitor; monitor at negative coordinates; hotkey rebinding without
  restart; clipboard contention (hold it from another app); lone-Win-key does
  *not* open Start after a suppressed combo.
- Keep a `/test-assets/` folder of QR sample images committed.

---

## 8. Open Decisions / TODO

- [ ] **Empirically confirm** whether `RegisterHotKey` can claim Win+Q. This
      gates the entire Problem 1 architecture.
- [ ] Packaged (MSIX) vs unpackaged. Unpackaged is simpler; MSIX unlocks
      `GraphicsCaptureAccess` and cleaner install. Default: unpackaged.
- [ ] Multi-code UX: copy-all vs copy-first vs prompt.
- [ ] Whether to support non-QR formats ZXing already decodes for free.
- [ ] Code-signing certificate (mitigates Problem 1 AV flags).
