const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const directory = path.join(__dirname, "../src/PeekDows.App/wwwroot");
const script = fs.readFileSync(path.join(directory, "guide.js"), "utf8");
const html = fs.readFileSync(path.join(directory, "index.html"), "utf8");

function loadGuide() {
  const ids = new Map();
  const visited = [];
  const content = { scrollTop: 100 };
  const state = { dirty: true, draft: { animate: false }, snapshot: { hotkeys: { arrangeNow: "Ctrl+F9" } } };
  const element = () => ({
    children: [],
    append(...items) { this.children.push(...items); },
    replaceChildren(...items) { this.children = items; },
    addEventListener(name, callback) { this[name] = callback; },
    focus() { this.focused = true; },
  });
  const get = id => {
    assert.ok(html.includes(`id="${id}"`), `Present in Settings: ${id}`);
    if (!ids.has(id)) ids.set(id, element());
    return ids.get(id);
  };
  const document = {
    createElement: element,
    querySelector(selector) {
      if (selector === ".content") return content;
      const page = selector.match(/data-page="([^"]+)"/)[1];
      assert.ok(html.includes(`data-page="${page}"`));
      return { click() { visited.push(page); }, focus() {} };
    },
  };
  vm.runInNewContext(script, { $: get, document, state });
  return { get, state, visited, content };
}

test("the guide starts on demand, teaches the configured hotkey and leaves the draft intact", () => {
  const { get, state, visited, content } = loadGuide();
  assert.deepEqual(visited, []);
  get("openGuide").click();
  get("guideStart").click();
  assert.equal(get("guideProgress").textContent, "Chapitre 1 sur 7");
  assert.ok(get("guideDetails").children[0].textContent.includes("Ctrl+F9"));
  assert.equal(get("guideTitle").focused, true);
  assert.equal(get("guidePrevious").disabled, true);
  get("guideLinks").children[0].click();
  assert.deepEqual(visited, ["guide", "hotkeys"]);
  assert.equal(content.scrollTop, 0);
  assert.deepEqual(state.draft, { animate: false });
  assert.equal(state.dirty, true);
});

test("chapters can be selected, resumed, completed and restarted", () => {
  const { get } = loadGuide();
  get("guideChapters").children.at(-1).children[0].click();
  assert.equal(get("guideNext").textContent, "Terminer");
  get("guideContents").click();
  assert.equal(get("guideResume").hidden, false);
  get("guideResume").click();
  get("guidePrevious").click();
  assert.equal(get("guideTitle").textContent, "Personnaliser le son");
  get("guideNext").click();
  get("guideNext").click();
  assert.equal(get("guideDone").hidden, false);
  get("guideReview").click();
  assert.equal(get("guideResume").hidden, true);
  get("guideStart").click();
  assert.equal(get("guideProgress").textContent, "Chapitre 1 sur 7");
});
