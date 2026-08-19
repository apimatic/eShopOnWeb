using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record TeamCreditUsageResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public Data5? Data { get; init; }
}
