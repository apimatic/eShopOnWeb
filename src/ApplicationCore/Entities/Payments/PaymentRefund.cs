using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId,
        string idempotencyKey,
        long amountCents,
        string finixTransferId,
        string state)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        FinixTransferId = finixTransferId;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public OrderPayment OrderPayment { get; private set; } = null!;

    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string FinixTransferId { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateState(string state)
    {
        State = state;
    }

    public static bool IsFailedState(string? state)
    {
        return string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase);
    }
}
