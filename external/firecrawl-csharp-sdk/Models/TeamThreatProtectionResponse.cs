using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record TeamThreatProtectionResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public Data9? Data { get; init; }
}
