using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the SuspendSubscription operation.
/// </summary>
public sealed record SuspendSubscriptionRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    public SuspendSubscription? Body { get; init; }
}
