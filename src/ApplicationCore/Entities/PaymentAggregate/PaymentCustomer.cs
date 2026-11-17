using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop user to the buyer Identity created for them in Finix.
/// </summary>
public class PaymentCustomer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentCustomer() { }

    public PaymentCustomer(string ownerId, string finixIdentityId)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        Guard.Against.NullOrWhiteSpace(finixIdentityId, nameof(finixIdentityId));

        OwnerId = ownerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
