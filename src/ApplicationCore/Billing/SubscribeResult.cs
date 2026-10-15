namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <param name="Subscription">Null only when <paramref name="Outcome"/> is <see cref="SubscribeOutcome.PendingConfirmation"/>.</param>
public sealed record SubscribeResult(SubscribeOutcome Outcome, SubscriptionSummary? Subscription, string SubscriptionReference);
