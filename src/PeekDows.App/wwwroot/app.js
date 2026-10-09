"use strict";

// Pure UI state: snapshot = last truth from C#, draft = editable copy.
const EDITABLE_KEYS = [
  "enabled", "autoArrange", "animate", "directionalFocus", "startWithWindows",
  "allowRepositionMaximized", "preset", "arrangeOnStartup",
  "showTrayNotifications", "thresholdPx",
  "ignoredProcesses", "ignoredClasses", "hotkeys",
  "focusHintMode",
];

const state = { snapshot: null, draft: null, dirty: false };

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

// ---------- incoming messages ----------
window.chrome?.webview?.addEventListener("message", (event) => {
  const msg = event.data;
  if (!msg || typeof msg.type !== "string") return;

  if (msg.type === "settings") adoptSnapshot(msg.data);
  else if (msg.type === "externalChange") onExternalChange(msg.data);
  else if (msg.type === "applied") onApplied(msg);
  else if (msg.type === "windows") { windowsRows = msg.data ?? []; renderWindowsRows(); }
});

function adoptSnapshot(data) {
  state.snapshot = data;
  // Beta default on both copies: keeps dirty tracking exact even if an older
  // bridge ever omits this key (undefined vs default would read as dirty).
  state.snapshot.focusHintMode ??= "Overlay";
  state.draft = pick(data);
  state.draft.ignoredProcesses ??= [];
  state.draft.ignoredClasses ??= [];
  state.draft.hotkeys ??= {};
  state.draft.focusHintMode ??= "Overlay";
  setControlsFrom(data);
  markClean();
  $("versionLine").textContent = `v${data.appVersion} · settings schema v${data.version}`;
  renderPresetSelection(data.preset);
  renderPreview(data.preset);
  $("thresholdValue").textContent = `${data.thresholdPx} px`;
  renderHintModeSelection(state.draft.focusHintMode);
}

function onExternalChange(data) {
  if (state.dirty) {
    $("externalBanner").hidden = false;
  } else {
    $("externalBanner").hidden = true;
    adoptSnapshot(data);
  }
}

function onApplied(msg) {
  if (!msg.ok) {
    $("unsaved").textContent = "⚠ Save failed — see log";
    $("unsaved").style.visibility = "visible";
    return;
  }
  $("externalBanner").hidden = true;
  if (msg.data) adoptSnapshot(msg.data);   // fresh snapshot rides along
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
  renderIgnoredList();
  renderIgnoredSearch();
  const ic = $("ignoredClasses");
  if (ic) ic.value = (data.ignoredClasses ?? []).join("\n");
  const hk = data.hotkeys ?? {};
  for (const btn of document.querySelectorAll("button[data-hotkey]")) {
    const name = btn.dataset.hotkey;
    if (hk[name]) btn.textContent = hk[name];
  }
  // Windows table stays empty until Refresh — never filled from snapshot.
}

// ---------- dirty tracking ----------
function markClean() {
  if (state.dirty) post({ type: "dirtyChanged", value: false });
  state.dirty = false;
  $("saveBtn").disabled = true;
  $("unsaved").style.visibility = "hidden";
}

function syncDirty() {
  const dirty = JSON.stringify(state.draft) !== JSON.stringify(pick(state.snapshot));
  if (dirty !== state.dirty) {
    state.dirty = dirty;
    post({ type: "dirtyChanged", value: dirty });
  }
  $("saveBtn").disabled = !dirty;
  $("unsaved").style.visibility = dirty ? "visible" : "hidden";
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

function isIgnored(proc) {
  return (state.draft?.ignoredProcesses ?? []).some((p) => sameProc(p, proc));
}

function addIgnored(proc) {
  if (!state.draft || !proc || isIgnored(proc)) return;
  state.draft.ignoredProcesses = [...(state.draft.ignoredProcesses ?? []), proc];
  syncDirty();
  renderIgnoredList();
  renderIgnoredSearch();
}

let pendingUnignore = null; // { proc } while the confirm modal is open

function askUnignore(proc) {
  pendingUnignore = { proc };
  $("unignoreName").textContent = proc;
  $("unignoreModalBack").hidden = false;
}

function closeUnignoreModal() {
  pendingUnignore = null;
  $("unignoreModalBack").hidden = true;
  // Re-render: the unchecked box returns to checked since the draft never changed.
  renderIgnoredList();
  renderIgnoredSearch();
}

function onIgnoredCheck(proc, checkbox) {
  if (!state.draft) return;
  if (checkbox.checked) addIgnored(proc);
  else {
    // Keep the box visually unchecked under the modal; Cancel re-renders it checked.
    askUnignore(proc);
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
    syncDirty();
  }
  closeUnignoreModal();
});
const unignoreCancel = $("unignoreCancel");
if (unignoreCancel) unignoreCancel.addEventListener("click", closeUnignoreModal);
const unignoreBack = $("unignoreModalBack");
if (unignoreBack) unignoreBack.addEventListener("click", (e) => {
  if (e.target === unignoreBack) closeUnignoreModal();
});

