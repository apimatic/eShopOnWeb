namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can enroll in (a product in the configured Maxio product family).
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle used to subscribe (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Current numeric Maxio product id (informational; may change on re-seed).</summary>
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
