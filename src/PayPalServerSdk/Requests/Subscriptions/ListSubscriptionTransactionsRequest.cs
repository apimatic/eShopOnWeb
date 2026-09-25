using System.ComponentModel.DataAnnotations;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ListSubscriptionTransactions operation.
/// </summary>
public sealed record ListSubscriptionTransactionsRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The start time of the range of transactions to list.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public required string StartTime { get; init; }

    /// <summary>
    /// The end time of the range of transactions to list.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public required string EndTime { get; init; }
}
