"use strict";

// Pure UI state: snapshot = last truth from C#, draft = editable copy.
const EDITABLE_KEYS = [
  "enabled", "autoArrange", "animate", "directionalFocus", "startWithWindows",
  "allowRepositionMaximized", "preset", "arrangeOnStartup",
  "showTrayNotifications", "thresholdPx",
  "ignoredProcesses", "ignoredClasses", "hotkeys", "appRules",
  "focusHintMode", "overlayShowIcons",
  "soundEnabled", "soundName", "soundAutoOffMinutes", "layoutMode",
];

const state = { snapshot: null, draft: null, dirty: false };
let deferredSnapshot = null;
let runtimeStatus = null;
let pendingSave = null;
let nextSaveId = 0;
let reportedDirty = false;

// User-imported sounds are refreshed independently from setting edits.
let userSounds = [];

// Read-only Windows snapshot rows (filled on Refresh, never part of the draft).
let windowsRows = [];

const $ = (id) => document.getElementById(id);
const post = (msg) => {
  if (window.chrome?.webview?.postMessage) window.chrome.webview.postMessage(msg);
};

function pick(snapshot) {
  const out = {};
  for (const k of EDITABLE_KEYS) {
    const v = snapshot[k];
    if (Array.isArray(v)) out[k] = [...v];
    else if (v !== null && typeof v === "object") out[k] = { ...v };
    else out[k] = v;
  }
  return out;
}

function differs(left, right) {
  return JSON.stringify(left) !== JSON.stringify(right);
}

// ---------- incoming messages ----------
window.chrome?.webview?.addEventListener("message", (event) => {
  const msg = event.data;
  if (!msg || typeof msg.type !== "string") return;

  if (msg.type === "settings" || msg.type === "externalChange") onExternalChange(msg.data);
  else if (msg.type === "applied") onApplied(msg);
  else if (msg.type === "runtimeStatus") updateRuntimeStatus(msg.data);
  else if (msg.type === "soundFile") onSoundFile(msg);
  else if (msg.type === "soundError") onSoundError(msg.operation);
  else if (msg.type === "windows") { windowsRows = msg.data ?? []; renderWindowsRows(); }
});

function adoptSnapshot(data, lateValues = {}, lateHotkeys = {}) {
  deferredSnapshot = null;
  $("externalBanner").hidden = true;
  state.snapshot = data;
  // Preference defaults on both copies: keeps dirty tracking exact even if an older
  // bridge ever omits this key (undefined vs default would read as dirty).
  state.snapshot.focusHintMode ??= "Overlay";
  state.snapshot.layoutMode ??= "Grid";
  state.snapshot.appRules ??= [];
  state.snapshot.overlayShowIcons ??= true;
  state.snapshot.soundName ??= "chime-soft";
  state.snapshot.soundAutoOffMinutes ??= 0;
  state.snapshot.ignoredProcesses ??= [];
  state.snapshot.ignoredClasses ??= [];
  state.snapshot.hotkeys ??= {};
  userSounds = data.userSounds ?? [];
  state.draft = pick(data);
  Object.assign(state.draft, lateValues);
  Object.assign(state.draft.hotkeys, lateHotkeys);
  updateRuntimeStatus(runtimeStatus ?? data.runtime);
  setControlsFrom(state.draft);
  syncDirty();
  $("versionLine").textContent = `v${data.appVersion} · settings schema v${data.version}`;
  renderPresetSelection(state.draft.preset);
  renderPreview(state.draft.preset, state.draft.layoutMode);
  renderLayoutModeSelection(state.draft.layoutMode);
  renderAppRules();
  renderWindowsRows();
  $("thresholdValue").textContent = `${state.draft.thresholdPx} px`;
  renderHintModeSelection(state.draft.focusHintMode);
  renderSoundSelection(state.draft.soundName);
  renderUserSounds();
}

function onSoundFile(msg) {
  if (!Array.isArray(msg.userSounds)) return;
  $("soundError").hidden = true;
  userSounds = msg.userSounds;
  const deleted = typeof msg.deletedName === "string" && typeof msg.soundName === "string";
  for (const snapshot of [state.snapshot, deferredSnapshot]) {
    if (!snapshot) continue;
    snapshot.userSounds = userSounds;
    if (deleted) snapshot.soundName = msg.soundName;
  }
  if (deleted && state.draft) {
    if (state.draft.soundName?.toLowerCase() === msg.deletedName.toLowerCase())
      state.draft.soundName = msg.soundName;
    renderSoundSelection(state.draft.soundName);
    syncDirty();
  }
  renderUserSounds();
}

function onSoundError(operation) {
  const message = $("soundError");
  message.textContent = operation === "delete"
    ? "Could not delete the sound. See the log for details."
    : "Could not add the sound. Choose an MP3 or WAV up to 5 MB, or see the log for details.";
  message.hidden = false;
}

