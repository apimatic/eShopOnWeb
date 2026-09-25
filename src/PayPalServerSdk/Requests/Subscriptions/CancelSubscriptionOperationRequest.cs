using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the CancelSubscription operation.
/// </summary>
public sealed record CancelSubscriptionOperationRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    public CancelSubscriptionRequest? Body { get; init; }
}
