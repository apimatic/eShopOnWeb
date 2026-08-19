using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record SuccessResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }
}
