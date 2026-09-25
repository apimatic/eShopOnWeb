using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ReviseSubscription operation.
/// </summary>
public sealed record ReviseSubscriptionRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    public ModifySubscriptionRequest? Body { get; init; }
}
