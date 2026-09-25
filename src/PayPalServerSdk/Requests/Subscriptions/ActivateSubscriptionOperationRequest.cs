using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ActivateSubscription operation.
/// </summary>
public sealed record ActivateSubscriptionOperationRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    public ActivateSubscriptionRequest? Body { get; init; }
}
