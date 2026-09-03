namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> distinguishes a freshly created
/// subscription from an idempotent no-op (the caller already had a live subscription to the plan),
/// so a double-click surfaces the existing subscription rather than a duplicate.
/// </summary>
public record SubscribeResult
{
    public required BillingSubscription Subscription { get; init; }

    public required bool AlreadySubscribed { get; init; }
}
