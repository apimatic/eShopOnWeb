using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CancellationOptions
{
    /// <summary>
    /// An indication as to why the subscription is being canceled. For your internal use.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; init; }

    /// <summary>
    /// The reason code associated with the cancellation. Use the <see href="$e/Reason%20Codes/listReasonCodes">List Reason Codes</see> endpoint to retrieve the reason codes associated with your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// When true, the subscription is cancelled at the current period end instead of immediately. To use this option, the Schedule Subscription Cancellation feature must be enabled on your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; init; }

    /// <summary>
    /// Schedules the cancellation on the provided date. This option is not applicable for prepaid subscriptions. To use this option, the Schedule Subscription Cancellation feature must be enabled on your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scheduled_cancellation_at")]
    public DateTimeOffset? ScheduledCancellationAt { get; init; }

    /// <summary>
    /// Applies to prepaid subscriptions. When true, which is the default, the remaining prepaid balance is refunded as part of cancellation processing. When false, prepaid balance is not refunded as part of cancellation processing. To use this option, the Schedule Subscription Cancellation feature must be enabled on your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refund_prepayment_account_balance")]
    public bool? RefundPrepaymentAccountBalance { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
