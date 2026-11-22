namespace PeekDows.App.Settings;

/// <summary>Read-only window details for the local Settings page.</summary>
public sealed record WindowRow(
    string Hwnd,
    string Title,
    string Process,
    string ClassName,
    bool Eligible,
    string Reason,
    bool HasProfileRule = false);
