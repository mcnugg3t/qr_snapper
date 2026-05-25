# QR Snapper

<img src="src/QrSnip/Assets/readme_img.png" alt="QR Snapper logo — a snapping turtle with a QR-patterned shell" width="400" />

A tiny Windows tray app that snips QR codes off your screen.

Press a hotkey, drag a box around any QR code, and the decoded text lands on your clipboard — ready to paste anywhere.

---

## Install

1. **Download** `QRSnapperSetup.exe` from the [latest release](https://github.com/mcnugg3t/qr_snapper/releases/latest).
2. **Double-click** `QRSnapperSetup.exe` to start the installer.
3. Windows may show a blue **"Windows protected your PC"** screen. Click **More info** → **Run anyway**. (This is normal for apps without a paid signing certificate.)
4. **Click through the installer.** Default settings are fine.
5. **Open QR Snapper from your Start menu** when install finishes. The tray icon — a tiny snapping turtle — should appear near your clock.

That's it. The default snip hotkey is **Win + Shift + Q**.

> **If the installer opens itself a second time during install:** just let the second one finish — that's the one that actually completes the install. (Known quirk on machines with active antivirus.)

## Use

1. Press **Win + Shift + Q** (or whatever hotkey you set).
2. Your screen freezes and dims. Your cursor becomes a crosshair.
3. **Drag a rectangle** around the QR code.
4. Release. A green flash means it worked — the decoded text is now on your clipboard. **Paste anywhere** with Ctrl + V.
5. A red flash means no QR was found in that area. Just press the hotkey again and try a different selection.
6. **Press Esc** anytime during the snip to cancel.

## Settings

Right-click the tray icon → **Settings...** to configure:

- **Snip hotkey** — click the box and press a new combination if you want something other than Win + Shift + Q.
- **Start QR Snapper when I sign in to Windows** — auto-launch on login.
- **Auto-paste decoded text into the active window** — for power users. After a snip, QR Snapper restores focus to the window you were just typing in and presses Ctrl + V for you. Off by default because it can paste into the wrong window if you switch focus quickly. A blue flash confirms when it fires.
- **Show a notification after each successful snip** — quiet by default. A notification always appears on failure regardless.
- **Debug mode** — verbose logging, plus a "Test Capture" item in the tray menu for diagnostics.

## If antivirus flags it

Some antivirus programs (Avast, Norton, McAfee) may flag QR Snapper or its installer as suspicious. This is because QR Snapper does three things that, taken together, look exactly like a keylogger:

- Registers a global keyboard shortcut
- Reads pixels from your screen
- Writes to your clipboard

QR Snapper does none of those things maliciously — but the byte-level pattern is similar. If your antivirus quarantines QR Snapper:

1. Open your antivirus's quarantine / virus chest.
2. Find the QR Snapper entry and **Restore** it.
3. Add an exception for the install folder: `%LOCALAPPDATA%\Programs\QRSnapper\`

If you're on a managed work computer and can't add exceptions yourself, your IT department can.

## Uninstall

Settings → Apps → Installed apps → QR Snapper → Uninstall.

The uninstaller also removes the auto-start entry if you had it enabled. Your settings file at `%APPDATA%\QRSnapper\config.json` stays put in case you reinstall later; delete it manually if you want a clean slate.

## Troubleshooting

**Nothing happens when I press the hotkey.** Open Settings and check whether your hotkey was claimed by another program. Try a different combination.

**It can't decode the QR.** Try snipping a tighter rectangle around just the QR (not the whole page around it). Very low-resolution or low-contrast scans may still fail.

**Something else is wrong.** Open Debug mode in Settings, reproduce the issue, then send Caleb the log file at `%LOCALAPPDATA%\QRSnapper\startup.log`.

---

Built with [Claude](https://www.anthropic.com/claude) (Anthropic).
