using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Schedule for monitor checks. Provide either <c>cron</c> or <c>text</c>.
/// </summary>
public record MonitorSchedule
{
    /// <summary>
    /// Five-field cron expression. Minimum interval is 5 minutes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cron")]
    public string? Cron { get; init; }

    /// <summary>
    /// Natural language schedule. Supported examples include <c>every 30 minutes</c>, <c>every 15 minutes starting at :07</c>, <c>hourly</c>, <c>every 2 hours</c>, <c>daily</c>, <c>daily at 9:00</c>, <c>daily at 9am</c>, <c>daily at 5:30 PM</c>, and <c>weekly</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// IANA timezone for the schedule.
    /// </summary>
    [JsonPropertyName("timezone")]
    public string? Timezone { get; init; } = "UTC";
}
