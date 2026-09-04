namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class MaxioCustomerRecord
{
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
}
