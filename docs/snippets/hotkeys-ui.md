# Hotkeys UI snippet (Agent D: copy-paste into `wwwroot/`)

Protocol: `draft.hotkeys = { arrangeNow, pauseResume }` (gesture strings
`"Ctrl+Alt+X"`). Save sends them inside the `apply{data}` payload; the bridge
answers `applied{ok:false, error:"hotkey-conflict"}` when the OS refuses the
combo — keep the old displayed value in that case.

```html
<section class="page" id="page-hotkeys">
  <h2>Hotkeys</h2>
  <p class="sub">Click a combo, press the new keys, then Save.</p>
  <div class="card">
    <div class="row"><span>Arrange now</span><button class="btn capture" data-hotkey="arrangeNow">Ctrl+Alt+Space</button></div>
    <div class="row"><span>Pause / resume</span><button class="btn capture" data-hotkey="pauseResume">Ctrl+Alt+P</button></div>
    <div class="row"><button class="btn" id="hotkeysReset">Reset defaults</button></div>
  </div>
</section>
```

```js
// hotkeys section — D: merge into app.js EDITABLE_KEYS + setControlsFrom/syncDirty
// draft.hotkeys <-> buttons [data-hotkey]; capture next keydown as gesture string.
// Reset defaults: arrangeNow="Ctrl+Alt+Space", pauseResume="Ctrl+Alt+P".
```
