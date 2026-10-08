# Windows UI snippet (Agent D: paste into `wwwroot/index.html` + `app.js`)

Read-only page. Refresh posts `{type:"requestWindows"}` and renders the
`{type:"windows", data: [...]}` answer. "Ignorer ce process" copies the
process name into the ignored-processes draft (no save — user presses Save).

```html
<section class="page" id="page-windows">
  <h2>Windows</h2>
  <p class="sub">Read-only snapshot. Refresh to reload.</p>
  <div class="row">
    <input id="windowsFilter" type="text" placeholder="Filter by title or process…" />
    <button id="windowsRefresh" class="btn">Refresh</button>
  </div>
  <div class="card">
    <table class="table" id="windowsTable">
      <thead>
        <tr><th>Title</th><th>Process</th><th>Eligible</th><th></th></tr>
      </thead>
      <tbody></tbody>
    </table>
  </div>
</section>
```

```js
// windows section — D: merge into app.js (snapshot handler + refresh + filter)
let windowsRows = [];
function renderWindowsRows() {
  const q = (document.getElementById("windowsFilter").value || "").toLowerCase();
  const tb = document.querySelector("#windowsTable tbody");
  tb.innerHTML = "";
  for (const w of windowsRows) {
    if (q && !(w.title + " " + w.process).toLowerCase().includes(q)) continue;
    const tr = document.createElement("tr");
    const tdT = document.createElement("td"); tdT.textContent = w.title;
    const tdP = document.createElement("td"); tdP.textContent = w.process;
    const tdE = document.createElement("td"); tdE.textContent = w.eligible ? "yes" : (w.reason || "no");
    const tdB = document.createElement("td");
    const btn = document.createElement("button");
    btn.className = "btn";
    btn.textContent = "Ignorer ce process";
    btn.addEventListener("click", () => {
      // copies into the ignored-processes draft textarea, no save
      const ta = document.getElementById("ignoredProcesses");
      const lines = ta.value.split("\n").map(s => s.trim()).filter(Boolean);
      if (!lines.some(l => l.toLowerCase() === w.process.toLowerCase())) {
        lines.push(w.process);
        ta.value = lines.join("\n");
        draft.ignoredProcesses = lines;
        syncDirty();
      }
    });
    tdB.appendChild(btn);
    tr.append(tdT, tdP, tdE, tdB);
    tb.appendChild(tr);
  }
}
document.getElementById("windowsRefresh").addEventListener("click", () => post({ type: "requestWindows" }));
document.getElementById("windowsFilter").addEventListener("input", renderWindowsRows);
// on message {type:"windows"}: windowsRows = msg.data; renderWindowsRows();
```
