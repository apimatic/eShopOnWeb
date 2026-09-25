using System.Collections.Generic;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the PatchSubscription operation.
/// </summary>
public sealed record PatchSubscriptionRequest
{
    /// <summary>
    /// The ID for the subscription.
    /// </summary>
    public required string Id { get; init; }

    public IReadOnlyList<Patch>? Body { get; init; }
}
