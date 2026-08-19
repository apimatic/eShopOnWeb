using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Map500Error1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
