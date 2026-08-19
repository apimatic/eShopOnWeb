using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record FeedbackResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("feedbackId")]
    public required Guid FeedbackId { get; init; }

    [JsonPropertyName("creditsRefunded")]
    public required double CreditsRefunded { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("alreadySubmitted")]
    public bool? AlreadySubmitted { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dailyCapReached")]
    public bool? DailyCapReached { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("creditsRefundedToday")]
    public double? CreditsRefundedToday { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dailyRefundCap")]
    public double? DailyRefundCap { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("warning")]
    public string? Warning { get; init; }
}
