using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper to the single Finix buyer Identity that owns their saved cards.
/// </summary>
public class ShopperIdentity
{
    public string BuyerId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
