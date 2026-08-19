using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record SupportProxyErrorResponseError
{
    /// <summary>
    /// Support proxy or upstream error code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
