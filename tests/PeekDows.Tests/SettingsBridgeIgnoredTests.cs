using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeIgnoredTests
{
    [Fact]
    public void Snapshot_ContainsIgnoredArrays()
    {
        using var tmp = new TempSettingsScope();
        var bridge = new SettingsBridge(tmp.Controller, appVersionProvider: () => "9.9.9");
        using var doc = JsonDocument.Parse(bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredProcesses").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredClasses").ValueKind);
    }

    [Fact]
    public void Apply_IgnoredLists_DedupsPersistsAndNotifies()
    {
        using var tmp = new TempSettingsScope();
        var bridge = new SettingsBridge(tmp.Controller, appVersionProvider: () => "9.9.9");
        var resp = bridge.HandleMessage("""{"type":"apply","data":{"ignoredProcesses":["a.exe","","A.EXE "],"ignoredClasses":["Foo"]}}""");
        Assert.Single(tmp.Controller.CurrentSettings.IgnoredProcesses);
        Assert.Equal("a.exe", tmp.Controller.CurrentSettings.IgnoredProcesses[0]);
        Assert.Equal(1, tmp.Controller.CommitCount);
        Assert.Equal(["a.exe"], tmp.Service.Load().IgnoredProcesses);
        Assert.Equal(["Foo"], tmp.Service.Load().IgnoredClasses);
        using var doc = JsonDocument.Parse(resp!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    private sealed class TempSettingsScope : IDisposable
    {
        private readonly string _tempDir;

        public TempSettingsScope()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(_tempDir);
            Service = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
            Controller = new SettingsBridgeTests.FakeBridgeController(Service.Load(), Service);
        }

        public SettingsService Service { get; }
        public SettingsBridgeTests.FakeBridgeController Controller { get; }

        public void Dispose()
        {
            Controller.Dispose();
            if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
        }
    }
}
