using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured payment. The caller-supplied idempotency key
/// makes a repeated refund request return this record instead of refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount, string currency, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        NoteToPayer = noteToPayer;
        Status = RefundStatus.Pending;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? NoteToPayer { get; private set; }
    /// <summary>PayPal's refund id, set once PayPal confirms the refund.</summary>
    public string? PayPalRefundId { get; private set; }
    public RefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkCompleted(string payPalRefundId, Payment payment)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = RefundStatus.Completed;
        payment.OnRefundCompleted();
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
    }
}
