namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public long? InitialChargeInCents { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public string? ExpirationIntervalUnit { get; set; }

    public bool RequiresCreditCard { get; set; }

    public bool Taxable { get; set; }

    public string? ProductFamilyName { get; set; }

    public string? ProductFamilyHandle { get; set; }
}
