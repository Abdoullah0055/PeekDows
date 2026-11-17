const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(
  path.join(__dirname, "../src/PeekDows.App/wwwroot/app.js"), "utf8");

function runInSettingsPage(scenario) {
  const elements = new Map();
  const sent = [];
  const element = () => ({
    style: {},
    dataset: {},
    value: "",
    children: [],
    querySelectorAll: () => [],
    append(...children) { this.children.push(...children); },
    appendChild(child) { this.children.push(child); },
    setAttribute(key, value) { this[key] = value; },
    focus() {},
    set innerHTML(value) { this.children = []; },
    addEventListener(name, callback) { this[name] = callback; },
  });
  const document = {
    querySelectorAll: () => [],
    querySelector: selector => document.getElementById(selector),
    createElement: element,
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, element());
      return elements.get(id);
    },
  };
  const window = { addEventListener(name, callback) { this[name] = callback; }, chrome: { webview: {
    addEventListener() {},
    postMessage(message) { sent.push(message); },
  } } };
  vm.runInNewContext(script + "\n" + scenario, { assert, document, sent, window, setTimeout: callback => callback() });
}

test("preview shows the correct slots after switching modes", () => {
  runInSettingsPage(`
    for (const [mode, visible] of [["Halves", 2], ["Thirds", 3], ["Grid", 4], ["Halves", 2]]) {
      renderPreview("Small", mode);
      const slots = ["slotA", "slotB", "slotC", "slotD"];
      assert.equal(slots.filter(id => $(id).style.display !== "none").length, visible);
    }
  `);
});

test("pin map shows other apps in their reserved positions", () => {
  runInSettingsPage(`
    state.snapshot = { appRules: [
      {process:"editor.exe", action:"pin", slot:"C"},
      {process:"notes.exe", action:"pin", slot:"H"}
    ] };
    state.draft = pick(state.snapshot);
    const map = renderPinMap("notes.exe", collectPinnedSlots());
    const [a, e, c, h] = map.children[0].children;
    assert.deepEqual([a.textContent, e.textContent, c.textContent, h.textContent], ["A", "E", "C", "H"]);
    assert.match(c.title, /editor.exe/);
    assert.equal(h["aria-pressed"], "true");
    a.click();
    assert.equal(pinnedSlotFor("notes.exe"), "A");
  `);
});

test("Windows exclusion can be enabled and removed before saving", () => {
  runInSettingsPage(`
    state.snapshot = { ignoredProcesses: [], appRules: [] };
    state.draft = pick(state.snapshot);
    const row = { process: "editor.exe", eligible: true };
    renderIgnoreButton(row).click();
    assert.equal(isIgnored("editor.exe"), true);
    const ignored = renderIgnoreButton(row);
    assert.equal(ignored["aria-pressed"], "true");
    ignored.click();
    $("unignoreConfirm").click();
    assert.equal(isIgnored("editor.exe"), false);
    assert.equal(state.dirty, false);
  `);
});

test("Cancel restores the latest external snapshot and keeps newly imported sounds", () => {
  runInSettingsPage(`
    adoptSnapshot({ animate: true, autoArrange: false, userSounds: ["old.wav"] });
    state.draft.animate = false;
    syncDirty();
    onExternalChange({ animate: true, autoArrange: true, userSounds: ["old.wav"] });
    onSoundFile({ userSounds: ["old.wav", "new.wav"] });
    $("cancelBtn").click();
    assert.equal(state.draft.animate, true);
    assert.equal(state.draft.autoArrange, true);
    assert.equal(state.dirty, false);
    assert.equal(userSounds[1], "new.wav");
    assert.equal($("externalBanner").hidden, true);
  `);
});

test("hotkey capture waits for modifiers and leaves unsupported keys out of the draft", () => {
  runInSettingsPage(`
    state.snapshot = { hotkeys: { arrangeNow: "Ctrl+Alt+Space" } };
    state.draft = pick(state.snapshot);
    const button = $("capture");
    button.dataset.hotkey = "arrangeNow";
    beginHotkeyCapture(button);
    const press = (key, mods = {}) => window.keydown({ key, ...mods, preventDefault() {}, stopPropagation() {} });
    press("Control", {ctrlKey: true});
    press("Alt", {altKey: true, ctrlKey: true});
    press("ArrowLeft", {ctrlKey: true, altKey: true});
    assert.equal(state.draft.hotkeys.arrangeNow, "Ctrl+Alt+Space");
    press("F9", {ctrlKey: true, altKey: true});
    assert.equal(state.draft.hotkeys.arrangeNow, "Ctrl+Alt+F9");
    beginHotkeyCapture(button);
    press("Escape");
    assert.equal(button.textContent, "Ctrl+Alt+F9");
  `);
});

test("an unrelated save preserves a disabled overlay icon setting", () => {
  runInSettingsPage(`
    state.snapshot = { overlayShowIcons: false, animate: true, hotkeys: {} };
    state.draft = pick(state.snapshot);
    syncDirty();
    assert.equal(state.dirty, false);
    state.draft.animate = false;
    $("saveBtn").click();
    assert.equal(JSON.stringify(sent.at(-1).data), '{"animate":false}');
  `);
});

test("custom preview draws the configured splits and clears inactive slots", () => {
  runInSettingsPage(`
    state.snapshot = { customZones: { widthPercent: 65, heightPercent: 45, splitSecondary: true } };
    renderPreview("Small", "Custom");
    assert.equal($("slotA").style.width, "65%");
    assert.equal($("slotB").style.height, "45%");
    assert.equal($("slotC").style.top, "45%");
    assert.equal($("slotD").style.display, "none");
    state.snapshot.customZones.splitSecondary = false;
    renderPreview("Small", "Custom");
    assert.equal($("slotB").style.height, "100%");
    assert.equal($("slotC").style.display, "none");
  `);
});

test("a sound import refreshes the library and keeps pending edits", () => {
  runInSettingsPage(`
    state.snapshot = { animate: true, soundName: "chime-soft" };
    state.draft = { animate: false, soundName: "chime-soft" };
    state.dirty = true;
    sent.length = 0;
    onSoundFile({ name: "new.wav", userSounds: ["existing.wav", "new.wav"] });
    assert.equal($("userSounds").children[1]?.children[0].textContent, "new.wav");
    assert.equal(state.draft.animate, false);
    assert.equal(state.dirty, true);
    assert.equal(sent.length, 0);
  `);
});