function onExternalChange(data) {
  updateRuntimeStatus(data.runtime);
  if (state.dirty || pendingSave) {
    deferredSnapshot = data;
    $("externalBanner").hidden = false;
  } else {
    $("externalBanner").hidden = true;
    adoptSnapshot(data);
  }
}

function updateRuntimeStatus(runtime) {
  if (!runtime) return;
  runtimeStatus = runtime;
  $("statusFoot").textContent = runtime.isPaused ? runtime.pauseDescription ?? "Paused" : "Running";
  $("statusFoot").parentElement.classList.toggle("paused", runtime.isPaused);
  $("pauseFor").disabled = false;
  $("resume").hidden = !runtime.isPaused;
  syncLayoutActions();
}

function syncLayoutActions() {
  for (const id of ["editLayoutProfiles", "importLayout"]) $(id).disabled = state.dirty;
  $("previewArrange").disabled = state.dirty || !!runtimeStatus?.isPaused || !state.snapshot?.enabled;
}

function onApplied(msg) {
  if (!pendingSave || msg.requestId !== pendingSave.id) return;
  const sentDraft = pendingSave.draft;
  pendingSave = null;
  if (!msg.ok || !msg.data) {
    syncDirty();
    $("unsaved").textContent = msg.error === "hotkey-conflict"
      ? "⚠ Shortcut already in use"
      : msg.error === "bad-gesture" ? "⚠ Unsupported shortcut"
      : msg.error === "sound-unavailable" ? "⚠ Sound no longer available" : "⚠ Save failed — see log";
    $("unsaved").style.visibility = "visible";
    return;
  }
  const lateValues = Object.fromEntries(EDITABLE_KEYS
    .filter(key => key !== "hotkeys" && differs(state.draft[key], sentDraft[key]))
    .map(key => [key, state.draft[key]]));
  const lateHotkeys = Object.fromEntries(Object.entries(state.draft.hotkeys ?? {})
    .filter(([name, gesture]) => gesture !== sentDraft.hotkeys?.[name]));
  adoptSnapshot(msg.data, lateValues, lateHotkeys);
  const saved = $("savedMsg");
  saved.classList.add("show");
  setTimeout(() => saved.classList.remove("show"), 2200);
}

// ---------- control sync ----------
function setControlsFrom(data) {
  for (const el of document.querySelectorAll("input[type=checkbox][data-key]")) {
    el.checked = !!data[el.dataset.key];
  }
  $("threshold").value = data.thresholdPx;
  renderSoundDuration(data.soundAutoOffMinutes);
  renderIgnoredList();
  renderIgnoredSearch();
  const ic = $("ignoredClasses");
  if (ic) ic.value = (data.ignoredClasses ?? []).join("\n");
  const hk = data.hotkeys ?? {};
  for (const btn of document.querySelectorAll("button[data-hotkey]")) {
    if (hotkeyCapture?.button === btn) continue;
    const name = btn.dataset.hotkey;
    if (hk[name]) btn.textContent = hk[name];
  }
  // Windows table stays empty until Refresh — never filled from snapshot.
}

// ---------- dirty tracking ----------
function syncDirty() {
  state.dirty = !!state.snapshot && differs(state.draft, pick(state.snapshot));
  const busy = pendingSave !== null;
  const dirtyForClose = state.dirty || busy;
  if (dirtyForClose !== reportedDirty) {
    reportedDirty = dirtyForClose;
    post({ type: "dirtyChanged", value: dirtyForClose });
  }
  $("saveBtn").disabled = busy || !state.dirty;
  $("cancelBtn").disabled = busy;
  $("unsaved").textContent = "● Unsaved changes";
  $("unsaved").style.visibility = state.dirty ? "visible" : "hidden";
  syncLayoutActions();
}

// ---------- control bindings ----------
for (const el of document.querySelectorAll("input[type=checkbox][data-key]")) {
  el.addEventListener("change", () => {
    state.draft[el.dataset.key] = el.checked;
    syncDirty();
  });
}

const threshold = $("threshold");
threshold.addEventListener("input", () => {
  state.draft.thresholdPx = parseInt(threshold.value, 10);
  $("thresholdValue").textContent = `${threshold.value} px`;
  syncDirty();
});

// ---------- ignored classes (one entry per line, cleaned C#-side on Save) ----------
for (const id of ["ignoredClasses"]) {
  const ta = $(id);
  if (!ta) continue;
  ta.addEventListener("input", () => {
    if (!state.draft) return;
    state.draft[id] = ta.value.split("\n");
    syncDirty();
  });
}

