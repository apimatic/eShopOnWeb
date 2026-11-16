using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A refund issued against the captured transfer for an order. Only Finix-owned identifiers
/// and amounts are persisted; card data never enters this application's database.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderId, string refundId, long amountCents, string? status,
        string idempotencyKey, DateTimeOffset createdAt)
    {
        OrderId = orderId;
        RefundId = refundId;
        AmountCents = amountCents;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string RefundId { get; private set; }
    public long AmountCents { get; private set; }
    public string? Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string? status)
    {
        Status = status;
    }
}
