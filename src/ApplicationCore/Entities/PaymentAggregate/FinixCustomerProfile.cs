using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Application-side reference to a Finix buyer identity. Card data never lives here.
/// </summary>
public class FinixCustomerProfile : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private FinixCustomerProfile() { }

    public FinixCustomerProfile(string buyerId, string finixIdentityId)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
