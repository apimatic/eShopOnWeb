using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Partial monitor update payload. Include at least one field.
/// </summary>
public record MonitorUpdateRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [MaxLength(256)]
    public string? Name { get; init; }

    /// <summary>
    /// Schedule for monitor checks. Provide either <c>cron</c> or <c>text</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("schedule")]
    public MonitorSchedule? Schedule { get; init; }

    /// <summary>
    /// Webhook destination for monitor page and check completion events.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook")]
    public MonitorWebhook? Webhook { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notification")]
    public MonitorNotification? Notification { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("targets")]
    [MinLength(1)]
    [MaxLength(50)]
    public IReadOnlyList<MonitorTarget>? Targets { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("retentionDays")]
    [Minimum(1)]
    [Maximum(365)]
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Plain-language goal used to judge whether changed pages are meaningful. If provided and <c>judgeEnabled</c> is omitted, judging is enabled automatically. Required (non-empty) when any target is a <c>search</c> target, unless <c>judgeEnabled</c> is <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("goal")]
    [MaxLength(2000)]
    public string? Goal { get; init; }

    /// <summary>
    /// Whether to judge changed pages against <c>goal</c>. Requires a non-empty <c>goal</c> to run.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("judgeEnabled")]
    public bool? JudgeEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public Status? Status { get; init; }
}
