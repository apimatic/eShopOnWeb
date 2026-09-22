using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation.Attributes;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment preferences to override at subscription level.
/// </summary>
public record PaymentPreferencesOverride
{
    /// <summary>
    /// Indicates whether to automatically bill the outstanding amount in the next billing cycle.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auto_bill_outstanding")]
    public bool? AutoBillOutstanding { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("setup_fee")]
    public Money? SetupFee { get; init; }

    /// <summary>
    /// The action to take on the subscription if the initial payment for the setup fails.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("setup_fee_failure_action")]
    public SetupFeeFailureAction? SetupFeeFailureAction { get; init; }

    /// <summary>
    /// The maximum number of payment failures before a subscription is suspended. For example, if <c>payment_failure_threshold</c> is <c>2</c>, the subscription automatically updates to the <c>SUSPEND</c> state if two consecutive payments fail.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_failure_threshold")]
    [Minimum(0)]
    [Maximum(999)]
    public int? PaymentFailureThreshold { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
