using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an application shopper to the Finix buyer Identity that owns their payment instruments.
/// Only the Finix identifier is persisted; no card data is ever stored here.
/// </summary>
public class PaymentCustomer : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentCustomer() { }

    public PaymentCustomer(string ownerId, string finixIdentityId, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        OwnerId = ownerId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
