namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionResponse
{
    public int id { get; set; }
    public string state { get; set; } = string.Empty;
    public long product_price_in_cents { get; set; }
    public string current_period_ends_at { get; set; } = string.Empty;
    public int customer_id { get; set; }
    public ProductResponse? product { get; set; }
}
