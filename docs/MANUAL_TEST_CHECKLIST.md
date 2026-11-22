# PeekDows — Manual Test Checklist

## Prerequisites
- Build: `dotnet build PeekDows.slnx`
- Run: `dotnet run --project src/PeekDows.App`
- Verify tray icon appears in system tray

---

## 1. Tray Icon & Menu

- [ ] Tray icon visible in system tray
- [ ] Right-click shows context menu
- [ ] Menu order: Arrange Now → Undo arrange (when available) → Pause/Resume → **Separator** → Settings → **Separator** → Exit
- [ ] The Settings sidebar shows the current status; the tray tooltip shows the pause state.

## 2. Arrange Now

- [ ] Click "Arrange Now" with 2+ eligible windows open → windows rearrange into ClassicPeekGrid layout
- [ ] Click "Arrange Now" with no eligible windows → no crash, no error dialog
- [ ] Click "Arrange Now" with 1 eligible window → window takes slot A (full 90% area)

## 3. Pause / Resume

- [ ] Click "Pause" → status shows "Pause enabled until resumed"
- [ ] While paused, "Arrange Now" is disabled
- [ ] While paused, click "Resume" (changed from "Pause") → status returns to "Running"
- [ ] After resume, auto-arrange resumes but does NOT trigger an immediate arrange

## 4. Pause for Duration

- [ ] In Settings → General, select 5 minutes and click "Pause" → status shows the end time.
- [ ] Repeat with 15 minutes and 1 hour → end time updates accordingly.
- [ ] Select "Until manually resumed" and click "Pause" → status has no end time.
- [ ] Timed pause expires automatically → status returns to "Running", no surprise arrange.

## 5. Auto Arrange Toggle

- [ ] In Settings → General, "Auto arrange" matches the current setting.
- [ ] Toggle "Auto arrange" and Save → auto-arrange service starts/stops.
- [ ] State persists after app restart

## 6. Start with Windows

- [ ] In Settings → General, "Start with Windows" matches the current setting.
- [ ] Toggle "Start with Windows" and Save → shortcut created/removed in Startup folder.
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

- [ ] In Settings → About, open "Log file" → log opens in default text editor.
- [ ] In Settings → About, open "Logs folder" → folder opens in Explorer.
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
- [ ] At 100% and 150% scaling, Tab to each of the ten Settings switches. Check the visible focus outline, the name and description announced by Narrator, and the state change when pressing Space.
- [ ] Editing any control shows "● Unsaved changes" and enables Save; reverting the edit hides it again (dirty tracking flips off).
- [ ] Save applies Auto arrange, Start with Windows and the Window size preset.
- [ ] Layout preset Small/Medium/Large animates the live preview; after Save the actual windows re-arrange at the new ratio.
- [ ] Directional focus threshold slider updates the px chip while dragging; persisted after Save (check settings.json).
- [ ] About tab: version line matches the app version; "Open" buttons open the log file, the logs folder and the settings folder respectively.
- [ ] Close with unsaved changes → confirmation dialog; "No" keeps the window open.
- [ ] Pause or resume from the tray with Settings open → the sidebar status updates without touching the draft or showing a settings change banner.
- [ ] Turn off Enable PeekDows and save → sidebar shows Disabled even if a pause is active, and Pause and Preview are unavailable. Re-enable and save → sidebar reflects the active pause or Running.
- [ ] Cancel closes the window without applying anything (verify settings.json untouched).
- [ ] DPI: repeat opening at 100%, 125% and 150% scaling — the window stays crisp and usable.
- [ ] Fallback: temporarily break WebView2 (e.g. set env var `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` to an invalid path), reopen Settings → basic General, Layout, About and Ignored apps tabs open and the log warns.
- [ ] Ignored apps search-and-check: open a test app (e.g. Notepad), on the Ignored apps page type its name → it appears with a checkbox → check it → Save → it is in `%APPDATA%\PeekDows\settings.json`; close the app and PeekDows, reopen both → the entry is still listed (persistence).
- [ ] Hotkeys: change Pause / resume from `Ctrl+Alt+P` to `Ctrl+Alt+O`, Save → the new combo toggles pause; then try a conflicting combo, Save → an error is shown and the previous combo still works.
- [ ] Windows: open the Windows page, Refresh → rows are listed (title, process, eligibility); click "Ignorer ce process" on a row → the process appears checked in the Ignored apps list (Save still required to apply).
- [ ] With two displays and two windows from the same test app, set only the left display profile to ignore that app. Refresh Windows and check that the left row says Ignored by display profile and the right row says Yes. Adding the app to Ignored apps in the draft marks both rows Ignored.
- [ ] On a test account, arrange, use directional focus and switch the keyboard layout with a test window named `PEEKDOWS_AUDIT_PRIVATE_1247`. Review only fresh log lines. Check that the title stays out of the logs and that the window identifiers, process and placement rectangles are available.
- [ ] Trigger the same Custom zone fallback twice with Show tray notifications off and then on. Check that the placement is the same both times and that the tray balloon appears only when enabled.
- [ ] Un-ignore confirm: on the Ignored apps page uncheck an entry → a dark confirm modal names the app → "Keep ignoring" leaves it checked and untouched; "Stop ignoring" + Save removes it from `settings.json`.
- [ ] Ignored classes stay collapsed under "Advanced: window classes" and still save one-entry-per-line.

