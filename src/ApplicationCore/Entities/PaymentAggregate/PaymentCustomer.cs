using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentCustomer : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentCustomer() { }
#pragma warning restore CS8618

    public PaymentCustomer(string buyerId, string finixIdentityId)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
