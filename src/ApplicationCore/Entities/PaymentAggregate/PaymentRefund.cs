using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId, string idempotencyKey, long amountCents, string? finixTransferId, string? state)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        FinixTransferId = finixTransferId;
        State = state;
        Status = MapStatus(state);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? State { get; private set; }
    public RefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateFromFinix(string? finixTransferId, string? state)
    {
        FinixTransferId = finixTransferId;
        State = state;
        Status = MapStatus(state);
    }

    private static RefundStatus MapStatus(string? state)
    {
        if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return RefundStatus.Succeeded;
        }

        if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            return RefundStatus.Failed;
        }

        return RefundStatus.Pending;
    }
}
