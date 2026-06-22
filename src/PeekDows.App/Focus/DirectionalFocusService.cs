using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

public sealed class DirectionalFocusService : IDisposable
{
    private const int TickIntervalMs = 40;

    /// <summary>
    /// Minimum gap between two activations while a Ctrl+Shift gesture is held. This is the
    /// pacing for HEALTHY slot-hopping: short enough that switching windows feels instant,
    /// long enough that we don't fire a second activation before the OS has settled the first.
    /// Per-hwnd cooldowns (foreground-mismatch, unstable tracker) carry the heavy protection;
    /// this global throttle only paces the gesture.
    /// </summary>
    private const int CooldownMs = 150;

    private const int RebuildCooldownMs = 500;

    private readonly DirectionalFocusGestureDetector _gestureDetector;
    private readonly DirectionalFocusRegistry _registry;
    private readonly IMonitorResolver _monitorResolver;
    private readonly WindowActivationService _activationService;
    private readonly IVirtualDesktopService _virtualDesktopService;
    private readonly DirectionalFocusLayoutSnapshotService _snapshotService;
    private readonly Func<IReadOnlyList<IntPtr>> _candidateWindowSource;
    private readonly Func<int> _getThresholdPx;
    private readonly FileLogger? _logger;
    private readonly DirectionalFocusInputGate _inputGate;
    private readonly Func<DateTime> _nowProvider;

    private readonly System.Windows.Forms.Timer _timer;

    private bool _gestureWasActive;
    private Point _anchor;
    private Rect _anchorMonitor;
    private DirectionalFocusSlot? _lastTriggeredSlot;
    private DateTime _lastFocusTime = DateTime.MinValue;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private DateTime _lastRegistryRebuildAttempt = DateTime.MinValue;
    private bool _disposed;

    public DirectionalFocusService(
        DirectionalFocusGestureDetector gestureDetector,
        DirectionalFocusRegistry registry,
        IMonitorResolver monitorResolver,
        WindowActivationService activationService,
        IVirtualDesktopService virtualDesktopService,
        DirectionalFocusLayoutSnapshotService snapshotService,
        Func<IReadOnlyList<IntPtr>> candidateWindowSource,
        Func<int> getThresholdPx,
        FileLogger? logger = null)
        : this(gestureDetector, registry, monitorResolver, activationService, virtualDesktopService,
               snapshotService, candidateWindowSource, getThresholdPx, new DirectionalFocusInputGate(), () => DateTime.Now, logger)
    {
    }

    internal DirectionalFocusService(
        DirectionalFocusGestureDetector gestureDetector,
        DirectionalFocusRegistry registry,
        IMonitorResolver monitorResolver,
        WindowActivationService activationService,
        IVirtualDesktopService virtualDesktopService,
        DirectionalFocusLayoutSnapshotService snapshotService,
        Func<IReadOnlyList<IntPtr>> candidateWindowSource,
        Func<int> getThresholdPx,
        DirectionalFocusInputGate inputGate,
        FileLogger? logger = null)
        : this(gestureDetector, registry, monitorResolver, activationService, virtualDesktopService,
               snapshotService, candidateWindowSource, getThresholdPx, inputGate, () => DateTime.Now, logger)
    {
    }

    /// <summary>
    /// Internal ctor that accepts a custom clock for deterministic throttle/cooldown tests.
    /// </summary>
    internal DirectionalFocusService(
        DirectionalFocusGestureDetector gestureDetector,
        DirectionalFocusRegistry registry,
        IMonitorResolver monitorResolver,
        WindowActivationService activationService,
        IVirtualDesktopService virtualDesktopService,
        DirectionalFocusLayoutSnapshotService snapshotService,
        Func<IReadOnlyList<IntPtr>> candidateWindowSource,
        Func<int> getThresholdPx,
        DirectionalFocusInputGate inputGate,
        Func<DateTime> nowProvider,
        FileLogger? logger = null)
    {
        _gestureDetector = gestureDetector;
        _registry = registry;
        _monitorResolver = monitorResolver;
        _activationService = activationService;
        _virtualDesktopService = virtualDesktopService;
        _snapshotService = snapshotService;
        _candidateWindowSource = candidateWindowSource;
        _getThresholdPx = getThresholdPx;
        _inputGate = inputGate;
        _nowProvider = nowProvider;
        _logger = logger;

        _timer = new System.Windows.Forms.Timer { Interval = TickIntervalMs };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (_timer.Enabled) return;
        _timer.Start();
        _logger?.Info("DirectionalFocusService started");
    }

