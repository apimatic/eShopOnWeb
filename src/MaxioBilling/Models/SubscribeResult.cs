namespace Microsoft.eShopWeb.MaxioBilling.Models;

/// <summary>Outcome of a subscribe request.</summary>
/// <param name="Subscription">The subscription the user now holds.</param>
/// <param name="AlreadyExisted">
/// True when an equivalent live subscription was already present and nothing was created —
/// the answer a repeated or double-clicked request gets.
/// </param>
public sealed record SubscribeResult(SubscriptionSummary Subscription, bool AlreadyExisted);
