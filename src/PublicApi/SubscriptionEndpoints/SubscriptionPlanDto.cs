using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as returned to API clients.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        ProductFamilyHandle = p.ProductFamilyHandle,
        PriceInCents = p.PriceInCents,
        FormattedPrice = p.FormattedPrice,
        Currency = p.Currency,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit
    };
}
