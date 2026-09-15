using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
