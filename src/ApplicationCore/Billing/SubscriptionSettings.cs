namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Provider-agnostic subscription configuration: which seeded entities this deployment operates against.
/// Bound from the billing configuration section so ApplicationCore never names a specific provider.
/// </summary>
public class SubscriptionSettings
{
    /// <summary>Handle of the plan a customer is enrolled in when none is specified.</summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>Handle of the second plan, used as the plan-change target.</summary>
    public string? AlternateProductHandle { get; set; }

    /// <summary>Handle of the metered component usage is recorded against.</summary>
    public string? MeteredComponentHandle { get; set; }
}
