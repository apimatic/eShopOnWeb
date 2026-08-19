using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record WaitByDuration
{
    /// <summary>
    /// Wait for a specified amount of milliseconds
    /// </summary>
    [JsonPropertyName("type")]
    public required Type18 Type { get; init; }

    /// <summary>
    /// Number of milliseconds to wait
    /// </summary>
    [JsonPropertyName("milliseconds")]
    [Minimum(1)]
    public required int Milliseconds { get; init; }
}
