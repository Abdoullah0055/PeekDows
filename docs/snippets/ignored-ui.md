# Ignored apps — snippet UI pour Agent D

Copier-coller tel quel dans `src/PeekDows.App/wwwroot/index.html` (section page).

```html
<section class="page" id="page-ignored">
  <h2>Ignored apps</h2>
  <p class="sub">One entry per line. Applied on Save.</p>
  <div class="card">
    <div class="t">Ignored processes</div>
    <textarea id="ignoredProcesses" rows="6" spellcheck="false"></textarea>
  </div>
  <div class="card">
    <div class="t">Ignored window classes</div>
    <textarea id="ignoredClasses" rows="6" spellcheck="false"></textarea>
  </div>
  <div class="card"><div class="t">Always ignored (built-in)</div><div class="d">SearchHost.exe, StartMenuExperienceHost.exe, Shell_TrayWnd, Progman… (read-only)</div></div>
</section>
```

```js
// ignored section — D: fusionner dans app.js EDITABLE_KEYS + setControlsFrom/syncDirty
// Ajouter "ignoredProcesses" et "ignoredClasses" à EDITABLE_KEYS.
// setControlsFrom: document.getElementById("ignoredProcesses").value = (draft.ignoredProcesses ?? []).join("\n");
//                  document.getElementById("ignoredClasses").value = (draft.ignoredClasses ?? []).join("\n");
// Lecture Save: draft.ignoredProcesses = textarea("ignoredProcesses").split("\n"), idem ignoredClasses.
// syncDirty() existant (JSON.stringify) gère déjà arrays — ne pas réécrire.
```
