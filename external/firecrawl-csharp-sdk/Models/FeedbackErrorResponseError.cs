using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record FeedbackErrorResponseError
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("feedbackErrorCode")]
    public string? FeedbackErrorCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    public IReadOnlyList<object>? Details { get; init; }
}
