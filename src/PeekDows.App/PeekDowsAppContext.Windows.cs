using System.Collections.Generic;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    public System.Collections.Generic.IReadOnlyList<Settings.WindowRow> GetWindowsSnapshot()
        => Settings.WindowsSnapshotProvider.Build(_discoveryService, _classifier);
}
