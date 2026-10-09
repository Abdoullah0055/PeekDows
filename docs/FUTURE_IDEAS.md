# PeekDows — Future Ideas & Parked Features

Ideas that are not in the app (yet). A feature lives in the Settings **Beta** page
while it is being tested; it is promoted to Layout/General only once tested at 100%.

## Parked — Directional Focus Spotlight

**What it was:** when a `Ctrl+Shift` gesture passed the threshold toward an occupied
slot, the whole monitor dimmed (~45% black overlay) except for a cut-out hole over
the target window, with a 2px white border. Modes `Spotlight` / `Both` plus an
opacity slider (20–70%). Removed in schema v5 (settings keys `spotlightOpacity`
dropped, `Spotlight`→`Off`, `Both`→`Overlay` migration).

**Why parked:** too many bugs for the current budget — layered-window `Region` /
dispose friction (WFO1000 build friction), 4-mode confusion (`Off/Overlay/Spotlight/
Both`), tray↔desktop sync gaps — and no time to fix them properly right now.

**Re-entry criteria:** the arrows-only overlay (v5) is validated at 100% and promoted
out of Beta first. Only then re-evaluate spotlight as an optional extra, with its own
Beta cycle and a mandatory `FocusHintModeChanged`-style live-sync event from day one.

## Candidates (not started)

- **Undo Arrange** (`Ctrl+Alt+Z`): snapshot window rects before `ApplyPlacements`,
  restore in one step.
- **Pin slots A–D**: `assignSlotA-D` hotkeys already exist in settings but are not
  wired in `HotkeyService` — wire them to pin a favorite app to a slot.
- **Arrange preview**: live grid preview on the wallpaper before Save in Layout.
- **Per-virtual-desktop profiles**: different arrange behavior for work vs. gaming
  desktops.
- **Learning auto-fade**: full hints for the first ~20 successful gestures, then
  minimal arrows once the spatial layout is memorized.
- **Slot badges**: small `1–8` badges on arranged windows matching overlay arrows.
- **Adjustable arrow size**: slider for overlay arrow length/width in Beta.
