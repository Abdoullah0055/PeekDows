using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed class AutoArrangeDecisionEngine
{
    public AutoArrangeDecision Decide(
        WindowDiff diff,
        bool isEnabled,
        bool isAutoArrange,
        bool isPaused,
        bool isAlreadyArranging)
    {
        if (!isEnabled)
            return AutoArrangeDecision.NoOp;

        if (!isAutoArrange)
            return AutoArrangeDecision.NoOp;

        if (isPaused)
            return AutoArrangeDecision.NoOp;

        if (isAlreadyArranging)
            return AutoArrangeDecision.NoOp;

        if (diff.Added.Count > 0)
            return AutoArrangeDecision.ArrangeAfterDelay;

        if (diff.Removed.Count > 0)
            return AutoArrangeDecision.ArrangeImmediately;

        return AutoArrangeDecision.NoOp;
    }

    public AutoArrangeDecision Decide(
        WindowDiff diff,
        bool isEnabled,
        bool isAutoArrange,
        bool isPaused,
        bool isAlreadyArranging,
        bool arrangeAfterWindowCloses)
    {
        if (!isEnabled)
            return AutoArrangeDecision.NoOp;

        if (!isAutoArrange)
            return AutoArrangeDecision.NoOp;

        if (isPaused)
            return AutoArrangeDecision.NoOp;

        if (isAlreadyArranging)
            return AutoArrangeDecision.NoOp;

        if (diff.Added.Count > 0)
            return AutoArrangeDecision.ArrangeAfterDelay;

        if (diff.Removed.Count > 0 && arrangeAfterWindowCloses)
            return AutoArrangeDecision.ArrangeImmediately;

        return AutoArrangeDecision.NoOp;
    }
}
