using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioCustomerMapping
{
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
