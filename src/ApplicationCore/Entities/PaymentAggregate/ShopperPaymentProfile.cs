using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class ShopperPaymentProfile : BaseEntity, IAggregateRoot
{
    private ShopperPaymentProfile() { }

    public ShopperPaymentProfile(string buyerId, string finixIdentityId)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
