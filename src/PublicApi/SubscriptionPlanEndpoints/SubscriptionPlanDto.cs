namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>A subscription plan (Maxio product) offered to shoppers.</summary>
public class SubscriptionPlanDto
{
    /// <summary>The Maxio product id.</summary>
    public long Id { get; set; }

    /// <summary>The stable plan handle (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price in dollars.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (month or day).</summary>
    public string? IntervalUnit { get; set; }
}
