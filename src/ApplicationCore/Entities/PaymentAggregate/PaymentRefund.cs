using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund request against the captured payment, keyed by the caller's idempotency key.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string payPalRequestId, DateTimeOffset now)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRequestId = payPalRequestId;
        Status = RefundStatus.Requested;
        RequestedAt = now;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The PayPal-Request-Id this refund is sent (and re-sent) under.</summary>
    public string PayPalRequestId { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string? PayPalStatus { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? OutcomeUnknownSince { get; private set; }

    /// <summary>Counts against the refundable balance: anything not definitively failed.</summary>
    public bool ReservesFunds => Status != RefundStatus.Failed;

    internal void Succeeded(string payPalRefundId, string? payPalStatus, DateTimeOffset now)
    {
        Status = RefundStatus.Succeeded;
        PayPalRefundId = payPalRefundId;
        PayPalStatus = payPalStatus;
        CompletedAt = now;
        OutcomeUnknownSince = null;
    }

    internal void Failed(string reason, DateTimeOffset now)
    {
        Status = RefundStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
        OutcomeUnknownSince = null;
    }

    internal void OutcomeUnknown(DateTimeOffset now) => OutcomeUnknownSince ??= now;
}
