using System;
using System.Windows.Forms;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.AutoArrange;

public sealed class AutoArrangeService : IDisposable
{
    private const int AutoArrangeCooldownMs = 1000;

    private readonly WindowDiscoveryService _discoveryService;
    private readonly WindowClassifier _classifier;
    private readonly AutoArrangeDecisionEngine _decisionEngine;
    private readonly IPeekDowsController _controller;
    private readonly Func<bool> _isPreviewOpen;
    private readonly FileLogger _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly object _lock = new();

    private bool _isArranging;
    private DateTime _lastArrangeTime = DateTime.MinValue;
    private bool _disposed;
    private System.Windows.Forms.Timer? _pendingDelayTimer;

    public bool IsRunning { get; private set; }

    public AutoArrangeService(
        WindowDiscoveryService discoveryService,
        WindowClassifier classifier,
        IPeekDowsController controller,
        FileLogger logger,
        Func<bool>? isPreviewOpen = null)
    {
        _discoveryService = discoveryService;
        _classifier = classifier;
        _decisionEngine = new AutoArrangeDecisionEngine();
        _controller = controller;
        _isPreviewOpen = isPreviewOpen ?? (() => false);
        _logger = logger;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };
        _timer.Tick += OnTimerTick;
        _controller.StateChanged += OnControllerStateChanged;
    }

    public void Start(AppSettings settings)
    {
        if (IsRunning) return;

        _timer.Interval = Math.Max(500, settings.WindowDetectionIntervalMs);

        var initialDiff = _discoveryService.Refresh(_classifier);
        _logger.Info($"AutoArrange baseline created: current={initialDiff.Current.Count}, ignoredAdded={initialDiff.Added.Count}, ignoredRemoved={initialDiff.Removed.Count}");

        IsRunning = true;
        _timer.Start();
        _logger.Info("AutoArrangeService started");
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;

            IsRunning = false;
            _timer.Stop();
            ClearPendingDelay();
        }

        _logger.Info("AutoArrangeService stopped");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Tick();
    }

    private void OnControllerStateChanged(RuntimeState state)
    {
        if (state == RuntimeState.Paused)
        {
            lock (_lock) ClearPendingDelay();
        }
    }

    internal bool AcknowledgePreviewPlacement(WindowDiscoveryService? validatedDiscovery, PlacementResult result)
    {
        if (validatedDiscovery is null || result.SucceededHwnds.Count == 0) return false;

        lock (_lock)
        {
            _discoveryService.AdoptBaseline(validatedDiscovery);
            ClearPendingDelay();
        }
        return true;
    }

    internal void Tick()
    {
        lock (_lock)
        {
            if (!IsRunning || _isPreviewOpen()) return;

            var settings = _controller.CurrentSettings;
            if (settings == null) return;

            // Note: we intentionally do not log a line per tick. Each meaningful outcome below
            // (skipped for a reason, detected added/removed windows, triggering ArrangeNow) logs
            // its own descriptive line, so an unconditional "tick" line would just be noise.

            if (!settings.Enabled)
            {
                _logger.Info("AutoArrange skipped: disabled");
                return;
            }

            if (_controller.State == RuntimeState.Paused)
            {
                _logger.Info("AutoArrange skipped: paused");
                return;
            }

            if (!settings.AutoArrange)
            {
                _logger.Info("AutoArrange skipped: AutoArrange=false");
                return;
            }

            if (_isArranging)
            {
                _logger.Info("AutoArrange skipped because arrange already running");
                return;
            }

            if ((DateTime.UtcNow - _lastArrangeTime).TotalMilliseconds < AutoArrangeCooldownMs)
            {
                _logger.Info("AutoArrange skipped: cooldown active");
                return;
            }

            var diff = _discoveryService.Refresh(_classifier);

            var decision = _decisionEngine.Decide(
                diff,
                settings.Enabled,
                settings.AutoArrange,
                _controller.State == RuntimeState.Paused,
                _isArranging,
                settings.ArrangeAfterWindowCloses);

            switch (decision)
            {
                case AutoArrangeDecision.ArrangeAfterDelay:
                    _logger.Info($"AutoArrange detected added windows count={diff.Added.Count}");
                    ScheduleArrangeAfterDelay(settings.NewWindowStabilizationDelayMs);
                    break;

                case AutoArrangeDecision.ArrangeImmediately:
                    _logger.Info($"AutoArrange detected removed windows count={diff.Removed.Count}");
                    if (_pendingDelayTimer is not null)
                    {
                        _logger.Info("AutoArrange joining removed windows to the pending stabilization delay");
                        break;
                    }
                    _logger.Info("AutoArrange triggering ArrangeNow");
                    TriggerArrangeNow();
                    break;

                case AutoArrangeDecision.NoOp:
                default:
                    break;
            }
        }
    }

    private void ScheduleArrangeAfterDelay(int delayMs)
    {
        if (_pendingDelayTimer != null)
        {
            _logger.Info("AutoArrange delayed arrange already pending, skipping duplicate");
            return;
        }

        _logger.Info($"AutoArrange scheduling arrange after stabilization delay={delayMs}");
        _pendingDelayTimer = new System.Windows.Forms.Timer { Interval = delayMs };
        _pendingDelayTimer.Tick += OnPendingDelayTick;
        _pendingDelayTimer.Start();
    }

    private void OnPendingDelayTick(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _pendingDelayTimer)) TickPendingDelay();
    }

    internal void TickPendingDelay()
    {
        lock (_lock)
        {
            if (_pendingDelayTimer is null) return;

            var settings = _controller.CurrentSettings;
            bool canArrange = IsRunning && settings is { Enabled: true, AutoArrange: true }
                && _controller.State != RuntimeState.Paused;
            if (canArrange && _isPreviewOpen()) return;

            ClearPendingDelay();
            if (!canArrange) return;

            _logger.Info("AutoArrange triggering ArrangeNow (after stabilization)");
            TriggerArrangeNow();
        }
    }

    private void ClearPendingDelay()
    {
        if (_pendingDelayTimer is not { } timer) return;

        _pendingDelayTimer = null;
        timer.Tick -= OnPendingDelayTick;
        timer.Stop();
        timer.Dispose();
    }

    private void TriggerArrangeNow()
    {
        _isArranging = true;
        try
        {
            _controller.ArrangeNow();
        }
        finally
        {
            _isArranging = false;
            _lastArrangeTime = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _controller.StateChanged -= OnControllerStateChanged;
        _timer.Tick -= OnTimerTick;
        _timer.Dispose();
    }
}
