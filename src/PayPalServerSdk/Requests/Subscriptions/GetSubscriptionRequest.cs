using System.ComponentModel.DataAnnotations;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the GetSubscription operation.
/// </summary>
public sealed record GetSubscriptionRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// List of fields that are to be returned in the response. Possible value for fields are last_failed_payment and plan.
    /// </summary>
    [StringLength(100, MinimumLength = 1)]
    public string? Fields { get; init; }
}
