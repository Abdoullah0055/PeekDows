using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PeekDows.App.Settings;

/// <summary>Handler requestWindows. Enregistre via SetWindowsMessageHandler + SetWindowsProvider (D).</summary>
public static class WindowsMessageHandler
{
    public static string? TryHandle(
        string type,
        JsonElement root,
        Func<IReadOnlyList<WindowRow>>? provider,
        JsonSerializerOptions writeOptions)
    {
        if (type != "requestWindows") return null;
        var rows = provider?.Invoke() ?? (IReadOnlyList<WindowRow>)Array.Empty<WindowRow>();
        return JsonSerializer.Serialize(new { type = "windows", data = rows }, writeOptions);
    }

    public static JsonSerializerOptions CamelCase => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
