using System.Text.Json.Serialization;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Custom pricing for a product within a scheduled renewal.
/// </summary>
public record ScheduledRenewalProductPricePoint
{
    /// <summary>
    /// (Optional)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// (Optional)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// Required if using <c>custom_price</c> attribute.
    /// </summary>
    [JsonPropertyName("price_in_cents")]
    public required PriceInCents PriceInCents { get; init; }

    /// <summary>
    /// Required if using <c>custom_price</c> attribute.
    /// </summary>
    [JsonPropertyName("interval")]
    public required Interval Interval { get; init; }

    /// <summary>
    /// Required if using <c>custom_price</c> attribute.
    /// </summary>
    [JsonPropertyName("interval_unit")]
    public required IntervalUnit? IntervalUnit { get; init; }

    /// <summary>
    /// (Optional)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_included")]
    public bool? TaxIncluded { get; init; }

    /// <summary>
    /// The product price point initial charge, in integer cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; init; }

    /// <summary>
    /// The numerical expiration interval. e.g., an expiration_interval of ‘30’ coupled with an expiration_interval_unit of day would mean this product price point would expire after 30 days.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval")]
    public int? ExpirationInterval { get; init; }

    /// <summary>
    /// A string representing the expiration interval unit for this product price point, either month, day or never
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval_unit")]
    public ExpirationIntervalUnit? ExpirationIntervalUnit { get; init; }
}
