namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// The outcome of a subscribe request, derived from the Maxio subscription state and whether the
/// subscription was created on this call or already existed.
/// </summary>
public enum SubscribeOutcome
{
    /// <summary>A new, live (active/trialing) subscription was created on this call.</summary>
    Created,

    /// <summary>The buyer already had a live subscription to this plan; the existing one is returned.</summary>
    AlreadySubscribed,

    /// <summary>The subscription was accepted by Maxio but is not yet live (pending/awaiting signup).</summary>
    Pending,

    /// <summary>Maxio reported the subscription in a failed/problem state.</summary>
    Failed
}
