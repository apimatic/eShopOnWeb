namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionResponse
{
    public Subscription Subscription { get; set; } = new();
}

public class Subscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public int CustomerId { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
    public string NextAssessmentAt { get; set; } = string.Empty;
    public string ActivatedAt { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public ProductInfo Product { get; set; } = new();
}

public class ProductInfo
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
