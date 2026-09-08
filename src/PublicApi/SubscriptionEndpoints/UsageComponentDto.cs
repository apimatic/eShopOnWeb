namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The metered (usage) component available on the product family, when one exists.</summary>
public class UsageComponentDto
{
    public string Handle { get; set; }
    public string? Kind { get; set; }
    public long? PricePerUnitInCents { get; set; }
}
