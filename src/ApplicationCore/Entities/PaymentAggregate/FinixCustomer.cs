using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class FinixCustomer : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private FinixCustomer()
    {
        OwnerId = string.Empty;
        FinixIdentityId = string.Empty;
    }

    public FinixCustomer(string ownerId, string finixIdentityId)
    {
        OwnerId = ownerId;
        FinixIdentityId = finixIdentityId;
    }
}
