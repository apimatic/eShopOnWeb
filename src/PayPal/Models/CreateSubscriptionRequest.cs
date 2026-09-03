using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The create subscription request details.
/// </summary>
public record CreateSubscriptionRequest
{
    /// <summary>
    /// The ID of the plan.
    /// </summary>
    [JsonPropertyName("plan_id")]
    [StringLength(26, MinimumLength = 26)]
    [RegularExpression("^P-[A-Z0-9]*$")]
    public required string PlanId { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? StartTime { get; init; }

    /// <summary>
    /// The quantity of the product in the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    [StringLength(32, MinimumLength = 1)]
    [RegularExpression("^([0-9]+|([0-9]+)?[.][0-9]+)$")]
    public string? Quantity { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_amount")]
    public Money? ShippingAmount { get; init; }

    /// <summary>
    /// The subscriber request information .
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscriber")]
    public SubscriberRequest? Subscriber { get; init; }

    /// <summary>
    /// DEPRECATED. Indicates whether the subscription auto-renews after the billing cycles complete.
    /// </summary>
    [JsonPropertyName("auto_renewal")]
    public bool? AutoRenewal { get; init; } = false;

    /// <summary>
    /// DEPRECATED. The application context, which customizes the payer experience during the subscription approval process with PayPal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_context")]
    public SubscriptionApplicationContext? ApplicationContext { get; init; }

    /// <summary>
    /// The custom id for the subscription. Can be invoice id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[\\x20-\\x7E]+")]
    public string? CustomId { get; init; }

    /// <summary>
    /// An inline plan object to customise the subscription. You can override plan level default attributes by providing customised values for the subscription in this object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan")]
    public PlanOverride? Plan { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
