using System.Runtime.InteropServices;
using System.Text;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

/// <summary>Bounded history of window states captured before arranging.</summary>
public sealed class ArrangeHistoryService
{
    private const int MaxSnapshots = 3;
    private const uint AsyncWindowPlacement = 0x0004;
    private readonly FileLogger? _logger;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly bool _useNativePlacement;
    private readonly List<Entry[]> _history = new();
    private readonly object _lock = new();

    public sealed class Snapshot
    {
        internal Snapshot(Entry[] entries) => Entries = entries;
        internal Entry[] Entries { get; }
    }

    internal readonly record struct Entry(IntPtr Handle, Rect Rect, bool WasMaximized,
        uint Process, uint Thread, string ClassName, WINDOWPLACEMENT? NativePlacement);

    public ArrangeHistoryService(FileLogger? logger = null, Func<IntPtr, bool>? isWindowValid = null)
    {
        _logger = logger;
        _useNativePlacement = isWindowValid is null;
        _isWindowValid = isWindowValid ?? NativeMethods.IsWindow;
    }

    public bool CanUndo
    {
        get { lock (_lock) return _history.Count > 0; }
    }

    public Snapshot Capture(IEnumerable<ManagedWindow> windows)
    {
        var entries = new List<Entry>();
        foreach (var window in windows)
        {
            if (window is null || window.Hwnd == IntPtr.Zero)
                continue;

            if (!_useNativePlacement)
            {
                entries.Add(new Entry(window.Hwnd, window.CurrentRect, window.IsMaximized, 0, 0, "", null));
                continue;
            }

            uint thread = NativeMethods.GetWindowThreadProcessId(window.Hwnd, out uint process);
            if (thread == 0 || (window.ProcessId != 0 && process != (uint)window.ProcessId))
                continue;

            var native = new WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!NativeMethods.GetWindowPlacement(window.Hwnd, ref native))
                continue;
            native.Flags |= AsyncWindowPlacement;
            entries.Add(new Entry(window.Hwnd, window.CurrentRect, window.IsMaximized, process, thread,
                ReadClass(window.Hwnd), native));
        }
        return new Snapshot(entries.ToArray());
    }

    public void Commit(Snapshot snapshot, IEnumerable<IntPtr> movedWindows)
    {
        var moved = movedWindows.ToHashSet();
        var entries = snapshot.Entries.Where(entry => moved.Contains(entry.Handle)).ToArray();
        if (entries.Length == 0)
            return;

        lock (_lock)
        {
            _history.Add(entries);
            if (_history.Count > MaxSnapshots)
                _history.RemoveAt(0);
        }
    }

    public void Push(IReadOnlyList<ManagedWindow>? windows)
    {
        if (windows is null)
            return;
        var snapshot = Capture(windows);
        Commit(snapshot, snapshot.Entries.Select(entry => entry.Handle));
    }

    public void Undo(Action<IntPtr>? beforeRestore = null) => Undo(new Win32WindowPositioner(), beforeRestore);

    internal void Undo(IWindowPositioner positioner, Action<IntPtr>? beforeRestore = null)
    {
        Entry[] snapshot;
        lock (_lock)
        {
            if (_history.Count == 0)
                return;
            snapshot = _history[^1];
            _history.RemoveAt(_history.Count - 1);
        }

        foreach (var entry in snapshot)
        {
            try
            {
                beforeRestore?.Invoke(entry.Handle);
                if (!_isWindowValid(entry.Handle))
                    continue;

                if (entry.NativePlacement is { } native)
                {
                    uint thread = NativeMethods.GetWindowThreadProcessId(entry.Handle, out uint process);
                    if (thread != entry.Thread || process != entry.Process || ReadClass(entry.Handle) != entry.ClassName)
                        continue;

                    if (!NativeMethods.SetWindowPlacement(entry.Handle, in native))
                        _logger?.Warn($"Undo placement failed for hwnd={entry.Handle}: {Marshal.GetLastWin32Error()}");
                    continue;
                }

                positioner.SetWindowPosition(entry.Handle, entry.Rect);
                if (entry.WasMaximized)
                    positioner.MaximizeWindow(entry.Handle);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Undo failed for hwnd={entry.Handle}: {ex.Message}");
            }
        }
    }

    private static string ReadClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
