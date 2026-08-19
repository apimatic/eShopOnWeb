using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Passage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
