using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// Maps an eShop shopper to a reusable Finix buyer Identity. This lets a
/// shopper's saved cards and one-off payments share one buyer record at Finix.
/// </summary>
public class CustomerPaymentProfile : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private CustomerPaymentProfile() { }

    public CustomerPaymentProfile(string ownerId, string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        OwnerId = ownerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
