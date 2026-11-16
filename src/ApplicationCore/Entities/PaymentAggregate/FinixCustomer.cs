using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class FinixCustomer : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private FinixCustomer() { }
#pragma warning restore CS8618

    public FinixCustomer(string userId, string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        UserId = userId;
        FinixIdentityId = finixIdentityId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
