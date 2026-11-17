using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund()
    {
    }
#pragma warning restore CS8618

    public PaymentRefund(
        string finixRefundId,
        long amountCents,
        string idempotencyKey,
        string? state,
        string? failureCode,
        string? failureMessage,
        DateTimeOffset now)
    {
        FinixRefundId = finixRefundId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        State = state;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        CreatedAt = now;
    }

    public int OrderPaymentId { get; internal set; }
    public string FinixRefundId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? State { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
