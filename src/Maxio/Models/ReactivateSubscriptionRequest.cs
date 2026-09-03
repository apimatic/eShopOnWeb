using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record ReactivateSubscriptionRequest
{
    /// <summary>
    /// These values are only applicable to subscriptions using calendar billing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calendar_billing")]
    public ReactivationBilling? CalendarBilling { get; init; }

    /// <summary>
    /// If <c>true</c> is sent, the reactivated Subscription will include a trial if one is available. If <c>false</c> is sent, the trial period will be ignored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("include_trial")]
    public bool? IncludeTrial { get; init; }

    /// <summary>
    /// If <c>true</c> is passed, the existing subscription balance will NOT be cleared/reset before adding the additional reactivation charges.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("preserve_balance")]
    public bool? PreserveBalance { get; init; }

    /// <summary>
    /// The coupon code to be applied during reactivation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_code")]
    public string? CouponCode { get; init; }

    /// <summary>
    /// If true is sent, Advanced Billing will use service credits and prepayments upon reactivation. If false is sent, the service credits and prepayments will be ignored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_credits_and_prepayments")]
    public bool? UseCreditsAndPrepayments { get; init; }

    /// <summary>
    /// If <c>true</c>, Advanced Billing will attempt to resume the subscription's billing period. If not resumable, the subscription will be reactivated with a new billing period. If <c>false</c> or omitted, Advanced Billing will only attempt to reactivate the subscription with a new billing period, regardless of whether or not the subscription is resumable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resume")]
    public Resume? Resume { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
