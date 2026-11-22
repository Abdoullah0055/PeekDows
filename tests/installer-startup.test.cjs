const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const script = fs.readFileSync(path.join(__dirname, "../installer/PeekDows.iss"), "utf8");
const runEntries = script.match(/^\[Run\]\s*([\s\S]*?)^\[/m)[1]
  .split(/\r?\n/)
  .filter(line => line.startsWith("Filename:"));

test("Setup checks the previous per-user uninstall registration on initialization", () => {
  const appId = script.match(/^AppId=\{({[^}]+})\r?$/m)[1];
  const initializeSetup = script.match(/^function InitializeSetup\(\): Boolean;\s*begin\s*([\s\S]*?)\s*end;/m)[1];

  assert.match(script, /^PrivilegesRequired=lowest\r?$/m);
  const detection = initializeSetup.match(/IsUpgrade := RegKeyExists\(HKCU,\s*'([^']+)'\);/);
  assert.ok(detection);
  assert.equal(detection[1], "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + appId + "_is1");
  assert.ok(detection.index < initializeSetup.indexOf("KillRunningApp;"));
});

test("Only the initial setup can invoke the startup hook in either mode", () => {
  const hooks = runEntries.filter(entry => entry.includes('Parameters: "--enable-startup"'));
  assert.deepEqual(hooks.map(entry => entry.split("Check: ")[1]), [
    "ShouldEnableStartupOnGuiInstall",
    "ShouldEnableStartupOnSilentInstall",
  ]);
  assert.match(script, /function ShouldEnableStartupOnGuiInstall: Boolean;\s*begin\s*Result := \(not IsUpgrade\) and \(not WizardSilent\(\)\);\s*end;/);
  assert.match(script, /function ShouldEnableStartupOnSilentInstall: Boolean;\s*begin\s*Result := \(not IsUpgrade\) and WizardSilent\(\);\s*end;/);
});

test("GUI and silent installs retain their normal launch conditions", () => {
  const launches = runEntries.filter(entry => !entry.includes("Parameters:"));
  assert.equal(launches.length, 2);
  assert.match(launches[0], /Flags: nowait postinstall skipifsilent; Check: WizardSilentLaunchAllowed$/);
  assert.match(launches[1], /Flags: nowait; Check: WizardIsSilent$/);
});
