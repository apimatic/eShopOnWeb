using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Metrics related to Branded Calling bundled calls including CTIA for the report period.
/// </summary>
public record BrandedCalling
{
    /// <summary>
    /// Total number of Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_branded_calls")]
    public int? TotalBrandedCalls { get; init; }

    /// <summary>
    /// Percentage of Branded bundled calls over total outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("percent_branded_calls")]
    public double? PercentBrandedCalls { get; init; }

    /// <summary>
    /// Answer rate for Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer_rate")]
    public double? AnswerRate { get; init; }

    /// <summary>
    /// Rate of Branded bundled calls that were answered by Human.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("human_answer_rate")]
    public double? HumanAnswerRate { get; init; }

    /// <summary>
    /// Engagement Rate for Branded bundled calls where its call length is longer than 60 seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("engagement_rate")]
    public double? EngagementRate { get; init; }

    /// <summary>
    /// Details of branded calls by use case.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("by_use_case")]
    public IReadOnlyList<BrandedUseCaseDetail>? ByUseCase { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
