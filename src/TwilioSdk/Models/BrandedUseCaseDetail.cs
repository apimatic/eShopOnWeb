using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Associated metrics for Branded calls grouped by each use case.
/// </summary>
public record BrandedUseCaseDetail
{
    /// <summary>
    /// The name of supported use case for Branded calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_case")]
    public string? UseCase { get; init; }

    /// <summary>
    /// The number of phone numbers enabled Branded calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled_phonenumbers")]
    public int? EnabledPhonenumbers { get; init; }

    /// <summary>
    /// The number of total outbound calls for the use case.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// Answer rate per each use case for Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer_rate")]
    public double? AnswerRate { get; init; }

    /// <summary>
    /// Rate of Branded bundled calls that were answered by Human per each use case for Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("human_answer_rate")]
    public double? HumanAnswerRate { get; init; }

    /// <summary>
    /// Engagement Rate for Branded bundled calls where its call length is longer than 60 seconds per each use case for Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("engagement_rate")]
    public double? EngagementRate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
