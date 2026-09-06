namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of <see cref="Interfaces.ISubscriptionService.SubscribeAsync"/>.
/// </summary>
/// <param name="Subscription">The live subscription, whether it was just created or already existed.</param>
/// <param name="Created">
/// False when the shopper was already enrolled in the plan and the call was a no-op. Callers use
/// this to answer 201 vs 200.
/// </param>
public sealed record SubscribeResult(Subscription Subscription, bool Created);