// ---------- ignored apps (search + check to ignore, uncheck asks to confirm) ----------
// Source of truth is state.draft.ignoredProcesses (persisted to settings.json on
// Save), independent from the transient windowsRows cache: entries survive app
// and PeekDows restarts. Arrays are always REPLACED (never mutated in place) so
// pick()'s copies keep dirty tracking exact.
const BUILTIN_PROCESSES = new Set([
  "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
  "textinputhost.exe", "lockapp.exe",
]);

const sameProc = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();

function isIgnoredInList(proc) {
  return (state.draft?.ignoredProcesses ?? []).some((p) => sameProc(p, proc));
}

function isIgnoreRule(rule, proc) {
  return sameProc(rule.process, proc) && String(rule.action).toLowerCase() === "ignore";
}

function isIgnored(proc) {
  return isIgnoredInList(proc) || (state.draft?.appRules ?? []).some(rule => isIgnoreRule(rule, proc));
}

function addIgnored(proc) {
  if (!state.draft || !proc || isIgnored(proc)) return;
  state.draft.ignoredProcesses = [...(state.draft.ignoredProcesses ?? []), proc];
  syncDirty();
  renderIgnoredList();
  renderIgnoredSearch();
  renderWindowsRows();
}

let pendingUnignore = null; // { proc } while the confirm modal is open

function askUnignore(proc, trigger) {
  pendingUnignore = { proc, trigger };
  $("unignoreName").textContent = proc;
  $("unignoreModalBack").hidden = false;
  $("unignoreCancel").focus();
}

function closeUnignoreModal(restoreFocus = true) {
  const trigger = pendingUnignore?.trigger;
  pendingUnignore = null;
  $("unignoreModalBack").hidden = true;
  if (restoreFocus && trigger) {
    if (trigger.type === "checkbox") trigger.checked = true;
    const control = (trigger.id && $(trigger.id)) || trigger;
    control.focus();
  }
}

function onIgnoredCheck(proc, checkbox) {
  if (!state.draft) return;
  if (checkbox.checked) addIgnored(proc);
  else {
    // Keep the box visually unchecked under the modal; Cancel re-renders it checked.
    askUnignore(proc, checkbox);
  }
}

function checkRow(proc, sub, checked) {
  const row = document.createElement("label");
  row.className = "checkrow";
  const box = document.createElement("input");
  box.type = "checkbox";
  box.checked = checked;
  box.addEventListener("change", () => onIgnoredCheck(proc, box));
  const txt = document.createElement("span");
  txt.className = "txt";
  txt.textContent = proc;
  row.append(box, txt);
  if (sub) {
    const s = document.createElement("span");
    s.className = "sub";
    s.textContent = sub;
    row.append(s);
  }
  return row;
}

function renderIgnoredList() {
  const box = $("ignoredList");
  if (!box || !state.draft) return;
  box.innerHTML = "";
  const list = state.draft.ignoredProcesses ?? [];
  if (list.length === 0) {
    box.innerHTML = '<div class="empty">No ignored apps yet. Search above to add one.</div>';
    return;
  }
  for (const name of [...list].sort((a, b) => String(a).localeCompare(String(b)))) {
    box.append(checkRow(name, null, true));
  }
}

function renderIgnoredSearch() {
  const input = $("ignoredSearch");
  const box = $("ignoredSearchResults");
  if (!input || !box) return;
  box.innerHTML = "";
  const q = input.value.trim().toLowerCase();
  if (!q) return;
  if (windowsRows.length === 0) {
    post({ type: "requestWindows" });
    box.innerHTML = '<div class="empty">Loading open windows… type again in a moment.</div>';
    return;
  }
  const seen = new Set();
  const matches = [];
  for (const w of windowsRows) {
    const proc = String(w.process ?? "");
    const key = proc.toLowerCase();
    if (!proc || seen.has(key) || BUILTIN_PROCESSES.has(key)) continue;
    if (!key.includes(q) && !String(w.title ?? "").toLowerCase().includes(q)) continue;
    seen.add(key);
    matches.push({ process: proc, title: w.title });
  }
  if (matches.length === 0) {
    box.innerHTML = '<div class="empty">No running app matches — it will be searchable once open.</div>';
    return;
  }
  for (const m of matches.slice(0, 30)) {
    box.append(checkRow(m.process, m.title, isIgnored(m.process)));
  }
}

const ignoredSearch = $("ignoredSearch");
if (ignoredSearch) {
  ignoredSearch.addEventListener("input", renderIgnoredSearch);
  ignoredSearch.addEventListener("focus", () => {
    if (windowsRows.length === 0) post({ type: "requestWindows" });
  });
}

