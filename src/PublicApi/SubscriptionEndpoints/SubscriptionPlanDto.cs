namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public long PriceInCents { get; set; }
    public string Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }

    public static string FormatPrice(long priceInCents)
    {
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"${priceInCents / 100m:0.00}");
    }
}