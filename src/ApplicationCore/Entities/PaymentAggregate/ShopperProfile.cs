using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class ShopperProfile
{
    private ShopperProfile() { }

    public ShopperProfile(string shopperId, string finixIdentityId)
    {
        ShopperId = shopperId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string ShopperId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
