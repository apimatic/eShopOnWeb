using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>
/// Maps an application user to the Finix buyer identity that owns their payment instruments.
/// </summary>
public class CustomerProfile : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private CustomerProfile() { }
#pragma warning restore CS8618

    public CustomerProfile(string buyerId)
    {
        BuyerId = buyerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string? FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetFinixIdentityId(string identityId)
    {
        FinixIdentityId = identityId;
    }
}
