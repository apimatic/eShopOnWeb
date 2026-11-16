using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper (the JWT name) to the Finix buyer Identity used for their cards.
/// </summary>
public class ShopperProfile
{
    private ShopperProfile() { }

    public ShopperProfile(string buyerId, string finixIdentityId, string? email)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        Email = email;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
