using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record CrawlParamsPreviewResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public Data4? Data { get; init; }
}
