using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateOrUpdateProduct
{
    /// <summary>
    /// The product name
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The product API handle
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// The product description
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// E.g. Internal ID or SKU Number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accounting_code")]
    public string? AccountingCode { get; init; }

    /// <summary>
    /// Deprecated value that can be ignored unless you have legacy hosted pages. For Public Signup Page users, read this attribute from under the signup page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("require_credit_card")]
    public bool? RequireCreditCard { get; init; }

    /// <summary>
    /// The product price, in integer cents
    /// </summary>
    [JsonPropertyName("price_in_cents")]
    public required long PriceInCents { get; init; }

    /// <summary>
    /// The numerical interval. e.g., an interval of ‘30’ coupled with an interval_unit of day would mean this product would renew every 30 days.
    /// </summary>
    [JsonPropertyName("interval")]
    public required int Interval { get; init; }

    /// <summary>
    /// A string representing the interval unit for this product, either month or day
    /// </summary>
    [JsonPropertyName("interval_unit")]
    public required IntervalUnit IntervalUnit { get; init; }

    /// <summary>
    /// The product trial price, in integer cents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; init; }

    /// <summary>
    /// The numerical trial interval. e.g., an interval of ‘30’ coupled with a trial_interval_unit of day would mean this product trial would last 30 days.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; init; }

    /// <summary>
    /// A string representing the trial interval unit for this product, either month or day
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
    /// The numerical expiration interval. e.g., an expiration_interval of ‘30’ coupled with an expiration_interval_unit of day would mean this product would expire after 30 days.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval")]
    public int? ExpirationInterval { get; init; }

    /// <summary>
    /// A string representing the expiration interval unit for this product, either month, day or never
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval_unit")]
    public ExpirationIntervalUnit? ExpirationIntervalUnit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auto_create_signup_page")]
    public bool? AutoCreateSignupPage { get; init; }

    /// <summary>
    /// A string representing the tax code related to the product type. This is especially important when using AvaTax to tax based on locale. This attribute has a max length of 25 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_code")]
    public string? TaxCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
