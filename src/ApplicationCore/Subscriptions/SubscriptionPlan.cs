namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool RequireCreditCard { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
