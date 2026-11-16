using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(int paymentId, int orderId, string finixTransferId, long amountCents, string status,
        string idempotencyKey, DateTimeOffset createdAt)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        FinixTransferId = finixTransferId;
        AmountCents = amountCents;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int PaymentId { get; private set; }
    public int OrderId { get; private set; }
    public string FinixTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
