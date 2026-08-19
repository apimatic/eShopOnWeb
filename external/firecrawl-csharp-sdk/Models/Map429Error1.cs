using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Map429Error1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
