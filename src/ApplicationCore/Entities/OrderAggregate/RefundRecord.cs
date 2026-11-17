using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// An individual refund (Finix reversal Transfer) against a captured payment.
/// </summary>
public class RefundRecord : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private RefundRecord() { }

    public RefundRecord(int orderPaymentId, string idempotencyKey, int amountCents)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        State = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string? FinixRefundId { get; private set; }
    public string State { get; private set; }
    public int AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? FailureMessage { get; private set; }

    public void RecordFinixResult(string finixRefundId, string state)
    {
        FinixRefundId = finixRefundId;
        State = state;
    }

    public void RecordFailure(string message)
    {
        State = "FAILED";
        FailureMessage = message;
    }
}
