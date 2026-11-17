using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The Finix buyer Identity owned by one eShop shopper. The shopper id is the primary key, so
/// two concurrent provisioning attempts cannot create two provider identities.
/// </summary>
public class ShopperFinixIdentity
{
    public string ShopperId { get; set; } = string.Empty;
    public string? IdentityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
