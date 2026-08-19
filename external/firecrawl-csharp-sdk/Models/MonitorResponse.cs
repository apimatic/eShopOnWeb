using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record MonitorResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public MonitorModel? Data { get; init; }
}
