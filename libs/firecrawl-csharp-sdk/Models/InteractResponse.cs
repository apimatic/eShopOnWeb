using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record InteractResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>
    /// The unique session identifier
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// WebSocket URL for Chrome DevTools Protocol access
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cdpUrl")]
    public string? CdpUrl { get; init; }

    /// <summary>
    /// URL to view the interact session in real time
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("liveViewUrl")]
    public string? LiveViewUrl { get; init; }

    /// <summary>
    /// URL to interact with the interact session in real time (click, type, scroll)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interactiveLiveViewUrl")]
    public string? InteractiveLiveViewUrl { get; init; }

    /// <summary>
    /// When the session will expire based on TTL
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}