    public void Stop()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        ResetState();
        _logger?.Info("DirectionalFocusService stopped");
    }

    public DirectionalFocusRegistry Registry => _registry;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            _logger?.Error("DirectionalFocusService tick error", ex);
        }
    }

    internal void Tick()
    {
        bool ctrlDown = IsKeyDown(NativeMethods.VK_CONTROL);
        bool shiftDown = IsKeyDown(NativeMethods.VK_SHIFT);
        bool altDown = IsKeyDown(NativeMethods.VK_MENU);
        bool lWinDown = IsKeyDown(NativeMethods.VK_LWIN);
        bool rWinDown = IsKeyDown(NativeMethods.VK_RWIN);

        // A Windows-key chord (e.g. Ctrl+Win+Arrow virtual-desktop switch) must never
        // enter the gesture path. Reset everything and bail before reading the mouse,
        // so changing desktops cannot pollute gesture state.
        if (lWinDown || rWinDown)
        {
            if (_gestureWasActive)
            {
                ResetState();
                _gestureWasActive = false;
            }
            return;
        }

        bool gestureActive = _inputGate.IsGestureModifierActive(ctrlDown, shiftDown, altDown, lWinDown, rWinDown);

        if (!gestureActive)
        {
            if (_gestureWasActive)
            {
                ResetState();
                _gestureWasActive = false;
            }
            return;
        }

        if (!_gestureWasActive)
        {
            _gestureWasActive = true;
            _anchor = Cursor.Position;
            // Pin the monitor at gesture START. For the whole Ctrl+Shift hold we resolve
            // slots against THIS monitor, so a big horizontal move that drifts across a
            // monitor border doesn't suddenly switch the slot set mid-gesture (which caused
            // "no slots recognized" and ignored activations).
            _anchorMonitor = MonitorFromPointNative(_anchor.X, _anchor.Y);
            _lastTriggeredSlot = null;
            _logger?.Info($"Directional focus modifiers active: Ctrl+Shift, anchorMonitor={_anchorMonitor}");
            return;
        }

        var current = Cursor.Position;
        int dx = current.X - _anchor.X;
        int dy = current.Y - _anchor.Y;

        var threshold = _getThresholdPx();
        var slot = _gestureDetector.Detect(_anchor.X, _anchor.Y, current.X, current.Y, threshold);

        if (slot == null) return;

        // Don't re-trigger the slot we're already on while the gesture is held — the window
        // is already in front, re-activating it would just spam it.
        if (slot == _lastTriggeredSlot) return;

        // Global throttle during a held gesture: pace slot-hopping. Per-hwnd cooldowns and the
        // unstable tracker carry the heavy protection; this only bounds the gesture cadence.
        var now = _nowProvider();
        if (IsThrottled(now))
        {
            _logger?.Info($"Directional focus activation throttled, cooldown remaining={(_cooldownUntil - now).TotalMilliseconds}ms");
            return;
        }

        // Resolve against the pinned anchor monitor, NOT the current cursor position: the slot
        // set (A-H) belongs to the monitor where the gesture began, even if the cursor has
        // since crossed a monitor boundary.
        var mouseWorkArea = _anchorMonitor;

        _logger?.Info($"Directional focus trigger candidate: slot={slot.Value}, dx={dx}, dy={dy}, anchor=({_anchor.X},{_anchor.Y}), current=({current.X},{current.Y}), anchorMonitor={mouseWorkArea}, threshold={threshold}");

        ActivateSlot(mouseWorkArea, slot.Value, now);
    }

    /// <summary>
    /// Pure throttle decision: true when we are still inside the cooldown that follows the
    /// last activation (or last reset). Extracted so the throttle/backoff timing can be
    /// unit-tested without the Win32 mouse path.
    /// </summary>
    internal bool IsThrottled(DateTime now) => now < _cooldownUntil;

    /// <summary>
    /// Sets the post-activation cooldown. A success applies the normal cooldown; a failure
    /// applies the extended backoff so a struggling target is given time to settle before
    /// the next slot-hop. Pure state mutation — directly unit-testable.
    /// </summary>
    internal void SetPostActivationCooldown(DateTime now, bool success)
    {
        // Same short pacing for success and failure. A foreground-mismatch (the common
        // failure) is now handled by the per-hwnd short cooldown inside WindowActivationService
        // — it must NOT trigger a long global backoff here, or healthy windows get throttled
        // and the gesture feels sluggish. Genuinely dangerous failures (hung windows) are
        // excluded much earlier by the unstable tracker and the placement cooldowns.
        _cooldownUntil = now.AddMilliseconds(CooldownMs);
        _lastFocusTime = now;
    }

    /// <summary>
    /// Resolves the hwnd for a slot on a monitor: registry first, then a rate-limited
    /// on-demand rebuild from the current layout if the slot is missing/stale. Returns
    /// null when the layout is not recognised. Pure lookup logic — no Win32 calls,
    /// so it is directly unit-testable.
    /// </summary>
    /// <remarks>
    /// The registry fast path is never trusted blindly: even when a slot→hwnd mapping
    /// exists, the window must still be physically sitting in its expected slot rect
    /// (validated via <see cref="DirectionalFocusLayoutSnapshotService.IsWindowStillInSlot"/>).
    /// This stops Directional Focus from activating a window the user dragged away.
    /// </remarks>
    internal IntPtr? ResolveHwndForSlot(Rect mouseWorkArea, DirectionalFocusSlot slot)
    {
        // 1. Fast path: registry has a mapping — but only trust it if the window is
        //    still physically in its slot rect. Otherwise the registry is stale.
        var hwnd = _registry.GetHwndForSlot(mouseWorkArea, slot);

        if (hwnd != null && hwnd != IntPtr.Zero)
        {
            if (_snapshotService.IsWindowStillInSlot(hwnd.Value, mouseWorkArea, slot))
            {
                _logger?.Info($"Directional focus registry hit: hwnd={hwnd.Value}, slot={slot}, monitor={mouseWorkArea}");
            }
            else
            {
                _logger?.Info($"Directional focus registry hit rejected: hwnd={hwnd.Value}, slot={slot}, monitor={mouseWorkArea}, window no longer matches slot rect");
                hwnd = null;
            }
        }

        // 2. On miss/stale, try a one-shot rebuild from the current layout (rate-limited).
        if (hwnd == null || hwnd == IntPtr.Zero)
        {
            _logger?.Info($"Directional focus registry miss/stale: slot={slot}, monitor={mouseWorkArea}");

            if (TryRebuildRegistryForCurrentDesktop(mouseWorkArea))
            {
                hwnd = _registry.GetHwndForSlot(mouseWorkArea, slot);

                // Validate the rebuilt mapping too — a snapshot may map a slot to a
                // window that has since drifted again, so re-check the rect.
                if (hwnd != null && hwnd != IntPtr.Zero)
                {
                    if (_snapshotService.IsWindowStillInSlot(hwnd.Value, mouseWorkArea, slot))
                    {
                        _logger?.Info($"Directional focus rebuild result accepted: hwnd={hwnd.Value}, slot={slot}, monitor={mouseWorkArea}");
                    }
                    else
                    {
                        _logger?.Info($"Directional focus rebuild result rejected: hwnd={hwnd.Value}, slot={slot}, monitor={mouseWorkArea}, window does not match slot rect");
                        hwnd = null;
                    }
                }
            }
        }

        if (hwnd == null || hwnd == IntPtr.Zero)
        {
            // The exact slot is empty OR no layout is recognised at all. Distinguish the two:
            // if some slots ARE populated on this monitor, the layout is recognised and we can
            // fall back to the best-aligned available slot in the gesture direction. Only when
            // NO slot is populated do we tell the user to run Arrange Now.
            var fallback = ResolveWithFallback(mouseWorkArea, slot);
            if (fallback != null && fallback != IntPtr.Zero)
            {
                _logger?.Info($"Directional focus fallback selected: requested={slot}, monitor={mouseWorkArea}, hwnd={fallback}");
                return fallback;
            }

            if (_registry.HasSlotsForMonitor(mouseWorkArea))
            {
                var available = string.Join(",", _registry.GetAvailableSlots(mouseWorkArea).Select(s => s.Slot));
                _logger?.Info($"Directional focus exact slot empty: requested={slot}, availableSlots=[{available}], no aligned fallback on monitor={mouseWorkArea}");
            }
            else
            {
                _logger?.Info($"Directional focus ignored: current layout not recognized; run Arrange Now first. slot={slot}, monitor={mouseWorkArea}");
            }
        }

        return hwnd;
    }

    /// <summary>
    /// Fallback used when the exact requested slot is empty (e.g. only 6 windows, so the
    /// edge-centred slots are unpopulated). Picks the available slot whose screen position is
    /// most aligned with the gesture direction, so a "move right" still focuses a window on
    /// the right edge even when MiddleRight is empty. Returns null only if no reasonable
    /// target exists in that direction.
    /// </summary>
    private IntPtr? ResolveWithFallback(Rect mouseWorkArea, DirectionalFocusSlot requested)
    {
        var available = _registry.GetAvailableSlots(mouseWorkArea);
        if (available.Count == 0)
            return null;

        var requestedDir = SlotDirection(requested);
        IntPtr? best = null;
        double bestScore = double.MinValue;

        foreach (var (candidateSlot, candidateHwnd) in available)
        {
            if (candidateSlot == requested)
                continue; // already tried, it's empty

            // Skip the candidate if its window is no longer in its slot rect (same staleness
            // guard as the primary path) — a drifted window is not a reliable target.
            if (!_snapshotService.IsWindowStillInSlot(candidateHwnd, mouseWorkArea, candidateSlot))
                continue;

            var candidateDir = SlotDirection(candidateSlot);
            // Dot product of unit direction vectors: 1 = same direction, 0 = perpendicular,
            // -1 = opposite. Higher = better alignment with the gesture.
            double score = requestedDir.dx * candidateDir.dx + requestedDir.dy * candidateDir.dy;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidateHwnd;
            }
        }

        // Only accept a fallback that is at least roughly aligned (dot > 0.3): we never want to
        // focus a window in the OPPOSITE direction of the gesture just because it's the only
        // one available. 0.3 ≈ within ~70° of the requested direction.
        return bestScore > 0.3 ? best : null;
    }

    /// <summary>
    /// Unit direction vector (dx, dy) pointing from screen centre toward a slot's anchor.
    /// dy is negative for "up" to match the gesture detector's convention. Used to score
    /// fallback candidates by alignment with the requested gesture direction.
    /// </summary>
    private static (double dx, double dy) SlotDirection(DirectionalFocusSlot slot)
    {
        return slot switch
        {
            DirectionalFocusSlot.TopLeft => (-1, -1),
            DirectionalFocusSlot.TopCenter => (0, -1),
            DirectionalFocusSlot.TopRight => (1, -1),
            DirectionalFocusSlot.MiddleLeft => (-1, 0),
            DirectionalFocusSlot.MiddleRight => (1, 0),
            DirectionalFocusSlot.BottomLeft => (-1, 1),
            DirectionalFocusSlot.BottomCenter => (0, 1),
            DirectionalFocusSlot.BottomRight => (1, 1),
            _ => (0, 0)
        };
    }

    private void ActivateSlot(Rect mouseWorkArea, DirectionalFocusSlot slot, DateTime now)
    {
        var hwnd = ResolveHwndForSlot(mouseWorkArea, slot);

        if (hwnd == null || hwnd == IntPtr.Zero)
        {
            // Nothing to activate: mark the slot visited and apply the normal cooldown so we
            // don't keep re-resolving an empty slot on every tick, but no backoff is needed.
            _lastTriggeredSlot = slot;
            SetPostActivationCooldown(now, success: true);
            return;
        }

        bool onCurrentDesktop = _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd.Value);
        if (!onCurrentDesktop)
        {
            _logger?.Info($"Directional focus ignored: target is not on current virtual desktop, hwnd={hwnd.Value}");
            // Still apply the normal cooldown — we made a resolve attempt.
            _lastTriggeredSlot = slot;
            SetPostActivationCooldown(now, success: true);
            return;
        }

        var targetTitle = GetWindowTitle(hwnd.Value);
        var targetPid = GetWindowProcessId(hwnd.Value);
        bool isIconic = NativeMethods.IsIconic(hwnd.Value);
        var currentForeground = NativeMethods.GetForegroundWindow();

        _logger?.Info($"Directional focus target: hwnd={hwnd.Value}, title={targetTitle}, pid={targetPid}, onCurrentDesktop={onCurrentDesktop}, isMinimized={isIconic}, currentForegroundHwnd={currentForeground}");

        var status = _activationService.Activate(hwnd.Value);

        // Pending is NOT a failure: the activation was issued and a delayed recheck is in
        // flight, so we neither log a hard failure nor apply a negative cooldown. Only
        // genuine denials / skips / hung windows go down the failure branch.
        bool successLike = status == ActivationStatus.Success || status == ActivationStatus.Pending;

        if (status == ActivationStatus.Success)
        {
            _logger?.Info($"Directional focus success: slot={slot}, hwnd={hwnd.Value}, title={targetTitle}");
        }
        else if (status == ActivationStatus.Pending)
        {
            _logger?.Info($"Directional focus pending: slot={slot}, hwnd={hwnd.Value}, title={targetTitle}, delayed recheck scheduled");
        }
        else if (status == ActivationStatus.NotResponding)
        {
            _logger?.Warn($"Directional focus target not responding: slot={slot}, hwnd={hwnd.Value}, title={targetTitle}");
        }
        else if (status == ActivationStatus.UnstableSkipped)
        {
            _logger?.Info($"Directional focus skipped unstable target: slot={slot}, hwnd={hwnd.Value}, title={targetTitle}");
        }
        else // ForegroundDenied
        {
            var finalForeground = NativeMethods.GetForegroundWindow();
            _logger?.Warn($"Directional focus foreground denied: slot={slot}, hwnd={hwnd.Value}, foregroundHwnd={finalForeground}");
        }

        // The per-hwnd cooldown inside WindowActivationService already shields this specific
        // hwnd; here we apply the throttle so the user's next rapid slot-hop is paced. Pending
        // uses the normal pacing, not a failure backoff.
        _lastTriggeredSlot = slot;
        SetPostActivationCooldown(now, success: successLike);
    }

    /// <summary>
    /// Rebuilds the registry for the monitor under the cursor by reading the current
    /// physical positions of the eligible windows on the current virtual desktop.
    /// Rate-limited so the snapshot cannot run more than once per
    /// <see cref="RebuildCooldownMs"/>. Never moves any window.
    /// </summary>
    private bool TryRebuildRegistryForCurrentDesktop(Rect mouseWorkArea)
    {
        var now = DateTime.Now;
        if ((now - _lastRegistryRebuildAttempt).TotalMilliseconds < RebuildCooldownMs)
        {
            _logger?.Info($"Directional focus rebuild skipped: rate-limited (cooldown={RebuildCooldownMs}ms)");
            return false;
        }

        _lastRegistryRebuildAttempt = now;

        _logger?.Info("Directional focus attempting layout snapshot rebuild");

        IReadOnlyList<IntPtr> candidates;
        try
        {
            candidates = _candidateWindowSource();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Directional focus rebuild: candidate window source failed: {ex.Message}");
            return false;
        }

        var slotMap = _snapshotService.BuildSlotMap(mouseWorkArea, candidates);

        if (slotMap.Count == 0)
        {
            _logger?.Info($"Directional focus snapshot: no PeekDows slots recognized on monitor={mouseWorkArea}");
            return false;
        }

        _registry.SetSlotsForMonitor(mouseWorkArea, slotMap);
        _logger?.Info($"Directional focus snapshot rebuild complete: monitor={mouseWorkArea}, slots={slotMap.Count}");
        return true;
    }

    private void ResetState()
    {
        _anchor = Point.Empty;
        _anchorMonitor = default;
        _lastTriggeredSlot = null;
        // Releasing Ctrl+Shift (or stopping the service) clears the throttle so the next
        // gesture begins fresh rather than inheriting the previous hold's cooldown.
        _lastFocusTime = DateTime.MinValue;
        _cooldownUntil = DateTime.MinValue;
    }

    private static bool IsKeyDown(int vk)
    {
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "<unknown>";
        }
    }

    private static uint GetWindowProcessId(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }
        catch
        {
            return 0;
        }
    }

    private Rect MonitorFromPointNative(int x, int y)
    {
        var point = new POINT { X = x, Y = y };
        var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return new Rect(
                mi.rcWork.left,
                mi.rcWork.top,
                mi.rcWork.right - mi.rcWork.left,
                mi.rcWork.bottom - mi.rcWork.top
            );
        }

        var primary = _monitorResolver.GetPrimaryMonitor();
        return primary.WorkArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
