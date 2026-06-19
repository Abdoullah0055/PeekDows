# PeekDows — Manual Test Checklist

## Prerequisites
- Build: `dotnet build PeekDows.slnx`
- Run: `dotnet run --project src/PeekDows.App`
- Verify tray icon appears in system tray

---

## 1. Tray Icon & Menu

- [ ] Tray icon visible in system tray
- [ ] Right-click shows context menu
- [ ] Menu items in correct order: Status → Arrange Now → Pause → Pause for → **Separator** → Enable Auto Arrange → Start with Windows → **Separator** → Settings → Open Log File → Open Logs Folder → **Separator** → Exit
- [ ] "Status: Running" is disabled (grayed out, non-clickable)

## 2. Arrange Now

- [ ] Click "Arrange Now" with 2+ eligible windows open → windows rearrange into ClassicPeekGrid layout
- [ ] Click "Arrange Now" with no eligible windows → no crash, no error dialog
- [ ] Click "Arrange Now" with 1 eligible window → window takes slot A (full 90% area)

## 3. Pause / Resume

- [ ] Click "Pause" → status changes to "Status: Paused (until resumed)"
- [ ] While paused, click "Arrange Now" → no arrangement occurs
- [ ] While paused, click "Resume" (changed from "Pause") → status returns to "Status: Running"
- [ ] After resume, auto-arrange resumes but does NOT trigger an immediate arrange

## 4. Pause for Duration

- [ ] Click "Pause for → 5 minutes" → status shows "Status: Paused (5 min remaining)"
- [ ] Click "Pause for → 15 minutes" → status updates accordingly
- [ ] Click "Pause for → 1 hour" → status updates accordingly
- [ ] Click "Pause for → Until manually resumed" → status shows "Status: Paused (until resumed)"
- [ ] Pause expires automatically → status returns to "Status: Running", no surprise arrange

## 5. Auto Arrange Toggle

- [ ] "Enable Auto Arrange" has checkmark when Auto Arrange is on
- [ ] Click "Enable Auto Arrange" → checkmark toggles, auto-arrange service starts/stops
- [ ] State persists after app restart

## 6. Start with Windows

- [ ] "Start with Windows" has checkmark when enabled
- [ ] Click "Start with Windows" → shortcut created/removed in Startup folder
- [ ] Setting persists after app restart

## 7. Hotkeys

- [ ] `Ctrl+Alt+Space` triggers Arrange Now (same as clicking menu item)
- [ ] `Ctrl+Alt+P` toggles Pause/Resume
- [ ] If hotkey is already in use by another app, app logs warning but does not crash
- [ ] Hotkeys work while another window has focus

## 8. Settings Window

- [ ] Click "Settings" → Settings dialog opens
- [ ] Can add/remove ignored processes
- [ ] Can add/remove ignored classes
- [ ] Changes apply immediately (reflected in next arrange cycle)
- [ ] Settings persist after app restart

## 9. Ignored Windows

- [ ] Windows with process name in ignored list are not arranged
- [ ] Windows with class name in ignored list are not arranged
- [ ] Built-in ignored windows (taskbar, Start menu, Search, etc.) are never arranged

## 10. Fullscreen Handling

- [ ] Fullscreen (non-maximized) windows covering entire work area are skipped
- [ ] Maximized windows are restored correctly before placement

## 11. Multi-Monitor

- [ ] Windows are arranged independently per monitor
- [ ] Each monitor uses 90% of its work area for the ClassicPeekGrid layout

## 12. Log Files

- [ ] Click "Open Log File" → log file opens in default text editor
- [ ] Click "Open Logs Folder" → folder opens in Explorer
- [ ] Log file at `%APPDATA%\PeekDows\logs\peekdows.log` contains entries

## 13. Exit

- [ ] Click "Exit" → application exits cleanly
- [ ] No lingering process after exit
- [ ] Tray icon removed after exit

## 14. Settings Persistence (Restart Test)

- [ ] Change Auto Arrange state → restart app → state preserved
- [ ] Change Start with Windows → restart app → state preserved
- [ ] Add custom ignored process → restart app → still ignored
- [ ] Add custom ignored class → restart app → still ignored

## 15. Edge Cases

- [ ] Rapidly clicking "Arrange Now" multiple times → no crash
- [ ] Toggling Pause rapidly → no inconsistent state
- [ ] Closing a window that was just arranged → next arrange cycle handles gracefully
- [ ] No eligible windows → app runs normally, no crash
- [ ] Corrupt settings.json → app creates backup, loads defaults, continues normally