## Layout — Arrange sound (bibliothèque + auto-off)
- [ ] Layout → Arrange sound : les 3 presets jouent en preview (sans Save) ; upload MP3/WAV → apparaît dans « Mes sons » ; fichier invalide/>5 Mo → refusé proprement.
- [ ] Supprimer un son perso → confirmation + retour sur `chime-soft` si c'était le sélectionné ; presets d'usine non-supprimables.
- [ ] Activer et enregistrer → Arrange Now avec fenêtres → son audible ; arrange vide/en pause → silence.
- [ ] Auto-off : picker tambour heures (0-23) + minutes (00-59) style horloge iOS, scroll + clic, surbrillance suivie ; Save persiste le total en minutes (`soundAutoOffMinutes`).
- [ ] Auto-off : activer avec 1 h 30 → passé l'échéance, extinction + Save ; Save arme désormais la deadline (bug corrigé : avant, elle ne s'armait jamais depuis les réglages).
- [ ] Fichier son supprimé à la main → arrange silencieux + warn dans le log, pas de crash.

## General — Direction hints (pie overlay, Ctrl+Win hold)
- [ ] Settings → General → Focus hints : Off/Overlay suit le mode, `settings.json:focusHintMode` suit après Save.
- [ ] `Ctrl+Win` tenu → cercle en parts vers les slots occupés (2 apps en diagonale → 2 parts, 4 → 4, 8 → 8, trou central sur l'ancre) ; part active blanche + glow ; release d'une touche → disparition.
- [ ] General → « Show app icons in the overlay » coché (défaut) : chaque part affiche le logo de son app, part active en blanc ; console/sans icône → pastille avec initiale ; décoché → parts seules ; Save persiste.
- [ ] DPI 125/150 % : logos nets, pas flous ; gestes répétés : pas de fuite de handles GDI (compteur stable dans le Gestionnaire des tâches).
- [ ] 3e touche pendant le tenu (`D`, flèches, `Tab`, lettre) → geste annulé aussitôt, la touche atteint sa cible (ex. `Ctrl+Win+D` crée un bureau, `Ctrl+Win+←/→` change de bureau).
- [ ] `Win` relâché en dernier (seul) → noter si le menu Démarrer s'ouvre (cas à trancher — relâcher `Win` en premier en attendant).
- [ ] Touches volume/média/majuscules-verrou tenues pendant le geste → le geste survit (touches silencieuses) ; `D`/flèches/`Tab`/lettres → annulation toujours active.
- [ ] `Esc` / clic pendant le tenu → annulation, pas d'activation surprise.
- [ ] Spam de gestes : jamais de flip de langue, jamais de majuscules fantômes (rien n'est avalé, aucun hook installé).
- [ ] Typing normal (`Ctrl+C`, `Alt+Tab`, `Win+D` sans `Ctrl`) → inchangé.
- [ ] Overlay n'est jamais arrangé (classifier : titre `PeekDowsHintOverlay` exclu).
- [ ] Overlay : disque de base à peine visible, parts grises semi-transparentes, active blanche + glow ; aucun flash à l'apparition, fade ~140ms.
- [ ] Fonds clairs + sombres : pas de franges autour des parts ; 125%/150% DPI : cercle net et bien dimensionné.
- [ ] Log après un geste : `HintOverlay: ULW ok=True ... populated=N` (si `ok=False`, le fallback keyed affiche quand même les parts — signaler).

## Diagnostic langue clavier / Task Manager (logs `%APPDATA%\PeekDows\logs\peekdows.log`)
- [ ] Démarrage : lignes `KB_TOGGLE_KEYS` + `KB_LAYOUTS fr-CA,...` présentes (plus aucun hook installé — traçage passif uniquement).
- [ ] Gestes répétés (`Ctrl+Win` tenu) : `GESTURE_HKL_START/END` avec le même HKL, pas de `KB_LAYOUT_FLIP` lié au geste.
- [ ] Conflit connu assumé : relâcher `Win` en dernier peut ouvrir Démarrer (voir cas dédié plus haut).


## 16. Undo Arrange

- [ ] Arrange Now avec 2+ fenetres -> "Undo arrange" restaure chaque fenetre pixel-identique (position + taille d'avant l'arrange)
- [ ] 3 arranges successifs -> 3 undo successifs reviennent en arriere niveau par niveau ; 4e undo = no-op sans crash
- [ ] Fenetre fermee entre l'arrange et l'undo -> skippee silencieusement, autres fenetres restaurees, pas de crash
- [ ] Fenetre maximisee avant l'arrange -> apres undo elle est reposee PUIS re-maximisee
- [ ] Ctrl+Win+Z a froid (sans geste) = undo seul, sans ouvrir le menu Demarrer ni changer de bureau
- [ ] Ctrl+Win+Z en plein geste directionnel (Ctrl+Win tenu) = le geste meurt ET l'undo s'execute
- [ ] Hotkey deja pris par un autre programme -> warn dans le log, pas de crash
- [ ] Item tray "Undo arrange" masqué sans historique, visible après un arrange.

## 17. Layout Modes (Grid / Halves / Thirds)

- [ ] Layout -> Halves : 2 fenetres = gauche/droite 50/50 pleine hauteur ; 3e fenetre -> Left,Right,Left
- [ ] Layout -> Thirds : 3 colonnes jointives SANS gap ni overlap (2/3/4 fenetres) ; log `Placement verification clean` (post-animation, significatif)
- [ ] Si `Placement deviation` stabilise dans le log -> noter l'app + attendu vs reel (taille mini clampée : cas documenté, pas force)
- [ ] Changer de mode dans Settings → Layout et sauvegarder = re-arrange immédiat visible.
- [ ] Preview Layout = réalité à l'écran ; Save persiste layoutMode.
- [ ] Settings → Layout : lancer « Preview arrange », puis Annuler ou Appliquer ; exporter puis importer un fichier de réglages.
- [ ] Avec plusieurs zones empilées, « Next window in zone » demande quelle pile utiliser, même si Settings est affiché au-dessus d'une zone.
- [ ] Cercle directionnel coherent : 2 parts en Halves, 3 en Thirds
- [ ] Mode invalide dans settings.json -> Grid, app demarre
- [ ] Page Layout : preset SOUS le mode ; preset visible en Grid uniquement, cache en Halves/Thirds ; retour Grid = ancienne valeur restauree

## 18. Per-App Rules (pin / ignore)

- [ ] Notepad epingle en A -> 3 arranges successifs, Notepad reste en A (meme a 9+ fenetres)
- [ ] 2 apps epinglees au meme slot -> la 1re regle gagne + warn log
- [ ] Meme process pin + ignore -> exclu (ignore prioritaire)
- [ ] Save -> reload -> appRules intact dans settings.json
- [ ] Regles invalides (process vide, action inconnue, slot Z) -> rejetees, le reste applique
