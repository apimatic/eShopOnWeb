using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable link between an eShop identity user and its Maxio customer.</summary>
public class MaxioCustomer
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
