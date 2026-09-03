using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListSegmentsFilter
{
    /// <summary>
    /// The value passed here would be used to filter segments. Pass a value related to <c>segment_property_1</c> on attached Metric. If empty string is passed, this filter would be rejected. Use in query <c>filter[segment_property_1_value]=EU</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_1_value")]
    public string? SegmentProperty1Value { get; init; }

    /// <summary>
    /// The value passed here would be used to filter segments. Pass a value related to <c>segment_property_2</c> on attached Metric. If empty string is passed, this filter would be rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_2_value")]
    public string? SegmentProperty2Value { get; init; }

    /// <summary>
    /// The value passed here would be used to filter segments. Pass a value related to <c>segment_property_3</c> on attached Metric. If empty string is passed, this filter would be rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_3_value")]
    public string? SegmentProperty3Value { get; init; }

    /// <summary>
    /// The value passed here would be used to filter segments. Pass a value related to <c>segment_property_4</c> on attached Metric. If empty string is passed, this filter would be rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_4_value")]
    public string? SegmentProperty4Value { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
