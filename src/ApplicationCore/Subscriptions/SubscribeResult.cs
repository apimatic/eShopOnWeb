namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>The outcome of a subscribe request.</summary>
public record SubscribeResult
{
    /// <summary>The resulting (new or pre-existing) subscription.</summary>
    public required SubscriptionSummary Subscription { get; init; }

    /// <summary>
    /// True when an active subscription to the requested plan already existed and was returned
    /// instead of creating a duplicate; false when a brand-new subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; init; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; init; }

    /// <summary>The eShopOnWeb reference stored on the Maxio customer.</summary>
    public string CustomerReference { get; init; } = string.Empty;
}
