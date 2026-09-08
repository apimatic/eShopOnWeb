namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for enrollment.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    public int BillingInterval { get; set; }

    public string BillingIntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
