using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix transfer reversal) recorded against an order's captured transfer.
/// </summary>
public class PaymentRefund
{
    private PaymentRefund() { }

    public PaymentRefund(int orderId, string buyerId, string refundTransferId, string idempotencyKey,
        long amountCents, string? state)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        RefundTransferId = refundTransferId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string RefundTransferId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string? State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
