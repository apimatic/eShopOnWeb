namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A publishable subscription plan (a product in the configured Maxio product family).
/// </summary>
public class SubscriptionPlanDto
{
    public string PlanHandle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring price in whole currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing frequency unit, as Maxio reports it (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/> per billing cycle.</summary>
    public int? Interval { get; set; }

    public bool RequiresCreditCard { get; set; }
}