const unignoreConfirm = $("unignoreConfirm");
if (unignoreConfirm) unignoreConfirm.addEventListener("click", () => {
  if (pendingUnignore && state.draft) {
    const proc = pendingUnignore.proc;
    state.draft.ignoredProcesses =
      (state.draft.ignoredProcesses ?? []).filter((p) => !sameProc(p, proc));
    state.draft.appRules = (state.draft.appRules ?? []).filter(rule => !isIgnoreRule(rule, proc));
    syncDirty();
    renderIgnoredList();
    renderIgnoredSearch();
    renderAppRules();
    renderWindowsRows();
  }
  closeUnignoreModal();
});
const unignoreCancel = $("unignoreCancel");
if (unignoreCancel) unignoreCancel.addEventListener("click", () => closeUnignoreModal());
const unignoreBack = $("unignoreModalBack");
if (unignoreBack) unignoreBack.addEventListener("click", (e) => {
  if (e.target === unignoreBack) closeUnignoreModal();
});
if (unignoreBack) unignoreBack.addEventListener("keydown", (e) => {
  if (e.key === "Escape") closeUnignoreModal();
});

// ---------- hotkeys ----------
const MODIFIER_KEYS = new Set(["Control", "Alt", "Shift", "Meta", "AltGraph"]);
let hotkeyCapture = null;

function gestureModifiers(e) {
  const parts = [];
  if (e.ctrlKey) parts.push("Ctrl");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (e.metaKey) parts.push("Win");
  return parts;
}

function buildGesture(e) {
  const parts = gestureModifiers(e);
  if (!parts.length || e.isComposing || e.location === 3 || e.getModifierState?.("AltGraph")) return null;
  const k = e.key;
  let last;
  if (k === " " || k === "Spacebar") last = "Space";
  else if (/^(?:[a-z0-9]|F(?:[1-9]|1[0-2]))$/i.test(k ?? "")) last = k.toUpperCase();
  else if (/^Digit[0-9]$/.test(e.code ?? "")) last = e.code.at(-1);
  else return null;
  parts.push(last);
  return parts.join("+");
}

function stopHotkeyCapture() {
  if (!hotkeyCapture) return;
  const { button, previous } = hotkeyCapture;
  hotkeyCapture = null;
  button.textContent = state.draft?.hotkeys?.[button.dataset.hotkey] ?? previous;
  button.setAttribute("aria-pressed", "false");
  $("hotkeyCaptureHint").hidden = true;
}

function beginHotkeyCapture(button) {
  if (!state.draft) return;
  stopHotkeyCapture();
  hotkeyCapture = { button, previous: button.textContent };
  button.setAttribute("aria-pressed", "true");
  button.textContent = "Press keys…";
  $("hotkeyCaptureHint").textContent = "Hold Ctrl, Alt, Shift or Win, then press a letter, digit, Space or F1–F12. Esc cancels.";
  $("hotkeyCaptureHint").hidden = false;
}

window.addEventListener("keydown", (e) => {
  if (!hotkeyCapture) return;
  if (e.key === "Tab") { stopHotkeyCapture(); return; }
  e.preventDefault();
  e.stopPropagation();
  if (e.key === "Escape") { stopHotkeyCapture(); return; }
  if (e.repeat) return;
  const { button } = hotkeyCapture;
  if (MODIFIER_KEYS.has(e.key)) {
    button.textContent = [...gestureModifiers(e), "…"].join("+");
    return;
  }
  const gesture = buildGesture(e);
  if (!gesture) {
    $("hotkeyCaptureHint").textContent = "Use a modifier with a letter, digit, Space or F1–F12. Esc cancels.";
    return;
  }
  state.draft.hotkeys ??= {};
  state.draft.hotkeys[button.dataset.hotkey] = gesture;
  stopHotkeyCapture();
  syncDirty();
}, true);
window.addEventListener("blur", stopHotkeyCapture);

for (const btn of document.querySelectorAll("button[data-hotkey]")) {
  btn.addEventListener("click", () => beginHotkeyCapture(btn));
  btn.addEventListener("blur", () => {
    if (hotkeyCapture?.button === btn) stopHotkeyCapture();
  });
}

const hotkeysReset = $("hotkeysReset");
if (hotkeysReset) {
  hotkeysReset.addEventListener("click", () => {
    if (!state.draft) return;
    stopHotkeyCapture();
    state.draft.hotkeys ??= {};
    state.draft.hotkeys.arrangeNow = "Ctrl+Alt+Space";
    state.draft.hotkeys.pauseResume = "Ctrl+Alt+P";
    for (const btn of document.querySelectorAll("button[data-hotkey]")) {
      const v = state.draft.hotkeys[btn.dataset.hotkey];
      if (v) btn.textContent = v;
    }
    syncDirty();
  });
}

