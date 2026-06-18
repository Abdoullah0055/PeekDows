namespace PeekDows.App.Startup;

public interface IStartupService
{
    bool IsEnabled();
    bool Enable();
    bool Disable();
    bool SetEnabled(bool enabled);
}
