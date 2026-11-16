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
    children: [],
    append(...children) { this.children.push(...children); },
    set innerHTML(value) { this.children = []; },
    addEventListener(name, callback) { this[name] = callback; },
  });
  const document = {
    querySelectorAll: () => [],
    createElement: element,
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, element());
      return elements.get(id);
    },
  };
  const window = { chrome: { webview: {
    addEventListener() {},
    postMessage(message) { sent.push(message); },
  } } };
  vm.runInNewContext(script + "\n" + scenario, { assert, document, sent, window });
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
