using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Map402Error
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
