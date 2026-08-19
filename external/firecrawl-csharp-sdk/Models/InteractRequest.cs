using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record InteractRequest
{
    /// <summary>
    /// Total time-to-live in seconds for the interact session
    /// </summary>
    [JsonPropertyName("ttl")]
    [Minimum(30)]
    [Maximum(3600)]
    public int? Ttl { get; init; } = 300;

    /// <summary>
    /// Time in seconds before the session is destroyed due to inactivity
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activityTtl")]
    [Minimum(10)]
    [Maximum(3600)]
    public int? ActivityTtl { get; init; }

    /// <summary>
    /// Whether to stream a live view of the browser
    /// </summary>
    [JsonPropertyName("streamWebView")]
    public bool? StreamWebView { get; init; } = true;

    /// <summary>
    /// Enable persistent storage across interact sessions. Data saved in one session can be loaded in a later session using the same name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("profile")]
    public Profile1? Profile { get; init; }
}