// ---------- windows snapshot (read-only + "copy process to draft") ----------
function renderWindowsRows() {
  const filterEl = $("windowsFilter");
  const q = ((filterEl && filterEl.value) || "").toLowerCase();
  const tb = document.querySelector("#windowsTable tbody");
  if (!tb) return;
  tb.innerHTML = "";
  const occupied = collectPinnedSlots();
  for (const w of windowsRows) {
    if (q && !((w.title + " " + w.process).toLowerCase().includes(q))) continue;
    const tr = document.createElement("tr");
    const tdT = document.createElement("td"); tdT.textContent = w.title;
    const tdP = document.createElement("td"); tdP.textContent = w.process;
    const tdE = document.createElement("td");
    tdE.textContent = isIgnored(w.process) ? "Ignored" :
      w.reason === "ignored-process" && !BUILTIN_PROCESSES.has(String(w.process).toLowerCase())
        ? "Check after Save" : w.eligible ? "Yes" : (w.reason || "No");
    const tdB = document.createElement("td");
    tdB.append(renderIgnoreButton(w));
    const tdPin = document.createElement("td");
    if (w.process && !BUILTIN_PROCESSES.has(String(w.process).toLowerCase()) && w.reason !== "system")
      tdPin.append(renderPinMap(w.process, occupied, w.hwnd));
    tr.append(tdT, tdP, tdE, tdPin, tdB);
    tb.appendChild(tr);
  }
}

function renderIgnoreButton(w) {
  const locked = !w.process || BUILTIN_PROCESSES.has(String(w.process).toLowerCase()) || w.reason === "system";
  const ignored = locked ? !!w.process : isIgnored(w.process);
  const button = document.createElement("button");
  if (w.hwnd) button.id = `ignore-${w.hwnd}`;
  button.className = "btn ignore-toggle";
  button.disabled = locked;
  button.textContent = locked ? (ignored ? "System" : "—") : ignored ? "Ignored ↶" : "Ignore";
  button.title = locked ? "Protected or unavailable process" :
    ignored ? "Stop ignoring this process" : "Ignore this process";
  button.setAttribute("aria-label", `${button.title}: ${w.process}`);
  button.setAttribute("aria-pressed", String(ignored));
  button.addEventListener("click", () => {
    if (locked) return;
    if (isIgnored(w.process)) askUnignore(w.process, button);
    else addIgnored(w.process);
  });
  return button;
}

// ---------- per-app rules (pin to a Grid slot; arrays REPLACED, never mutated) ----------
const PIN_SLOTS = ["A", "B", "C", "D", "E", "F", "G", "H"];
const PIN_MAP_ORDER = ["A", "E", "C", "H", "Off", "G", "D", "F", "B"];

function collectPinnedSlots() {
  const occupied = new Map();
  for (const rule of state.draft?.appRules ?? []) {
    const slot = String(rule.slot ?? "").toUpperCase();
    if (String(rule.action).toLowerCase() !== "pin" || !PIN_SLOTS.includes(slot)) continue;
    if (!occupied.has(slot)) occupied.set(slot, []);
    occupied.get(slot).push(rule.process);
  }
  return occupied;
}

function renderPinMap(proc, occupied, hwnd) {
  const map = document.createElement("div");
  map.className = "pin-map";
  const grid = document.createElement("div");
  grid.className = "pin-grid";
  grid.setAttribute("role", "group");
  grid.setAttribute("aria-label", `Grid pin for ${proc}`);
  const selected = pinnedSlotFor(proc);
  for (const slot of PIN_MAP_ORDER) {
    const button = document.createElement("button");
    const others = (occupied.get(slot) ?? []).filter(name => !sameProc(name, proc));
    const description = slot === "Off" ? "Unpin" :
      others.length ? `Slot ${slot} · pinned to ${others.join(", ")}` : `Slot ${slot}`;
    button.className = others.length ? "pin-cell occupied" : "pin-cell";
    button.textContent = slot === "Off" ? "×" : slot;
    button.title = description;
    button.setAttribute("aria-label", description);
    button.setAttribute("aria-pressed", String(selected === slot));
    if (hwnd) button.id = `pin-${hwnd}-${slot}`;
    button.addEventListener("click", () => {
      setPin(proc, slot);
      if (hwnd) $(button.id)?.focus();
    });
    grid.append(button);
  }
  map.append(grid);
  const reserved = [...occupied].filter(([, owners]) => owners.some(name => !sameProc(name, proc)));
  if (reserved.length) {
    const caption = document.createElement("small");
    caption.className = "pin-owners";
    caption.textContent = reserved.map(([slot, owners]) =>
      `${slot} · ${owners.filter(name => !sameProc(name, proc)).join(", ")}`).join(" / ");
    caption.title = caption.textContent;
    map.append(caption);
  }
  return map;
}

function pinnedSlotFor(proc) {
  const rule = (state.draft?.appRules ?? []).find((r) => sameProc(r.process, proc) && String(r.action).toLowerCase() === "pin");
  const slot = rule ? String(rule.slot || "").toUpperCase() : "";
  return PIN_SLOTS.includes(slot) ? slot : "Off";
}

