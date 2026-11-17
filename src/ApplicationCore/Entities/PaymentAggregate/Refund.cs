using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string TransferId { get; private set; }
    public long Amount { get; private set; }
    public string State { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(int orderId, string transferId, long amount, string state, string idempotencyKey)
    {
        OrderId = orderId;
        TransferId = transferId;
        Amount = amount;
        State = state;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
