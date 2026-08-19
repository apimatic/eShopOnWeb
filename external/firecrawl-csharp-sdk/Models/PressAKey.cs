using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Press a key on the page. See https://asawicki.info/nosense/doc/devices/keyboard/key_codes.html for key codes.
/// </summary>
public record PressAKey
{
    /// <summary>
    /// Press a key on the page
    /// </summary>
    [JsonPropertyName("type")]
    public required Type23 Type { get; init; }

    /// <summary>
    /// Key to press
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}