function setPin(proc, slot) {
  if (!state.draft || !proc) return;
  if (pinnedSlotFor(proc) === slot) return;
  const rest = (state.draft.appRules ?? []).filter((r) => !sameProc(r.process, proc));
  state.draft.appRules = slot === "Off" ? rest : [...rest, { process: proc, action: "pin", slot }];
  syncDirty();
  renderIgnoredList();
  renderIgnoredSearch();
  renderAppRules();
  renderWindowsRows();
}

function renderAppRules() {
  const box = $("appRulesList");
  if (!box || !state.draft) return;
  box.innerHTML = "";
  const list = [...(state.draft.appRules ?? [])].sort((a, b) => String(a.process).localeCompare(String(b.process)));
  if (list.length === 0) {
    box.innerHTML = '<div class="empty">No rules yet. Pin an app from the table above.</div>';
    return;
  }
  for (const rule of list) {
    const row = document.createElement("div");
    row.className = "checkrow";
    const txt = document.createElement("span");
    txt.className = "txt";
    txt.textContent = `${rule.process} → ${String(rule.action).toLowerCase() === "ignore" ? "ignored" : "slot " + rule.slot}`;
    const btn = document.createElement("button");
    btn.className = "btn";
    btn.textContent = isIgnoreRule(rule, rule.process) ? "Stop ignoring" : "Unpin";
    btn.addEventListener("click", () => {
      if (isIgnoreRule(rule, rule.process)) { askUnignore(rule.process, btn); return; }
      if (!confirm(`Remove rule for "${rule.process}"?`)) return;
      state.draft.appRules = (state.draft.appRules ?? []).filter((r) => !sameProc(r.process, rule.process));
      syncDirty();
      renderAppRules();
      renderWindowsRows();
    });
    row.append(txt, btn);
    box.append(row);
  }
}

const windowsRefresh = $("windowsRefresh");
if (windowsRefresh) windowsRefresh.addEventListener("click", () => post({ type: "requestWindows" }));
const windowsFilter = $("windowsFilter");
if (windowsFilter) windowsFilter.addEventListener("input", renderWindowsRows);

for (const btn of document.querySelectorAll("#preset .seg")) {
  btn.addEventListener("click", () => {
    state.draft.preset = btn.dataset.preset;
    renderPresetSelection(btn.dataset.preset);
    renderPreview(btn.dataset.preset, state.draft?.layoutMode);
    syncDirty();
  });
}

function renderPresetSelection(preset) {
  for (const b of document.querySelectorAll("#preset .seg"))
    b.classList.toggle("sel", b.dataset.preset === preset);
}

for (const btn of document.querySelectorAll("#hintmode .seg")) {
  btn.addEventListener("click", () => {
    state.draft.focusHintMode = btn.dataset.hintmode;
    renderHintModeSelection(btn.dataset.hintmode);
    syncDirty();
  });
}

function renderHintModeSelection(mode) {
  for (const b of document.querySelectorAll("#hintmode .seg"))
    b.classList.toggle("sel", b.dataset.hintmode === mode);
}

for (const btn of document.querySelectorAll("#layoutmode .seg")) {
  btn.addEventListener("click", () => {
    if (!state.draft) return;
    state.draft.layoutMode = btn.dataset.layoutmode;
    renderLayoutModeSelection(btn.dataset.layoutmode);
    renderPreview(state.draft.preset, btn.dataset.layoutmode);
    syncDirty();
  });
}
$("editLayoutProfiles")?.addEventListener("click", () => post({ type: "openLayoutProfiles" }));
$("exportLayout").addEventListener("click", () => post({ type: "exportLayoutSettings" }));
$("importLayout").addEventListener("click", () => post({ type: "importLayoutSettings" }));
$("previewArrange").addEventListener("click", () => post({ type: "previewArrange" }));
$("cycleZoneWindow").addEventListener("click", () => post({ type: "cycleZoneWindow" }));
$("pauseFor").addEventListener("click", () => post({ type: "pauseFor", duration: $("pauseDuration").value }));
$("resume").addEventListener("click", () => post({ type: "resume" }));

function renderLayoutModeSelection(mode) {
  for (const b of document.querySelectorAll("#layoutmode .seg"))
    b.classList.toggle("sel", b.dataset.layoutmode === mode);
  // The size preset only applies to Grid mode: hide its card otherwise
  // (the stored value is kept and re-applied when back on Grid).
  const presetCard = $("presetCard");
  if (presetCard) presetCard.style.display = mode === "Grid" ? "" : "none";
  const legend = $("presetLegend");
  if (legend) legend.style.display = mode === "Grid" ? "" : "none";
}

