using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateProductPricePoint
{
    /// <summary>
    /// The product price point name
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The product price point API handle
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// The product price point price, in integer cents
    /// </summary>
    [JsonPropertyName("price_in_cents")]
    public required long PriceInCents { get; init; }

    /// <summary>
    /// The numerical interval. e.g., an interval of ‘30’ coupled with an interval_unit of day would mean this product price point would renew every 30 days.
    /// </summary>
    [JsonPropertyName("interval")]
    public required int Interval { get; init; }

    /// <summary>
    /// A string representing the interval unit for this product price point, either month or day
    /// </summary>
    [JsonPropertyName("interval_unit")]
    public required IntervalUnit IntervalUnit { get; init; }

    /// <summary>
    /// The product price point trial price, in integer cents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; init; }

    /// <summary>
    /// The numerical trial interval. e.g., an interval of ‘30’ coupled with a trial_interval_unit of day would mean this product price point trial would last 30 days.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; init; }

    /// <summary>
    /// A string representing the trial interval unit for this product price point, either month or day
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_interval_unit")]
    public IntervalUnit? TrialIntervalUnit { get; init; }

    /// <summary>
    /// Indicates how a trial is handled when the trial period ends and there is no credit card on file. For <c>no_obligation</c>, the subscription transitions to a Trial Ended state. Maxio will not send any emails or statements. For <c>payment_expected</c>, the subscription transitions to a Past Due state. Maxio will send normal dunning emails and statements according to your other settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_type")]
    public TrialType? TrialType { get; init; }

    /// <summary>
    /// The product price point initial charge, in integer cents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initial_charge_after_trial")]
    public bool? InitialChargeAfterTrial { get; init; }

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

    /// <summary>
    /// Whether or not to use the site's exchange rate or define your own pricing when your site has multiple currencies defined.
    /// </summary>
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; } = true;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
