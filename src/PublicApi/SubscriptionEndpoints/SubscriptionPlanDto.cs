namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan (Maxio product) surfaced to shoppers.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable Maxio product handle - the value a client passes to subscribe.</summary>
    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Price per billing cycle, expressed in the site currency (decimal dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string? IntervalUnit { get; set; }
}