// ---------- arrange sound ----------
for (const btn of document.querySelectorAll("#sounds .seg")) {
  btn.addEventListener("click", () => {
    if (!state.draft) return;
    state.draft.soundName = btn.dataset.sound;
    renderSoundSelection(btn.dataset.sound);
    renderUserSounds();
    post({ type: "previewSound", name: btn.dataset.sound });
    syncDirty();
  });
}

function renderSoundSelection(name) {
  for (const b of document.querySelectorAll("#sounds .seg"))
    b.classList.toggle("sel", b.dataset.sound === name);
}

function renderUserSounds() {
  const box = $("userSounds");
  if (!box) return;
  box.innerHTML = "";
  if (!userSounds || userSounds.length === 0) {
    box.innerHTML = '<div class="empty">No custom sounds yet. Add an MP3 or WAV.</div>';
    return;
  }
  for (const name of userSounds) {
    const row = document.createElement("div");
    row.className = "checkrow";
    const txt = document.createElement("span");
    txt.className = "txt";
    txt.textContent = name;
    if (state.draft && state.draft.soundName === name) {
      txt.style.fontWeight = "bold";
    }
    const play = document.createElement("button");
    play.className = "btn";
    play.textContent = "▶";
    play.title = "Preview";
    play.addEventListener("click", () => {
      if (!state.draft) return;
      state.draft.soundName = name;
      renderSoundSelection(name);
      renderUserSounds();
      post({ type: "previewSound", name });
      syncDirty();
    });
    const del = document.createElement("button");
    del.className = "btn";
    del.textContent = "Delete";
    del.addEventListener("click", () => {
      if (!confirm(`Delete sound "${name}"?`)) return;
      $("soundError").hidden = true;
      post({ type: "deleteSoundFile", name });
    });
    row.append(txt, play, del);
    box.append(row);
  }
}

const addSound = $("addSound");
if (addSound) addSound.addEventListener("click", () => {
  $("soundError").hidden = true;
  post({ type: "pickSoundFile" });
});

// ---------- auto-off drum picker (iOS clock style: hours 0-23, minutes 00-59) ----------
const DUR_ITEM_PX = 30;
const DUR_H_MAX = 23;
const DUR_M_MAX = 59;
const durProgrammaticPositions = new WeakMap();
let durSnapTimer = null;

function durPad2(n) {
  return String(n).padStart(2, "0");
}

function buildDurWheel(id, max, fmt) {
  const wheel = $(id);
  if (!wheel || wheel.dataset.built) return;
  wheel.dataset.built = "1";
  const padTop = document.createElement("div");
  padTop.className = "dur-pad";
  wheel.append(padTop);
  for (let i = 0; i <= max; i++) {
    const item = document.createElement("div");
    item.className = "dur-item";
    item.dataset.index = i;
    item.textContent = fmt(i);
    item.addEventListener("click", () => {
      wheel.scrollTop = i * DUR_ITEM_PX;
      onDurScroll();
      renderSoundDuration(state.draft?.soundAutoOffMinutes ?? 0);
    });
    wheel.append(item);
  }
  const padBottom = document.createElement("div");
  padBottom.className = "dur-pad";
  wheel.append(padBottom);
  wheel.addEventListener("scroll", () => {
    if (durProgrammaticPositions.get(wheel) === wheel.scrollTop) return;
    durProgrammaticPositions.delete(wheel);
    onDurScroll();
    clearTimeout(durSnapTimer);
    durSnapTimer = setTimeout(() => renderSoundDuration(state.draft?.soundAutoOffMinutes ?? 0), 110);
  });
}

function durSelected(wheel) {
  if (!wheel) return 0;
  const items = wheel.querySelectorAll(".dur-item");
  if (items.length === 0) return 0;
  return Math.max(0, Math.min(items.length - 1, Math.round(wheel.scrollTop / DUR_ITEM_PX)));
}

function onDurScroll() {
  if (!state.draft) return;
  const h = durSelected($("durH"));
  const m = durSelected($("durM"));
  state.draft.soundAutoOffMinutes = h * 60 + m;
  syncDirty();
}

function positionDurWheel(wheel, index) {
  if (!wheel) return;
  wheel.scrollTop = index * DUR_ITEM_PX;
  durProgrammaticPositions.set(wheel, wheel.scrollTop);
  for (const el of wheel.querySelectorAll(".dur-item"))
    el.classList.toggle("sel", Number(el.dataset.index) === index);
}

