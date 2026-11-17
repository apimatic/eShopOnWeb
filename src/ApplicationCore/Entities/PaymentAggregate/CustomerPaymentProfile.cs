using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper to the Finix buyer identity that owns their
/// tokenized payment instruments.
/// </summary>
public class CustomerPaymentProfile : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private CustomerPaymentProfile() { }
#pragma warning restore CS8618

    public CustomerPaymentProfile(string buyerId, string finixIdentityId)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
