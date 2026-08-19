using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record MonitorNotification
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public Email? Email { get; init; }
}
