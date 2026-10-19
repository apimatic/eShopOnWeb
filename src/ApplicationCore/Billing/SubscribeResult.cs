namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscribeResult(SubscribeOutcome Outcome, string Reference, SubscriptionSummary? Subscription)
{
    public static SubscribeResult Created(string reference, SubscriptionSummary subscription) =>
        new(SubscribeOutcome.Created, reference, subscription);

    public static SubscribeResult Replayed(string reference, SubscriptionSummary subscription) =>
        new(SubscribeOutcome.Replayed, reference, subscription);

    public static SubscribeResult Pending(string reference) =>
        new(SubscribeOutcome.OutcomePending, reference, null);
}
