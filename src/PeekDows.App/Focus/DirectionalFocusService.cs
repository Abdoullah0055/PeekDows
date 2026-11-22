using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

public sealed class DirectionalFocusService : IDisposable
{
    // P-A5: adaptive polling — 40ms during gesture (responsive), 100ms idle (CPU ÷2.5)
    private const int TickIntervalMsActive = 40;
    private const int TickIntervalMsIdle = 100;

    /// <summary>
    /// Minimum gap between two activations while an armed gesture is held. This is the
    /// pacing for HEALTHY slot-hopping: short enough that switching windows feels instant,
    /// long enough that we don't fire a second activation before the OS has settled the first.
    /// Per-hwnd cooldowns (foreground-mismatch, unstable tracker) carry the heavy protection;
    /// this global throttle only paces the gesture.
    /// </summary>
    private const int CooldownMs = 150;

    private const int RebuildCooldownMs = 500;
    private const int HeldSnapshotMaxAgeMs = 1500;

    // Cancellation inputs polled while held (GetAsyncKeyState already in NativeMethods).
    private const int VkEscape = 0x1B;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    private readonly DirectionalFocusGestureDetector _gestureDetector;
    private readonly DirectionalFocusRegistry _registry;
    private readonly IMonitorResolver _monitorResolver;
    private readonly WindowActivationService _activationService;
    private readonly IVirtualDesktopService _virtualDesktopService;
    private readonly DirectionalFocusLayoutSnapshotService _snapshotService;
    private readonly Func<IReadOnlyList<IntPtr>> _candidateWindowSource;
    private readonly Func<int> _getThresholdPx;
    private readonly DirectionalFocusInputGate _inputGate;
    private readonly FileLogger? _logger;
    private readonly Func<DateTime> _nowProvider;
    private readonly IWindowAnimator? _animator;

    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>
    /// Consecutive end-requests (same reason) required before the gesture
    /// actually ends. Kills single-tick sampling ghosts: a real release or key
    /// stays visible across ticks, a one-tick dropout does not. Worst case adds
    /// ~40ms of hide latency (the idle poll is already 100ms).
    /// </summary>
    private const int EndDebounceTicks = 2;

