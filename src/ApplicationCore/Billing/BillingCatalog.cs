using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Everything a shopper can buy in the configured Maxio product family: the plans and the
/// usage components that can be attached to an enrollment.
/// </summary>
public record BillingCatalog
{
    public IReadOnlyList<SubscriptionPlan> Plans { get; init; } = new List<SubscriptionPlan>();

    public IReadOnlyList<UsageComponent> UsageComponents { get; init; } = new List<UsageComponent>();
}
