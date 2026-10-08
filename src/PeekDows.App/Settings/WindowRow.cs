namespace PeekDows.App.Settings;

/// <summary>Ligne lecture-seule pour la page Windows v2. Aucune donnée perso.</summary>
public sealed record WindowRow(
    string Hwnd,
    string Title,
    string Process,
    string ClassName,
    bool Eligible,
    string Reason);
