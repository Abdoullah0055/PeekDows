using System.Text.Json.Serialization;

namespace PeekDows.Core.Models;

/// <summary>
/// Agent F3 — per-app arrange rule: pin a process to a slot, or ignore it.
/// JSON contract (camelCase, like the other AppSettings camelCase keys):
/// <c>{ "process": "notepad.exe", "action": "pin"|"ignore", "slot": "A".."H" }</c>.
/// Slot is required for <c>pin</c>; it is accepted-but-ignored for <c>ignore</c>.
/// Validation lives in <see cref="Services.AppRuleService"/> (never throws).
/// </summary>
public sealed class AppRule
{
    [JsonPropertyName("process")]
    public string Process { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "pin";

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "A";
}