function renderSoundDuration(totalMinutes) {
  clearTimeout(durSnapTimer);
  durSnapTimer = null;

  buildDurWheel("durH", DUR_H_MAX, (i) => String(i));
  buildDurWheel("durM", DUR_M_MAX, durPad2);
  const total = Math.max(0, totalMinutes | 0);
  const h = Math.min(DUR_H_MAX, Math.floor(total / 60));
  const m = Math.min(DUR_M_MAX, total % 60);
  // CSS proximity snap can stop between rows. Land on the exact selected row.
  positionDurWheel($("durH"), h);
  positionDurWheel($("durM"), m);
}

// ---------- footer actions ----------
$("saveBtn").addEventListener("click", () => {
  if (!state.dirty || pendingSave) return;
  const sentDraft = JSON.parse(JSON.stringify(state.draft));
  const changes = {};
  for (const key of EDITABLE_KEYS) {
    const value = sentDraft[key];
    if (key === "hotkeys") {
      const hotkeys = Object.fromEntries(Object.entries(value ?? {})
        .filter(([name, gesture]) => gesture !== state.snapshot.hotkeys?.[name]));
      if (Object.keys(hotkeys).length) changes.hotkeys = hotkeys;
    } else if (differs(value, state.snapshot[key])) {
      changes[key] = value;
    }
  }
  pendingSave = { id: ++nextSaveId, draft: sentDraft };
  syncDirty();
  post({ type: "apply", requestId: pendingSave.id, data: changes });
});
$("cancelBtn").addEventListener("click", () => {
  if (pendingSave) return;
  stopHotkeyCapture();
  if (pendingUnignore) closeUnignoreModal(false);
  if (state.snapshot) adoptSnapshot(deferredSnapshot ?? state.snapshot);
});

$("openLog").addEventListener("click", () => post({ type: "openLogFile" }));
$("openLogsFolder").addEventListener("click", () => post({ type: "openLogsFolder" }));
$("openSettingsFolder").addEventListener("click", () => post({ type: "openSettingsFolder" }));

// ---------- tabs ----------
for (const nav of document.querySelectorAll(".navitem[data-page]")) {
  nav.addEventListener("click", () => {
    document.querySelectorAll(".navitem").forEach((b) => b.classList.remove("active"));
    nav.classList.add("active");
    document.querySelectorAll(".page").forEach((p) => p.classList.remove("active"));
    $(`page-${nav.dataset.page}`).classList.add("active");
    if (nav.dataset.page === "windows" && windowsRows.length === 0) post({ type: "requestWindows" });
  });
}

// ---------- live slot preview (same math as LayoutEngine.CreateSlotRectsUncached) ----------
const PRESET_RATIO = { Small: 0.90, Medium: 0.95, Large: 0.98 };

function renderPreview(preset, mode) {
  const show = (id, visible) => {
    const s = $(id);
    if (s) s.style.display = visible ? "" : "none";
  };
  const place = (id, left, top, width, height) => {
    const s = $(id);
    if (!s) return;
    s.style.left = `${left}%`;
    s.style.top = `${top}%`;
    s.style.width = `${width}%`;
    s.style.height = `${height}%`;
  };
  const custom = state.snapshot?.customZones;
  const grid = mode !== "Halves" && mode !== "Thirds" && mode !== "Custom";
  show("slotC", grid || mode === "Thirds" || (mode === "Custom" && custom?.splitSecondary === true));
  show("slotD", grid);
  let pct;
  if (mode === "Halves") {
    place("slotA", 0, 0, 50, 100);
    place("slotB", 50, 0, 50, 100);
    pct = 50;
  } else if (mode === "Thirds") {
    place("slotA", 0, 0, 100 / 3, 100);
    place("slotB", 100 / 3, 0, 100 / 3, 100);
    place("slotC", 200 / 3, 0, 100 / 3, 100);
    pct = 33;
  } else if (mode === "Custom") {
    const left = Math.min(80, Math.max(20, custom?.widthPercent ?? 60));
    const top = Math.min(80, Math.max(20, custom?.heightPercent ?? 50));
    place("slotA", 0, 0, left, 100);
    place("slotB", left, 0, 100 - left, custom?.splitSecondary ? top : 100);
    if (custom?.splitSecondary) place("slotC", left, top, 100 - left, 100 - top);
    pct = left;
  } else {
    const ratio = PRESET_RATIO[preset] ?? 0.90;
    const span = ratio * 100;
    const off = 100 - span;
    const pos = {
      slotA: [0, 0], slotC: [off, 0], slotD: [0, off], slotB: [off, off],
    };
    for (const [id, [left, top]] of Object.entries(pos)) {
      place(id, left, top, span, span);
    }
    pct = Math.round(ratio * 100);
  }
  $("ratioLabel").textContent = pct;
  $("ratioLabel2").textContent = pct;
}

// ---------- boot ----------
requestSnapshot();
function requestSnapshot() {
  post({ type: "requestSnapshot" });
  // When opened directly in a browser (no WebView2 bridge) the page stays on its
  // neutral defaults; nothing throws.
}
