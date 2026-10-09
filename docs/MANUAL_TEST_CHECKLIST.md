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

## Settings App v2 (WebView2)

Prerequisite: `dotnet run --project src/PeekDows.App`.

- [ ] Tray → Settings opens the dark WebView2 window; sidebar shows General / Layout / About active and Windows / Hotkeys / Ignored apps disabled with "v2" chips.
- [ ] Every switch renders with the current settings values (compare with `%APPDATA%\PeekDows\settings.json`).
- [ ] Editing any control shows "● Unsaved changes" and enables Save; reverting the edit hides it again (dirty tracking flips off).
- [ ] Save applies and the tray reflects it: Auto Arrange label flips, Start with Windows checkbox follows, Window size preset radio moves.
- [ ] Layout preset Small/Medium/Large animates the live preview; after Save the actual windows re-arrange at the new ratio.
- [ ] Directional focus threshold slider updates the px chip while dragging; persisted after Save (check settings.json).
- [ ] About tab: version line matches the app version; "Open" buttons open the log file, the logs folder and the settings folder respectively.
- [ ] Close with unsaved changes → confirmation dialog; "No" keeps the window open.
- [ ] While the window is open and the draft is clean, toggling from the tray updates the UI silently.
- [ ] While the draft is dirty, a tray toggle shows the amber "changed from the tray" banner instead of clobbering.
- [ ] Cancel closes the window without applying anything (verify settings.json untouched).
- [ ] DPI: repeat opening at 100%, 125% and 150% scaling — the window stays crisp and usable.
- [ ] Fallback: temporarily break WebView2 (e.g. set env var `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` to an invalid path), reopen Settings → legacy window opens and the log warns.
- [ ] Ignored apps search-and-check: open a test app (e.g. Notepad), on the Ignored apps page type its name → it appears with a checkbox → check it → Save → it is in `%APPDATA%\PeekDows\settings.json`; close the app and PeekDows, reopen both → the entry is still listed (persistence).
- [ ] Hotkeys: change Pause / resume from `Ctrl+Alt+P` to `Ctrl+Alt+O`, Save → the new combo toggles pause; then try a conflicting combo, Save → an error is shown and the previous combo still works.
- [ ] Windows: open the Windows page, Refresh → rows are listed (title, process, eligibility); click "Ignorer ce process" on a row → the process appears checked in the Ignored apps list (Save still required to apply).
- [ ] Un-ignore confirm: on the Ignored apps page uncheck an entry → a dark confirm modal names the app → "Keep ignoring" leaves it checked and untouched; "Stop ignoring" + Save removes it from `settings.json`.
- [ ] Ignored classes stay collapsed under "Advanced: window classes" and still save one-entry-per-line.

## Beta — Direction hints (arrows-only overlay)
- [ ] Tray → Direction hints ▸ Off/Overlay : radio suit le mode, `settings.json:focusHintMode` suit après clic.
- [ ] `Ctrl+Shift` tenu → seules les flèches vers les slots occupés s'affichent (2 apps en diagonale → 2 flèches, 4 → 4, 8 → 8), sans aucun fond ; slot actif en blanc.
- [ ] Aucun slot occupé → rien ne s'affiche ; relâche `Ctrl/Shift` ou `Win` → disparition aussitôt, sans focus volé.
- [ ] Settings → Beta : segmented Off/Overlay, Save → tray suit ; tray → Beta suit sans Save (bannière si draft dirty) — régression du bug sync : vérifier dans les deux sens.
- [ ] Vieux `settings.json` (`Both`/`Spotlight`/`spotlightOpacity`) → migré au chargement (`Both`→Overlay, `Spotlight`→Off), app démarre.
- [ ] Overlay n'est jamais arrangé (classifier : titre `PeekDowsHintOverlay` exclu).
- [ ] Overlay : halo gris à peine visible, flèches grises semi-transparentes, active blanche + glow ; aucun flash de fond (mauve) à l'apparition, fade ~140ms.
- [ ] Fonds clairs + sombres : pas de franges autour des flèches ; 125%/150% DPI : flèches nettes et bien dimensionnées.
- [ ] Log après un geste : `HintOverlay: ULW ok=True ... populated=N` (si `ok=False`, le fallback keyed affiche quand même les flèches — signaler).
- [ ] Garde clavier : layout FR, 5 gestes `Ctrl+Shift` tenu + souris → toujours FR ; `Ctrl+Shift+Esc` → Gestionnaire des tâches ; taper `aaa` après un geste → minuscules (pas de Shift fantôme) ; Beta → décoche la garde → le layout rebascule (comportement Windows natif).

## Diagnostic langue clavier / Task Manager (logs `%APPDATA%\PeekDows\logs\peekdows.log`)
- [ ] Démarrage : lignes `KB_TOGGLE_KEYS cu=... def=...` (present/absent explicite) + `KB_LAYOUTS fr-CA,...` présentes.
- [ ] Geste tenu + souris : `GESTURE_HKL_START/END` avec le même HKL (hwnd/tid inclus), pas de `KB_LAYOUT_FLIP` pendant.
- [ ] Flip réel : `KB_LAYOUT_FLIP fr-FR -> en-US hwnd=0x... tid=... sameHwnd=... hook=ok ...` → lecture :
  - `ALT_SHIFT_CHORD` juste avant = accord Alt+Shift (autre toggle OS) ;
  - `sameHwnd=False` ou tid différent = restore par fenêtre/onglet ;
  - `inj=True` = outil tiers qui injecte ;
  - `hook=gone` = retrait silencieux du hook ;
  - même hwnd+tid + rien = TSF/IME profond.
- [ ] `Esc` pendant un geste : `CHORD_THIRD_KEY vk=Esc` puis `TASKMGR_OPEN ... hint=(Esc-pendant-geste => accord OS Ctrl+Shift+Esc)` — cause exacte prouvée.
