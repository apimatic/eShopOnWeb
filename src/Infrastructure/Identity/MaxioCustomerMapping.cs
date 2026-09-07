using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerMapping
{
    public string ApplicationUserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