// ---------- hotkeys (capture next keydown as "Ctrl+Alt+X") ----------
function buildGesture(e) {
  const parts = [];
  if (e.ctrlKey) parts.push("Ctrl");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (e.metaKey) parts.push("Win");
  const k = e.key;
  if (k === "Control" || k === "Alt" || k === "Shift" || k === "Meta") return null;
  let last;
  if (k === " " || k === "Spacebar") last = "Space";
  else if (typeof k === "string" && k.length === 1) last = k.toUpperCase();
  else if (typeof k === "string" && /^F\d{1,2}$/i.test(k)) last = k.toUpperCase();
  else last = k;
  if (parts.length === 0) return null;
  parts.push(last);
  return parts.join("+");
}

for (const btn of document.querySelectorAll("button[data-hotkey]")) {
  btn.addEventListener("click", () => {
    if (!state.draft) return;
    const name = btn.dataset.hotkey;
    const prev = btn.textContent;
    btn.textContent = "Press keys…";
    const onKey = (e) => {
      e.preventDefault();
      e.stopPropagation();
      const gesture = buildGesture(e);
      if (gesture) {
        state.draft.hotkeys ??= {};
        state.draft.hotkeys[name] = gesture;
        btn.textContent = gesture;
        syncDirty();
      } else {
        btn.textContent = prev;
      }
    };
    window.addEventListener("keydown", onKey, { once: true, capture: true });
  });
}

const hotkeysReset = $("hotkeysReset");
if (hotkeysReset) {
  hotkeysReset.addEventListener("click", () => {
    if (!state.draft) return;
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
  for (const w of windowsRows) {
    if (q && !((w.title + " " + w.process).toLowerCase().includes(q))) continue;
    const tr = document.createElement("tr");
    const tdT = document.createElement("td"); tdT.textContent = w.title;
    const tdP = document.createElement("td"); tdP.textContent = w.process;
    const tdE = document.createElement("td"); tdE.textContent = w.eligible ? "yes" : (w.reason || "no");
    const tdB = document.createElement("td");
    const btn = document.createElement("button");
    btn.className = "btn";
    btn.textContent = "Ignorer ce process";
    btn.addEventListener("click", () => {
      // Adds to the ignored-apps draft (persistent on Save) and refreshes that
      // page if visible — user presses Save to apply.
      addIgnored(w.process);
    });
    tdB.appendChild(btn);
    tr.append(tdT, tdP, tdE, tdB);
    tb.appendChild(tr);
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
    renderPreview(btn.dataset.preset);
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

// ---------- footer actions ----------
$("saveBtn").addEventListener("click", () => post({ type: "apply", data: state.draft }));
$("cancelBtn").addEventListener("click", () => {
  markClean();
  window.close();   // SettingsHostForm maps WindowCloseRequested → Close()
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
  });
}

// ---------- live slot preview (same math as LayoutEngine.CreateSlotRectsUncached) ----------
const PRESET_RATIO = { Small: 0.90, Medium: 0.95, Large: 0.98 };

function renderPreview(preset) {
  const ratio = PRESET_RATIO[preset] ?? 0.90;
  const span = ratio * 100;
  const off = 100 - span;
  const pos = {
    slotA: [0, 0], slotC: [off, 0], slotD: [0, off], slotB: [off, off],
  };
  for (const [id, [left, top]] of Object.entries(pos)) {
    const s = $(id);
    s.style.left = `${left}%`;
    s.style.top = `${top}%`;
    s.style.width = `${span}%`;
    s.style.height = `${span}%`;
  }
  const pct = Math.round(ratio * 100);
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
