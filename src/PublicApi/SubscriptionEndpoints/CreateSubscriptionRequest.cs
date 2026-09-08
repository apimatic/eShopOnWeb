using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan.
/// </summary>
public sealed class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name used when provisioning the billing customer. When omitted a
    /// value is derived from the authenticated user's email address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional display name used when provisioning the billing customer. When omitted a
    /// value is derived from the authenticated user's email address.
    /// </summary>
    public string? LastName { get; set; }
}
