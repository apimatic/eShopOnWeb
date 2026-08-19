using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record InteractResponse2
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>
    /// Total session duration in milliseconds
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sessionDurationMs")]
    public int? SessionDurationMs { get; init; }

    /// <summary>
    /// Number of credits billed for the session
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("creditsBilled")]
    public double? CreditsBilled { get; init; }
}
