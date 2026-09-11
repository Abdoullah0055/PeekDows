"use strict";

// Pure UI state: snapshot = last truth from C#, draft = editable copy.
const EDITABLE_KEYS = [
  "enabled", "autoArrange", "animate", "directionalFocus", "startWithWindows",
  "allowRepositionMaximized", "preset", "arrangeOnStartup",
  "showTrayNotifications", "thresholdPx",
];

const state = { snapshot: null, draft: null, dirty: false };

const $ = (id) => document.getElementById(id);
const post = (msg) => {
  if (window.chrome?.webview?.postMessage) window.chrome.webview.postMessage(msg);
};

function pick(snapshot) {
  const out = {};
  for (const k of EDITABLE_KEYS) out[k] = snapshot[k];
  return out;
}

// ---------- incoming messages ----------
window.chrome?.webview?.addEventListener("message", (event) => {
  const msg = event.data;
  if (!msg || typeof msg.type !== "string") return;

  if (msg.type === "settings") adoptSnapshot(msg.data);
  else if (msg.type === "externalChange") onExternalChange(msg.data);
  else if (msg.type === "applied") onApplied(msg);
});

function adoptSnapshot(data) {
  state.snapshot = data;
  state.draft = pick(data);
  setControlsFrom(data);
  markClean();
  $("versionLine").textContent = `v${data.appVersion} · settings schema v${data.version}`;
  renderPresetSelection(data.preset);
  renderPreview(data.preset);
  $("thresholdValue").textContent = `${data.thresholdPx} px`;
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
