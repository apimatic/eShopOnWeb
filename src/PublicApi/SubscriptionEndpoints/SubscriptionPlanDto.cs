namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A saleable subscription plan (a product in the configured Maxio product family).
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio product id. Not stable across sandbox re-seeds.</summary>
    public int? Id { get; set; }

    /// <summary>Stable plan handle, e.g. "eshop-pro". Use this when subscribing.</summary>
    public string? Handle { get; set; }

    public string? Name { get; set; }

    /// <summary>Recurring price in cents (per billing interval).</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents is { } cents ? cents / 100m : 0m;

    /// <summary>ISO currency code of the site (e.g. "USD").</summary>
    public string? Currency { get; set; }
}
