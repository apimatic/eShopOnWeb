using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation.Attributes;

namespace PayPal.Models;

/// <summary>
/// The billing details for the subscription. If the subscription was or is active, these fields are populated.
/// </summary>
public record SubscriptionBillingInformation
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonPropertyName("outstanding_balance")]
    public required Money OutstandingBalance { get; init; }

    /// <summary>
    /// The trial and regular billing executions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cycle_executions")]
    [MinLength(0)]
    [MaxLength(3)]
    public IReadOnlyList<CycleExecution>? CycleExecutions { get; init; }

    /// <summary>
    /// The details for the last payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_payment")]
    public LastPaymentDetails? LastPayment { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_billing_time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? NextBillingTime { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("final_payment_time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? FinalPaymentTime { get; init; }

    /// <summary>
    /// The number of consecutive payment failures. Resets to <c>0</c> after a successful payment. If this reaches the <c>payment_failure_threshold</c> value, the subscription updates to the <c>SUSPENDED</c> state.
    /// </summary>
    [JsonPropertyName("failed_payments_count")]
    [Minimum(0)]
    [Maximum(999)]
    public required int FailedPaymentsCount { get; init; }

    /// <summary>
    /// The details for the failed payment of the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_failed_payment")]
    public FailedPaymentDetails? LastFailedPayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
