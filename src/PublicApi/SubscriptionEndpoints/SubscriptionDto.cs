using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription returned to API clients.
/// </summary>
public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? FormattedPrice { get; set; }

    /// <summary>The date the customer is next billed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public string? CustomerReference { get; set; }
}
