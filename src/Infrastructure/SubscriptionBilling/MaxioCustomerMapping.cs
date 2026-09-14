using System;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public class MaxioCustomerMapping
{
    public string UserId { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
