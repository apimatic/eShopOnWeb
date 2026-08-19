using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Font
{
    /// <summary>
    /// Font family name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("family")]
    public string? Family { get; init; }
}
