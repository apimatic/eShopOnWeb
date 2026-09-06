namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Application-level policy for the subscription capability. Nothing here identifies a particular
/// billing site or catalog; provider connection settings live with the provider client.
/// </summary>
public class SubscriptionOptions
{
    /// <summary>
    /// Prefix applied to the billing-provider customer reference so eShopOnWeb customers are
    /// distinguishable from records created by other systems on a shared billing site.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Plan handle used when a subscribe request does not name one. Left unset by default so that
    /// no plan handle is baked into the build; when unset, the plan handle is required.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }
}
