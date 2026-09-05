using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable association between an eShop user and its Maxio customer.</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
