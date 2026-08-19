using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record MonitorPageJudgment
{
    /// <summary>
    /// Whether the changed page is meaningful for the monitor goal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meaningful")]
    public bool? Meaningful { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("confidence")]
    public Confidence? Confidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>
    /// Goal-relevant changes selected by the judge from the page diff.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meaningfulChanges")]
    public IReadOnlyList<MeaningfulChange>? MeaningfulChanges { get; init; }
}