    private bool _gestureWasActive;
    private int _inactiveStreak;
    private string? _pendingEndReason;
    private Point _anchor;
    private (IntPtr Handle, Rect WorkArea) _anchorMonitor;
    private (DirectionalFocusSlot Slot, IntPtr? Hwnd)? _lastTargetAttempt;
    private DateTime _lastFocusTime = DateTime.MinValue;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private DateTime _lastRegistryRebuildAttempt = DateTime.MinValue;
    private IntPtr _foregroundAtLastSnapshot;
    private int _displayChanged;
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
        FileLogger? logger = null,
        IWindowAnimator? animator = null)
        : this(gestureDetector, registry, monitorResolver, activationService, virtualDesktopService,
               snapshotService, candidateWindowSource, getThresholdPx, new DirectionalFocusInputGate(), () => DateTime.UtcNow, logger, animator)
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
        FileLogger? logger = null,
        IWindowAnimator? animator = null)
        : this(gestureDetector, registry, monitorResolver, activationService, virtualDesktopService,
               snapshotService, candidateWindowSource, getThresholdPx, inputGate, () => DateTime.UtcNow, logger, animator)
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
        FileLogger? logger = null,
        IWindowAnimator? animator = null)
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
        _animator = animator;

        _timer = new System.Windows.Forms.Timer { Interval = TickIntervalMsIdle };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (_timer.Enabled) return;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _timer.Start();
        _logger?.Info("DirectionalFocusService started");
    }

    public void Stop()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        EndGesture("stop");
        _logger?.Info("DirectionalFocusService stopped");
    }

    public DirectionalFocusRegistry Registry => _registry;

    internal void InvalidateLayout()
    {
        _registry.Clear();
        _lastRegistryRebuildAttempt = DateTime.MinValue;
        EndGesture("layout changed");
    }

    internal void RefreshGestureSlots(Rect monitorWorkArea)
        => TryRebuildRegistryForCurrentDesktop(monitorWorkArea, force: true);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Interlocked.Exchange(ref _displayChanged, 1);

    /// <summary>
    /// Hint lifecycle for Overlay UI (Beta). Raised on the WinForms UI
    /// thread (service timer thread): GestureStarted once per hold with the
    /// pinned anchor, GestureUpdated every held tick (slot may be null below
    /// threshold, with threshold px + distance px), GestureEnded on release,
    /// third key, click, Esc or Stop().
    /// Subscribers must never throw (service swallows subscriber errors).
    /// </summary>
    public event Action<Point, Rect>? GestureStarted;
    public event Action<DirectionalFocusSlot?, int, double>? GestureUpdated;
    public event Action? GestureEnded;

    /// <summary>
    /// Ends the gesture if one is active: resets state, slows the timer and
    /// hides the overlay via GestureEnded. No-op when idle. The reason names
    /// the exact killer (release/shift/alt/esc/click/thirdkey:0xXX/stop) so a
    /// flicker loop is diagnosable from the log in a single run.
    /// </summary>
    private void EndGesture(string reason)
    {
        if (!_gestureWasActive)
            return;
        _gestureWasActive = false;
        _inactiveStreak = 0;
        _pendingEndReason = null;
        var anchor = _anchor;
        ResetState();
        _timer.Interval = TickIntervalMsIdle;
        _logger?.Info($"Directional focus gesture ended: {reason} (anchor=({anchor.X},{anchor.Y}))");
        NotifyGestureEnded();
    }

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
        if (Interlocked.Exchange(ref _displayChanged, 0) != 0)
            InvalidateLayout();

        bool ctrlDown = IsKeyDown(NativeMethods.VK_CONTROL);
        bool shiftDown = IsKeyDown(NativeMethods.VK_SHIFT);
        bool altDown = IsKeyDown(NativeMethods.VK_MENU);
        bool lWinDown = IsKeyDown(NativeMethods.VK_LWIN);
        bool rWinDown = IsKeyDown(NativeMethods.VK_RWIN);

        bool gestureActive = _inputGate.IsGestureModifierActive(ctrlDown, shiftDown, altDown, lWinDown, rWinDown);

        // End requests are debounced: the SAME reason must repeat on consecutive
        // ticks before the gesture ends. A single-tick dropout (sampling ghost)
        // is ignored; a real release or held key persists and ends promptly.
        string? endReason = null;
        if (!gestureActive)
        {
            endReason = "release";
        }
        else if (!_gestureWasActive)
        {
            _gestureWasActive = true;
            _inactiveStreak = 0;
            _pendingEndReason = null;
            _timer.Interval = TickIntervalMsActive;
            _anchor = Cursor.Position;
            // Pin the monitor at gesture START. For the whole hold we resolve
            // slots against THIS monitor, so a big horizontal move that drifts
            // across a monitor border doesn't suddenly switch the slot set
            // mid-gesture (which caused "no slots recognized" and ignored
            // activations).
            _anchorMonitor = MonitorFromPointNative(_anchor.X, _anchor.Y);
            _lastTargetAttempt = null;
            _logger?.Info($"Directional focus modifiers active: Ctrl+Win, anchorMonitor={_anchorMonitor}");
            _foregroundAtLastSnapshot = NativeMethods.GetForegroundWindow();
            RefreshGestureSlots(_anchorMonitor.WorkArea);
            NotifyGestureStarted(_anchor, _anchorMonitor.WorkArea);
            return;
        }
        else if (shiftDown)
        {
            endReason = "shift";
        }
        else if (altDown)
        {
            endReason = "alt";
        }
        else if (IsKeyDown(VkEscape))
        {
            endReason = "esc";
        }
        else if (IsKeyDown(VkLButton))
        {
            endReason = "click:L";
        }
        else if (IsKeyDown(VkRButton))
        {
            endReason = "click:R";
        }
        else if (IsKeyDown(VkMButton))
        {
            endReason = "click:M";
        }
        else if (TryGetThirdKeyDown(out int thirdVk))
        {
            endReason = $"thirdkey:0x{thirdVk:X2}";
        }

        if (endReason != null)
        {
            if (string.Equals(_pendingEndReason, endReason, StringComparison.Ordinal))
                _inactiveStreak++;
            else
            {
                _pendingEndReason = endReason;
                _inactiveStreak = 1;
            }
            if (_inactiveStreak >= EndDebounceTicks)
                EndGesture(endReason);
            return;
        }

        _inactiveStreak = 0;
        _pendingEndReason = null;

        if (MonitorFromPointNative(_anchor.X, _anchor.Y) != _anchorMonitor)
        {
            InvalidateLayout();
            return;
        }

        // Flick detection: anchor → cursor vector against the pinned monitor.
        var current = Cursor.Position;
        int dx = current.X - _anchor.X;
        int dy = current.Y - _anchor.Y;

        var threshold = _getThresholdPx();
        var slot = _gestureDetector.Detect(_anchor.X, _anchor.Y, current.X, current.Y, threshold);
        double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
        var now = _nowProvider();
        RefreshHeldGestureSlots(now);
        bool canAttempt = slot is not null && !IsThrottled(now);
        bool canRebuild = canAttempt && (_lastTargetAttempt?.Slot != slot || _lastTargetAttempt?.Hwnd is null);
        var target = slot is { } requested ? ResolveTarget(_anchorMonitor.WorkArea, requested, canRebuild) : null;
        NotifyGestureUpdated(target?.Slot, threshold, distance);
        if (!canAttempt || slot is not { } direction || !TryBeginTargetAttempt(direction, target?.Hwnd)) return;

        _logger?.Info($"Directional focus trigger candidate: slot={direction}, dx={dx}, dy={dy}, anchor=({_anchor.X},{_anchor.Y}), current=({current.X},{current.Y}), anchorMonitor={_anchorMonitor}, threshold={threshold}");

        ActivateSlot(direction, target?.Hwnd, now);
    }

    internal bool TryBeginTargetAttempt(DirectionalFocusSlot direction, IntPtr? hwnd)
    {
        if (_lastTargetAttempt is { } previous && previous.Slot == direction &&
            (hwnd is null || hwnd == previous.Hwnd))
            return false;

        _lastTargetAttempt = (direction, hwnd);
        return true;
    }

    private void RefreshHeldGestureSlots(DateTime now)
    {
        var age = (now - _lastRegistryRebuildAttempt).TotalMilliseconds;
        if (age < RebuildCooldownMs) return;

        var foreground = NativeMethods.GetForegroundWindow();
        // A desktop change typically changes the foreground hwnd. Refresh periodically
        // as well, including when an app is pinned across desktops and its hwnd stays put.
        if (foreground == _foregroundAtLastSnapshot && age < HeldSnapshotMaxAgeMs) return;

        _foregroundAtLastSnapshot = foreground;
        TryRebuildRegistryForCurrentDesktop(_anchorMonitor.WorkArea);
    }

    /// <summary>
    /// True for keys that must never end the gesture: media/volume keys, browser
    /// and app-launch keys, and lock keys. None of them can form a meaningful OS
    /// chord with the held Ctrl+Win (no layout toggle, no desktop switch, no
    /// typing), but they are the usual stuck/phantom-key suspects (laptop Fn
    /// layers, headset docks, crumbs) — a stuck Volume-Down once killed every
    /// gesture in a flicker loop (log: thirdkey:0xAE). Pure, unit-tested.
    /// </summary>
    public static bool IsSilentKey(int vk)
    {
        switch (vk)
        {
            // Lock keys.
            case 0x14: // VK_CAPITAL (CapsLock)
            case 0x90: // VK_NUMLOCK
            case 0x91: // VK_SCROLL
                return true;
            // Browser keys.
            case 0xA6: // VK_BROWSER_BACK
            case 0xA7: // VK_BROWSER_FORWARD
            case 0xA8: // VK_BROWSER_REFRESH
            case 0xA9: // VK_BROWSER_STOP
            case 0xAA: // VK_BROWSER_SEARCH
            case 0xAB: // VK_BROWSER_FAVORITES
            case 0xAC: // VK_BROWSER_HOME
                return true;
            // Volume / media keys.
            case 0xAD: // VK_VOLUME_MUTE
            case 0xAE: // VK_VOLUME_DOWN
            case 0xAF: // VK_VOLUME_UP
            case 0xB0: // VK_MEDIA_NEXT_TRACK
            case 0xB1: // VK_MEDIA_PREV_TRACK
            case 0xB2: // VK_MEDIA_STOP
            case 0xB3: // VK_MEDIA_PLAY_PAUSE
                return true;
            // App-launch keys.
            case 0xB4: // VK_LAUNCH_MAIL
            case 0xB5: // VK_LAUNCH_MEDIA_SELECT
            case 0xB6: // VK_LAUNCH_APP1
            case 0xB7: // VK_LAUNCH_APP2
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// True + the VK when any non-chord, non-silent key is physically down. Held ticks only:
    /// scans VK 0x08-0xFE, skipping the chord modifiers (Ctrl 0x11/0xA2/0xA3, Win
    /// 0x5B/0x5C) and Shift/Alt (generic + L/R, handled separately above).
    /// Mouse buttons (0x01-0x07) are outside the range (covered by the click
    /// check). The VK is reported so the end reason names the exact killer.
    /// </summary>
    private static bool TryGetThirdKeyDown(out int vk)
    {
        for (vk = 0x08; vk <= 0xFE; vk++)
        {
            switch (vk)
            {
                case NativeMethods.VK_SHIFT:
                case NativeMethods.VK_CONTROL:
                case NativeMethods.VK_MENU:
                case NativeMethods.VK_LWIN:
                case NativeMethods.VK_RWIN:
                case NativeMethods.VK_LSHIFT:
                case NativeMethods.VK_RSHIFT:
                case NativeMethods.VK_LCONTROL:
                case NativeMethods.VK_RCONTROL:
                case NativeMethods.VK_LMENU:
                case NativeMethods.VK_RMENU:
                    continue;
            }
            if (IsSilentKey(vk))
                continue;
            if (IsKeyDown(vk))
                return true;
        }
        vk = -1;
        return false;
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

    internal IntPtr? ResolveHwndForSlot(Rect mouseWorkArea, DirectionalFocusSlot slot)
        => ResolveTarget(mouseWorkArea, slot)?.Hwnd;

    // Resolve once so the overlay highlights the same target that activation uses.
    internal (DirectionalFocusSlot Slot, IntPtr Hwnd)? ResolveTarget(
        Rect monitorWorkArea, DirectionalFocusSlot requested, bool allowRebuild = true)
    {
        var target = FindExactTarget(monitorWorkArea, requested);
        if (target is null && allowRebuild && TryRebuildRegistryForCurrentDesktop(monitorWorkArea))
            target = FindExactTarget(monitorWorkArea, requested);

        return target ?? ResolveWithFallback(monitorWorkArea, requested);
    }

    private (DirectionalFocusSlot Slot, IntPtr Hwnd)? FindExactTarget(
        Rect monitorWorkArea, DirectionalFocusSlot requested)
    {
        var hwnd = _registry.GetHwndForSlot(monitorWorkArea, requested);
        if (hwnd is { } exact && IsWindowValidInSlot(exact, monitorWorkArea, requested))
            return (requested, exact);

        // Thirds uses one canonical overlay entry for Col2, with both vertical
        // gestures routed to it. An empty Col2 keeps the original direction for fallback.
        var centerSlot = DirectionalFocusSlot.TopCenter;
        if (requested == DirectionalFocusSlot.BottomCenter &&
            _registry.GetHwndForSlot(monitorWorkArea, centerSlot) is { } center &&
            _snapshotService.IsThirdsCenterColumn(center, monitorWorkArea) &&
            IsWindowValidInSlot(center, monitorWorkArea, centerSlot))
            return (centerSlot, center);

        return null;
    }

    /// <summary>
    /// Slot validation with the animation exemption: a window mid-tween has a transient
    /// interpolated rect that will not match the slot rect yet, so the strict check must
    /// not reject it (spec §2, IsAnimating query).
    /// </summary>
    private bool IsWindowValidInSlot(IntPtr hwnd, Rect monitorWorkArea, DirectionalFocusSlot slot)
    {
        if (_animator?.IsAnimating(hwnd) == true)
            return _registry.GetPlannedSlotsForMonitor(monitorWorkArea).TryGetValue(hwnd, out var placement) &&
                _snapshotService.IsPlannedSlotStillValid(placement, monitorWorkArea, slot);
        return _snapshotService.IsWindowStillInSlot(hwnd, monitorWorkArea, slot);
    }

    /// <summary>
    /// Fallback used when the exact requested slot is empty (e.g. only 6 windows, so the
    /// edge-centred slots are unpopulated). Picks the available slot whose screen position is
    /// most aligned with the gesture direction, so a "move right" still focuses a window on
    /// the right edge even when MiddleRight is empty. Returns null only if no reasonable
    /// target exists in that direction.
    /// </summary>
    private (DirectionalFocusSlot Slot, IntPtr Hwnd)? ResolveWithFallback(Rect mouseWorkArea, DirectionalFocusSlot requested)
    {
        var available = _registry.GetAvailableSlots(mouseWorkArea);
        if (available.Count == 0)
            return null;

        var requestedDir = SlotDirection(requested);
        (DirectionalFocusSlot Slot, IntPtr Hwnd)? best = null;
        double bestScore = double.MinValue;

        foreach (var (candidateSlot, candidateHwnd) in available)
        {
            if (candidateSlot == requested)
                continue; // already tried, it's empty

            // Skip the candidate if its window is no longer in its slot rect (same staleness
            // guard as the primary path) — a drifted window is not a reliable target.
            if (!IsWindowValidInSlot(candidateHwnd, mouseWorkArea, candidateSlot))
                continue;

            var candidateDir = SlotDirection(candidateSlot);
            // Dot product of unit direction vectors: 1 = same direction, 0 = perpendicular,
            // -1 = opposite. Higher = better alignment with the gesture.
            double score = requestedDir.dx * candidateDir.dx + requestedDir.dy * candidateDir.dy;
            if (score > bestScore)
            {
                bestScore = score;
                best = (candidateSlot, candidateHwnd);
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

    private void ActivateSlot(DirectionalFocusSlot slot, IntPtr? hwnd, DateTime now)
    {
        if (hwnd == null || hwnd == IntPtr.Zero)
        {
            // Pace the next activation while continuing to watch for a target in this direction.
            SetPostActivationCooldown(now, success: true);
            return;
        }

        bool onCurrentDesktop = _virtualDesktopService.IsWindowOnCurrentVirtualDesktopNow(hwnd.Value);
        bool isIconic = NativeMethods.IsIconic(hwnd.Value);
        if (!onCurrentDesktop || isIconic)
        {
            _logger?.Info($"Directional focus ignored: hwnd={hwnd.Value}, onCurrentDesktop={onCurrentDesktop}, isMinimized={isIconic}");
            // Still apply the normal cooldown — we made a resolve attempt.
            SetPostActivationCooldown(now, success: true);
            return;
        }

        var targetTitle = GetWindowTitle(hwnd.Value);
        var targetPid = GetWindowProcessId(hwnd.Value);
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
        SetPostActivationCooldown(now, success: successLike);
    }

    /// <summary>
    /// Rebuilds the registry for the monitor under the cursor by reading the current
    /// physical positions of the eligible windows on the current virtual desktop.
    /// Rate-limited so the snapshot cannot run more than once per
    /// <see cref="RebuildCooldownMs"/>. Never moves any window.
    /// </summary>
    private bool TryRebuildRegistryForCurrentDesktop(Rect mouseWorkArea, bool force = false)
    {
        var now = _nowProvider();
        if (!force && (now - _lastRegistryRebuildAttempt).TotalMilliseconds < RebuildCooldownMs)
            return false;

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

        var animatingSlots = _registry.GetPlannedSlotsForMonitor(mouseWorkArea)
            .Where(entry => _animator?.IsAnimating(entry.Key) == true).ToDictionary();
        var eligible = candidates.Where(hwnd =>
            _animator?.IsAnimating(hwnd) != true || animatingSlots.ContainsKey(hwnd));
        // A registered window may be too small for discovery partway through its
        // tween. Keep its planned zone eligible while the tween is in flight.
        var slotMap = _snapshotService.BuildSlotMap(mouseWorkArea,
            eligible.Concat(animatingSlots.Keys).Distinct(), animatingSlots);

        _registry.SetSlotsForMonitor(mouseWorkArea, slotMap);
        _logger?.Info($"Directional focus snapshot rebuild complete: monitor={mouseWorkArea}, slots={slotMap.Count}");
        return slotMap.Count != 0;
    }

    private void ResetState()
    {
        _anchor = Point.Empty;
        _anchorMonitor = default;
        _lastTargetAttempt = null;
        // Disarming (or stopping the service) clears the throttle so the next
        // armed gesture begins fresh rather than inheriting the previous one's cooldown.
        _lastFocusTime = DateTime.MinValue;
        _cooldownUntil = DateTime.MinValue;
    }

    private void NotifyGestureStarted(Point anchor, Rect monitor)
    {
        try { GestureStarted?.Invoke(anchor, monitor); } catch { }
    }

    private void NotifyGestureUpdated(DirectionalFocusSlot? slot, int threshold, double distance)
    {
        try { GestureUpdated?.Invoke(slot, threshold, distance); } catch { }
    }

    private void NotifyGestureEnded()
    {
        try { GestureEnded?.Invoke(); } catch { }
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

    // C7 fix: deduplicated P/Invoke — use MonitorNativeMethods as single source of truth.
    private (IntPtr Handle, Rect WorkArea) MonitorFromPointNative(int x, int y)
    {
        var point = new MonitorNativeMethods.POINT { X = x, Y = y };
        var hMonitor = MonitorNativeMethods.MonitorFromPoint(point, MonitorNativeMethods.MONITOR_DEFAULTTONEAREST);

        var mi = new MonitorNativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);

        if (MonitorNativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            return (hMonitor, new Rect(
                mi.rcWork.left,
                mi.rcWork.top,
                mi.rcWork.right - mi.rcWork.left,
                mi.rcWork.bottom - mi.rcWork.top
            ));
        }

        var primary = _monitorResolver.GetPrimaryMonitor();
        return (primary.Handle, primary.WorkArea);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Tick -= OnTimerTick;
        if (_timer.Enabled) SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Stop();
        _timer.Dispose();
    }
}
