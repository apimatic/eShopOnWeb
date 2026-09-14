namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Outcome of a subscribe request.</summary>
public class SubscriptionEnrollment
{
    /// <summary>The subscription the caller now has for the requested plan.</summary>
    public SubscriptionInfo? Subscription { get; set; }

    /// <summary>
    /// True when this request created the subscription; false when an existing subscription for
    /// the same (user, plan) was returned instead (idempotent replay of a double-click).
    /// </summary>
    public bool IsNew { get; set; }
}
