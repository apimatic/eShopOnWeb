using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single (full or partial) refund against a captured payment. The caller-supplied
/// idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";
    public const string StatusFailed = "FAILED";

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount, string currency, string? note)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Note = note;
        Status = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? Note { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkCompleted(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    public void MarkFailed()
    {
        Status = StatusFailed;
    }
}
