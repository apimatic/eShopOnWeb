using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateSegment
{
    /// <summary>
    /// A value that will occur in your events that you want to bill upon. The type of the value depends on the property type in the related event based billing metric.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_1_value")]
    public SegmentProperty1Value? SegmentProperty1Value { get; init; }

    /// <summary>
    /// A value that will occur in your events that you want to bill upon. The type of the value depends on the property type in the related event based billing metric.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_2_value")]
    public SegmentProperty2Value? SegmentProperty2Value { get; init; }

    /// <summary>
    /// A value that will occur in your events that you want to bill upon. The type of the value depends on the property type in the related event based billing metric.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_3_value")]
    public SegmentProperty3Value? SegmentProperty3Value { get; init; }

    /// <summary>
    /// A value that will occur in your events that you want to bill upon. The type of the value depends on the property type in the related event based billing metric.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment_property_4_value")]
    public SegmentProperty4Value? SegmentProperty4Value { get; init; }

    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prices")]
    public IReadOnlyList<CreateOrUpdateSegmentPrice>? Prices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
